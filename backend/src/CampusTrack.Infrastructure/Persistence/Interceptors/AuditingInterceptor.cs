using System.Text.Json;
using CampusTrack.Application.Common.Interfaces;
using CampusTrack.Domain.Auditing;
using CampusTrack.Domain.Common;
using CampusTrack.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CampusTrack.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Stamps audit fields, turns deletes into soft deletes, and writes an <see cref="AuditLog"/>
/// row for every meaningful change.
///
/// This lives in an interceptor rather than in each service on purpose: an audit trail that
/// depends on developers remembering to call it is an audit trail with holes in it. Anything
/// that reaches SaveChanges is recorded, including bulk operations from background workers.
/// </summary>
public class AuditingInterceptor : SaveChangesInterceptor
{
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;

    /// <summary>
    /// Tables excluded from change auditing. These are either append-only logs (auditing them
    /// would recurse or double the write volume) or extremely high-frequency RFID rows whose
    /// history is already the point of the table.
    /// </summary>
    private static readonly HashSet<string> ExcludedEntities =
    [
        nameof(AuditLog), nameof(SystemLog),
        "RfidRawRead", "RfidEvent", "DeviceLog",
        "Notification", "NotificationDelivery",
        "LoginAttempt", "RefreshToken",
        "StudentPresence", "ClassroomPresence"
    ];

    /// <summary>Never written to the log, even as "changed".</summary>
    private static readonly HashSet<string> SensitiveProperties =
    [
        "PasswordHash", "SecurityStamp", "ConcurrencyStamp", "TokenHash",
        "ApiKeyHash", "ReplacedByTokenHash", "Token"
    ];

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    public AuditingInterceptor(ICurrentUser currentUser, IDateTimeProvider clock)
    {
        _currentUser = currentUser;
        _clock = clock;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (eventData.Context is not null) Apply(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData, InterceptionResult<int> result)
    {
        if (eventData.Context is not null) Apply(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    private void Apply(DbContext context)
    {
        var now = _clock.UtcNow;
        var userId = _currentUser.UserId;

        context.ChangeTracker.DetectChanges();

        var auditRows = new List<AuditLog>();

        foreach (var entry in context.ChangeTracker.Entries().ToList())
        {
            if (entry.State is EntityState.Detached or EntityState.Unchanged) continue;

            StampAuditFields(entry, now, userId);
            ConvertDeleteToSoftDelete(entry, now, userId);

            var log = BuildAuditLog(entry, now);
            if (log is not null) auditRows.Add(log);
        }

        if (auditRows.Count > 0) context.Set<AuditLog>().AddRange(auditRows);
    }

    private static void StampAuditFields(EntityEntry entry, DateTime now, int? userId)
    {
        if (entry.Entity is not IAuditableEntity auditable) return;

        switch (entry.State)
        {
            case EntityState.Added:
                auditable.CreatedAtUtc = auditable.CreatedAtUtc == default ? now : auditable.CreatedAtUtc;
                auditable.CreatedByUserId ??= userId;
                break;
            case EntityState.Modified:
                auditable.UpdatedAtUtc = now;
                auditable.UpdatedByUserId = userId;
                break;
        }
    }

    /// <summary>
    /// A hard delete would orphan attendance history and RFID events that legitimately point
    /// at the row. Deletes become updates so the graph stays intact and recoverable.
    /// </summary>
    private static void ConvertDeleteToSoftDelete(EntityEntry entry, DateTime now, int? userId)
    {
        if (entry.State != EntityState.Deleted) return;
        if (entry.Entity is not ISoftDeletable deletable) return;

        entry.State = EntityState.Modified;
        deletable.IsDeleted = true;
        deletable.DeletedAtUtc = now;
        deletable.DeletedByUserId = userId;
    }

    private AuditLog? BuildAuditLog(EntityEntry entry, DateTime now)
    {
        var entityName = entry.Entity.GetType().Name;
        if (ExcludedEntities.Contains(entityName)) return null;

        // A soft delete arrives here as Modified; report it as the delete it really is.
        var isSoftDelete = entry.Entity is ISoftDeletable { IsDeleted: true }
                           && entry.Properties.Any(p => p.Metadata.Name == nameof(ISoftDeletable.IsDeleted) && p.IsModified);

        var action = entry.State switch
        {
            EntityState.Added => AuditAction.Create,
            EntityState.Deleted => AuditAction.Delete,
            EntityState.Modified when isSoftDelete => AuditAction.Delete,
            EntityState.Modified => AuditAction.Update,
            _ => (AuditAction?)null
        };
        if (action is null) return null;

        var oldValues = new Dictionary<string, object?>();
        var newValues = new Dictionary<string, object?>();
        var changed = new List<string>();

        foreach (var property in entry.Properties)
        {
            var name = property.Metadata.Name;
            if (SensitiveProperties.Contains(name)) continue;

            switch (action)
            {
                case AuditAction.Create:
                    newValues[name] = property.CurrentValue;
                    break;

                case AuditAction.Delete:
                    oldValues[name] = property.OriginalValue;
                    break;

                case AuditAction.Update when property.IsModified &&
                                             !Equals(property.OriginalValue, property.CurrentValue):
                    changed.Add(name);
                    oldValues[name] = property.OriginalValue;
                    newValues[name] = property.CurrentValue;
                    break;
            }
        }

        // An "update" that changed nothing but the audit stamps is noise, not history.
        if (action == AuditAction.Update &&
            changed.All(c => c is nameof(IAuditableEntity.UpdatedAtUtc) or nameof(IAuditableEntity.UpdatedByUserId)))
        {
            return null;
        }

        return new AuditLog
        {
            SchoolId = _currentUser.SchoolId,
            UserId = _currentUser.UserId,
            UserName = _currentUser.UserName,
            UserRole = _currentUser.Roles.Count > 0 ? string.Join(",", _currentUser.Roles) : null,
            Action = action.Value,
            EntityName = entityName,
            EntityId = TryGetPrimaryKey(entry),
            OldValuesJson = oldValues.Count > 0 ? JsonSerializer.Serialize(oldValues, JsonOptions) : null,
            NewValuesJson = newValues.Count > 0 ? JsonSerializer.Serialize(newValues, JsonOptions) : null,
            AffectedColumns = changed.Count > 0 ? string.Join(",", changed) : null,
            IpAddress = _currentUser.IpAddress,
            UserAgent = Truncate(_currentUser.UserAgent, 400),
            CorrelationId = _currentUser.CorrelationId,
            OccurredAtUtc = now
        };
    }

    private static string? TryGetPrimaryKey(EntityEntry entry)
    {
        var key = entry.Metadata.FindPrimaryKey();
        if (key is null) return null;

        var values = key.Properties
            .Select(p => entry.Property(p.Name).CurrentValue?.ToString())
            .Where(v => !string.IsNullOrEmpty(v))
            .ToList();

        return values.Count == 0 ? null : string.Join("|", values);
    }

    private static string? Truncate(string? value, int max) =>
        value is null || value.Length <= max ? value : value[..max];
}
