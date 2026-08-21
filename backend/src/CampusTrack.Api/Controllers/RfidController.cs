using CampusTrack.Application.Authorization;
using CampusTrack.Application.Common.Interfaces;
using CampusTrack.Application.Common.Models;
using CampusTrack.Application.Rfid;
using CampusTrack.Domain.Common;
using CampusTrack.Domain.Enums;
using CampusTrack.Domain.Rfid;
using CampusTrack.Infrastructure.Identity;
using CampusTrack.Infrastructure.Persistence;
using CampusTrack.Infrastructure.Rfid;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CampusTrack.Api.Controllers;

/// <summary>
/// RFID administration and monitoring: events, readers, locations, tags, the live view,
/// the simulator and the dead-letter queue.
/// </summary>
[Route("api/v1/rfid")]
public class RfidController : ApiControllerBase
{
    private readonly CampusTrackDbContext _db;
    private readonly IRfidQueryService _queries;
    private readonly IRfidSimulator _simulator;
    private readonly IRfidIngestQueue _queue;
    private readonly TagSequenceBuffer _buffer;
    private readonly ITokenHasher _hasher;
    private readonly IDateTimeProvider _clock;

    public RfidController(
        CampusTrackDbContext db,
        IRfidQueryService queries,
        IRfidSimulator simulator,
        IRfidIngestQueue queue,
        TagSequenceBuffer buffer,
        ITokenHasher hasher,
        IDateTimeProvider clock)
    {
        _db = db;
        _queries = queries;
        _simulator = simulator;
        _queue = queue;
        _buffer = buffer;
        _hasher = hasher;
        _clock = clock;
    }

    // ------------------------------------------------------------------ events ----

    /// <summary>Movement events, filtered and paged.</summary>
    [HttpGet("events")]
    [HasPermission(Permissions.Rfid.ViewEvents)]
    public async Task<ActionResult<PagedResult<RfidEventDto>>> GetEvents(
        [FromQuery] RfidEventQuery query, CancellationToken ct)
        => Paged(await _queries.GetEventsAsync(query, ct));

    /// <summary>The newest events, for the live monitor's initial paint before SignalR takes over.</summary>
    [HttpGet("events/recent")]
    [HasPermission(Permissions.Rfid.ViewEvents)]
    public async Task<ActionResult<IReadOnlyList<RfidEventDto>>> GetRecentEvents(
        [FromQuery] int count = 25, CancellationToken ct = default)
        => Ok(await _queries.GetRecentAsync(count, ct));

    /// <summary>A student's movement timeline for one day.</summary>
    [HttpGet("students/{studentId:int}/timeline")]
    [HasPermission(Permissions.Rfid.ViewEvents)]
    public async Task<ActionResult<IReadOnlyList<ActivityTimelineEntry>>> GetTimeline(
        int studentId, [FromQuery] DateOnly? date, CancellationToken ct)
        => Ok(await _queries.GetStudentTimelineAsync(studentId, date ?? _clock.SchoolToday, ct));

    /// <summary>How many students are on site right now.</summary>
    [HttpGet("presence")]
    [HasPermission(Permissions.Rfid.Monitor)]
    public async Task<ActionResult<PresenceSummary>> GetPresence(CancellationToken ct)
        => Ok(await _queries.GetPresenceSummaryAsync(ct));

    /// <summary>Who is currently inside, with where they were last seen.</summary>
    [HttpGet("presence/students")]
    [HasPermission(Permissions.Rfid.Monitor)]
    public async Task<ActionResult<PagedResult<object>>> GetStudentsOnSite(
        [FromQuery] PagedQuery query, CancellationToken ct)
    {
        var q = _db.StudentPresences.AsNoTracking()
            .Where(p => p.State != PresenceState.Outside)
            .OrderByDescending(p => p.SinceUtc)
            .Select(p => (object)new
            {
                p.StudentId,
                studentName = p.Student!.User!.FirstName + " " + p.Student.User.LastName,
                studentCode = p.Student.StudentCode,
                sectionName = p.Student.CurrentSection!.DisplayName,
                state = p.State.ToString(),
                locationName = p.CurrentLocation!.Name,
                sinceUtc = p.SinceUtc,
                lastEntryAtUtc = p.LastEntryAtUtc
            });

        return Paged(await q.ToPagedResultAsync(query.Page, query.PageSize, ct));
    }

