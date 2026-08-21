using CampusTrack.Application.Common;
using CampusTrack.Application.Common.Interfaces;
using CampusTrack.Application.Rfid;
using CampusTrack.Domain.Enums;
using CampusTrack.Infrastructure.Persistence;
using CampusTrack.Infrastructure.Rfid;
using Microsoft.EntityFrameworkCore;

namespace CampusTrack.Infrastructure.Dashboards;

public record AdminDashboard
{
    public int TotalStudents { get; init; }
    public int TotalTeachers { get; init; }
    public int TotalGuardians { get; init; }
    public int TotalStaff { get; init; }

    public int StudentsOnCampus { get; init; }
    public int StudentsOffsite { get; init; }
    public int StudentsInRooms { get; init; }

    public int PresentToday { get; init; }
    public int AbsentToday { get; init; }
    public int LateToday { get; init; }
    public decimal AttendanceRateToday { get; init; }

    public int ReadersTotal { get; init; }
    public int ReadersOnline { get; init; }
    public int ReadersOffline { get; init; }
    public int UnassignedCards { get; init; }

    public int EventsToday { get; init; }
    public int UnknownTagReadsToday { get; init; }
    public int PendingDeadLetters { get; init; }
    public int PendingGuardianLinks { get; init; }

    public IReadOnlyList<RfidEventDto> RecentEvents { get; init; } = [];
    public IReadOnlyList<ReaderStatusDto> Readers { get; init; } = [];
    public IReadOnlyList<AttendanceTrendPoint> AttendanceTrend { get; init; } = [];
    public IReadOnlyList<HourlyFlowPoint> ArrivalFlow { get; init; } = [];
    public IReadOnlyList<DashboardAlert> Alerts { get; init; } = [];
}

public record AttendanceTrendPoint(DateOnly Date, int Present, int Absent, int Late, decimal Percentage);
public record HourlyFlowPoint(int Hour, int Entries, int Exits);
public record DashboardAlert(string Severity, string Title, string Message, string? Link);

public record TeacherDashboard
{
    public int TeacherId { get; init; }
    public string TeacherName { get; init; } = string.Empty;
    public int SectionCount { get; init; }
    public int StudentCount { get; init; }
    public int SubjectCount { get; init; }
    public IReadOnlyList<TodayLesson> TodayLessons { get; init; } = [];
    public int PendingSubmissions { get; init; }
    public int PendingQuizGrading { get; init; }
    public int AssignmentsDueThisWeek { get; init; }
    public decimal AverageAttendance { get; init; }
    public IReadOnlyList<StudentAtRisk> StudentsAtRisk { get; init; } = [];
    public int UnreadNotifications { get; init; }
}

public record TodayLesson
{
    public int SlotId { get; init; }
    public string SubjectName { get; init; } = string.Empty;
    public string SectionName { get; init; } = string.Empty;
    public string? ClassroomName { get; init; }
    public TimeOnly StartTime { get; init; }
    public TimeOnly EndTime { get; init; }
    public int StudentCount { get; init; }
    public bool AttendanceTaken { get; init; }
    public bool IsInProgress { get; init; }
    public bool IsMonitored { get; init; }
}

public record StudentAtRisk(int StudentId, string StudentName, string? SectionName, decimal AttendancePercentage, string Reason);

public record StudentDashboard
{
    public int StudentId { get; init; }
    public string StudentName { get; init; } = string.Empty;
    public string StudentCode { get; init; } = string.Empty;
    public string? SectionName { get; init; }
    public PresenceState PresenceState { get; init; }
    public decimal AttendancePercentage { get; init; }
    public IReadOnlyList<TodayLesson> TodayLessons { get; init; } = [];
    public int UpcomingAssignments { get; init; }
    public int OverdueAssignments { get; init; }
    public int UpcomingQuizzes { get; init; }
    public int UnreadNotifications { get; init; }
    public IReadOnlyList<RecentGrade> RecentGrades { get; init; } = [];
    public bool HasActiveCard { get; init; }
}

public record RecentGrade(string Subject, string Title, decimal Score, decimal MaxScore, decimal Percentage, string? Letter, DateOnly RecordedOn);

