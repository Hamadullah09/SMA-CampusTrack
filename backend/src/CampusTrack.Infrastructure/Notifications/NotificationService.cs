using System.Text.Json;
using CampusTrack.Application.Common.Interfaces;
using CampusTrack.Domain.Communication;
using CampusTrack.Domain.Enums;
using CampusTrack.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CampusTrack.Infrastructure.Notifications;

/// <summary>
/// Persists notifications, then delivers them on whichever channels the recipient has left
/// enabled.
///
/// Persist-then-deliver is the important ordering. A push that fails because a phone is in a
/// tunnel must not mean the parent never learns their child arrived - the record is already in
/// the in-app inbox, and the delivery row carries its own retry state.
/// </summary>
public class NotificationService : INotificationService
{
    private readonly CampusTrackDbContext _db;
    private readonly IPushSender _push;
    private readonly IEmailSender _email;
    private readonly IRealtimePublisher _realtime;
    private readonly IDateTimeProvider _clock;
    private readonly ILogger<NotificationService> _logger;

    public NotificationService(
        CampusTrackDbContext db,
        IPushSender push,
        IEmailSender email,
        IRealtimePublisher realtime,
        IDateTimeProvider clock,
        ILogger<NotificationService> logger)
    {
        _db = db;
        _push = push;
        _email = email;
        _realtime = realtime;
        _clock = clock;
        _logger = logger;
    }

    public async Task<long> NotifyAsync(NotificationRequest request, CancellationToken ct = default)
    {
        var notification = await PersistAsync(request, ct);
        await DeliverAsync(notification, request, ct);
        return notification.Id;
    }

    public async Task NotifyManyAsync(IEnumerable<NotificationRequest> requests, CancellationToken ct = default)
    {
        foreach (var request in requests)
        {
            try
            {
                await NotifyAsync(request, ct);
            }
            catch (Exception ex)
            {
                // One bad recipient must not stop a whole announcement fan-out.
                _logger.LogError(ex, "Notification to user {UserId} failed", request.UserId);
            }
        }
    }

    public async Task NotifyGuardiansOfStudentAsync(
        int studentId, NotificationRequest template, CancellationToken ct = default)
    {
        // Only approved links, and only guardians who have not opted out of this child's
        // updates. An unapproved link must never receive a child's movements.
        var guardianUserIds = await _db.GuardianStudents
            .Where(gs => gs.StudentId == studentId
                         && gs.IsApproved
                         && gs.ReceivesNotifications
                         && !gs.IsDeleted)
            .Select(gs => gs.Guardian!.UserId)
            .ToListAsync(ct);

        if (guardianUserIds.Count == 0)
        {
            _logger.LogDebug("Student {StudentId} has no guardian subscribed to notifications", studentId);
            return;
        }

        var requests = guardianUserIds.Select(userId => new NotificationRequest
        {
            UserId = userId,
            Category = template.Category,
            Priority = template.Priority,
            Title = template.Title,
            Body = template.Body,
            Data = template.Data,
            StudentId = studentId,
            RelatedEntityType = template.RelatedEntityType,
            RelatedEntityId = template.RelatedEntityId
        });

        await NotifyManyAsync(requests, ct);
    }

    private async Task<Notification> PersistAsync(NotificationRequest request, CancellationToken ct)
    {
        var notification = new Notification
        {
            UserId = request.UserId,
            Category = request.Category,
            Priority = request.Priority,
            Title = Truncate(request.Title, 200),
            Body = Truncate(request.Body, 1000),
            DataJson = request.Data is null ? null : JsonSerializer.Serialize(request.Data),
            StudentId = request.StudentId,
            RelatedEntityType = request.RelatedEntityType,
            RelatedEntityId = request.RelatedEntityId,
            CreatedAtUtc = _clock.UtcNow
        };

        _db.Notifications.Add(notification);
        await _db.SaveChangesAsync(ct);
        return notification;
    }

    private async Task DeliverAsync(Notification notification, NotificationRequest request, CancellationToken ct)
    {
        var preference = await _db.NotificationPreferences
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == request.UserId && p.Category == request.Category, ct);

        // No stored preference means the defaults apply: in-app and push on, email off.
        var pushEnabled = preference?.PushEnabled ?? true;
        var emailEnabled = preference?.EmailEnabled ?? false;

        // Live in-app delivery always happens; it is the channel the user is looking at.
        await _realtime.PublishToUserAsync(request.UserId, "notification", new
        {
            id = notification.Id,
            category = notification.Category.ToString(),
            title = notification.Title,
            body = notification.Body,
            priority = notification.Priority.ToString(),
            studentId = notification.StudentId,
            createdAtUtc = notification.CreatedAtUtc
        }, ct);

        RecordDelivery(notification.Id, NotificationChannel.InApp, DeliveryStatus.Sent);

