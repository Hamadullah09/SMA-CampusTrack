using CampusTrack.Application.Common.Interfaces;
using CampusTrack.Application.Rfid;
using CampusTrack.Domain.Enums;
using CampusTrack.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CampusTrack.Infrastructure.Rfid;

public interface IRfidSimulator
{
    Task<SimulationResult> SimulatePassAsync(SimulationRequest request, CancellationToken ct = default);
    Task<SimulationResult> SimulateSchoolDayAsync(int studentId, DateOnly date, CancellationToken ct = default);
}

public class SimulationRequest
{
    public required string DeviceId { get; set; }
    public required string Epc { get; set; }
    public MovementDirection Direction { get; set; } = MovementDirection.Entry;
    /// <summary>Reads emitted per antenna, mimicking a real reader's repeated detections.</summary>
    public int ReadsPerAntenna { get; set; } = 6;
    public DateTime? AtUtc { get; set; }
    /// <summary>Emit reads on a single antenna only - reproduces the ambiguous-pass case.</summary>
    public bool SingleAntennaOnly { get; set; }
}

public record SimulationResult(bool Accepted, int ReadsGenerated, string Message);

/// <summary>
/// Injects synthetic reads that are indistinguishable, once inside the queue, from a real
/// reader's traffic.
///
/// This exists so the pipeline can be exercised end to end without hardware - during
/// development, in automated tests, and when a school wants to prove the parent notification
/// flow before the readers are mounted. Two properties keep it honest:
/// <list type="bullet">
///   <item>it enters through the same queue as real traffic, so nothing downstream is
///   special-cased for simulated data;</item>
///   <item>it is gated behind its own permission, so it cannot be used to fabricate
///   attendance by anyone who merely has admin access.</item>
/// </list>
/// </summary>
public class RfidSimulator : IRfidSimulator
{
    private readonly CampusTrackDbContext _db;
    private readonly IRfidIngestQueue _queue;
    private readonly IDateTimeProvider _clock;
    private readonly ILogger<RfidSimulator> _logger;

    public RfidSimulator(
        CampusTrackDbContext db,
        IRfidIngestQueue queue,
        IDateTimeProvider clock,
        ILogger<RfidSimulator> logger)
    {
        _db = db;
        _queue = queue;
        _clock = clock;
        _logger = logger;
    }

    public async Task<SimulationResult> SimulatePassAsync(SimulationRequest request, CancellationToken ct = default)
    {
        var reader = await _db.RfidReaders.AsNoTracking()
            .Where(r => r.DeviceId == request.DeviceId)
            .Select(r => new { r.Id, r.DeviceId, r.SchoolId, r.AntennaCount, r.IsActive })
            .FirstOrDefaultAsync(ct);

        if (reader is null)
            return new SimulationResult(false, 0, $"No reader is registered with device id '{request.DeviceId}'.");

        if (!reader.IsActive)
            return new SimulationResult(false, 0, "That reader is disabled.");

        var epc = RfidIngestionService.NormaliseEpc(request.Epc);
        if (epc is null)
            return new SimulationResult(false, 0, "That EPC is not valid hexadecimal.");

        var antennaCount = Math.Max(reader.AntennaCount, 1);
        var start = request.AtUtc ?? _clock.UtcNow;

        // Walk the antennas in the order a person moving that way would trip them.
        var antennaOrder = request.SingleAntennaOnly
            ? [1]
            : request.Direction == MovementDirection.Entry
                ? Enumerable.Range(1, antennaCount).ToArray()
                : Enumerable.Range(1, antennaCount).Reverse().ToArray();

        var generated = 0;
        var offsetMs = 0;

        foreach (var antenna in antennaOrder)
        {
            for (var i = 0; i < Math.Max(1, request.ReadsPerAntenna); i++)
            {
                var read = new QueuedRead(
                    reader.Id, reader.DeviceId, epc, antenna,
                    start.AddMilliseconds(offsetMs), _clock.UtcNow,
                    // Plausible signal strength with a little jitter, so RSSI filtering is exercised.
                    Rssi: -45 - Random.Shared.Next(0, 12),
                    TagUid: null,
                    BatchId: null,
                    SchoolId: reader.SchoolId);

                if (_queue.TryEnqueue(read)) generated++;
                offsetMs += 60 + Random.Shared.Next(0, 40);
            }
        }

        _logger.LogInformation(
            "Simulated {Direction} pass for {Epc} at {DeviceId}: {Count} read(s) queued",
            request.Direction, RfidMovementService.MaskEpc(epc), reader.DeviceId, generated);

        return new SimulationResult(true, generated,
            $"Queued {generated} read(s). The movement will appear once the quiet window elapses.");
    }

