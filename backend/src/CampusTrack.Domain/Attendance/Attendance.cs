using CampusTrack.Domain.Academics;
using CampusTrack.Domain.Common;
using CampusTrack.Domain.Enums;
using CampusTrack.Domain.Facilities;
using CampusTrack.Domain.People;
using CampusTrack.Domain.Rfid;
using CampusTrack.Domain.Scheduling;

namespace CampusTrack.Domain.Attendance;

/// <summary>
/// One row per student per school day: the whole-campus view a guardian asks about.
/// Derived from gate movements, then optionally corrected by a human - the row always
/// records which of the two it currently reflects.
/// </summary>
public class DailyAttendance : TenantEntity<long>
{
    public int StudentId { get; set; }
    public Student? Student { get; set; }
    public DateOnly Date { get; set; }
    public int AcademicSessionId { get; set; }

    /// <summary>Section at the time of the record - keeps history correct after a transfer.</summary>
    public int? SectionId { get; set; }
    public Section? Section { get; set; }

    public AttendanceStatus Status { get; set; } = AttendanceStatus.NotRecorded;

    public DateTime? FirstEntryAtUtc { get; set; }
    public DateTime? LastExitAtUtc { get; set; }
    public int? MinutesOnCampus { get; set; }

    /// <summary>Minutes past the late threshold; 0 when the student arrived on time.</summary>
    public int LateMinutes { get; set; }
    /// <summary>Minutes the student left before the school day ended.</summary>
    public int EarlyLeaveMinutes { get; set; }

    public int ScheduledSessions { get; set; }
    public int AttendedSessions { get; set; }
    public int MissedSessions { get; set; }

    public EventSource Source { get; set; } = EventSource.Rfid;

    /// <summary>True once a human overrode the RFID-derived value.</summary>
    public bool IsManuallyAdjusted { get; set; }
    public string? Remarks { get; set; }

    /// <summary>Set when an approved leave request covers this day.</summary>
    public int? LeaveRequestId { get; set; }
}

/// <summary>
/// One row per student per timetabled lesson: the view a subject teacher grades against.
/// The RFID engine writes it from classroom movements; teachers can correct it, and every
/// correction leaves an <see cref="AttendanceCorrection"/> behind.
/// </summary>
public class SessionAttendance : TenantEntity<long>
{
    public int StudentId { get; set; }
    public Student? Student { get; set; }
    public DateOnly Date { get; set; }

    public int TimetableSlotId { get; set; }
    public TimetableSlot? TimetableSlot { get; set; }
    public int SubjectId { get; set; }
    public Subject? Subject { get; set; }
    public int SectionId { get; set; }
    public Section? Section { get; set; }
    public int? TeacherId { get; set; }
    public Teacher? Teacher { get; set; }

    public AttendanceStatus Status { get; set; } = AttendanceStatus.NotRecorded;

    public DateTime? EnteredAtUtc { get; set; }
    public DateTime? LeftAtUtc { get; set; }
    public int? MinutesPresent { get; set; }
    public int LateMinutes { get; set; }
    public int EarlyLeaveMinutes { get; set; }

    public EventSource Source { get; set; } = EventSource.Rfid;
    public bool IsManuallyAdjusted { get; set; }
    public int? MarkedByUserId { get; set; }
    public DateTime? MarkedAtUtc { get; set; }
    public string? Remarks { get; set; }
}

/// <summary>
/// An interval a student spent inside a monitored room, opened by an entry event and
/// closed by the matching exit. Open intervals answer "where is this student right now";
/// closed ones drive dwell-time reporting and the daily timeline.
/// </summary>
public class ClassroomPresence : TenantEntity<long>
{
    public int StudentId { get; set; }
    public Student? Student { get; set; }

    public int LocationId { get; set; }
    public RfidLocation? Location { get; set; }
    public int? ClassroomId { get; set; }
    public Classroom? Classroom { get; set; }

    public DateOnly Date { get; set; }
    public DateTime EnteredAtUtc { get; set; }
    public DateTime? ExitedAtUtc { get; set; }
    public int? DurationMinutes { get; set; }

    public long? EntryEventId { get; set; }
    public long? ExitEventId { get; set; }

    public int? TimetableSlotId { get; set; }
    public int? SubjectId { get; set; }

    /// <summary>An interval closed by the end-of-day sweep rather than by a real exit read.</summary>
    public bool ClosedBySystem { get; set; }

    public bool IsOpen => ExitedAtUtc is null;
}

/// <summary>
/// Current campus state per student, kept as a single mutable row so "who is inside right
/// now" is an index lookup rather than a scan over the event history.
/// </summary>
public class StudentPresence : TenantEntity<int>
{
    public int StudentId { get; set; }
    public Student? Student { get; set; }

    public PresenceState State { get; set; } = PresenceState.Outside;
    public DateTime? SinceUtc { get; set; }

    public int? CurrentLocationId { get; set; }
    public RfidLocation? CurrentLocation { get; set; }

    public DateTime? LastEntryAtUtc { get; set; }
    public DateTime? LastExitAtUtc { get; set; }
    public long? LastEventId { get; set; }
}

/// <summary>
/// The audit trail behind every manual attendance change: who changed what, from what,
/// to what, and why. Required for any dispute between a school and a guardian.
/// </summary>
public class AttendanceCorrection : TenantEntity<long>
{
    public int StudentId { get; set; }
    public Student? Student { get; set; }
    public DateOnly Date { get; set; }

    /// <summary>"Daily" or "Session" - which record was corrected.</summary>
    public string RecordType { get; set; } = "Daily";
    public long RecordId { get; set; }
    public int? TimetableSlotId { get; set; }

    public AttendanceStatus OldStatus { get; set; }
    public AttendanceStatus NewStatus { get; set; }
    public string Reason { get; set; } = string.Empty;

    public int CorrectedByUserId { get; set; }
    public DateTime CorrectedAtUtc { get; set; }
    public string? IpAddress { get; set; }
}
