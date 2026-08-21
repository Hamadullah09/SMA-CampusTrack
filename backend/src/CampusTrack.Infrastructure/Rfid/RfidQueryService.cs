using CampusTrack.Application.Common.Interfaces;
using CampusTrack.Application.Common.Models;
using CampusTrack.Application.Rfid;
using CampusTrack.Domain.Enums;
using CampusTrack.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CampusTrack.Infrastructure.Rfid;

public class RfidEventQuery : PagedQuery
{
    public int? StudentId { get; set; }
    public int? LocationId { get; set; }
    public int? ReaderId { get; set; }
    public RfidEventType? EventType { get; set; }
    public DateOnly? FromDate { get; set; }
    public DateOnly? ToDate { get; set; }
    public int? SectionId { get; set; }
    /// <summary>Include unknown-tag and rejected reads. Off by default: they are noise on most screens.</summary>
    public bool IncludeRejected { get; set; }
}

public interface IRfidQueryService
{
    Task<PagedResult<RfidEventDto>> GetEventsAsync(RfidEventQuery query, CancellationToken ct = default);
    Task<IReadOnlyList<RfidEventDto>> GetRecentAsync(int count, CancellationToken ct = default);
    Task<IReadOnlyList<ReaderStatusDto>> GetReaderStatusAsync(CancellationToken ct = default);
    Task<IReadOnlyList<ActivityTimelineEntry>> GetStudentTimelineAsync(int studentId, DateOnly date, CancellationToken ct = default);
    Task<PresenceSummary> GetPresenceSummaryAsync(CancellationToken ct = default);
}

public record PresenceSummary(
    int OnCampus,
    int Offsite,
    int InRooms,
    int TotalActiveStudents,
    DateTime AsOfUtc);

/// <summary>
/// Read-side queries for RFID data.
///
/// Kept separate from the write pipeline: these run against wide joins for the UI, project
/// straight to DTOs so no entity graph is materialised, and never track anything. Mixing them
/// into the movement service would drag change-tracking overhead onto the ingestion path.
/// </summary>
public class RfidQueryService : IRfidQueryService
{
    private readonly CampusTrackDbContext _db;
    private readonly IDateTimeProvider _clock;

    public RfidQueryService(CampusTrackDbContext db, IDateTimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<PagedResult<RfidEventDto>> GetEventsAsync(RfidEventQuery query, CancellationToken ct = default)
    {
        var q = _db.RfidEvents.AsNoTracking().AsQueryable();

        if (!query.IncludeRejected)
            q = q.Where(e => e.EventType != RfidEventType.UnknownTag && e.EventType != RfidEventType.Rejected);

        if (query.StudentId is { } studentId) q = q.Where(e => e.StudentId == studentId);
        if (query.LocationId is { } locationId) q = q.Where(e => e.LocationId == locationId);
        if (query.ReaderId is { } readerId) q = q.Where(e => e.ReaderId == readerId);
        if (query.EventType is { } eventType) q = q.Where(e => e.EventType == eventType);
        if (query.SectionId is { } sectionId) q = q.Where(e => e.SectionId == sectionId);
        if (query.FromDate is { } from) q = q.Where(e => e.LocalDate >= from);
        if (query.ToDate is { } to) q = q.Where(e => e.LocalDate <= to);

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            q = q.Where(e =>
                e.Student!.User!.FirstName.Contains(term) ||
                e.Student.User.LastName.Contains(term) ||
                e.Student.StudentCode.Contains(term) ||
                e.Location!.Name.Contains(term));
        }

        // Newest first is the only ordering that makes sense for a movement log.
        q = q.OrderByDescending(e => e.OccurredAtUtc);

        return await q.Select(ProjectEvent).ToPagedResultAsync(query.Page, query.PageSize, ct);
    }