    /// <summary>
    /// Replays a plausible school day for one student: arrival, the rooms their timetable
    /// says they should visit, and departure. Useful for demonstrating the parent timeline
    /// and the daily report with realistic data.
    /// </summary>
    public async Task<SimulationResult> SimulateSchoolDayAsync(int studentId, DateOnly date, CancellationToken ct = default)
    {
        var tag = await _db.RfidTags.AsNoTracking()
            .Where(t => t.StudentId == studentId && t.Status == RfidTagStatus.Active)
            .Select(t => t.Epc)
            .FirstOrDefaultAsync(ct);

        if (tag is null)
            return new SimulationResult(false, 0, "That student has no active RFID card.");

        var gate = await _db.RfidReaders.AsNoTracking()
            .Where(r => r.IsActive && r.Location!.IsCampusBoundary)
            .Select(r => r.DeviceId)
            .FirstOrDefaultAsync(ct);

        if (gate is null)
            return new SimulationResult(false, 0, "No gate reader is configured.");

        var sectionId = await _db.Students.AsNoTracking()
            .Where(s => s.Id == studentId).Select(s => s.CurrentSectionId).FirstOrDefaultAsync(ct);

        var isoDay = (int)date.DayOfWeek == 0 ? 7 : (int)date.DayOfWeek;

        var lessons = sectionId is null
            ? []
            : await _db.TimetableSlots.AsNoTracking()
                .Where(s => s.SectionId == sectionId && s.DayOfWeek == isoDay && s.IsActive)
                .OrderBy(s => s.StartTime)
                .Select(s => new { s.StartTime, s.EndTime, s.ClassroomId })
                .ToListAsync(ct);

        var total = 0;

        // Arrive a few minutes before the first lesson, or at 07:50 when there are none.
        var firstStart = lessons.Count > 0 ? lessons[0].StartTime : new TimeOnly(8, 0);
        var arrival = _clock.ToUtc(date.ToDateTime(firstStart.AddMinutes(-10)));

        var entry = await SimulatePassAsync(new SimulationRequest
        {
            DeviceId = gate, Epc = tag, Direction = MovementDirection.Entry, AtUtc = arrival
        }, ct);
        total += entry.ReadsGenerated;

        foreach (var lesson in lessons)
        {
            if (lesson.ClassroomId is null) continue;

            var roomDevice = await _db.RfidReaders.AsNoTracking()
                .Where(r => r.IsActive && r.Location!.ClassroomId == lesson.ClassroomId)
                .Select(r => r.DeviceId)
                .FirstOrDefaultAsync(ct);

            if (roomDevice is null) continue;

            var inAt = _clock.ToUtc(date.ToDateTime(lesson.StartTime.AddMinutes(-2)));
            var outAt = _clock.ToUtc(date.ToDateTime(lesson.EndTime));

            total += (await SimulatePassAsync(new SimulationRequest
            {
                DeviceId = roomDevice, Epc = tag, Direction = MovementDirection.Entry, AtUtc = inAt
            }, ct)).ReadsGenerated;

            total += (await SimulatePassAsync(new SimulationRequest
            {
                DeviceId = roomDevice, Epc = tag, Direction = MovementDirection.Exit, AtUtc = outAt
            }, ct)).ReadsGenerated;
        }

        var lastEnd = lessons.Count > 0 ? lessons[^1].EndTime : new TimeOnly(14, 30);
        var departure = _clock.ToUtc(date.ToDateTime(lastEnd.AddMinutes(10)));

        total += (await SimulatePassAsync(new SimulationRequest
        {
            DeviceId = gate, Epc = tag, Direction = MovementDirection.Exit, AtUtc = departure
        }, ct)).ReadsGenerated;

        return new SimulationResult(true, total,
            $"Simulated a full day for {date:d}: {lessons.Count} lesson(s), {total} read(s) queued.");
    }
}
