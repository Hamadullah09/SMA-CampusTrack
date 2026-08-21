using CampusTrack.Domain.Common;
using CampusTrack.Domain.Enums;
using CampusTrack.Domain.People;

namespace CampusTrack.Domain.Academics;

/// <summary>An academic year (or semester) — the time box every timetable, enrollment and grade hangs from.</summary>
public class AcademicSession : TenantEntity<int>
{
    public string Name { get; set; } = string.Empty;          // "2026/2027"
    public string Code { get; set; } = string.Empty;          // "AY2627"
    public TermType TermType { get; set; } = TermType.FullYear;
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public AcademicSessionStatus Status { get; set; } = AcademicSessionStatus.Planned;
    /// <summary>Exactly one session is current; new attendance and enrollment default to it.</summary>
    public bool IsCurrent { get; set; }

    public ICollection<Term> Terms { get; set; } = new List<Term>();
}

/// <summary>A subdivision of a session (Term 1, Semester 2) used for report cards.</summary>
public class Term : TenantEntity<int>
{
    public int AcademicSessionId { get; set; }
    public AcademicSession? AcademicSession { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Sequence { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public bool IsCurrent { get; set; }
}

/// <summary>A programme of study spanning several years, e.g. "Science Stream".</summary>
public class Course : TenantEntity<int>
{
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int DurationYears { get; set; } = 1;
    public bool IsActive { get; set; } = true;

    public ICollection<CourseSubject> CourseSubjects { get; set; } = new List<CourseSubject>();
    public ICollection<SchoolClass> Classes { get; set; } = new List<SchoolClass>();
}

/// <summary>A grade level, e.g. "Grade 7". Holds one or more sections.</summary>
public class SchoolClass : TenantEntity<int>
{
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    /// <summary>Numeric level used for ordering and progression rules.</summary>
    public int Level { get; set; }
    public int? CourseId { get; set; }
    public Course? Course { get; set; }
    public bool IsActive { get; set; } = true;

    public ICollection<Section> Sections { get; set; } = new List<Section>();
}

/// <summary>A concrete group of students taught together, e.g. "Grade 7 - B".</summary>
public class Section : TenantEntity<int>
{
    public int SchoolClassId { get; set; }
    public SchoolClass? SchoolClass { get; set; }

    public string Name { get; set; } = string.Empty;          // "B"
    public string DisplayName { get; set; } = string.Empty;   // "Grade 7 - B"
    public int Capacity { get; set; } = 40;

    /// <summary>Form teacher — sees this section's pastoral data by default.</summary>
    public int? HomeroomTeacherId { get; set; }
    public Teacher? HomeroomTeacher { get; set; }

    /// <summary>Default room; individual timetable slots may override it.</summary>
    public int? DefaultClassroomId { get; set; }
    public Facilities.Classroom? DefaultClassroom { get; set; }

    public bool IsActive { get; set; } = true;

    public ICollection<Enrollment> Enrollments { get; set; } = new List<Enrollment>();
    public ICollection<TeachingAssignment> TeachingAssignments { get; set; } = new List<TeachingAssignment>();
}

public class Subject : TenantEntity<int>
{
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int Credits { get; set; } = 1;
    /// <summary>Expected sessions across the year — the denominator for subject attendance %.</summary>
    public int TotalPlannedClasses { get; set; }
    public bool IsElective { get; set; }
    public string? ColourHex { get; set; }
    public bool IsActive { get; set; } = true;

    public ICollection<CourseSubject> CourseSubjects { get; set; } = new List<CourseSubject>();
}

public class CourseSubject
{
    public int CourseId { get; set; }
    public Course? Course { get; set; }
    public int SubjectId { get; set; }
    public Subject? Subject { get; set; }
    /// <summary>Which year of the course this subject belongs to.</summary>
    public int YearLevel { get; set; } = 1;
    public bool IsMandatory { get; set; } = true;
}

/// <summary>A student's membership of a section for one session.</summary>
public class Enrollment : TenantEntity<int>
{
    public int StudentId { get; set; }
    public Student? Student { get; set; }
    public int SectionId { get; set; }
    public Section? Section { get; set; }
    public int AcademicSessionId { get; set; }
    public AcademicSession? AcademicSession { get; set; }

    public string? RollNumber { get; set; }
    public DateOnly EnrolledOn { get; set; }
    public DateOnly? EndedOn { get; set; }
    public EnrollmentStatus Status { get; set; } = EnrollmentStatus.Active;
    public string? Notes { get; set; }
}

/// <summary>
/// Who teaches what, to whom, for a session. This is the row that scopes a teacher's
/// entire portal: their classes, their students, their gradebook.
/// </summary>
public class TeachingAssignment : TenantEntity<int>
{
    public int TeacherId { get; set; }
    public Teacher? Teacher { get; set; }
    public int SectionId { get; set; }
    public Section? Section { get; set; }
    public int SubjectId { get; set; }
    public Subject? Subject { get; set; }
    public int AcademicSessionId { get; set; }
    public AcademicSession? AcademicSession { get; set; }

    /// <summary>Lead teacher for the subject in this section (vs. assistant/substitute).</summary>
    public bool IsPrimary { get; set; } = true;
    public DateOnly? StartsOn { get; set; }
    public DateOnly? EndsOn { get; set; }
    public bool IsActive { get; set; } = true;
}
