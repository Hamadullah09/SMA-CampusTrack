using CampusTrack.Application.Common;
using CampusTrack.Application.Common.Interfaces;
using CampusTrack.Application.Rfid;
using CampusTrack.Domain.Enums;
using CampusTrack.Domain.Rfid;
using CampusTrack.Infrastructure.Attendance;
using CampusTrack.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace CampusTrack.Infrastructure.Rfid;

public interface IRfidMovementService
{
    Task<RfidEvent?> ProcessSequenceAsync(CompletedSequence sequence, CancellationToken ct = default);
}

/// <summary>
/// Turns one finished pass-through into a business event.
///
/// The pipeline is: validate the reader, resolve the tag to a person, work out the direction,
/// suppress duplicates, classify the movement against the location and timetable, persist it,
/// and hand off to the attendance engine and notifications.
///
/// Every rejection is recorded rather than dropped. An unknown tag at the main gate is
/// operationally interesting - it is usually a visitor, a stale card, or a card that was
/// never assigned - and silently discarding it would make that invisible.
/// </summary>
public class RfidMovementService : IRfidMovementService
{
    private readonly CampusTrackDbContext _db;
    private readonly ISettingsProvider _settings;
    private readonly IDateTimeProvider _clock;
    private readonly IAttendanceEngine _attendance;
    private readonly IRfidNotificationDispatcher _notifications;
    private readonly IRealtimePublisher _realtime;
    private readonly IMemoryCache _cache;
    private readonly ILogger<RfidMovementService> _logger;

    public RfidMovementService(
        CampusTrackDbContext db,
        ISettingsProvider settings,
        IDateTimeProvider clock,
        IAttendanceEngine attendance,
        IRfidNotificationDispatcher notifications,
        IRealtimePublisher realtime,
        IMemoryCache cache,
        ILogger<RfidMovementService> logger)
    {
        _db = db;
        _settings = settings;
        _clock = clock;
        _attendance = attendance;
        _notifications = notifications;
        _realtime = realtime;
        _cache = cache;
        _logger = logger;
    }

