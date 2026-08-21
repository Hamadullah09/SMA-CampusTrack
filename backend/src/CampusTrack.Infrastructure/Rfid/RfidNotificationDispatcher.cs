using CampusTrack.Application.Common;
using CampusTrack.Application.Common.Interfaces;
using CampusTrack.Domain.Enums;
using CampusTrack.Domain.Rfid;
using CampusTrack.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CampusTrack.Infrastructure.Rfid;

public interface IRfidNotificationDispatcher
{
    Task DispatchAsync(RfidEvent movement, CancellationToken ct = default);
}

/// <summary>
/// Decides whether a movement is worth telling a guardian about, and words it the way a
/// parent would want to read it.
///
/// Gate movements are on by default because they answer the question parents actually have -
/// did my child get to school, and have they left. Room movements are off by default: a
/// monitored corridor can produce a dozen events an hour, and a parent who is notified about
/// everything quickly stops reading any of it.
/// </summary>
public class RfidNotificationDispatcher : IRfidNotificationDispatcher
{
    private readonly CampusTrackDbContext _db;
    private readonly INotificationService _notifications;
    private readonly ISettingsProvider _settings;
    private readonly IDateTimeProvider _clock;
    private readonly ILogger<RfidNotificationDispatcher> _logger;

    public RfidNotificationDispatcher(
        CampusTrackDbContext db,
        INotificationService notifications,
        ISettingsProvider settings,
        IDateTimeProvider clock,
        ILogger<RfidNotificationDispatcher> logger)
    {
        _db = db;
        _notifications = notifications;
        _settings = settings;
        _clock = clock;
        _logger = logger;
    }

    public async Task DispatchAsync(RfidEvent movement, CancellationToken ct = default)
    {
        if (movement.StudentId is not { } studentId) return;
        if (movement.NotificationSent) return;

        var enabled = await IsCategoryEnabledAsync(movement.EventType, ct);
        if (!enabled) return;

        // A location can opt out of guardian notifications even when the category is on -
        // useful for a staff-only door that happens to sit on the boundary.
        var location = movement.LocationId is null
            ? null
            : await _db.RfidLocations.AsNoTracking()
                .Where(l => l.Id == movement.LocationId)
                .Select(l => new { l.Name, l.NotifyGuardians, l.IsCampusBoundary })
                .FirstOrDefaultAsync(ct);

        var isGateMovement = movement.EventType is RfidEventType.SchoolEntry or RfidEventType.SchoolExit;
        if (!isGateMovement && location?.NotifyGuardians != true) return;

        var student = await _db.Students.AsNoTracking()
            .Where(s => s.Id == studentId)
            .Select(s => new { s.Id, Name = s.User!.FirstName, FullName = s.User.FirstName + " " + s.User.LastName })
            .FirstOrDefaultAsync(ct);

        if (student is null) return;

        var localTime = _clock.ToSchoolTime(movement.OccurredAtUtc);
        var timeText = localTime.ToString("h:mm tt");
        var subjectName = movement.SubjectId is null
            ? null
            : await _db.Subjects.AsNoTracking()
                .Where(s => s.Id == movement.SubjectId).Select(s => s.Name).FirstOrDefaultAsync(ct);

        var (category, title, body) = Compose(
            movement.EventType, student.Name, student.FullName, location?.Name, subjectName, timeText);

        await _notifications.NotifyGuardiansOfStudentAsync(studentId, new NotificationRequest
        {
            Category = category,
            Priority = isGateMovement ? NotificationPriority.High : NotificationPriority.Normal,
            Title = title,
            Body = body,
            StudentId = studentId,
            RelatedEntityType = nameof(RfidEvent),
            RelatedEntityId = movement.Id,
            Data = new
            {
                type = movement.EventType.ToString(),
                studentId,
                eventId = movement.Id,
                locationName = location?.Name,
                occurredAtUtc = movement.OccurredAtUtc
            }
        }, ct);

        var tracked = await _db.RfidEvents.FirstOrDefaultAsync(e => e.Id == movement.Id, ct);
        if (tracked is not null)
        {
            tracked.NotificationSent = true;
            await _db.SaveChangesAsync(ct);
        }
    }

    private async Task<bool> IsCategoryEnabledAsync(RfidEventType eventType, CancellationToken ct) => eventType switch
    {
        RfidEventType.SchoolEntry => await _settings.GetAsync(SettingKeys.NotifyOnSchoolEntry, true, ct),
        RfidEventType.SchoolExit => await _settings.GetAsync(SettingKeys.NotifyOnSchoolExit, true, ct),
        RfidEventType.ClassroomEntry or RfidEventType.ClassroomExit or
        RfidEventType.ZoneEntry or RfidEventType.ZoneExit
            => await _settings.GetAsync(SettingKeys.NotifyOnClassroomMovement, false, ct),
        _ => false
    };

    /// <summary>
    /// Wording is centralised here rather than scattered through the pipeline so it can be
    /// reviewed as a whole, and so no caller invents its own phrasing for the same event.
    /// </summary>
    private static (NotificationCategory Category, string Title, string Body) Compose(
        RfidEventType eventType, string firstName, string fullName,
        string? locationName, string? subjectName, string timeText)
    {
        var place = string.IsNullOrWhiteSpace(locationName) ? "school" : locationName;

        return eventType switch
        {
            RfidEventType.SchoolEntry => (
                NotificationCategory.SchoolEntry,
                $"{firstName} arrived at school",
                $"Your child {fullName} entered the school at {timeText} via {place}."),

            RfidEventType.SchoolExit => (
                NotificationCategory.SchoolExit,
                $"{firstName} left school",
                $"Your child {fullName} left the school at {timeText} via {place}."),

            RfidEventType.ClassroomEntry => (
                NotificationCategory.ClassroomEntry,
                $"{firstName} entered {place}",
                subjectName is null
                    ? $"{fullName} entered {place} at {timeText}."
                    : $"{fullName} entered {place} for {subjectName} at {timeText}."),

            RfidEventType.ClassroomExit => (
                NotificationCategory.ClassroomExit,
                $"{firstName} left {place}",
                $"{fullName} left {place} at {timeText}."),

            RfidEventType.ZoneEntry => (
                NotificationCategory.ClassroomEntry,
                $"{firstName} entered {place}",
                $"{fullName} entered {place} at {timeText}."),

            _ => (
                NotificationCategory.ClassroomExit,
                $"{firstName} left {place}",
                $"{fullName} left {place} at {timeText}.")
        };
    }
}
