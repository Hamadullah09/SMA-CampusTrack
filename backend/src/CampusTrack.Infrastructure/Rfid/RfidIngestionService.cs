using CampusTrack.Application.Common;
using CampusTrack.Application.Common.Interfaces;
using CampusTrack.Application.Rfid;
using CampusTrack.Domain.Enums;
using CampusTrack.Domain.Rfid;
using CampusTrack.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace CampusTrack.Infrastructure.Rfid;

public interface IRfidIngestionService
{
    Task<RfidIngestResponse> IngestAsync(RfidReadBatch batch, int readerId, CancellationToken ct = default);
    Task RecordHeartbeatAsync(RfidHeartbeat heartbeat, int readerId, CancellationToken ct = default);
}

/// <summary>
/// The front door for reader traffic.
///
/// Its job is narrow on purpose: authenticate-adjacent validation, normalisation, and getting
/// the reads onto the queue. Anything that needs the database, the timetable or a notification
/// happens in <see cref="RfidEventProcessor"/>, off the request path.
/// </summary>
public class RfidIngestionService : IRfidIngestionService
{
    private readonly CampusTrackDbContext _db;
    private readonly IRfidIngestQueue _queue;
    private readonly ISettingsProvider _settings;
    private readonly IDateTimeProvider _clock;
    private readonly IMemoryCache _cache;
    private readonly ILogger<RfidIngestionService> _logger;

    public RfidIngestionService(
        CampusTrackDbContext db,
        IRfidIngestQueue queue,
        ISettingsProvider settings,
        IDateTimeProvider clock,
        IMemoryCache cache,
        ILogger<RfidIngestionService> logger)
    {
        _db = db;
        _queue = queue;
        _settings = settings;
        _clock = clock;
        _cache = cache;
        _logger = logger;
    }

    public async Task<RfidIngestionResponseInternal> LoadReaderAsync(int readerId, CancellationToken ct)
    {
        // Readers change rarely but are read on every ingest call, so this is cached briefly.
        var cacheKey = $"reader:{readerId}";
        if (_cache.TryGetValue(cacheKey, out RfidIngestionResponseInternal? cached) && cached is not null)
            return cached;

        var reader = await _db.RfidReaders.AsNoTracking()
            .Where(r => r.Id == readerId)
            .Select(r => new RfidIngestionResponseInternal(
                r.Id, r.DeviceId, r.SchoolId, r.AntennaCount, r.IsActive, r.MinimumRssi))
            .FirstOrDefaultAsync(ct)
            ?? throw new KeyNotFoundException($"Reader {readerId} not found.");

        _cache.Set(cacheKey, reader, TimeSpan.FromSeconds(60));
        return reader;
    }