    // ----------------------------------------------------------------- readers ----

    /// <summary>Every reader with its live status, for the monitoring screen and floor plan.</summary>
    [HttpGet("readers")]
    [HasPermission(Permissions.Rfid.ViewReaders)]
    public async Task<ActionResult<IReadOnlyList<ReaderStatusDto>>> GetReaders(CancellationToken ct)
        => Ok(await _queries.GetReaderStatusAsync(ct));

    [HttpGet("readers/{id:int}")]
    [HasPermission(Permissions.Rfid.ViewReaders)]
    public async Task<ActionResult<object>> GetReader(int id, CancellationToken ct)
    {
        var reader = await _db.RfidReaders.AsNoTracking()
            .Where(r => r.Id == id)
            .Select(r => new
            {
                r.Id, r.DeviceId, r.Name, r.Model, r.SerialNumber, r.FirmwareVersion,
                r.IpAddress, r.MacAddress, r.Port,
                r.LocationId, locationName = r.Location!.Name, locationType = r.Location.LocationType,
                r.DirectionStrategy, r.AntennaCount, r.FixedDirection,
                r.QuietWindowMs, r.DebounceSeconds, r.MinimumRssi,
                r.Status, r.LastHeartbeatUtc, r.LastEventUtc, r.LastErrorMessage, r.LastErrorAtUtc,
                r.HeartbeatIntervalSeconds, r.IsActive,
                hasApiKey = r.ApiKeyHash != null,
                antennas = r.Antennas.OrderBy(a => a.AntennaNumber)
                    .Select(a => new { a.Id, a.AntennaNumber, a.Role, a.Label, a.PowerDbm, a.IsActive })
            })
            .FirstOrDefaultAsync(ct);

        return Ok(Found(reader, "reader"));
    }

    /// <summary>Recent events from one reader — the drill-down when an operator clicks it on the map.</summary>
    [HttpGet("readers/{id:int}/events")]
    [HasPermission(Permissions.Rfid.ViewReaders)]
    public async Task<ActionResult<PagedResult<RfidEventDto>>> GetReaderEvents(
        int id, [FromQuery] PagedQuery paging, CancellationToken ct)
        => Paged(await _queries.GetEventsAsync(
            new RfidEventQuery { ReaderId = id, Page = paging.Page, PageSize = paging.PageSize, IncludeRejected = true }, ct));

    [HttpGet("readers/{id:int}/logs")]
    [HasPermission(Permissions.Audit.ViewDeviceLogs)]
    public async Task<ActionResult<PagedResult<object>>> GetReaderLogs(
        int id, [FromQuery] PagedQuery paging, CancellationToken ct)
    {
        var q = _db.DeviceLogs.AsNoTracking()
            .Where(l => l.ReaderId == id)
            .OrderByDescending(l => l.OccurredAtUtc)
            .Select(l => (object)new { l.Id, l.Level, l.EventName, l.Message, l.OccurredAtUtc });

        return Paged(await q.ToPagedResultAsync(paging.Page, paging.PageSize, ct));
    }