public record GuardianDashboard
{
    public int GuardianId { get; init; }
    public IReadOnlyList<ChildSummary> Children { get; init; } = [];
    public int UnreadNotifications { get; init; }
}

public record ChildSummary
{
    public int StudentId { get; init; }
    public string StudentName { get; init; } = string.Empty;
    public string StudentCode { get; init; } = string.Empty;
    public string? PhotoUrl { get; init; }
    public string? SectionName { get; init; }
    public PresenceState PresenceState { get; init; }
    public string PresenceLabel { get; init; } = string.Empty;
    public DateTime? LastEntryAtUtc { get; init; }
    public DateTime? LastExitAtUtc { get; init; }
    public string? CurrentLocation { get; init; }
    public AttendanceStatus TodayStatus { get; init; }
    public decimal AttendancePercentage { get; init; }
    public int UpcomingAssignments { get; init; }
    public IReadOnlyList<RecentGrade> RecentGrades { get; init; } = [];
    public bool CanViewAcademics { get; init; }
}

public interface IDashboardService
{
    Task<AdminDashboard> GetAdminAsync(CancellationToken ct = default);
    Task<TeacherDashboard> GetTeacherAsync(int teacherId, CancellationToken ct = default);
    Task<StudentDashboard> GetStudentAsync(int studentId, CancellationToken ct = default);
    Task<GuardianDashboard> GetGuardianAsync(int guardianId, CancellationToken ct = default);
}

/// <summary>
/// Assembles each portal's landing screen.
///
/// Dashboards are read-heavy and run on every sign-in, so each figure is a scalar aggregate
/// rather than a materialised list that is then counted in memory. The admin view in
/// particular answers the questions a head teacher asks first: who is in the building, is
/// anything broken, and is attendance where it should be.
/// </summary>
public class DashboardService : IDashboardService
{
    private readonly CampusTrackDbContext _db;
    private readonly IRfidQueryService _rfid;
    private readonly IDateTimeProvider _clock;
    private readonly ISettingsProvider _settings;

    public DashboardService(
        CampusTrackDbContext db,
        IRfidQueryService rfid,
        IDateTimeProvider clock,
        ISettingsProvider settings)
    {
        _db = db;
        _rfid = rfid;
        _clock = clock;
        _settings = settings;
    }

    public async Task<AdminDashboard> GetAdminAsync(CancellationToken ct = default)
    {
        var today = _clock.SchoolToday;
        var now = _clock.UtcNow;

        var presence = await _rfid.GetPresenceSummaryAsync(ct);
        var readers = await _rfid.GetReaderStatusAsync(ct);
        var recentEvents = await _rfid.GetRecentAsync(15, ct);

        var attendanceToday = await _db.DailyAttendances.AsNoTracking()
            .Where(a => a.Date == today)
            .GroupBy(a => 1)
            .Select(g => new
            {
                Present = g.Count(a => a.Status == AttendanceStatus.Present
                                       || a.Status == AttendanceStatus.EarlyLeave
                                       || a.Status == AttendanceStatus.Partial),
                Late = g.Count(a => a.Status == AttendanceStatus.Late),
                Absent = g.Count(a => a.Status == AttendanceStatus.Absent),
                Total = g.Count()
            })
            .FirstOrDefaultAsync(ct);

        var present = (attendanceToday?.Present ?? 0) + (attendanceToday?.Late ?? 0);
        var totalRecorded = attendanceToday?.Total ?? 0;

        var trend = await BuildAttendanceTrendAsync(today.AddDays(-13), today, ct);
        var flow = await BuildArrivalFlowAsync(today, ct);

        var offlineReaders = readers.Count(r => r.Status == ReaderStatus.Offline);
        var unknownReads = await _db.RfidEvents.AsNoTracking()
            .CountAsync(e => e.LocalDate == today && e.EventType == RfidEventType.UnknownTag, ct);
        var deadLetters = await _db.RfidDeadLetters.AsNoTracking().CountAsync(d => !d.IsResolved, ct);
        var pendingLinks = await _db.GuardianStudents.AsNoTracking()
            .CountAsync(g => !g.IsApproved && !g.IsDeleted, ct);

        return new AdminDashboard
        {
            TotalStudents = await _db.Students.AsNoTracking().CountAsync(s => s.Status == PersonStatus.Active, ct),
            TotalTeachers = await _db.Teachers.AsNoTracking().CountAsync(t => t.Status == PersonStatus.Active, ct),
            TotalGuardians = await _db.Guardians.AsNoTracking().CountAsync(g => g.Status == PersonStatus.Active, ct),
            TotalStaff = await _db.StaffMembers.AsNoTracking().CountAsync(s => s.Status == PersonStatus.Active, ct),

            StudentsOnCampus = presence.OnCampus,
            StudentsOffsite = presence.Offsite,
            StudentsInRooms = presence.InRooms,

            PresentToday = present,
            AbsentToday = attendanceToday?.Absent ?? 0,
            LateToday = attendanceToday?.Late ?? 0,
            AttendanceRateToday = totalRecorded == 0 ? 0 : Math.Round(present * 100m / totalRecorded, 1),

            ReadersTotal = readers.Count,
            ReadersOnline = readers.Count(r => r.Status == ReaderStatus.Online),
            ReadersOffline = offlineReaders,
            UnassignedCards = await _db.RfidTags.AsNoTracking()
                .CountAsync(t => t.Status == RfidTagStatus.Unassigned, ct),

            EventsToday = await _db.RfidEvents.AsNoTracking().CountAsync(e => e.LocalDate == today, ct),
            UnknownTagReadsToday = unknownReads,
            PendingDeadLetters = deadLetters,
            PendingGuardianLinks = pendingLinks,

            RecentEvents = recentEvents,
            Readers = readers,
            AttendanceTrend = trend,
            ArrivalFlow = flow,
            Alerts = BuildAlerts(offlineReaders, deadLetters, unknownReads, pendingLinks)
        };
    }