    public async Task<RfidEvent?> ProcessSequenceAsync(CompletedSequence sequence, CancellationToken ct = default)
    {
        var reader = await LoadReaderAsync(sequence.ReaderId, ct);
        if (reader is null)
        {
            _logger.LogWarning("Sequence for reader {ReaderId} discarded: reader no longer exists", sequence.ReaderId);
            return null;
        }

        var occurredAt = sequence.LastReadUtc;
        var localDate = _clock.ToSchoolDate(occurredAt);

        var tag = await _db.RfidTags
            .Include(t => t.Student)
            .FirstOrDefaultAsync(t => t.Epc == sequence.Epc && t.SchoolId == reader.SchoolId, ct);

        // ---- unknown or unusable tag --------------------------------------------------
        if (tag is null || !tag.IsUsable)
        {
            var reason = tag is null
                ? "This card is not registered."
                : tag.IsAssigned
                    ? $"This card is marked {tag.Status}."
                    : "This card has not been assigned to anyone.";

            var rejected = new RfidEvent
            {
                SchoolId = reader.SchoolId,
                EventType = tag is null ? RfidEventType.UnknownTag : RfidEventType.Rejected,
                Direction = MovementDirection.Unknown,
                OccurredAtUtc = occurredAt,
                LocalDate = localDate,
                Epc = sequence.Epc,
                TagId = tag?.Id,
                ReaderId = reader.Id,
                LocationId = reader.LocationId,
                Source = EventSource.Rfid,
                AntennaSequence = string.Join(",", DirectionResolver.CollapsePath(sequence.Hits)),
                RawReadCount = sequence.Hits.Count,
                Confidence = 0,
                RejectionReason = reason,
                CreatedAtUtc = _clock.UtcNow
            };

            _db.RfidEvents.Add(rejected);
            await _db.SaveChangesAsync(ct);

            _logger.LogInformation("Rejected read of {Epc} at {Location}: {Reason}",
                MaskEpc(sequence.Epc), reader.LocationName, reason);

            await _realtime.PublishRfidEventAsync(new
            {
                eventType = rejected.EventType.ToString(),
                locationName = reader.LocationName,
                maskedEpc = MaskEpc(sequence.Epc),
                occurredAtUtc = occurredAt,
                rejectionReason = reason
            }, ct);

            return rejected;
        }

        // ---- direction ------------------------------------------------------------------
        var roles = reader.Antennas.ToDictionary(a => a.Number, a => a.Role);
        PresenceState? presence = null;

        if (reader.DirectionStrategy == DirectionStrategy.PresenceToggle && tag.StudentId is { } presenceStudentId)
        {
            presence = await _db.StudentPresences
                .Where(p => p.StudentId == presenceStudentId)
                .Select(p => (PresenceState?)p.State)
                .FirstOrDefaultAsync(ct) ?? PresenceState.Outside;
        }

        var resolution = DirectionResolver.Resolve(
            sequence.Hits, reader.DirectionStrategy, roles, reader.FixedDirection, presence);

        if (!resolution.IsResolved)
        {
            // Someone walked up to the door and turned back, or only one antenna saw them.
            // Recording a guess here would put a false arrival in a parent's timeline.
            _logger.LogDebug("Ambiguous pass at {Location} for {Epc}: {Reason}",
                reader.LocationName, MaskEpc(sequence.Epc), resolution.Reason);
            return null;
        }

        // ---- duplicate suppression --------------------------------------------------------
        var debounceSeconds = reader.DebounceSeconds
                              ?? await _settings.GetAsync(SettingKeys.RfidDebounceSeconds, 60, ct);
        var debounceFrom = occurredAt.AddSeconds(-debounceSeconds);

        var isDuplicate = await _db.RfidEvents.AnyAsync(e =>
            e.TagId == tag.Id &&
            e.LocationId == reader.LocationId &&
            e.Direction == resolution.Direction &&
            e.OccurredAtUtc >= debounceFrom &&
            e.OccurredAtUtc <= occurredAt, ct);

        if (isDuplicate)
        {
            _logger.LogDebug("Suppressed duplicate {Direction} for tag {TagId} at {Location} within {Seconds}s",
                resolution.Direction, tag.Id, reader.LocationName, debounceSeconds);
            return null;
        }

        // ---- classification ------------------------------------------------------------
        var eventType = ClassifyEvent(reader.LocationType, reader.IsCampusBoundary, resolution.Direction);

        var context = await ResolveAcademicContextAsync(tag.StudentId, reader.ClassroomId, occurredAt, localDate, ct);

        var movement = new RfidEvent
        {
            SchoolId = reader.SchoolId,
            EventType = eventType,
            Direction = resolution.Direction,
            OccurredAtUtc = occurredAt,
            LocalDate = localDate,
            Epc = sequence.Epc,
            TagId = tag.Id,
            StudentId = tag.StudentId,
            TeacherId = tag.TeacherId,
            StaffMemberId = tag.StaffMemberId,
            ReaderId = reader.Id,
            LocationId = reader.LocationId,
            TimetableSlotId = context?.SlotId,
            SubjectId = context?.SubjectId,
            SectionId = context?.SectionId,
            Source = EventSource.Rfid,
            AntennaSequence = resolution.AntennaPath,
            RawReadCount = sequence.Hits.Count,
            Confidence = resolution.Confidence,
            CreatedAtUtc = _clock.UtcNow
        };

        _db.RfidEvents.Add(movement);

        tag.LastSeenAtUtc = occurredAt;
        tag.LastSeenLocationId = reader.LocationId;

        var readerEntity = await _db.RfidReaders.FirstOrDefaultAsync(r => r.Id == reader.Id, ct);
        if (readerEntity is not null)
        {
            readerEntity.LastEventUtc = occurredAt;
            if (readerEntity.Status != ReaderStatus.Online)
            {
                readerEntity.Status = ReaderStatus.Online;
                readerEntity.LastHeartbeatUtc = occurredAt;   // traffic proves it is alive
            }
        }

        await _db.SaveChangesAsync(ct);

        // ---- downstream ------------------------------------------------------------------
        // Attendance and notifications run after the event is durably stored. If either fails,
        // the movement itself is still on record and can be replayed.
        if (tag.StudentId is { } studentId)
        {
            try
            {
                await _attendance.ApplyMovementAsync(movement, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Attendance update failed for event {EventId}", movement.Id);
            }

            try
            {
                await _notifications.DispatchAsync(movement, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Guardian notification failed for event {EventId}", movement.Id);
            }
        }

        await PublishRealtimeAsync(movement, reader, tag.Student?.StudentCode, ct);
        return movement;
    }

    /// <summary>
    /// Boundary locations move a person on or off campus; rooms produce room movements;
    /// everything else is a zone movement (library, cafeteria) that is logged but does not
    /// change campus presence.
    /// </summary>
    private static RfidEventType ClassifyEvent(LocationType locationType, bool isBoundary, MovementDirection direction)
    {
        if (isBoundary || locationType is LocationType.MainGate or LocationType.ExitGate)
            return direction == MovementDirection.Entry ? RfidEventType.SchoolEntry : RfidEventType.SchoolExit;

        if (locationType is LocationType.Classroom or LocationType.Laboratory or LocationType.ComputerLab)
            return direction == MovementDirection.Entry ? RfidEventType.ClassroomEntry : RfidEventType.ClassroomExit;

        return direction == MovementDirection.Entry ? RfidEventType.ZoneEntry : RfidEventType.ZoneExit;
    }

    /// <summary>
    /// Finds the lesson this movement should be judged against: the student's own section
    /// timetable for the moment they were seen, preferring a slot scheduled in the room the
    /// reader is attached to.
    /// </summary>
    private async Task<AcademicContext?> ResolveAcademicContextAsync(
        int? studentId, int? classroomId, DateTime occurredAtUtc, DateOnly localDate, CancellationToken ct)
    {
        if (studentId is null) return null;

        var sectionId = await _db.Students
            .Where(s => s.Id == studentId)
            .Select(s => s.CurrentSectionId)
            .FirstOrDefaultAsync(ct);

        if (sectionId is null) return null;

        var localTime = TimeOnly.FromDateTime(_clock.ToSchoolTime(occurredAtUtc).DateTime);
        var isoDay = (int)localDate.DayOfWeek == 0 ? 7 : (int)localDate.DayOfWeek;

        var slots = await _db.TimetableSlots
            .AsNoTracking()
            .Where(s => s.SectionId == sectionId
                        && s.DayOfWeek == isoDay
                        && s.IsActive
                        && (s.EffectiveFrom == null || s.EffectiveFrom <= localDate)
                        && (s.EffectiveTo == null || s.EffectiveTo >= localDate))
            .Select(s => new { s.Id, s.SubjectId, s.SectionId, s.ClassroomId, s.StartTime, s.EndTime, s.TeacherId })
            .ToListAsync(ct);

        if (slots.Count == 0) return null;

        // A lesson in progress, allowing a short lead-in so an arrival just before the bell
        // still attaches to the right period.
        const int leadInMinutes = 15;
        var candidates = slots
            .Where(s => localTime >= s.StartTime.AddMinutes(-leadInMinutes) && localTime <= s.EndTime)
            .ToList();

        if (candidates.Count == 0) return null;

        var chosen = candidates.FirstOrDefault(s => classroomId != null && s.ClassroomId == classroomId)
                     ?? candidates.OrderBy(s => s.StartTime).First();

        return new AcademicContext(chosen.Id, chosen.SubjectId, chosen.SectionId, chosen.TeacherId);
    }

    private async Task PublishRealtimeAsync(RfidEvent movement, ReaderContext reader, string? studentCode, CancellationToken ct)
    {
        var studentName = movement.StudentId is null
            ? null
            : await _db.Students.Where(s => s.Id == movement.StudentId)
                .Select(s => s.User!.FirstName + " " + s.User.LastName)
                .FirstOrDefaultAsync(ct);

        await _realtime.PublishRfidEventAsync(new
        {
            id = movement.Id,
            eventType = movement.EventType.ToString(),
            direction = movement.Direction.ToString(),
            occurredAtUtc = movement.OccurredAtUtc,
            studentId = movement.StudentId,
            studentName,
            studentCode,
            locationId = movement.LocationId,
            locationName = reader.LocationName,
            locationType = reader.LocationType.ToString(),
            readerName = reader.Name,
            confidence = movement.Confidence
        }, ct);
    }

    private async Task<ReaderContext?> LoadReaderAsync(int readerId, CancellationToken ct)
    {
        var key = $"readerctx:{readerId}";
        if (_cache.TryGetValue(key, out ReaderContext? cached) && cached is not null) return cached;

        var loaded = await _db.RfidReaders
            .AsNoTracking()
            .Where(r => r.Id == readerId)
            .Select(r => new ReaderContext(
                r.Id,
                r.Name,
                r.SchoolId,
                r.LocationId,
                r.Location!.Name,
                r.Location.LocationType,
                r.Location.IsCampusBoundary,
                r.Location.ClassroomId,
                r.Location.NotifyGuardians,
                r.Location.AffectsAttendance,
                r.DirectionStrategy,
                r.FixedDirection,
                r.DebounceSeconds,
                r.Antennas.Select(a => new AntennaContext(a.AntennaNumber, a.Role)).ToList()))
            .FirstOrDefaultAsync(ct);

        if (loaded is not null) _cache.Set(key, loaded, TimeSpan.FromSeconds(60));
        return loaded;
    }

    /// <summary>Shows only the tail of an EPC, so logs and screenshots do not expose a full card id.</summary>
    public static string MaskEpc(string epc) =>
        epc.Length <= 6 ? epc : new string('*', epc.Length - 6) + epc[^6..];

    private record AcademicContext(int SlotId, int SubjectId, int SectionId, int? TeacherId);
}

public record AntennaContext(int Number, AntennaRole Role);

public record ReaderContext(
    int Id,
    string Name,
    int SchoolId,
    int LocationId,
    string LocationName,
    LocationType LocationType,
    bool IsCampusBoundary,
    int? ClassroomId,
    bool NotifyGuardians,
    bool AffectsAttendance,
    DirectionStrategy DirectionStrategy,
    MovementDirection FixedDirection,
    int? DebounceSeconds,
    IReadOnlyList<AntennaContext> Antennas);