    public async Task<RfidIngestResponse> IngestAsync(RfidReadBatch batch, int readerId, CancellationToken ct = default)
    {
        var response = new RfidIngestResponse { Received = batch.Reads.Count };
        var reader = await LoadReaderAsync(readerId, ct);

        if (!reader.IsActive)
        {
            response.Rejected = batch.Reads.Count;
            response.Warnings.Add("This reader is disabled; reads were discarded.");
            return response;
        }

        var maxBatch = await _settings.GetAsync(SettingKeys.RfidMaxIngestBatchSize, 500, ct);
        if (batch.Reads.Count > maxBatch)
        {
            response.Rejected = batch.Reads.Count;
            response.Warnings.Add($"Batch exceeds the maximum of {maxBatch} reads.");
            return response;
        }

        // A gateway that times out and retries must not double-count. The batch id makes the
        // call idempotent, which matters most on exactly the flaky links where retries happen.
        if (!string.IsNullOrWhiteSpace(batch.BatchId))
        {
            var batchKey = $"batch:{reader.DeviceId}:{batch.BatchId}";
            if (_cache.TryGetValue(batchKey, out _))
            {
                response.Duplicate = true;
                response.Warnings.Add("This batch was already accepted.");
                response.QueueDepth = _queue.Depth;
                return response;
            }
            _cache.Set(batchKey, true, TimeSpan.FromMinutes(10));
        }

        var minimumRssi = reader.MinimumRssi
                          ?? await _settings.GetAsync(SettingKeys.RfidMinimumRssi, -70, ct);
        var maxSkew = TimeSpan.FromMinutes(await _settings.GetAsync(SettingKeys.RfidMaxClockSkewMinutes, 10, ct));

        var now = _clock.UtcNow;
        var accepted = 0;
        var rejected = 0;
        var skewWarned = false;

        foreach (var item in batch.Reads)
        {
            var epc = NormaliseEpc(item.Epc);
            if (epc is null)
            {
                rejected++;
                continue;
            }

            if (item.AntennaNumber < 1 || item.AntennaNumber > Math.Max(reader.AntennaCount, 1))
            {
                rejected++;
                response.Warnings.Add($"Antenna {item.AntennaNumber} is not configured on this reader.");
                continue;
            }

            // Stray far-field reads pick up tags in a corridor or a bag two rooms away; they
            // would otherwise generate movement for someone who never approached the door.
            if (item.Rssi is { } rssi && rssi < minimumRssi)
            {
                rejected++;
                continue;
            }

            var readAt = item.ReadAtUtc ?? now;
            if (readAt.Kind != DateTimeKind.Utc) readAt = DateTime.SpecifyKind(readAt, DateTimeKind.Utc);

            // A reader whose clock is wrong would otherwise write attendance into the wrong
            // day. Clamp to server time and say so, rather than trusting or discarding it.
            var skew = readAt - now;
            if (Math.Abs(skew.TotalMinutes) > maxSkew.TotalMinutes)
            {
                if (!skewWarned)
                {
                    skewWarned = true;
                    response.Warnings.Add(
                        $"Device clock is off by {(int)skew.TotalMinutes} minute(s); timestamps were corrected to server time.");
                    _logger.LogWarning("Reader {DeviceId} clock skew of {Skew}; clamping read times",
                        reader.DeviceId, skew);
                }
                readAt = now;
            }

            var queued = new QueuedRead(
                reader.Id, reader.DeviceId, epc, item.AntennaNumber,
                readAt, now, item.Rssi, item.TagUid, batch.BatchId, reader.SchoolId);

            if (_queue.TryEnqueue(queued)) accepted++;
            else rejected++;
        }

        response.Accepted = accepted;
        response.Rejected = rejected;
        response.QueueDepth = _queue.Depth;

        if (rejected > 0 && accepted == 0)
            _logger.LogWarning("All {Count} reads from {DeviceId} were rejected", batch.Reads.Count, reader.DeviceId);

        return response;
    }

    public async Task RecordHeartbeatAsync(RfidHeartbeat heartbeat, int readerId, CancellationToken ct = default)
    {
        var reader = await _db.RfidReaders.FirstOrDefaultAsync(r => r.Id == readerId, ct);
        if (reader is null) return;

        var now = _clock.UtcNow;
        var wasOffline = reader.Status is ReaderStatus.Offline or ReaderStatus.Unknown or ReaderStatus.Error;

        reader.LastHeartbeatUtc = now;
        reader.Status = ReaderStatus.Online;
        if (!string.IsNullOrWhiteSpace(heartbeat.FirmwareVersion)) reader.FirmwareVersion = heartbeat.FirmwareVersion;
        if (!string.IsNullOrWhiteSpace(heartbeat.IpAddress)) reader.IpAddress = heartbeat.IpAddress;

        if (wasOffline)
        {
            reader.LastErrorMessage = null;
            _db.DeviceLogs.Add(new DeviceLog
            {
                SchoolId = reader.SchoolId,
                ReaderId = reader.Id,
                DeviceId = reader.DeviceId,
                Level = DeviceLogLevel.Info,
                EventName = "Reconnected",
                Message = $"Reader {reader.Name} came back online.",
                OccurredAtUtc = now
            });
            _logger.LogInformation("Reader {DeviceId} reconnected", reader.DeviceId);
        }

        await _db.SaveChangesAsync(ct);
        _cache.Remove($"reader:{readerId}");
    }

    /// <summary>
    /// Upper-cases and strips separators so the same physical tag always resolves to one
    /// value, whatever formatting a given reader model emits.
    /// </summary>
    public static string? NormaliseEpc(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        Span<char> buffer = stackalloc char[raw.Length];
        var length = 0;

        foreach (var c in raw)
        {
            if (c is ' ' or '-' or ':' or '.') continue;
            if (!Uri.IsHexDigit(c)) return null;      // an EPC is hex; anything else is corrupt
            buffer[length++] = char.ToUpperInvariant(c);
        }

        // Guards against both a truncated read and an absurdly long payload.
        return length is < 8 or > 64 ? null : new string(buffer[..length]);
    }
}

/// <summary>Cached reader facts needed on the ingestion path.</summary>
public record RfidIngestionResponseInternal(
    int Id, string DeviceId, int SchoolId, int AntennaCount, bool IsActive, int? MinimumRssi);
