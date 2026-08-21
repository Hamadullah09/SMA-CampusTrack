using CampusTrack.Domain.Common;
using CampusTrack.Domain.Enums;
using CampusTrack.Domain.Identity;

namespace CampusTrack.Domain.People;

/// <summary>
/// A parent or authorised guardian. The link to students is many-to-many on purpose:
/// one guardian can follow several children, and a child can have several guardians
/// (both parents, a grandparent, a driver authorised only for pickup).
/// </summary>
public class Guardian : TenantEntity<int>
{
    public int UserId { get; set; }
    public ApplicationUser? User { get; set; }

    public string GuardianCode { get; set; } = string.Empty;
    public string? Occupation { get; set; }
    public string? WorkplacePhone { get; set; }
    public string? AlternatePhone { get; set; }
    public PersonStatus Status { get; set; } = PersonStatus.Active;

    public ICollection<GuardianStudent> Students { get; set; } = new List<GuardianStudent>();
}

/// <summary>Join between a guardian and a child, carrying the rights that link grants.</summary>
public class GuardianStudent
{
    public int Id { get; set; }

    public int GuardianId { get; set; }
    public Guardian? Guardian { get; set; }
    public int StudentId { get; set; }
    public Student? Student { get; set; }

    public GuardianRelationship Relationship { get; set; } = GuardianRelationship.Parent;

    /// <summary>The contact the school calls first for this child.</summary>
    public bool IsPrimaryContact { get; set; }
    /// <summary>May collect the child from school.</summary>
    public bool IsAuthorisedForPickup { get; set; } = true;
    /// <summary>Receives movement/attendance notifications for this child.</summary>
    public bool ReceivesNotifications { get; set; } = true;
    /// <summary>May see academic records (grades, submissions) for this child.</summary>
    public bool CanViewAcademics { get; set; } = true;

    /// <summary>A guardian link is only live once the school confirms it.</summary>
    public bool IsApproved { get; set; }
    public DateTime? ApprovedAtUtc { get; set; }
    public int? ApprovedByUserId { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public int? CreatedByUserId { get; set; }
    public bool IsDeleted { get; set; }
}
