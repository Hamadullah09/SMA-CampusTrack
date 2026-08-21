using CampusTrack.Domain.Academics;
using CampusTrack.Domain.Common;
using CampusTrack.Domain.Enums;
using CampusTrack.Domain.Identity;

namespace CampusTrack.Domain.People;

public class Teacher : TenantEntity<int>
{
    public int UserId { get; set; }
    public ApplicationUser? User { get; set; }

    public string TeacherCode { get; set; } = string.Empty;
    public string? Qualification { get; set; }
    public string? Specialisation { get; set; }
    public DateOnly? HireDate { get; set; }
    public PersonStatus Status { get; set; } = PersonStatus.Active;
    public string? OfficeLocation { get; set; }

    public ICollection<TeachingAssignment> TeachingAssignments { get; set; } = new List<TeachingAssignment>();
    /// <summary>Sections where this teacher is the form/class teacher.</summary>
    public ICollection<Section> HomeroomSections { get; set; } = new List<Section>();
}

public class StaffMember : TenantEntity<int>
{
    public int UserId { get; set; }
    public ApplicationUser? User { get; set; }

    public string StaffCode { get; set; } = string.Empty;
    public string JobTitle { get; set; } = string.Empty;
    public string? Department { get; set; }
    public DateOnly? HireDate { get; set; }
    public PersonStatus Status { get; set; } = PersonStatus.Active;
}