    /// <summary>
    /// Only genuine calls to action. A dashboard that always shows warnings trains its
    /// audience to ignore them, so each alert here corresponds to something a person must do.
    /// </summary>
    private static List<DashboardAlert> BuildAlerts(
        int offlineReaders, int deadLetters, int unknownReads, int pendingLinks)
    {
        var alerts = new List<DashboardAlert>();

        if (offlineReaders > 0)
            alerts.Add(new DashboardAlert("error",
                $"{offlineReaders} reader{(offlineReaders == 1 ? "" : "s")} offline",
                "Movement is not being recorded at these locations. Check power and network.",
                "/rfid/readers"));

        if (deadLetters > 0)
            alerts.Add(new DashboardAlert("warning",
                $"{deadLetters} unprocessed RFID batch{(deadLetters == 1 ? "" : "es")}",
                "These reads could not be processed and are waiting for review.",
                "/rfid/events"));

        if (unknownReads > 5)
            alerts.Add(new DashboardAlert("warning",
                $"{unknownReads} unrecognised cards seen today",
                "Cards were read that are not registered. This may be visitors or unassigned cards.",
                "/rfid/events"));

        if (pendingLinks > 0)
            alerts.Add(new DashboardAlert("info",
                $"{pendingLinks} guardian link{(pendingLinks == 1 ? "" : "s")} awaiting approval",
                "Parents cannot see their child's information until these are approved.",
                "/guardians"));

        return alerts;
    }

    private async Task<List<AttendanceTrendPoint>> BuildAttendanceTrendAsync(
        DateOnly from, DateOnly to, CancellationToken ct)
    {
        var rows = await _db.DailyAttendances.AsNoTracking()
            .Where(a => a.Date >= from && a.Date <= to)
            .GroupBy(a => a.Date)
            .Select(g => new
            {
                Date = g.Key,
                Present = g.Count(a => a.Status == AttendanceStatus.Present
                                       || a.Status == AttendanceStatus.EarlyLeave
                                       || a.Status == AttendanceStatus.Partial),
                Late = g.Count(a => a.Status == AttendanceStatus.Late),
                Absent = g.Count(a => a.Status == AttendanceStatus.Absent),
                Total = g.Count()
            })
            .OrderBy(g => g.Date)
            .ToListAsync(ct);

        return rows.Select(r =>
        {
            var attended = r.Present + r.Late;
            return new AttendanceTrendPoint(r.Date, r.Present, r.Absent, r.Late,
                r.Total == 0 ? 0 : Math.Round(attended * 100m / r.Total, 1));
        }).ToList();
    }

