using System.Text.Json;
using CampusTrack.Application.Common.Interfaces;
using CampusTrack.Application.Rfid;
using CampusTrack.Domain.Communication;
using CampusTrack.Domain.Enums;
using CampusTrack.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CampusTrack.Infrastructure.Reporting;

public interface IDailyReportService
{
    Task<int> GenerateAndSendAsync(DateOnly date, CancellationToken ct = default);
    Task<DailyStudentReport?> GenerateForStudentAsync(int studentId, DateOnly date, bool send, CancellationToken ct = default);
}

/// <summary>
/// Builds the end-of-day summary a guardian receives, and the timeline behind it.
///
/// The timeline is rendered once and stored rather than recomputed on every view. A parent
/// opening last month's report should not cause a scan across the movement history, and the
/// stored version is also an honest record of what the school reported at the time.
/// </summary>
public class DailyReportService : IDailyReportService
{
    private readonly CampusTrackDbContext _db;
    private readonly INotificationService _notifications;
    private readonly IDateTimeProvider _clock;
    private readonly ILogger<DailyReportService> _logger;

    public DailyReportService(
        CampusTrackDbContext db,
        INotificationService notifications,
        IDateTimeProvider clock,
        ILogger<DailyReportService> logger)
    {
        _db = db;
        _notifications = notifications;
        _clock = clock;
        _logger = logger;
    }

    public async Task<int> GenerateAndSendAsync(DateOnly date, CancellationToken ct = default)
    {
        // Only students who actually have a guardian subscribed - generating reports nobody
        // reads is wasted work on the busiest job of the day.
        var studentIds = await _db.GuardianStudents
            .Where(gs => gs.IsApproved && gs.ReceivesNotifications && !gs.IsDeleted)
            .Select(gs => gs.StudentId)
            .Distinct()
            .ToListAsync(ct);

        var sent = 0;
        foreach (var studentId in studentIds)
        {
            try
            {
                var report = await GenerateForStudentAsync(studentId, date, send: true, ct);
                if (report is not null) sent++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Daily report failed for student {StudentId} on {Date}", studentId, date);
            }
        }

        return sent;
    }

    public async Task<DailyStudentReport?> GenerateForStudentAsync(
        int studentId, DateOnly date, bool send, CancellationToken ct = default)
    {
        var student = await _db.Students.AsNoTracking()
            .Where(s => s.Id == studentId)
            .Select(s => new
            {
                s.Id,
                s.SchoolId,
                FirstName = s.User!.FirstName,
                FullName = s.User.FirstName + " " + s.User.LastName,
                SectionName = s.CurrentSection!.DisplayName
            })
            .FirstOrDefaultAsync(ct);

        if (student is null) return null;

        var daily = await _db.DailyAttendances.AsNoTracking()
            .FirstOrDefaultAsync(a => a.StudentId == studentId && a.Date == date, ct);

        var events = await _db.RfidEvents.AsNoTracking()
            .Where(e => e.StudentId == studentId && e.LocalDate == date)
            .OrderBy(e => e.OccurredAtUtc)
            .Select(e => new
            {
                e.OccurredAtUtc,
                e.EventType,
                LocationName = e.Location!.Name,
                SubjectName = e.Subject!.Name
            })
            .ToListAsync(ct);

        var sessions = await _db.SessionAttendances.AsNoTracking()
            .Where(a => a.StudentId == studentId && a.Date == date)
            .Select(a => new { a.Status, SubjectName = a.Subject!.Name, a.LateMinutes, a.EarlyLeaveMinutes })
            .ToListAsync(ct);

        var scheduledCount = await CountScheduledLessonsAsync(studentId, date, ct);

        var attended = sessions.Count(s =>
            s.Status is AttendanceStatus.Present or AttendanceStatus.Late or AttendanceStatus.EarlyLeave);
        var missed = Math.Max(0, scheduledCount - attended);

        var timeline = events.Select(e => new ActivityTimelineEntry
        {
            OccurredAtUtc = e.OccurredAtUtc,
            Title = DescribeEvent(e.EventType, e.LocationName),
            Detail = e.SubjectName,
            EventType = e.EventType,
            LocationName = e.LocationName,
            SubjectName = e.SubjectName,
            Icon = IconFor(e.EventType)
        }).ToList();

        var firstRoomEntry = events.FirstOrDefault(e => e.EventType == RfidEventType.ClassroomEntry);
        var lastRoomExit = events.LastOrDefault(e => e.EventType == RfidEventType.ClassroomExit);

        var report = await _db.DailyStudentReports
            .FirstOrDefaultAsync(r => r.StudentId == studentId && r.Date == date, ct);

        if (report is null)
        {
            report = new DailyStudentReport { SchoolId = student.SchoolId, StudentId = studentId, Date = date };
            _db.DailyStudentReports.Add(report);
        }

        report.SchoolEntryAtUtc = daily?.FirstEntryAtUtc;
        report.SchoolExitAtUtc = daily?.LastExitAtUtc;
        report.FirstClassroomEntryAtUtc = firstRoomEntry?.OccurredAtUtc;
        report.LastClassroomExitAtUtc = lastRoomExit?.OccurredAtUtc;
        report.ClassesAttended = attended;
        report.ClassesMissed = missed;
        report.LateArrivals = sessions.Count(s => s.LateMinutes > 0) + (daily?.LateMinutes > 0 ? 1 : 0);
        report.EarlyExits = sessions.Count(s => s.EarlyLeaveMinutes > 0);
        report.DayStatus = daily?.Status ?? AttendanceStatus.NotRecorded;
        report.AttendancePercentage = scheduledCount == 0
            ? (daily?.Status is AttendanceStatus.Present or AttendanceStatus.Late ? 100m : 0m)
            : Math.Round(attended * 100m / scheduledCount, 1);
        report.TimelineJson = JsonSerializer.Serialize(timeline);
        report.GeneratedAtUtc = _clock.UtcNow;

        await _db.SaveChangesAsync(ct);

        if (send && !report.IsSent)
        {
            await _notifications.NotifyGuardiansOfStudentAsync(studentId, new NotificationRequest
            {
                Category = NotificationCategory.DailyReport,
                Priority = NotificationPriority.Normal,
                Title = $"{student.FirstName}'s day at school",
                Body = BuildSummaryLine(student.FirstName, report, daily),
                StudentId = studentId,
                RelatedEntityType = nameof(DailyStudentReport),
                RelatedEntityId = report.Id
            }, ct);

            report.IsSent = true;
            report.SentAtUtc = _clock.UtcNow;
            await _db.SaveChangesAsync(ct);
        }

        return report;
    }

