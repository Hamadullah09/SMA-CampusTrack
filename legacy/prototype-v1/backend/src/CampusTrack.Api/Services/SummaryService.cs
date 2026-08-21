using System.Text;
using CampusTrack.Api.Data;
using CampusTrack.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace CampusTrack.Api.Services;

/// <summary>
/// Builds the daily / weekly digest of a student's whole-day movements
/// (gate + every room visit) and teacher activity reports, and sends it
/// to the parent as a notification.
/// </summary>
public class SummaryService
{
    private readonly AppDbContext _db;
    private readonly NotificationService _notifications;

    public SummaryService(AppDbContext db, NotificationService notifications)
    {
        _db = db; _notifications = notifications;
    }

    public async Task SendDailySummariesAsync(DateOnly day, CancellationToken ct = default)
    {
        var dayStartUtc = day.ToDateTime(TimeOnly.MinValue).ToUniversalTime();
        var dayEndUtc = dayStartUtc.AddDays(1);
        await SendSummariesAsync(dayStartUtc, dayEndUtc, "DailySummary",
            s => $"Daily report – {s} – {day:dd MMM yyyy}", ct);
    }

    public async Task SendWeeklySummariesAsync(DateOnly weekEnd, CancellationToken ct = default)
    {
        var endUtc = weekEnd.ToDateTime(TimeOnly.MinValue).ToUniversalTime().AddDays(1);
        var startUtc = endUtc.AddDays(-7);
        await SendSummariesAsync(startUtc, endUtc, "WeeklySummary",
            s => $"Weekly report – {s} – week ending {weekEnd:dd MMM}", ct);
    }

    private async Task SendSummariesAsync(DateTime fromUtc, DateTime toUtc, string notifType,
                                          Func<string?, string> titleFor, CancellationToken ct)
    {
        var students = await _db.Students
            .Include(s => s.User).Include(s => s.Parent)
            .Where(s => s.ParentId != null)
            .ToListAsync(ct);

        foreach (var student in students)
        {
            var events = await _db.AttendanceEvents
                .Include(a => a.Room)
                .Where(a => a.StudentId == student.Id && a.EventTime >= fromUtc && a.EventTime < toUtc)
                .OrderBy(a => a.EventTime)
                .ToListAsync(ct);

            var reports = await _db.ActivityReports
                .Include(r => r.Teacher!).ThenInclude(t => t.User)
                .Where(r => r.StudentId == student.Id && r.CreatedAt >= fromUtc && r.CreatedAt < toUtc)
                .ToListAsync(ct);

            if (events.Count == 0 && reports.Count == 0) continue;

            var body = BuildBody(events, reports);
            await _notifications.SendAsync(student.Parent!.UserId, notifType,
                titleFor(student.User?.FullName), body,
                new { studentId = student.Id, from = fromUtc, to = toUtc }, ct);
        }
    }

    private static string BuildBody(List<AttendanceEvent> events, List<ActivityReport> reports)
    {
        var sb = new StringBuilder();

        var gate = events.Where(e => e.Room?.RoomType == RoomType.Gate).ToList();
        var arrival = gate.FirstOrDefault(e => e.Direction == Direction.Entry);
        var departure = gate.LastOrDefault(e => e.Direction == Direction.Exit);
        if (arrival is not null)
            sb.AppendLine($"Arrived: {arrival.EventTime.ToLocalTime():dd MMM hh\\:mm tt}");
        if (departure is not null)
            sb.AppendLine($"Departed: {departure.EventTime.ToLocalTime():dd MMM hh\\:mm tt}");
        if (gate.Count == 0)
            sb.AppendLine("No gate attendance recorded.");

        var roomVisits = events
            .Where(e => e.Room is not null && e.Room.RoomType != RoomType.Gate && e.Direction == Direction.Entry)
            .GroupBy(e => e.Room!.Name)
            .Select(g => $"{g.Key} x{g.Count()}")
            .ToList();
        if (roomVisits.Count > 0)
            sb.AppendLine("Rooms attended: " + string.Join(", ", roomVisits));

        if (reports.Count > 0)
        {
            sb.AppendLine("Teacher updates:");
            foreach (var r in reports)
                sb.AppendLine($"• [{r.Category}] {r.Title}" +
                              (r.Grade is null ? "" : $" – {r.Grade}") +
                              (r.Remarks is null ? "" : $" – {r.Remarks}"));
        }
        return sb.ToString().TrimEnd();
    }
}
