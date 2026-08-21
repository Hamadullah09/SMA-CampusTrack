using CampusTrack.Application.Authorization;
using CampusTrack.Application.Common.Interfaces;
using CampusTrack.Domain.Common;
using CampusTrack.Domain.Enums;
using CampusTrack.Infrastructure.Attendance;
using CampusTrack.Infrastructure.Identity;
using CampusTrack.Infrastructure.Persistence;
using CampusTrack.Infrastructure.Reporting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CampusTrack.Api.Controllers;

/// <summary>
/// Reporting across attendance, movement and academics.
///
/// Every report returns JSON by default and the same data as CSV, Excel or PDF when a format
/// is requested, so what a user sees on screen is exactly what they take away.
/// </summary>
public class ReportsController : ApiControllerBase
{
    private readonly CampusTrackDbContext _db;
    private readonly IAttendanceQueryService _attendance;
    private readonly IDailyReportService _dailyReports;
    private readonly IExportService _export;
    private readonly IDateTimeProvider _clock;

    public ReportsController(
        CampusTrackDbContext db,
        IAttendanceQueryService attendance,
        IDailyReportService dailyReports,
        IExportService export,
        IDateTimeProvider clock)
    {
        _db = db;
        _attendance = attendance;
        _dailyReports = dailyReports;
        _export = export;
        _clock = clock;
    }

    /// <summary>Attendance percentages per student over a date range.</summary>
    [HttpGet("attendance")]
    [HasPermission(Permissions.Reports.ViewAttendance)]
    public async Task<IActionResult> Attendance(
        [FromQuery] int? sectionId, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to,
        [FromQuery] string? format, CancellationToken ct)
    {
        var start = from ?? _clock.SchoolToday.AddDays(-30);
        var end = to ?? _clock.SchoolToday;

        var rows = await _attendance.GetSummaryAsync(sectionId, start, end, ct);

        var columns = new List<ExportColumn>
        {
            new("Student", r => ((AttendanceSummary)r).StudentName),
            new("Days recorded", r => ((AttendanceSummary)r).TotalDays),
            new("Present", r => ((AttendanceSummary)r).PresentDays),
            new("Absent", r => ((AttendanceSummary)r).AbsentDays),
            new("Late", r => ((AttendanceSummary)r).LateDays),
            new("Leave", r => ((AttendanceSummary)r).LeaveDays),
            new("Attendance %", r => ((AttendanceSummary)r).AttendancePercentage),
            new("At risk", r => ((AttendanceSummary)r).IsBelowRequirement)
        };

        return Deliver(rows, columns, format, "attendance-report",
            "Attendance report", $"{start:yyyy-MM-dd} to {end:yyyy-MM-dd}");
    }

    /// <summary>Every gate and room movement in a date range.</summary>
    [HttpGet("rfid-movements")]
    [HasPermission(Permissions.Reports.ViewRfid)]
    public async Task<IActionResult> Movements(
        [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] int? studentId,
        [FromQuery] int? locationId, [FromQuery] string? format, CancellationToken ct)
    {
        var start = from ?? _clock.SchoolToday;
        var end = to ?? _clock.SchoolToday;

        var q = _db.RfidEvents.AsNoTracking()
            .Where(e => e.LocalDate >= start && e.LocalDate <= end
                        && e.EventType != RfidEventType.UnknownTag
                        && e.EventType != RfidEventType.Rejected);

        if (studentId is { } sid) q = q.Where(e => e.StudentId == sid);
        if (locationId is { } lid) q = q.Where(e => e.LocationId == lid);

        var rows = await q.OrderBy(e => e.OccurredAtUtc)
            .Select(e => new MovementRow(
                e.LocalDate,
                e.OccurredAtUtc,
                e.Student == null ? "Unknown" : e.Student.User!.FirstName + " " + e.Student.User.LastName,
                e.Student == null ? "" : e.Student.StudentCode,
                e.Section == null ? "" : e.Section.DisplayName,
                e.EventType.ToString(),
                e.Location == null ? "" : e.Location.Name,
                e.Reader == null ? "" : e.Reader.DeviceId,
                e.Subject == null ? "" : e.Subject.Name,
                e.Confidence))
            .Take(20000)   // a bounded export; wider ranges should be narrowed by filter
            .ToListAsync(ct);

        var columns = new List<ExportColumn>
        {
            new("Date", r => ((MovementRow)r).Date),
            new("Time (UTC)", r => ((MovementRow)r).OccurredAtUtc),
            new("Student", r => ((MovementRow)r).StudentName),
            new("Code", r => ((MovementRow)r).StudentCode),
            new("Section", r => ((MovementRow)r).SectionName),
            new("Event", r => ((MovementRow)r).EventType),
            new("Location", r => ((MovementRow)r).LocationName),
            new("Reader", r => ((MovementRow)r).DeviceId),
            new("Subject", r => ((MovementRow)r).SubjectName),
            new("Confidence", r => ((MovementRow)r).Confidence)
        };

        return Deliver(rows, columns, format, "movement-report",
            "RFID movement report", $"{start:yyyy-MM-dd} to {end:yyyy-MM-dd}");
    }