    /// <summary>
    /// Arrivals and departures by hour. Shows the school its own rush: how long the morning
    /// intake actually takes, and whether one gate is carrying all of it.
    /// </summary>
    private async Task<List<HourlyFlowPoint>> BuildArrivalFlowAsync(DateOnly date, CancellationToken ct)
    {
        var events = await _db.RfidEvents.AsNoTracking()
            .Where(e => e.LocalDate == date
                        && (e.EventType == RfidEventType.SchoolEntry || e.EventType == RfidEventType.SchoolExit))
            .Select(e => new { e.OccurredAtUtc, e.EventType })
            .ToListAsync(ct);

        return events
            .GroupBy(e => _clock.ToSchoolTime(e.OccurredAtUtc).Hour)
            .Select(g => new HourlyFlowPoint(
                g.Key,
                g.Count(e => e.EventType == RfidEventType.SchoolEntry),
                g.Count(e => e.EventType == RfidEventType.SchoolExit)))
            .OrderBy(p => p.Hour)
            .ToList();
    }

    public async Task<TeacherDashboard> GetTeacherAsync(int teacherId, CancellationToken ct = default)
    {
        var today = _clock.SchoolToday;
        var isoDay = (int)today.DayOfWeek == 0 ? 7 : (int)today.DayOfWeek;
        var nowLocal = TimeOnly.FromDateTime(_clock.SchoolNow.DateTime);

        var teacher = await _db.Teachers.AsNoTracking()
            .Where(t => t.Id == teacherId)
            .Select(t => new { t.Id, Name = t.User!.FirstName + " " + t.User.LastName, t.UserId })
            .FirstOrDefaultAsync(ct)
            ?? throw new KeyNotFoundException("That teacher does not exist.");

        var sectionIds = await _db.TeachingAssignments.AsNoTracking()
            .Where(a => a.TeacherId == teacherId && a.IsActive)
            .Select(a => a.SectionId).Distinct().ToListAsync(ct);

        var lessons = await _db.TimetableSlots.AsNoTracking()
            .Where(s => s.TeacherId == teacherId && s.DayOfWeek == isoDay && s.IsActive)
            .OrderBy(s => s.StartTime)
            .Select(s => new TodayLesson
            {
                SlotId = s.Id,
                SubjectName = s.Subject!.Name,
                SectionName = s.Section!.DisplayName,
                ClassroomName = s.Classroom!.Name,
                StartTime = s.StartTime,
                EndTime = s.EndTime,
                StudentCount = _db.Students.Count(st => st.CurrentSectionId == s.SectionId),
                AttendanceTaken = _db.SessionAttendances.Any(a => a.TimetableSlotId == s.Id && a.Date == today),
                IsMonitored = s.ClassroomId != null &&
                              _db.RfidLocations.Any(l => l.ClassroomId == s.ClassroomId && l.IsActive)
            })
            .ToListAsync(ct);

        var withProgress = lessons
            .Select(l => l with { IsInProgress = nowLocal >= l.StartTime && nowLocal <= l.EndTime })
            .ToList();

        var required = await _settings.GetAsync(SettingKeys.AttendanceRequiredPercent, 75, ct);
        var atRisk = await BuildAtRiskAsync(sectionIds, required, ct);

        var weekEnd = today.AddDays(7);

        return new TeacherDashboard
        {
            TeacherId = teacherId,
            TeacherName = teacher.Name,
            SectionCount = sectionIds.Count,
            SubjectCount = await _db.TeachingAssignments.AsNoTracking()
                .Where(a => a.TeacherId == teacherId && a.IsActive)
                .Select(a => a.SubjectId).Distinct().CountAsync(ct),
            StudentCount = await _db.Students.AsNoTracking()
                .CountAsync(s => s.CurrentSectionId != null && sectionIds.Contains(s.CurrentSectionId.Value), ct),
            TodayLessons = withProgress,
            PendingSubmissions = await _db.AssignmentSubmissions.AsNoTracking()
                .CountAsync(s => s.Assignment!.TeacherId == teacherId
                                 && s.Status == SubmissionStatus.Submitted, ct),
            PendingQuizGrading = await _db.QuizAttempts.AsNoTracking()
                .CountAsync(a => a.Quiz!.TeacherId == teacherId
                                 && (a.Status == QuizAttemptStatus.Submitted
                                     || a.Status == QuizAttemptStatus.AutoSubmitted), ct),
            AssignmentsDueThisWeek = await _db.Assignments.AsNoTracking()
                .CountAsync(a => a.TeacherId == teacherId
                                 && a.Status == AssignmentStatus.Published
                                 && a.DueAtUtc >= _clock.UtcNow
                                 && a.DueAtUtc <= weekEnd.ToDateTime(TimeOnly.MaxValue), ct),
            AverageAttendance = await AverageAttendanceAsync(sectionIds, today.AddDays(-30), today, ct),
            StudentsAtRisk = atRisk,
            UnreadNotifications = await _db.Notifications.AsNoTracking()
                .CountAsync(n => n.UserId == teacher.UserId && !n.IsRead, ct)
        };
    }

