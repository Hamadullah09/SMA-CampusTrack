namespace CampusTrack.Domain.Common;

/// <summary>Marks an entity whose create/update provenance is tracked automatically.</summary>
public interface IAuditableEntity
{
    DateTime CreatedAtUtc { get; set; }
    int? CreatedByUserId { get; set; }
    DateTime? UpdatedAtUtc { get; set; }
    int? UpdatedByUserId { get; set; }
}

/// <summary>
/// Marks an entity that is never physically deleted. A global query filter hides
/// soft-deleted rows, so history, audit trails and RFID events keep their referents.
/// </summary>
public interface ISoftDeletable
{
    bool IsDeleted { get; set; }
    DateTime? DeletedAtUtc { get; set; }
    int? DeletedByUserId { get; set; }
}

/// <summary>
/// Rows belonging to one school. Single-school deployments simply use the one seeded
/// school; the column and its query filter exist so multi-school isolation can be
/// switched on without a schema migration.
/// </summary>
public interface ITenantScoped
{
    int SchoolId { get; set; }
}

/// <summary>Base for entities that carry audit provenance and support soft deletion.</summary>
public abstract class AuditableEntity<TKey> : IAuditableEntity, ISoftDeletable
{
    public TKey Id { get; set; } = default!;

    public DateTime CreatedAtUtc { get; set; }
    public int? CreatedByUserId { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
    public int? UpdatedByUserId { get; set; }

    public bool IsDeleted { get; set; }
    public DateTime? DeletedAtUtc { get; set; }
    public int? DeletedByUserId { get; set; }

    // Note: no byte[] RowVersion here. MySQL has no server-maintained rowversion column,
    // so such a property would never actually change and would give false confidence.
    // Entities that need optimistic concurrency mark UpdatedAtUtc as a concurrency token
    // in their EF configuration instead, and the audit interceptor keeps it moving.
}

/// <summary>Auditable, soft-deletable and scoped to a school.</summary>
public abstract class TenantEntity<TKey> : AuditableEntity<TKey>, ITenantScoped
{
    public int SchoolId { get; set; } = 1;
}