    /// <summary>Students who arrived after the late threshold.</summary>
    [HttpGet("late-arrivals")]
    [HasPermission(Permissions.Reports.ViewAttendance)]
    public async Task<IActionResult> LateArrivals(
        [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] string? format, CancellationToken ct)
    {
        var start = from ?? _clock.SchoolToday.AddDays(-30);
        var end = to ?? _clock.SchoolToday;

        var rows = await _db.DailyAttendances.AsNoTracking()
            .Where(a => a.Date >= start && a.Date <= end && a.LateMinutes > 0)
            .OrderByDescending(a => a.LateMinutes)
            .Select(a => new LateRow(
                a.Date,
                a.Student!.User!.FirstName + " " + a.Student.User.LastName,
                a.Student.StudentCode,
                a.Section == null ? "" : a.Section.DisplayName,
                a.FirstEntryAtUtc,
                a.LateMinutes))
            .ToListAsync(ct);

        var columns = new List<ExportColumn>
        {
            new("Date", r => ((LateRow)r).Date),
            new("Student", r => ((LateRow)r).StudentName),
            new("Code", r => ((LateRow)r).StudentCode),
            new("Section", r => ((LateRow)r).SectionName),
            new("Arrived (UTC)", r => ((LateRow)r).ArrivedAtUtc),
            new("Minutes late", r => ((LateRow)r).LateMinutes)
        };

        return Deliver(rows, columns, format, "late-arrivals",
            "Late arrivals", $"{start:yyyy-MM-dd} to {end:yyyy-MM-dd}");
    }

    /// <summary>Grades per student for a subject or section.</summary>
    [HttpGet("academic")]
    [HasPermission(Permissions.Reports.ViewAcademic)]
    public async Task<IActionResult> Academic(
        [FromQuery] int? sectionId, [FromQuery] int? subjectId, [FromQuery] string? format, CancellationToken ct)
    {
        var q = _db.GradeRecords.AsNoTracking().Where(g => g.IsPublished);
        if (sectionId is { } sid) q = q.Where(g => g.SectionId == sid);
        if (subjectId is { } subId) q = q.Where(g => g.SubjectId == subId);

        // Aggregated in SQL, then rounded in memory: MySQL cannot translate Math.Round over
        // an aggregate in this position, and rounding is presentation anyway.
        var aggregated = await q
            .GroupBy(g => new
            {
                g.StudentId,
                Name = g.Student!.User!.FirstName + " " + g.Student.User.LastName,
                Code = g.Student.StudentCode,
                Subject = g.Subject!.Name
            })
            .Select(g => new
            {
                g.Key.Name,
                g.Key.Code,
                g.Key.Subject,
                Count = g.Count(),
                Average = g.Average(x => x.Percentage),
                Best = g.Max(x => x.Percentage),
                Lowest = g.Min(x => x.Percentage)
            })
            .ToListAsync(ct);

        var rows = aggregated
            .Select(a => new AcademicRow(
                a.Name, a.Code, a.Subject, a.Count,
                Math.Round(a.Average, 1), a.Best, a.Lowest))
            .OrderBy(r => r.StudentName)
            .ThenBy(r => r.SubjectName)
            .ToList();

        var columns = new List<ExportColumn>
        {
            new("Student", r => ((AcademicRow)r).StudentName),
            new("Code", r => ((AcademicRow)r).StudentCode),
            new("Subject", r => ((AcademicRow)r).SubjectName),
            new("Assessments", r => ((AcademicRow)r).AssessmentCount),
            new("Average %", r => ((AcademicRow)r).AveragePercentage),
            new("Best %", r => ((AcademicRow)r).BestPercentage),
            new("Lowest %", r => ((AcademicRow)r).LowestPercentage)
        };

        return Deliver(rows, columns, format, "academic-report", "Academic performance", null);
    }