    public async Task<IReadOnlyList<RfidEventDto>> GetRecentAsync(int count, CancellationToken ct = default)
    {
        count = Math.Clamp(count, 1, 100);

        return await _db.RfidEvents.AsNoTracking()
            .OrderByDescending(e => e.OccurredAtUtc)
            .Take(count)
            .Select(ProjectEvent)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<ReaderStatusDto>> GetReaderStatusAsync(CancellationToken ct = default)
    {
        var today = _clock.SchoolToday;
        var now = _clock.UtcNow;

        var readers = await _db.RfidReaders.AsNoTracking()
            .OrderBy(r => r.Location!.Name).ThenBy(r => r.Name)
            .Select(r => new ReaderStatusDto
            {
                Id = r.Id,
                DeviceId = r.DeviceId,
                Name = r.Name,
                Model = r.Model,
                Status = r.Status,
                StatusName = r.Status.ToString(),
                LocationId = r.LocationId,
                LocationName = r.Location!.Name,
                LocationType = r.Location.LocationType,
                IpAddress = r.IpAddress,
                FirmwareVersion = r.FirmwareVersion,
                LastHeartbeatUtc = r.LastHeartbeatUtc,
                LastEventUtc = r.LastEventUtc,
                LastErrorMessage = r.LastErrorMessage,
                AntennaCount = r.AntennaCount,
                MapX = r.Location.MapX,
                MapY = r.Location.MapY,
                EventsToday = _db.RfidEvents.Count(e => e.ReaderId == r.Id && e.LocalDate == today)
            })
            .ToListAsync(ct);

        // Computed here rather than in SQL so the value is always relative to "now" at read
        // time, which is what the dashboard's "last seen 12s ago" needs.
        foreach (var reader in readers)
        {
            reader.SecondsSinceHeartbeat = reader.LastHeartbeatUtc is null
                ? null
                : (int)(now - reader.LastHeartbeatUtc.Value).TotalSeconds;
        }

        return readers;
    }

    public async Task<IReadOnlyList<ActivityTimelineEntry>> GetStudentTimelineAsync(
        int studentId, DateOnly date, CancellationToken ct = default)
    {
        var events = await _db.RfidEvents.AsNoTracking()
            .Where(e => e.StudentId == studentId
                        && e.LocalDate == date
                        && e.EventType != RfidEventType.UnknownTag
                        && e.EventType != RfidEventType.Rejected)
            .OrderBy(e => e.OccurredAtUtc)
            .Select(e => new
            {
                e.OccurredAtUtc,
                e.EventType,
                LocationName = e.Location!.Name,
                SubjectName = e.Subject!.Name,
                TeacherName = e.TimetableSlot!.Teacher!.User!.FirstName + " " + e.TimetableSlot.Teacher.User.LastName
            })
            .ToListAsync(ct);

        // Dwell time comes from the presence intervals, which already pair entries with exits.
        var stays = await _db.ClassroomPresences.AsNoTracking()
            .Where(p => p.StudentId == studentId && p.Date == date)
            .Select(p => new { p.EnteredAtUtc, p.DurationMinutes })
            .ToListAsync(ct);

        var durationByEntry = stays
            .Where(s => s.DurationMinutes is not null)
            .GroupBy(s => s.EnteredAtUtc)
            .ToDictionary(g => g.Key, g => g.First().DurationMinutes);

        return events.Select(e => new ActivityTimelineEntry
        {
            OccurredAtUtc = e.OccurredAtUtc,
            Title = Describe(e.EventType, e.LocationName),
            Detail = e.SubjectName is null ? null : $"{e.SubjectName}{(e.TeacherName is null ? "" : $" with {e.TeacherName}")}",
            EventType = e.EventType,
            LocationName = e.LocationName,
            SubjectName = e.SubjectName,
            Icon = Icon(e.EventType),
            DurationMinutes = durationByEntry.TryGetValue(e.OccurredAtUtc, out var minutes) ? minutes : null
        }).ToList();
    }

    public async Task<PresenceSummary> GetPresenceSummaryAsync(CancellationToken ct = default)
    {
        var counts = await _db.StudentPresences.AsNoTracking()
            .GroupBy(p => p.State)
            .Select(g => new { State = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var onCampus = counts.Where(c => c.State != PresenceState.Outside).Sum(c => c.Count);
        var inRooms = counts.Where(c => c.State == PresenceState.InRoom).Sum(c => c.Count);

        var totalActive = await _db.Students.AsNoTracking()
            .CountAsync(s => s.Status == PersonStatus.Active, ct);

        return new PresenceSummary(onCampus, Math.Max(0, totalActive - onCampus), inRooms, totalActive, _clock.UtcNow);
    }

    /// <summary>
    /// Shared projection, held as an Expression rather than a method.
    ///
    /// This distinction matters: a static method call inside Select() cannot be translated to
    /// SQL, so EF would materialise whole entities and run the mapping on the client - which
    /// silently returns null for every navigation that was not eagerly loaded. As an
    /// Expression it becomes real LEFT JOINs and one flat query.
    /// </summary>
    private static readonly System.Linq.Expressions.Expression<Func<Domain.Rfid.RfidEvent, RfidEventDto>>
        ProjectEvent = e => new RfidEventDto
    {
        Id = e.Id,
        EventType = e.EventType,
        EventTypeName = e.EventType.ToString(),
        Direction = e.Direction,
        OccurredAtUtc = e.OccurredAtUtc,
        LocalDate = e.LocalDate,
        StudentId = e.StudentId,
        StudentName = e.Student == null ? null : e.Student.User!.FirstName + " " + e.Student.User.LastName,
        StudentCode = e.Student == null ? null : e.Student.StudentCode,
        StudentPhotoUrl = e.Student == null ? null : e.Student.User!.ProfileImagePath,
        SectionName = e.Section == null ? null : e.Section.DisplayName,
        LocationId = e.LocationId,
        LocationName = e.Location == null ? null : e.Location.Name,
        LocationType = e.Location == null ? null : e.Location.LocationType,
        ReaderName = e.Reader == null ? null : e.Reader.Name,
        DeviceId = e.Reader == null ? null : e.Reader.DeviceId,
        SubjectName = e.Subject == null ? null : e.Subject.Name,
        Source = e.Source,
        Confidence = e.Confidence,
        AntennaSequence = e.AntennaSequence,
        RejectionReason = e.RejectionReason,
        // Only the tail of the EPC is ever sent to a client.
        MaskedEpc = e.Epc.Length <= 6 ? e.Epc : "***" + e.Epc.Substring(e.Epc.Length - 6)
    };

    private static string Describe(RfidEventType type, string? location) => type switch
    {
        RfidEventType.SchoolEntry => $"Arrived at school ({location})",
        RfidEventType.SchoolExit => $"Left school ({location})",
        RfidEventType.ClassroomEntry => $"Entered {location}",
        RfidEventType.ClassroomExit => $"Left {location}",
        RfidEventType.ZoneEntry => $"Entered {location}",
        RfidEventType.ZoneExit => $"Left {location}",
        _ => location ?? "Movement"
    };

    private static string Icon(RfidEventType type) => type switch
    {
        RfidEventType.SchoolEntry => "login",
        RfidEventType.SchoolExit => "logout",
        RfidEventType.ClassroomEntry or RfidEventType.ZoneEntry => "door-enter",
        RfidEventType.ClassroomExit or RfidEventType.ZoneExit => "door-exit",
        _ => "location"
    };
}
