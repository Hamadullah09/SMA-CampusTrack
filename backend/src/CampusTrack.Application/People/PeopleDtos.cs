using System.ComponentModel.DataAnnotations;
using CampusTrack.Application.Common.Models;
using CampusTrack.Domain.Enums;

namespace CampusTrack.Application.People;

public class PersonQuery : PagedQuery
{
    public PersonStatus? Status { get; set; }
    public int? SectionId { get; set; }
    public int? SchoolClassId { get; set; }
    public bool? HasRfidTag { get; set; }
}

public record StudentListItem
{
    public int Id { get; init; }
    public string StudentCode { get; init; } = string.Empty;
    public string FullName { get; init; } = string.Empty;
    public string? Email { get; init; }
    public string? PhoneNumber { get; init; }
    public string? PhotoUrl { get; init; }
    public Gender Gender { get; init; }
    public DateOnly? DateOfBirth { get; init; }
    public PersonStatus Status { get; init; }
    public int? SectionId { get; init; }
    public string? SectionName { get; init; }
    public string? ClassName { get; init; }
    public string? RollNumber { get; init; }

    /// <summary>Masked for list views; the full value is only on the detail screen.</summary>
    public string? RfidCard { get; init; }
    public bool HasActiveCard { get; init; }

    /// <summary>Live campus state, so a list can show who is in the building.</summary>
    public PresenceState PresenceState { get; init; }
    public decimal? AttendancePercentage { get; init; }
    public int GuardianCount { get; init; }
}

public record StudentDetail : StudentListItem
{
    public string? AdmissionNumber { get; init; }
    public DateOnly? AdmissionDate { get; init; }
    public string? Address { get; init; }
    public string? City { get; init; }
    public string? BloodGroup { get; init; }
    public string? MedicalNotes { get; init; }
    public string? EmergencyContactName { get; init; }
    public string? EmergencyContactPhone { get; init; }
    public string? TransportRoute { get; init; }
    public string? NationalId { get; init; }
    public int UserId { get; init; }
    public bool IsAccountActive { get; init; }
    public DateTime? LastLoginAtUtc { get; init; }
    public IReadOnlyList<GuardianLink> Guardians { get; init; } = [];
    public IReadOnlyList<EnrollmentSummary> Enrollments { get; init; } = [];
    public IReadOnlyList<TagSummary> Cards { get; init; } = [];
}

public record GuardianLink
{
    public int GuardianId { get; init; }
    public int LinkId { get; init; }
    public string FullName { get; init; } = string.Empty;
    public string? PhoneNumber { get; init; }
    public string? Email { get; init; }
    public GuardianRelationship Relationship { get; init; }
    public bool IsPrimaryContact { get; init; }
    public bool IsAuthorisedForPickup { get; init; }
    public bool ReceivesNotifications { get; init; }
    public bool CanViewAcademics { get; init; }
    public bool IsApproved { get; init; }
}

public record EnrollmentSummary
{
    public int Id { get; init; }
    public int SectionId { get; init; }
    public string SectionName { get; init; } = string.Empty;
    public string SessionName { get; init; } = string.Empty;
    public string? RollNumber { get; init; }
    public DateOnly EnrolledOn { get; init; }
    public EnrollmentStatus Status { get; init; }
}

public record TagSummary
{
    public int Id { get; init; }
    public string Epc { get; init; } = string.Empty;
    public string? CardNumber { get; init; }
    public RfidTagStatus Status { get; init; }
    public DateTime? IssuedAtUtc { get; init; }
    public DateTime? LastSeenAtUtc { get; init; }
    public string? LastSeenLocation { get; init; }
}

/// <summary>
/// Creates a student and their sign-in account together. The account is always created:
/// a student without one cannot be handed the mobile app later without a second workflow.
/// </summary>
public record CreateStudentRequest
{
    [Required, MaxLength(80)] public string FirstName { get; init; } = string.Empty;
    [Required, MaxLength(80)] public string LastName { get; init; } = string.Empty;

    /// <summary>Left empty, a code is generated from the configured prefix and the next number.</summary>
    public string? StudentCode { get; init; }
    public string? AdmissionNumber { get; init; }
    public DateOnly? AdmissionDate { get; init; }

    [EmailAddress] public string? Email { get; init; }
    public string? PhoneNumber { get; init; }
    public Gender Gender { get; init; } = Gender.Unspecified;
    public DateOnly? DateOfBirth { get; init; }
    public string? NationalId { get; init; }
    public string? Address { get; init; }
    public string? City { get; init; }

    public string? BloodGroup { get; init; }
    public string? MedicalNotes { get; init; }
    public string? EmergencyContactName { get; init; }
    public string? EmergencyContactPhone { get; init; }
    public string? TransportRoute { get; init; }

    public int? SectionId { get; init; }
    public string? RollNumber { get; init; }

    /// <summary>Assigns an RFID card during enrolment, saving a second trip to the office.</summary>
    public string? RfidEpc { get; init; }
    public string? CardNumber { get; init; }