    [HttpPost("readers")]
    [HasPermission(Permissions.Rfid.ManageReaders)]
    public async Task<ActionResult<object>> CreateReader(CreateReaderRequest request, CancellationToken ct)
    {
        var duplicate = await _db.RfidReaders.AnyAsync(r => r.DeviceId == request.DeviceId, ct);
        if (duplicate) throw DomainException.Conflict($"A reader with device id '{request.DeviceId}' already exists.");

        var locationExists = await _db.RfidLocations.AnyAsync(l => l.Id == request.LocationId, ct);
        if (!locationExists) throw DomainException.Invalid("That location does not exist.");

        var reader = new RfidReader
        {
            DeviceId = request.DeviceId.Trim(),
            Name = request.Name.Trim(),
            Model = string.IsNullOrWhiteSpace(request.Model) ? "D2184" : request.Model.Trim(),
            SerialNumber = request.SerialNumber,
            IpAddress = request.IpAddress,
            MacAddress = request.MacAddress,
            Port = request.Port,
            LocationId = request.LocationId,
            DirectionStrategy = request.DirectionStrategy,
            AntennaCount = Math.Clamp(request.AntennaCount, 1, 32),
            FixedDirection = request.FixedDirection,
            QuietWindowMs = request.QuietWindowMs,
            DebounceSeconds = request.DebounceSeconds,
            MinimumRssi = request.MinimumRssi,
            HeartbeatIntervalSeconds = request.HeartbeatIntervalSeconds ?? 60,
            IsActive = true,
            Status = ReaderStatus.Unknown
        };

        // Antenna roles are what make direction resolution robust, so a reader is created
        // with a row per port even when the installer has not declared roles yet.
        for (var i = 1; i <= reader.AntennaCount; i++)
        {
            var declared = request.Antennas?.FirstOrDefault(a => a.AntennaNumber == i);
            reader.Antennas.Add(new ReaderAntenna
            {
                AntennaNumber = i,
                Role = declared?.Role ?? AntennaRole.Unspecified,
                Label = declared?.Label,
                PowerDbm = declared?.PowerDbm,
                IsActive = true
            });
        }

        _db.RfidReaders.Add(reader);
        await _db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(GetReader), new { id = reader.Id }, new { reader.Id, reader.DeviceId, reader.Name });
    }

    [HttpPut("readers/{id:int}")]
    [HasPermission(Permissions.Rfid.ManageReaders)]
    public async Task<IActionResult> UpdateReader(int id, UpdateReaderRequest request, CancellationToken ct)
    {
        var reader = Found(await _db.RfidReaders.Include(r => r.Antennas)
            .FirstOrDefaultAsync(r => r.Id == id, ct), "reader");

        reader.Name = request.Name.Trim();
        reader.Model = request.Model ?? reader.Model;
        reader.SerialNumber = request.SerialNumber;
        reader.IpAddress = request.IpAddress;
        reader.MacAddress = request.MacAddress;
        reader.Port = request.Port;
        reader.LocationId = request.LocationId;
        reader.DirectionStrategy = request.DirectionStrategy;
        reader.FixedDirection = request.FixedDirection;
        reader.QuietWindowMs = request.QuietWindowMs;
        reader.DebounceSeconds = request.DebounceSeconds;
        reader.MinimumRssi = request.MinimumRssi;
        reader.HeartbeatIntervalSeconds = request.HeartbeatIntervalSeconds ?? reader.HeartbeatIntervalSeconds;
        reader.IsActive = request.IsActive;

        if (request.Antennas is { Count: > 0 })
        {
            foreach (var update in request.Antennas)
            {
                var antenna = reader.Antennas.FirstOrDefault(a => a.AntennaNumber == update.AntennaNumber);
                if (antenna is null)
                {
                    reader.Antennas.Add(new ReaderAntenna
                    {
                        AntennaNumber = update.AntennaNumber,
                        Role = update.Role,
                        Label = update.Label,
                        PowerDbm = update.PowerDbm,
                        IsActive = true
                    });
                }
                else
                {
                    antenna.Role = update.Role;
                    antenna.Label = update.Label;
                    antenna.PowerDbm = update.PowerDbm;
                }
            }

            reader.AntennaCount = reader.Antennas.Count(a => a.IsActive);
        }

        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>
    /// Issues a new device key. The plaintext is returned exactly once and only its hash is
    /// stored, so it must be copied into the reader's configuration now.
    /// </summary>
    [HttpPost("readers/{id:int}/api-key")]
    [HasPermission(Permissions.Rfid.ManageReaders)]
    public async Task<ActionResult<object>> IssueApiKey(int id, CancellationToken ct)
    {
        var reader = Found(await _db.RfidReaders.FirstOrDefaultAsync(r => r.Id == id, ct), "reader");

        var plainKey = _hasher.GenerateSecureToken(32);
        reader.ApiKeyHash = _hasher.Hash(plainKey);
        reader.ApiKeyIssuedAtUtc = _clock.UtcNow;
        await _db.SaveChangesAsync(ct);

        return Ok(new
        {
            deviceId = reader.DeviceId,
            apiKey = plainKey,
            issuedAtUtc = reader.ApiKeyIssuedAtUtc,
            warning = "Copy this key now. It is stored only as a hash and cannot be shown again."
        });
    }

    [HttpDelete("readers/{id:int}")]
    [HasPermission(Permissions.Rfid.ManageReaders)]
    public async Task<IActionResult> DeleteReader(int id, CancellationToken ct)
    {
        var reader = Found(await _db.RfidReaders.FirstOrDefaultAsync(r => r.Id == id, ct), "reader");
        _db.RfidReaders.Remove(reader);      // the interceptor turns this into a soft delete
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    // --------------------------------------------------------------- locations ----

    [HttpGet("locations")]
    [HasPermission(Permissions.Rfid.ViewLocations)]
    public async Task<ActionResult<IReadOnlyList<object>>> GetLocations(CancellationToken ct)
    {
        var locations = await _db.RfidLocations.AsNoTracking()
            .OrderBy(l => l.LocationType).ThenBy(l => l.Name)
            .Select(l => (object)new
            {
                l.Id, l.Name, l.Code, l.LocationType, l.Description, l.Building, l.Floor,
                l.IsCampusBoundary, l.ClassroomId, classroomName = l.Classroom!.Name,
                l.MapX, l.MapY, l.NotifyGuardians, l.AffectsAttendance, l.IsActive,
                readerCount = l.Readers.Count(r => r.IsActive),
                onlineReaders = l.Readers.Count(r => r.IsActive && r.Status == ReaderStatus.Online)
            })
            .ToListAsync(ct);

        return Ok(locations);
    }

    [HttpPost("locations")]
    [HasPermission(Permissions.Rfid.ManageLocations)]
    public async Task<ActionResult<object>> CreateLocation(CreateLocationRequest request, CancellationToken ct)
    {
        if (await _db.RfidLocations.AnyAsync(l => l.Code == request.Code, ct))
            throw DomainException.Conflict($"A location with code '{request.Code}' already exists.");

        var location = new RfidLocation
        {
            Name = request.Name.Trim(),
            Code = request.Code.Trim().ToUpperInvariant(),
            LocationType = request.LocationType,
            Description = request.Description,
            Building = request.Building,
            Floor = request.Floor,
            // Gates default to being campus boundaries, which is what drives arrival and
            // departure notifications; anything else defaults to not.
            IsCampusBoundary = request.IsCampusBoundary
                               ?? request.LocationType is LocationType.MainGate or LocationType.ExitGate,
            ClassroomId = request.ClassroomId,
            MapX = request.MapX,
            MapY = request.MapY,
            NotifyGuardians = request.NotifyGuardians
                              ?? request.LocationType is LocationType.MainGate or LocationType.ExitGate,
            AffectsAttendance = request.AffectsAttendance ?? true,
            IsActive = true
        };

        _db.RfidLocations.Add(location);
        await _db.SaveChangesAsync(ct);

        return Created($"/api/v1/rfid/locations/{location.Id}", new { location.Id, location.Name, location.Code });
    }

    [HttpPut("locations/{id:int}")]
    [HasPermission(Permissions.Rfid.ManageLocations)]
    public async Task<IActionResult> UpdateLocation(int id, CreateLocationRequest request, CancellationToken ct)
    {
        var location = Found(await _db.RfidLocations.FirstOrDefaultAsync(l => l.Id == id, ct), "location");

        location.Name = request.Name.Trim();
        location.LocationType = request.LocationType;
        location.Description = request.Description;
        location.Building = request.Building;
        location.Floor = request.Floor;
        location.ClassroomId = request.ClassroomId;
        location.MapX = request.MapX;
        location.MapY = request.MapY;
        if (request.IsCampusBoundary is { } boundary) location.IsCampusBoundary = boundary;
        if (request.NotifyGuardians is { } notify) location.NotifyGuardians = notify;
        if (request.AffectsAttendance is { } affects) location.AffectsAttendance = affects;

        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>
    /// Removes a location. Refused while a reader is still mounted there, because a
    /// reader with no location cannot have its passes resolved into a direction.
    /// </summary>
    [HttpDelete("locations/{id:int}")]
    [HasPermission(Permissions.Rfid.ManageLocations)]
    public async Task<IActionResult> DeleteLocation(int id, CancellationToken ct)
    {
        var location = Found(await _db.RfidLocations.FirstOrDefaultAsync(l => l.Id == id, ct), "location");

        var readers = await _db.RfidReaders.CountAsync(r => r.LocationId == id, ct);
        if (readers > 0)
            throw DomainException.Conflict(
                $"{readers} reader(s) are installed at this location. Move or remove them first.");

        _db.RfidLocations.Remove(location);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    // -------------------------------------------------------------------- tags ----

    [HttpGet("tags")]
    [HasPermission(Permissions.Rfid.ViewTags)]
    public async Task<ActionResult<PagedResult<object>>> GetTags(
        [FromQuery] PagedQuery paging, [FromQuery] RfidTagStatus? status, CancellationToken ct)
    {
        var q = _db.RfidTags.AsNoTracking().AsQueryable();
        if (status is { } s) q = q.Where(t => t.Status == s);

        if (!string.IsNullOrWhiteSpace(paging.Search))
        {
            var term = paging.Search.Trim();
            q = q.Where(t => t.Epc.Contains(term)
                             || (t.CardNumber != null && t.CardNumber.Contains(term))
                             || (t.Student != null && t.Student.StudentCode.Contains(term))
                             || (t.Student != null && t.Student.User!.FirstName.Contains(term))
                             || (t.Student != null && t.Student.User!.LastName.Contains(term)));
        }

        var projected = q.OrderByDescending(t => t.CreatedAtUtc).Select(t => (object)new
        {
            t.Id,
            // Full EPC is shown only to holders of the tag-management permission, which this
            // endpoint already requires - it is needed to match a card against its printout.
            t.Epc,
            t.CardNumber,
            t.Status,
            t.StudentId,
            studentName = t.Student == null ? null : t.Student.User!.FirstName + " " + t.Student.User.LastName,
            studentCode = t.Student == null ? null : t.Student.StudentCode,
            t.TeacherId,
            teacherName = t.Teacher == null ? null : t.Teacher.User!.FirstName + " " + t.Teacher.User.LastName,
            t.IssuedAtUtc,
            t.LastSeenAtUtc,
            lastSeenLocation = _db.RfidLocations.Where(l => l.Id == t.LastSeenLocationId).Select(l => l.Name).FirstOrDefault()
        });

        return Paged(await projected.ToPagedResultAsync(paging.Page, paging.PageSize, ct));
    }

    /// <summary>
    /// Assigns a card to a student. Any card the student currently holds is revoked first, so
    /// a lost card cannot keep generating movement in their name.
    /// </summary>
    [HttpPost("tags/assign")]
    [HasPermission(Permissions.Rfid.ManageTags)]
    public async Task<ActionResult<object>> AssignTag(AssignTagRequest request, CancellationToken ct)
    {
        var epc = RfidIngestionService.NormaliseEpc(request.Epc)
                  ?? throw DomainException.Invalid("That EPC is not valid hexadecimal.");

        var student = Found(await _db.Students.FirstOrDefaultAsync(s => s.Id == request.StudentId, ct), "student");

        var existing = await _db.RfidTags.FirstOrDefaultAsync(t => t.Epc == epc, ct);
        if (existing is not null && existing.StudentId is not null && existing.StudentId != request.StudentId)
            throw DomainException.Conflict("That card is already assigned to another person.");

        var currentTags = await _db.RfidTags
            .Where(t => t.StudentId == request.StudentId && t.Status == RfidTagStatus.Active && t.Epc != epc)
            .ToListAsync(ct);

        foreach (var previous in currentTags)
        {
            previous.Status = RfidTagStatus.Replaced;
            previous.RevokedAtUtc = _clock.UtcNow;
            previous.RevokedByUserId = CurrentUser.UserId;
            previous.RevokedReason = "Replaced by a newly issued card";
        }

        if (existing is null)
        {
            existing = new RfidTag { Epc = epc };
            _db.RfidTags.Add(existing);
        }

        existing.StudentId = request.StudentId;
        existing.TeacherId = null;
        existing.StaffMemberId = null;
        existing.CardNumber = request.CardNumber;
        existing.TagUid = request.TagUid;
        existing.Status = RfidTagStatus.Active;
        existing.IssuedAtUtc = _clock.UtcNow;
        existing.IssuedByUserId = CurrentUser.UserId;
        existing.RevokedAtUtc = null;
        existing.RevokedReason = null;
        if (currentTags.Count > 0) existing.ReplacesTagId = currentTags[0].Id;

        await _db.SaveChangesAsync(ct);

        return Ok(new
        {
            tagId = existing.Id,
            studentCode = student.StudentCode,
            epc = existing.Epc,
            status = existing.Status.ToString(),
            replacedCount = currentTags.Count
        });
    }

    /// <summary>Revokes a card (lost, damaged or withdrawn). Its movement history is retained.</summary>
    [HttpPost("tags/{id:int}/revoke")]
    [HasPermission(Permissions.Rfid.ManageTags)]
    public async Task<IActionResult> RevokeTag(int id, RevokeTagRequest request, CancellationToken ct)
    {
        var tag = Found(await _db.RfidTags.FirstOrDefaultAsync(t => t.Id == id, ct), "card");

        tag.Status = request.Status is RfidTagStatus.Lost or RfidTagStatus.Damaged or RfidTagStatus.Revoked
            ? request.Status
            : RfidTagStatus.Revoked;
        tag.RevokedAtUtc = _clock.UtcNow;
        tag.RevokedByUserId = CurrentUser.UserId;
        tag.RevokedReason = request.Reason;

        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    // -------------------------------------------------------------- diagnostics ----

    /// <summary>Pipeline health: queue depth, throughput and how many passes are mid-flight.</summary>
    [HttpGet("pipeline")]
    [HasPermission(Permissions.Rfid.Monitor)]
    public ActionResult<object> GetPipelineStatus() => Ok(new
    {
        queueDepth = _queue.Depth,
        queueCapacity = _queue.Capacity,
        totalEnqueued = _queue.TotalEnqueued,
        totalDropped = _queue.TotalDropped,
        pendingSequences = _buffer.PendingSequences,
        // A non-zero drop count means the processor could not keep up and reads were lost.
        healthy = _queue.TotalDropped == 0 && _queue.Depth < _queue.Capacity * 0.8,
        asOfUtc = _clock.UtcNow
    });

    /// <summary>Injects a synthetic pass for testing, without hardware.</summary>
    [HttpPost("simulate")]
    [HasPermission(Permissions.Rfid.Simulate)]
    public async Task<ActionResult<SimulationResult>> Simulate(SimulationRequest request, CancellationToken ct)
        => Ok(await _simulator.SimulatePassAsync(request, ct));

    /// <summary>Replays a plausible full school day for one student.</summary>
    [HttpPost("simulate/school-day")]
    [HasPermission(Permissions.Rfid.Simulate)]
    public async Task<ActionResult<SimulationResult>> SimulateDay(
        [FromQuery] int studentId, [FromQuery] DateOnly? date, CancellationToken ct)
        => Ok(await _simulator.SimulateSchoolDayAsync(studentId, date ?? _clock.SchoolToday, ct));

    /// <summary>Batches that failed processing and are awaiting attention.</summary>
    [HttpGet("dead-letters")]
    [HasPermission(Permissions.Rfid.ReplayDeadLetters)]
    public async Task<ActionResult<PagedResult<object>>> GetDeadLetters(
        [FromQuery] PagedQuery paging, [FromQuery] bool includeResolved = false, CancellationToken ct = default)
    {
        var q = _db.RfidDeadLetters.AsNoTracking().AsQueryable();
        if (!includeResolved) q = q.Where(d => !d.IsResolved);

        var projected = q.OrderByDescending(d => d.LastFailedAtUtc)
            .Select(d => (object)new
            {
                d.Id, d.DeviceId, d.ErrorMessage, d.RetryCount,
                d.FirstFailedAtUtc, d.LastFailedAtUtc, d.IsResolved, d.PayloadJson
            });

        return Paged(await projected.ToPagedResultAsync(paging.Page, paging.PageSize, ct));
    }

    [HttpPost("dead-letters/{id:long}/resolve")]
    [HasPermission(Permissions.Rfid.ReplayDeadLetters)]
    public async Task<IActionResult> ResolveDeadLetter(long id, [FromBody] ResolveDeadLetterRequest request, CancellationToken ct)
    {
        var letter = Found(await _db.RfidDeadLetters.FirstOrDefaultAsync(d => d.Id == id, ct), "entry");

        letter.IsResolved = true;
        letter.ResolvedAtUtc = _clock.UtcNow;
        letter.ResolvedByUserId = CurrentUser.UserId;
        letter.ResolutionNotes = request.Notes;

        await _db.SaveChangesAsync(ct);
        return NoContent();
    }
}

// ------------------------------------------------------------------- requests ----

public record CreateReaderRequest
{
    public required string DeviceId { get; init; }
    public required string Name { get; init; }
    public string? Model { get; init; }
    public string? SerialNumber { get; init; }
    public string? IpAddress { get; init; }
    public string? MacAddress { get; init; }
    public int? Port { get; init; }
    public required int LocationId { get; init; }
    public DirectionStrategy DirectionStrategy { get; init; } = DirectionStrategy.AntennaOrder;
    public int AntennaCount { get; init; } = 2;
    public MovementDirection FixedDirection { get; init; } = MovementDirection.Unknown;
    public int? QuietWindowMs { get; init; }
    public int? DebounceSeconds { get; init; }
    public int? MinimumRssi { get; init; }
    public int? HeartbeatIntervalSeconds { get; init; }
    public List<AntennaConfig>? Antennas { get; init; }
}

public record UpdateReaderRequest : CreateReaderRequest
{
    public bool IsActive { get; init; } = true;
}

public record AntennaConfig
{
    public int AntennaNumber { get; init; }
    public AntennaRole Role { get; init; } = AntennaRole.Unspecified;
    public string? Label { get; init; }
    public int? PowerDbm { get; init; }
}

public record CreateLocationRequest
{
    public required string Name { get; init; }
    public required string Code { get; init; }
    public LocationType LocationType { get; init; } = LocationType.Other;
    public string? Description { get; init; }
    public string? Building { get; init; }
    public string? Floor { get; init; }
    public bool? IsCampusBoundary { get; init; }
    public int? ClassroomId { get; init; }
    public double? MapX { get; init; }
    public double? MapY { get; init; }
    public bool? NotifyGuardians { get; init; }
    public bool? AffectsAttendance { get; init; }
}

public record AssignTagRequest
{
    public required string Epc { get; init; }
    public required int StudentId { get; init; }
    public string? CardNumber { get; init; }
    public string? TagUid { get; init; }
}

public record RevokeTagRequest
{
    public RfidTagStatus Status { get; init; } = RfidTagStatus.Revoked;
    public string? Reason { get; init; }
}

public record ResolveDeadLetterRequest
{
    public string? Notes { get; init; }
}