    private async Task<List<StudentAtRisk>> BuildAtRiskAsync(List<int> sectionIds, int required, CancellationToken ct)
    {
        if (sectionIds.Count == 0) return [];

        var from = _clock.SchoolToday.AddDays(-30);

        var rows = await _db.DailyAttendances.AsNoTracking()
            .Where(a => a.Date >= from
                        && a.SectionId != null && sectionIds.Contains(a.SectionId.Value)
                        && a.Status != AttendanceStatus.Holiday)
            .GroupBy(a => new
            {
                a.StudentId,
                Name = a.Student!.User!.FirstName + " " + a.Student.User.LastName,
                Section = a.Section!.DisplayName
            })
            .Select(g => new
            {
                g.Key.StudentId,
                g.Key.Name,
                g.Key.Section,
                Total = g.Count(),
                Attended = g.Count(a => a.Status == AttendanceStatus.Present
                                        || a.Status == AttendanceStatus.Late
                                        || a.Status == AttendanceStatus.EarlyLeave)
            })
            .ToListAsync(ct);

        return rows
            .Where(r => r.Total >= 5)   // too few days to judge anyone on
            .Select(r => new
            {
                r.StudentId, r.Name, r.Section,
                Percentage = Math.Round(r.Attended * 100m / r.Total, 1)
            })
            .Where(r => r.Percentage < required)
            .OrderBy(r => r.Percentage)
            .Take(10)
            .Select(r => new StudentAtRisk(r.StudentId, r.Name, r.Section, r.Percentage,
                $"Attendance is {r.Percentage}%, below the {required}% requirement."))
            .ToList();
    }

    private async Task<decimal> AverageAttendanceAsync(
        List<int> sectionIds, DateOnly from, DateOnly to, CancellationToken ct)
    {
        if (sectionIds.Count == 0) return 0;

        var totals = await _db.DailyAttendances.AsNoTracking()
            .Where(a => a.Date >= from && a.Date <= to
                        && a.SectionId != null && sectionIds.Contains(a.SectionId.Value)
                        && a.Status != AttendanceStatus.Holiday)
            .GroupBy(a => 1)
            .Select(g => new
            {
                Total = g.Count(),
                Attended = g.Count(a => a.Status == AttendanceStatus.Present
                                        || a.Status == AttendanceStatus.Late
                                        || a.Status == AttendanceStatus.EarlyLeave)
            })
            .FirstOrDefaultAsync(ct);

        return totals is null or { Total: 0 } ? 0 : Math.Round(totals.Attended * 100m / totals.Total, 1);
    }