    private async Task<int> CountScheduledLessonsAsync(int studentId, DateOnly date, CancellationToken ct)
    {
        var sectionId = await _db.Students.AsNoTracking()
            .Where(s => s.Id == studentId).Select(s => s.CurrentSectionId).FirstOrDefaultAsync(ct);

        if (sectionId is null) return 0;

        var isoDay = (int)date.DayOfWeek == 0 ? 7 : (int)date.DayOfWeek;

        return await _db.TimetableSlots.AsNoTracking()
            .CountAsync(s => s.SectionId == sectionId
                             && s.DayOfWeek == isoDay
                             && s.IsActive
                             && (s.EffectiveFrom == null || s.EffectiveFrom <= date)
                             && (s.EffectiveTo == null || s.EffectiveTo >= date), ct);
    }

    /// <summary>
    /// One sentence a parent reads on a lock screen. Leads with the fact that matters most -
    /// whether the child was there - rather than with statistics.
    /// </summary>
    private string BuildSummaryLine(string firstName, DailyStudentReport report, Domain.Attendance.DailyAttendance? daily)
    {
        if (daily is null || daily.Status is AttendanceStatus.NotRecorded)
            return $"No attendance was recorded for {firstName} today.";

        if (daily.Status == AttendanceStatus.Absent)
            return $"{firstName} was marked absent today.";

        if (daily.Status == AttendanceStatus.Leave)
            return $"{firstName} was on approved leave today.";

        var parts = new List<string>();

        if (report.SchoolEntryAtUtc is { } entry)
            parts.Add($"arrived at {_clock.ToSchoolTime(entry):h:mm tt}");

        if (report.SchoolExitAtUtc is { } exit)
            parts.Add($"left at {_clock.ToSchoolTime(exit):h:mm tt}");

        if (report.ClassesAttended > 0)
            parts.Add($"attended {report.ClassesAttended} of {report.ClassesAttended + report.ClassesMissed} lesson(s)");

        if (daily.LateMinutes > 0) parts.Add($"was {daily.LateMinutes} minute(s) late");

        return parts.Count == 0
            ? $"{firstName} was at school today."
            : $"{firstName} " + string.Join(", ", parts) + ".";
    }

    private static string DescribeEvent(RfidEventType type, string? location) => type switch
    {
        RfidEventType.SchoolEntry => $"Arrived at school ({location})",
        RfidEventType.SchoolExit => $"Left school ({location})",
        RfidEventType.ClassroomEntry => $"Entered {location}",
        RfidEventType.ClassroomExit => $"Left {location}",
        RfidEventType.ZoneEntry => $"Entered {location}",
        RfidEventType.ZoneExit => $"Left {location}",
        _ => location ?? "Movement recorded"
    };

    private static string IconFor(RfidEventType type) => type switch
    {
        RfidEventType.SchoolEntry => "login",
        RfidEventType.SchoolExit => "logout",
        RfidEventType.ClassroomEntry or RfidEventType.ZoneEntry => "door-enter",
        RfidEventType.ClassroomExit or RfidEventType.ZoneExit => "door-exit",
        _ => "location"
    };
}