    /// <summary>Reader uptime and throughput — the operational health of the RFID estate.</summary>
    [HttpGet("reader-activity")]
    [HasPermission(Permissions.Reports.ViewRfid)]
    public async Task<IActionResult> ReaderActivity(
        [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] string? format, CancellationToken ct)
    {
        var start = from ?? _clock.SchoolToday.AddDays(-7);
        var end = to ?? _clock.SchoolToday;

        // Counted with two grouped queries rather than correlated subqueries inside the
        // projection: MySQL cannot translate a correlated aggregate over a joined set here,
        // and two round trips are cheaper than the alternative anyway.
        var readers = await _db.RfidReaders.AsNoTracking()
            .Select(r => new
            {
                r.Id, r.DeviceId, r.Name, LocationName = r.Location!.Name,
                Status = r.Status.ToString(), r.LastHeartbeatUtc, r.LastEventUtc
            })
            .OrderBy(r => r.LocationName)
            .ToListAsync(ct);

        var eventCounts = await _db.RfidEvents.AsNoTracking()
            .Where(e => e.LocalDate >= start && e.LocalDate <= end && e.ReaderId != null)
            .GroupBy(e => e.ReaderId!.Value)
            .Select(g => new { ReaderId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.ReaderId, g => g.Count, ct);

        var errorCounts = await _db.DeviceLogs.AsNoTracking()
            .Where(l => l.Level == DeviceLogLevel.Error && l.ReaderId != null)
            .GroupBy(l => l.ReaderId!.Value)
            .Select(g => new { ReaderId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.ReaderId, g => g.Count, ct);

        var rows = readers.Select(r => new ReaderRow(
            r.DeviceId, r.Name, r.LocationName, r.Status, r.LastHeartbeatUtc, r.LastEventUtc,
            eventCounts.GetValueOrDefault(r.Id),
            errorCounts.GetValueOrDefault(r.Id))).ToList();

        var columns = new List<ExportColumn>
        {
            new("Device", r => ((ReaderRow)r).DeviceId),
            new("Name", r => ((ReaderRow)r).Name),
            new("Location", r => ((ReaderRow)r).LocationName),
            new("Status", r => ((ReaderRow)r).Status),
            new("Last heartbeat", r => ((ReaderRow)r).LastHeartbeatUtc),
            new("Last event", r => ((ReaderRow)r).LastEventUtc),
            new("Events in range", r => ((ReaderRow)r).EventCount),
            new("Errors logged", r => ((ReaderRow)r).ErrorCount)
        };

        return Deliver(rows, columns, format, "reader-activity",
            "RFID reader activity", $"{start:yyyy-MM-dd} to {end:yyyy-MM-dd}");
    }

    /// <summary>The stored end-of-day report for one child, including the movement timeline.</summary>
    [HttpGet("daily/{studentId:int}")]
    [HasPermission(Permissions.Reports.ViewAttendance)]
    public async Task<ActionResult<object>> DailyReport(
        int studentId, [FromQuery] DateOnly? date, CancellationToken ct)
    {
        var target = date ?? _clock.SchoolToday;
        var report = await _dailyReports.GenerateForStudentAsync(studentId, target, send: false, ct);

        if (report is null) throw new KeyNotFoundException("That student does not exist.");

        return Ok(new
        {
            report.StudentId,
            report.Date,
            report.SchoolEntryAtUtc,
            report.SchoolExitAtUtc,
            report.FirstClassroomEntryAtUtc,
            report.LastClassroomExitAtUtc,
            report.ClassesAttended,
            report.ClassesMissed,
            report.LateArrivals,
            report.EarlyExits,
            report.AttendancePercentage,
            dayStatus = report.DayStatus.ToString(),
            timeline = report.TimelineJson is null
                ? null
                : System.Text.Json.JsonSerializer.Deserialize<object>(report.TimelineJson),
            report.GeneratedAtUtc
        });
    }

    /// <summary>
    /// Returns the rows as JSON, or renders them in the requested format. Kept in one place so
    /// every report supports every format without repeating the plumbing.
    /// </summary>
    private IActionResult Deliver<T>(
        IReadOnlyList<T> rows, IReadOnlyList<ExportColumn> columns,
        string? format, string fileName, string title, string? subtitle)
    {
        if (string.IsNullOrWhiteSpace(format) || format.Equals("json", StringComparison.OrdinalIgnoreCase))
            return Ok(new { count = rows.Count, title, subtitle, items = rows });

        var stamped = $"{fileName}-{_clock.SchoolToday:yyyyMMdd}";

        var file = format.ToLowerInvariant() switch
        {
            "csv" => _export.ToCsv(rows, columns, stamped),
            "xlsx" or "excel" => _export.ToExcel(rows, columns, title, stamped),
            "pdf" => _export.ToPdf(rows, columns, title, subtitle, stamped),
            _ => throw DomainException.Invalid($"'{format}' is not a supported format. Use csv, xlsx or pdf.")
        };

        return File(file.Content, file.ContentType, file.FileName);
    }

    // Named record types rather than anonymous ones, so the export columns can cast safely.
    private record MovementRow(
        DateOnly Date, DateTime OccurredAtUtc, string StudentName, string StudentCode, string SectionName,
        string EventType, string LocationName, string DeviceId, string SubjectName, double Confidence);

    private record LateRow(
        DateOnly Date, string StudentName, string StudentCode, string SectionName,
        DateTime? ArrivedAtUtc, int LateMinutes);

    private record AcademicRow(
        string StudentName, string StudentCode, string SubjectName, int AssessmentCount,
        decimal AveragePercentage, decimal BestPercentage, decimal LowestPercentage);

    private record ReaderRow(
        string DeviceId, string Name, string LocationName, string Status,
        DateTime? LastHeartbeatUtc, DateTime? LastEventUtc, int EventCount, int ErrorCount);
}