    public async Task<StudentDashboard> GetStudentAsync(int studentId, CancellationToken ct = default)
    {
        var today = _clock.SchoolToday;
        var isoDay = (int)today.DayOfWeek == 0 ? 7 : (int)today.DayOfWeek;
        var nowLocal = TimeOnly.FromDateTime(_clock.SchoolNow.DateTime);

        var student = await _db.Students.AsNoTracking()
            .Where(s => s.Id == studentId)
            .Select(s => new
            {
                s.Id, s.UserId, s.StudentCode,
                Name = s.User!.FirstName + " " + s.User.LastName,
                s.CurrentSectionId,
                SectionName = s.CurrentSection!.DisplayName,
                HasCard = s.RfidTags.Any(t => t.Status == RfidTagStatus.Active)
            })
            .FirstOrDefaultAsync(ct)
            ?? throw new KeyNotFoundException("That student does not exist.");

        var lessons = student.CurrentSectionId is null
            ? []
            : await _db.TimetableSlots.AsNoTracking()
                .Where(s => s.SectionId == student.CurrentSectionId && s.DayOfWeek == isoDay && s.IsActive)
                .OrderBy(s => s.StartTime)
                .Select(s => new TodayLesson
                {
                    SlotId = s.Id,
                    SubjectName = s.Subject!.Name,
                    SectionName = s.Section!.DisplayName,
                    ClassroomName = s.Classroom!.Name,
                    StartTime = s.StartTime,
                    EndTime = s.EndTime,
                    AttendanceTaken = _db.SessionAttendances
                        .Any(a => a.TimetableSlotId == s.Id && a.Date == today && a.StudentId == studentId)
                })
                .ToListAsync(ct);

        var withProgress = lessons
            .Select(l => l with { IsInProgress = nowLocal >= l.StartTime && nowLocal <= l.EndTime })
            .ToList();

        var attendance = await _db.DailyAttendances.AsNoTracking()
            .Where(a => a.StudentId == studentId && a.Status != AttendanceStatus.Holiday)
            .GroupBy(a => 1)
            .Select(g => new
            {
                Total = g.Count(),
                Attended = g.Count(a => a.Status == AttendanceStatus.Present
                                        || a.Status == AttendanceStatus.Late
                                        || a.Status == AttendanceStatus.EarlyLeave)
            })
            .FirstOrDefaultAsync(ct);

        return new StudentDashboard
        {
            StudentId = studentId,
            StudentName = student.Name,
            StudentCode = student.StudentCode,
            SectionName = student.SectionName,
            HasActiveCard = student.HasCard,
            PresenceState = await _db.StudentPresences.AsNoTracking()
                .Where(p => p.StudentId == studentId).Select(p => p.State).FirstOrDefaultAsync(ct),
            AttendancePercentage = attendance is null or { Total: 0 }
                ? 0
                : Math.Round(attendance.Attended * 100m / attendance.Total, 1),
            TodayLessons = withProgress,
            UpcomingAssignments = await _db.Assignments.AsNoTracking()
                .CountAsync(a => a.SectionId == student.CurrentSectionId
                                 && a.Status == AssignmentStatus.Published
                                 && a.DueAtUtc >= _clock.UtcNow, ct),
            OverdueAssignments = await _db.Assignments.AsNoTracking()
                .CountAsync(a => a.SectionId == student.CurrentSectionId
                                 && a.Status == AssignmentStatus.Published
                                 && a.DueAtUtc < _clock.UtcNow
                                 && !a.Submissions.Any(s => s.StudentId == studentId
                                                            && s.Status != SubmissionStatus.NotSubmitted), ct),
            UpcomingQuizzes = await _db.Quizzes.AsNoTracking()
                .CountAsync(q => q.SectionId == student.CurrentSectionId
                                 && q.Status == QuizStatus.Published
                                 && (q.ClosesAtUtc == null || q.ClosesAtUtc >= _clock.UtcNow), ct),
            UnreadNotifications = await _db.Notifications.AsNoTracking()
                .CountAsync(n => n.UserId == student.UserId && !n.IsRead, ct),
            RecentGrades = await RecentGradesAsync(studentId, ct)
        };
    }

    private async Task<List<RecentGrade>> RecentGradesAsync(int studentId, CancellationToken ct) =>
        await _db.GradeRecords.AsNoTracking()
            .Where(g => g.StudentId == studentId && g.IsPublished)
            .OrderByDescending(g => g.RecordedOn)
            .Take(5)
            .Select(g => new RecentGrade(
                g.Subject!.Name, g.Title, g.Score, g.MaxScore, g.Percentage, g.Letter, g.RecordedOn))
            .ToListAsync(ct);