    /// <summary>Sign-in name. Defaults to a slug of the student's name plus their code.</summary>
    public string? UserName { get; init; }
    /// <summary>Omitted, a temporary password is generated and returned once.</summary>
    public string? Password { get; init; }
    public PersonStatus Status { get; init; } = PersonStatus.Active;
}

public record UpdateStudentRequest
{
    [Required, MaxLength(80)] public string FirstName { get; init; } = string.Empty;
    [Required, MaxLength(80)] public string LastName { get; init; } = string.Empty;
    [EmailAddress] public string? Email { get; init; }
    public string? PhoneNumber { get; init; }
    public Gender Gender { get; init; }
    public DateOnly? DateOfBirth { get; init; }
    public string? NationalId { get; init; }
    public string? Address { get; init; }
    public string? City { get; init; }
    public string? BloodGroup { get; init; }
    public string? MedicalNotes { get; init; }
    public string? EmergencyContactName { get; init; }
    public string? EmergencyContactPhone { get; init; }
    public string? TransportRoute { get; init; }
    public string? AdmissionNumber { get; init; }
    public DateOnly? AdmissionDate { get; init; }
    public PersonStatus Status { get; init; }
    public int? SectionId { get; init; }
}

public record CreatedPersonResult
{
    public int Id { get; init; }
    public int UserId { get; init; }
    public string Code { get; init; } = string.Empty;
    public string UserName { get; init; } = string.Empty;
    /// <summary>Returned once when the system generated the password. Never stored in clear.</summary>
    public string? TemporaryPassword { get; init; }
}

// ------------------------------------------------------------------ teachers ----

public record TeacherListItem
{
    public int Id { get; init; }
    public string TeacherCode { get; init; } = string.Empty;
    public string FullName { get; init; } = string.Empty;
    public string? Email { get; init; }
    public string? PhoneNumber { get; init; }
    public string? PhotoUrl { get; init; }
    public string? Qualification { get; init; }
    public string? Specialisation { get; init; }
    public DateOnly? HireDate { get; init; }
    public PersonStatus Status { get; init; }
    public int SectionCount { get; init; }
    public int SubjectCount { get; init; }
    public IReadOnlyList<string> Subjects { get; init; } = [];
}

public record CreateTeacherRequest
{
    [Required, MaxLength(80)] public string FirstName { get; init; } = string.Empty;
    [Required, MaxLength(80)] public string LastName { get; init; } = string.Empty;
    public string? TeacherCode { get; init; }
    [EmailAddress] public string? Email { get; init; }
    public string? PhoneNumber { get; init; }
    public Gender Gender { get; init; } = Gender.Unspecified;
    public DateOnly? DateOfBirth { get; init; }
    public string? Qualification { get; init; }
    public string? Specialisation { get; init; }
    public DateOnly? HireDate { get; init; }
    public string? OfficeLocation { get; init; }
    public string? Address { get; init; }
    public string? UserName { get; init; }
    public string? Password { get; init; }
    public PersonStatus Status { get; init; } = PersonStatus.Active;
}

// ----------------------------------------------------------------- guardians ----

public record GuardianListItem
{
    public int Id { get; init; }
    public string GuardianCode { get; init; } = string.Empty;
    public string FullName { get; init; } = string.Empty;
    public string? Email { get; init; }
    public string? PhoneNumber { get; init; }
    public string? AlternatePhone { get; init; }
    public string? Occupation { get; init; }
    public PersonStatus Status { get; init; }
    public int ChildCount { get; init; }
    public IReadOnlyList<string> Children { get; init; } = [];
    public bool HasPendingLinks { get; init; }
}

public record CreateGuardianRequest
{
    [Required, MaxLength(80)] public string FirstName { get; init; } = string.Empty;
    [Required, MaxLength(80)] public string LastName { get; init; } = string.Empty;
    public string? GuardianCode { get; init; }
    [EmailAddress] public string? Email { get; init; }
    public string? PhoneNumber { get; init; }
    public string? AlternatePhone { get; init; }
    public string? Occupation { get; init; }
    public string? WorkplacePhone { get; init; }
    public string? Address { get; init; }
    public Gender Gender { get; init; } = Gender.Unspecified;
    public string? UserName { get; init; }
    public string? Password { get; init; }

    /// <summary>Children to link at creation. Links are approved immediately when an
    /// administrator creates them, since the school is the authority on the relationship.</summary>
    public List<LinkChildRequest>? Children { get; init; }
}

public record LinkChildRequest
{
    [Required] public int StudentId { get; init; }
    public GuardianRelationship Relationship { get; init; } = GuardianRelationship.Parent;
    public bool IsPrimaryContact { get; init; }
    public bool IsAuthorisedForPickup { get; init; } = true;
    public bool ReceivesNotifications { get; init; } = true;
    public bool CanViewAcademics { get; init; } = true;
}
