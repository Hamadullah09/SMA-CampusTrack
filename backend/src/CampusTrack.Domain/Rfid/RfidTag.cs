using CampusTrack.Domain.Common;
using CampusTrack.Domain.Enums;
using CampusTrack.Domain.People;

namespace CampusTrack.Domain.Rfid;

/// <summary>
/// The bridge between a physical UHF tag and a person. Keeping the EPC here rather than
/// on <see cref="Student"/> is deliberate:
/// <list type="bullet">
///   <item>a lost card is revoked and reissued without mutating the student record;</item>
///   <item>the EPC is never a primary key, so a cloned tag cannot impersonate an identity;</item>
///   <item>tag history stays queryable - every past EPC keeps pointing at its holder.</item>
/// </list>
/// Exactly one active tag per holder is enforced by a filtered unique index.
/// </summary>
public class RfidTag : TenantEntity<int>
{
    /// <summary>Electronic Product Code as read from the air, normalised to upper-case hex.</summary>
    public string Epc { get; set; } = string.Empty;

    /// <summary>
    /// Optional TID / chip serial. Unlike the EPC this cannot be rewritten, so it is the
    /// strongest available signal that a tag is genuine rather than cloned.
    /// </summary>
    public string? TagUid { get; set; }

    /// <summary>Human-readable number printed on the card.</summary>
    public string? CardNumber { get; set; }

    public RfidTagStatus Status { get; set; } = RfidTagStatus.Unassigned;

    // Exactly one holder is set. Nullable FKs rather than a polymorphic type column, so
    // the database itself can enforce each relationship.
    public int? StudentId { get; set; }
    public Student? Student { get; set; }
    public int? TeacherId { get; set; }
    public Teacher? Teacher { get; set; }
    public int? StaffMemberId { get; set; }
    public StaffMember? StaffMember { get; set; }

    public DateTime? IssuedAtUtc { get; set; }
    public int? IssuedByUserId { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
    public int? RevokedByUserId { get; set; }
    public string? RevokedReason { get; set; }

    /// <summary>Set when this tag replaced a lost or damaged one, preserving the chain.</summary>
    public int? ReplacesTagId { get; set; }

    public DateTime? LastSeenAtUtc { get; set; }
    public int? LastSeenLocationId { get; set; }

    public bool IsAssigned => StudentId is not null || TeacherId is not null || StaffMemberId is not null;
    public bool IsUsable => Status == RfidTagStatus.Active && IsAssigned && !IsDeleted;
}