    public async Task<GuardianDashboard> GetGuardianAsync(int guardianId, CancellationToken ct = default)
    {
        var today = _clock.SchoolToday;

        var userId = await _db.Guardians.AsNoTracking()
            .Where(g => g.Id == guardianId).Select(g => g.UserId).FirstOrDefaultAsync(ct);

        // Only approved links. An unapproved one must reveal nothing about the child.
        var links = await _db.GuardianStudents.AsNoTracking()
            .Where(gs => gs.GuardianId == guardianId && gs.IsApproved && !gs.IsDeleted)
            .Select(gs => new { gs.StudentId, gs.CanViewAcademics })
            .ToListAsync(ct);

        var children = new List<ChildSummary>();

        foreach (var link in links)
        {
            var student = await _db.Students.AsNoTracking()
                .Where(s => s.Id == link.StudentId)
                .Select(s => new
                {
                    s.Id, s.StudentCode,
                    Name = s.User!.FirstName + " " + s.User.LastName,
                    Photo = s.User.ProfileImagePath,
                    s.CurrentSectionId,
                    SectionName = s.CurrentSection!.DisplayName
                })
                .FirstOrDefaultAsync(ct);

            if (student is null) continue;

            var presence = await _db.StudentPresences.AsNoTracking()
                .Where(p => p.StudentId == link.StudentId)
                .Select(p => new { p.State, p.LastEntryAtUtc, p.LastExitAtUtc, Location = p.CurrentLocation!.Name })
                .FirstOrDefaultAsync(ct);

            var todayRecord = await _db.DailyAttendances.AsNoTracking()
                .Where(a => a.StudentId == link.StudentId && a.Date == today)
                .Select(a => a.Status)
                .FirstOrDefaultAsync(ct);

            var attendance = await _db.DailyAttendances.AsNoTracking()
                .Where(a => a.StudentId == link.StudentId && a.Status != AttendanceStatus.Holiday)
                .GroupBy(a => 1)
                .Select(g => new
                {
                    Total = g.Count(),
                    Attended = g.Count(a => a.Status == AttendanceStatus.Present
                                            || a.Status == AttendanceStatus.Late
                                            || a.Status == AttendanceStatus.EarlyLeave)
                })
                .FirstOrDefaultAsync(ct);

            var state = presence?.State ?? PresenceState.Outside;

            children.Add(new ChildSummary
            {
                StudentId = student.Id,
                StudentName = student.Name,
                StudentCode = student.StudentCode,
                PhotoUrl = student.Photo,
                SectionName = student.SectionName,
                PresenceState = state,
                PresenceLabel = state switch
                {
                    PresenceState.OnCampus => "At school",
                    PresenceState.InRoom => presence?.Location is { } room ? $"In {room}" : "In class",
                    _ => "Not at school"
                },
                LastEntryAtUtc = presence?.LastEntryAtUtc,
                LastExitAtUtc = presence?.LastExitAtUtc,
                CurrentLocation = presence?.Location,
                TodayStatus = todayRecord,
                AttendancePercentage = attendance is null or { Total: 0 }
                    ? 0
                    : Math.Round(attendance.Attended * 100m / attendance.Total, 1),
                CanViewAcademics = link.CanViewAcademics,
                UpcomingAssignments = link.CanViewAcademics
                    ? await _db.Assignments.AsNoTracking()
                        .CountAsync(a => a.SectionId == student.CurrentSectionId
                                         && a.Status == AssignmentStatus.Published
                                         && a.DueAtUtc >= _clock.UtcNow, ct)
                    : 0,
                // Academic detail is withheld when the link does not grant it.
                RecentGrades = link.CanViewAcademics ? await RecentGradesAsync(link.StudentId, ct) : []
            });
        }

        return new GuardianDashboard
        {
            GuardianId = guardianId,
            Children = children,
            UnreadNotifications = await _db.Notifications.AsNoTracking()
                .CountAsync(n => n.UserId == userId && !n.IsRead, ct)
        };
    }
}