        if (pushEnabled && !IsQuietHours(preference, notification.Priority))
            await SendPushAsync(notification, request, ct);
        else if (pushEnabled)
            RecordDelivery(notification.Id, NotificationChannel.Push, DeliveryStatus.Skipped, "Quiet hours");

        if (emailEnabled) await SendEmailAsync(notification, request, ct);

        await _db.SaveChangesAsync(ct);
    }

    private async Task SendPushAsync(Notification notification, NotificationRequest request, CancellationToken ct)
    {
        if (!_push.IsConfigured)
        {
            RecordDelivery(notification.Id, NotificationChannel.Push, DeliveryStatus.Skipped, "Push is not configured");
            return;
        }

        var tokens = await _db.DeviceTokens
            .Where(d => d.UserId == request.UserId && d.IsActive)
            .Select(d => d.Token)
            .ToListAsync(ct);

        if (tokens.Count == 0)
        {
            RecordDelivery(notification.Id, NotificationChannel.Push, DeliveryStatus.Skipped, "No registered device");
            return;
        }

        var data = new Dictionary<string, string>
        {
            ["notificationId"] = notification.Id.ToString(),
            ["category"] = notification.Category.ToString()
        };
        if (notification.StudentId is { } sid) data["studentId"] = sid.ToString();
        if (notification.RelatedEntityType is { } type) data["entityType"] = type;
        if (notification.RelatedEntityId is { } entityId) data["entityId"] = entityId.ToString();

        var result = await _push.SendAsync(
            new PushMessage(tokens, notification.Title, notification.Body, data, notification.Priority), ct);

        // A token the provider rejects as unregistered is dead - the app was uninstalled or
        // the token rotated. Deactivating it stops it being retried forever.
        if (result.InvalidTokens.Count > 0)
        {
            var stale = await _db.DeviceTokens
                .Where(d => result.InvalidTokens.Contains(d.Token))
                .ToListAsync(ct);

            foreach (var token in stale)
            {
                token.IsActive = false;
                token.InvalidatedAtUtc = _clock.UtcNow;
            }

            _logger.LogInformation("Deactivated {Count} stale device token(s)", stale.Count);
        }

        RecordDelivery(
            notification.Id,
            NotificationChannel.Push,
            result.Success ? DeliveryStatus.Sent : DeliveryStatus.Retrying,
            result.ErrorMessage,
            result.ProviderMessageId,
            result.Success ? null : _clock.UtcNow.AddMinutes(2));
    }

    private async Task SendEmailAsync(Notification notification, NotificationRequest request, CancellationToken ct)
    {
        if (!_email.IsConfigured)
        {
            RecordDelivery(notification.Id, NotificationChannel.Email, DeliveryStatus.Skipped, "Email is not configured");
            return;
        }

        var address = await _db.Users
            .Where(u => u.Id == request.UserId)
            .Select(u => u.Email)
            .FirstOrDefaultAsync(ct);

        if (string.IsNullOrWhiteSpace(address))
        {
            RecordDelivery(notification.Id, NotificationChannel.Email, DeliveryStatus.Skipped, "No email address");
            return;
        }

        var sent = await _email.SendAsync(address, notification.Title,
            $"<p>{System.Net.WebUtility.HtmlEncode(notification.Body)}</p>", ct);

        RecordDelivery(notification.Id, NotificationChannel.Email,
            sent ? DeliveryStatus.Sent : DeliveryStatus.Failed);
    }

    private void RecordDelivery(long notificationId, NotificationChannel channel, DeliveryStatus status,
        string? error = null, string? providerMessageId = null, DateTime? nextRetry = null)
    {
        _db.NotificationDeliveries.Add(new NotificationDelivery
        {
            NotificationId = notificationId,
            Channel = channel,
            Status = status,
            AttemptCount = status is DeliveryStatus.Sent or DeliveryStatus.Failed or DeliveryStatus.Retrying ? 1 : 0,
            SentAtUtc = status == DeliveryStatus.Sent ? _clock.UtcNow : null,
            NextRetryAtUtc = nextRetry,
            ErrorMessage = error is null ? null : Truncate(error, 500),
            ProviderMessageId = providerMessageId
        });
    }

    /// <summary>
    /// Quiet hours suppress pushes but never the in-app record. Critical notifications - an
    /// emergency, a child leaving campus unexpectedly - ignore them by design.
    /// </summary>
    private bool IsQuietHours(NotificationPreference? preference, NotificationPriority priority)
    {
        if (preference?.QuietHoursStart is not { } start || preference.QuietHoursEnd is not { } end)
            return false;

        if (priority == NotificationPriority.Critical) return false;

        var localNow = TimeOnly.FromDateTime(_clock.SchoolNow.DateTime);

        // Handles windows that wrap midnight (22:00 to 07:00).
        return start <= end
            ? localNow >= start && localNow <= end
            : localNow >= start || localNow <= end;
    }

    private static string Truncate(string value, int max) =>
        string.IsNullOrEmpty(value) ? string.Empty : value.Length <= max ? value : value[..max];
}
