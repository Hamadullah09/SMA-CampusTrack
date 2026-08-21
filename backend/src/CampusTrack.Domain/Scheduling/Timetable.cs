using CampusTrack.Domain.Academics;
using CampusTrack.Domain.Common;
using CampusTrack.Domain.Facilities;
using CampusTrack.Domain.People;

namespace CampusTrack.Domain.Scheduling;

/// <summary>
/// A named bell-period, e.g. "Period 3" 10:00–10:45, or "Morning Break".
/// Periods are defined once per session so every section shares the same bell times.
/// </summary>
public class TimetablePeriod : TenantEntity<int>
{
    public int AcademicSessionId { get; set; }
    public AcademicSession? AcademicSession { get; set; }

    public string Name { get; set; } = string.Empty;
    public int Sequence { get; set; }
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
    /// <summary>Breaks hold no lesson and are excluded from attendance expectations.</summary>
    public bool IsBreak { get; set; }

    public int DurationMinutes => (int)(EndTime - StartTime).TotalMinutes;
}

/// <summary>
/// One recurring lesson: this section studies this subject with this teacher in this room,
/// on this weekday, during this period. The RFID engine reads these rows to decide which
/// class a student was expected to be in when their tag was seen.
/// </summary>
public class TimetableSlot : TenantEntity<int>
{
    public int AcademicSessionId { get; set; }
    public AcademicSession? AcademicSession { get; set; }
    public int SectionId { get; set; }
    public Section? Section { get; set; }
    public int SubjectId { get; set; }
    public Subject? Subject { get; set; }
    public int? TeacherId { get; set; }
    public Teacher? Teacher { get; set; }
    public int? ClassroomId { get; set; }
    public Classroom? Classroom { get; set; }
    public int TimetablePeriodId { get; set; }
    public TimetablePeriod? TimetablePeriod { get; set; }

    /// <summary>ISO-8601 day number: 1 = Monday … 7 = Sunday.</summary>
    public int DayOfWeek { get; set; }

    /// <summary>Denormalised from the period so attendance maths avoids a join per event.</summary>
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }

    /// <summary>Timetables change mid-year; these bound when this row applies.</summary>
    public DateOnly? EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }

    public bool IsActive { get; set; } = true;
    public string? Notes { get; set; }
}

/// <summary>A non-teaching day. Attendance is not expected and is reported as Holiday.</summary>
public class SchoolHoliday : TenantEntity<int>
{
    public int AcademicSessionId { get; set; }
    public AcademicSession? AcademicSession { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public string? Description { get; set; }
    /// <summary>Staff-only closure (students off, teachers in) vs. full closure.</summary>
    public bool AppliesToStaff { get; set; } = true;
}
