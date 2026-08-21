using CampusTrack.Domain.Academics;
using CampusTrack.Domain.Attendance;
using CampusTrack.Domain.Common;
using CampusTrack.Domain.Enums;
using CampusTrack.Domain.Identity;
using CampusTrack.Domain.Rfid;

namespace CampusTrack.Domain.People;

/// <summary>
/// A learner. <see cref="StudentCode"/> (e.g. STU-000123) is the identity shown to
/// humans and printed on cards; the RFID EPC is deliberately kept out of this record
/// and lives on <see cref="RfidTag"/>, so a tag can be replaced without touching the
/// student and an EPC is never treated as a primary identity.
/// </summary>
public class Student : TenantEntity<int>
{
    public int UserId { get; set; }
    public ApplicationUser? User { get; set; }

    public string StudentCode { get; set; } = string.Empty;
    public string? AdmissionNumber { get; set; }
    public DateOnly? AdmissionDate { get; set; }
    public PersonStatus Status { get; set; } = PersonStatus.Pending;

    public string? BloodGroup { get; set; }
    public string? MedicalNotes { get; set; }
    public string? EmergencyContactName { get; set; }
    public string? EmergencyContactPhone { get; set; }
    public string? TransportRoute { get; set; }

    /// <summary>Denormalised pointer to the student's current section, kept in step with
    /// the active <see cref="Enrollment"/>. Saves a join on nearly every screen.</summary>
    public int? CurrentSectionId { get; set; }
    public Section? CurrentSection { get; set; }

    public ICollection<Enrollment> Enrollments { get; set; } = new List<Enrollment>();
    public ICollection<GuardianStudent> Guardians { get; set; } = new List<GuardianStudent>();
    public ICollection<RfidTag> RfidTags { get; set; } = new List<RfidTag>();
    public ICollection<DailyAttendance> DailyAttendances { get; set; } = new List<DailyAttendance>();
}
