using CampusTrack.Application.Common;
using CampusTrack.Application.Common.Interfaces;
using CampusTrack.Application.Rfid;
using CampusTrack.Domain.Enums;
using CampusTrack.Domain.Rfid;
using CampusTrack.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CampusTrack.Infrastructure.Rfid;

/// <summary>
/// The background half of the RFID pipeline.
///
/// Two loops run concurrently:
/// <list type="number">
///   <item><b>Drain</b> - pulls reads off the queue, persists them in batches, and feeds the
///   sequence buffer. Batching matters: writing one row per antenna hit individually would
///   put thousands of round trips per second on MySQL during arrival.</item>
///   <item><b>Sweep</b> - every second, asks the buffer which pass-throughs have finished and
///   converts each into a movement event.</item>
/// </list>
///
/// Failures are contained per item. One malformed read, one student with a broken timetable,
/// or one notification provider outage must not stop the pipeline for the whole school, so
/// each sequence is processed in its own scope and its own try/catch, with anything that
/// exhausts its retries going to the dead-letter table rather than to the floor.
/// </summary>
public class RfidEventProcessor : BackgroundService
{
    private static readonly TimeSpan SweepInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan BatchLingerTime = TimeSpan.FromMilliseconds(250);
    private const int MaxBatchSize = 500;
    private const int MaxProcessAttempts = 3;

    private readonly IRfidIngestQueue _queue;
    private readonly TagSequenceBuffer _buffer;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<RfidEventProcessor> _logger;

    public RfidEventProcessor(
        IRfidIngestQueue queue,
        TagSequenceBuffer buffer,
        IServiceScopeFactory scopeFactory,
        ILogger<RfidEventProcessor> logger)
    {
        _queue = queue;
        _buffer = buffer;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("RFID event processor started");

        var drain = DrainQueueAsync(stoppingToken);
        var sweep = SweepSequencesAsync(stoppingToken);

        await Task.WhenAll(drain, sweep);

        // On shutdown, flush whatever is still buffered so a restart does not lose the
        // pass-throughs that were mid-flight.
        await FlushOnShutdownAsync();
        _logger.LogInformation("RFID event processor stopped");
    }

    private async Task DrainQueueAsync(CancellationToken ct)
    {
        var batch = new List<QueuedRead>(MaxBatchSize);

        // A periodic flush is essential, not a nicety. Flushing only when the next read
        // arrives means the tail of a burst - the last reads before the corridor goes quiet -
        // would sit in memory indefinitely and be lost on restart. This timer guarantees a
        // batch is never held longer than the linger time regardless of incoming traffic.
        using var flushTimer = new PeriodicTimer(BatchLingerTime);
        var gate = new SemaphoreSlim(1, 1);

        var flushLoop = Task.Run(async () =>
        {
            try
            {
                while (await flushTimer.WaitForNextTickAsync(ct))
                {
                    await gate.WaitAsync(ct);
                    try
                    {
                        if (batch.Count == 0) continue;
                        var pending = batch.ToList();
                        batch.Clear();
                        await PersistRawReadsAsync(pending, ct);
                    }
                    finally { gate.Release(); }
                }
            }
            catch (OperationCanceledException) { /* shutting down */ }
        }, ct);

        try
        {
            await foreach (var read in _queue.DequeueAllAsync(ct))
            {
                // Feed the sequence buffer first and outside the lock: direction resolution
                // must never wait on a database write.
                _buffer.Add(read.ReaderId, read.Epc, read.AntennaNumber, read.ReadAtUtc, read.Rssi);

                await gate.WaitAsync(ct);
                List<QueuedRead>? full = null;
                try
                {
                    batch.Add(read);
                    if (batch.Count >= MaxBatchSize)
                    {
                        full = batch.ToList();
                        batch.Clear();
                    }
                }
                finally { gate.Release(); }

                if (full is not null) await PersistRawReadsAsync(full, ct);
            }
        }
        catch (OperationCanceledException) { /* shutting down */ }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RFID queue drain loop failed");
        }
        finally
        {
            flushTimer.Dispose();
            try { await flushLoop; } catch { /* already logged */ }

            if (batch.Count > 0) await PersistRawReadsAsync(batch, CancellationToken.None);
            gate.Dispose();
        }
    }

    /// <summary>
    /// Persists a batch of raw hits. A failure here is logged but not retried: the reads have
    /// already reached the sequence buffer, so attendance is unaffected, and the raw table is
    /// an audit aid rather than the system of record for movement.
    /// </summary>
    private async Task PersistRawReadsAsync(List<QueuedRead> batch, CancellationToken ct)
    {
        if (batch.Count == 0) return;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<CampusTrackDbContext>();

            db.RfidRawReads.AddRange(batch.Select(r => new RfidRawRead
            {
                SchoolId = r.SchoolId,
                ReaderId = r.ReaderId,
                AntennaNumber = r.AntennaNumber,
                Epc = r.Epc,
                Rssi = r.Rssi,
                ReadAtUtc = r.ReadAtUtc,
                ReceivedAtUtc = r.ReceivedAtUtc,
                State = RawReadState.Buffered,
                IngestBatchId = r.BatchId
            }));

            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist {Count} raw RFID read(s)", batch.Count);
        }
    }

    private async Task SweepSequencesAsync(CancellationToken ct)
    {
        // Read tuning once at start-up, then refresh periodically: pulling settings on every
        // one-second tick would make the settings table the busiest one in the database.
        var quietWindow = TimeSpan.FromMilliseconds(4000);
        var maxSpan = TimeSpan.FromMilliseconds(30000);
        var lastSettingsRefresh = DateTime.MinValue;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (DateTime.UtcNow - lastSettingsRefresh > TimeSpan.FromMinutes(1))
                {
                    using var scope = _scopeFactory.CreateScope();
                    var settings = scope.ServiceProvider.GetRequiredService<ISettingsProvider>();
                    quietWindow = TimeSpan.FromMilliseconds(
                        await settings.GetAsync(SettingKeys.RfidQuietWindowMs, 4000, ct));
                    maxSpan = TimeSpan.FromMilliseconds(
                        await settings.GetAsync(SettingKeys.RfidMaxSequenceMs, 30000, ct));
                    lastSettingsRefresh = DateTime.UtcNow;
                }

                var completed = _buffer.Sweep(DateTime.UtcNow, quietWindow, maxSpan);
                foreach (var sequence in completed) await ProcessWithRetryAsync(sequence, ct);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "RFID sweep iteration failed");
            }

            try { await Task.Delay(SweepInterval, ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    /// <summary>
    /// Processes one pass-through, retrying transient failures with a short backoff. Anything
    /// still failing after the last attempt is dead-lettered so it can be inspected and
    /// replayed rather than lost.
    /// </summary>
    private async Task ProcessWithRetryAsync(CompletedSequence sequence, CancellationToken ct)
    {
        Exception? lastError = null;

        for (var attempt = 1; attempt <= MaxProcessAttempts; attempt++)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var movements = scope.ServiceProvider.GetRequiredService<IRfidMovementService>();
                await movements.ProcessSequenceAsync(sequence, ct);
                return;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                lastError = ex;
                _logger.LogWarning(ex,
                    "Attempt {Attempt}/{Max} failed for sequence {SequenceId} (reader {ReaderId})",
                    attempt, MaxProcessAttempts, sequence.SequenceId, sequence.ReaderId);

                if (attempt < MaxProcessAttempts)
                {
                    // Exponential backoff gives a database failover or a brief lock storm time
                    // to clear before the next attempt.
                    var delay = TimeSpan.FromMilliseconds(200 * Math.Pow(2, attempt - 1));
                    try { await Task.Delay(delay, ct); }
                    catch (OperationCanceledException) { return; }
                }
            }
        }

        await DeadLetterAsync(sequence, lastError);
    }

    private async Task DeadLetterAsync(CompletedSequence sequence, Exception? error)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<CampusTrackDbContext>();

            var payload = System.Text.Json.JsonSerializer.Serialize(new
            {
                sequence.SequenceId,
                sequence.ReaderId,
                sequence.Epc,
                sequence.FirstReadUtc,
                sequence.LastReadUtc,
                Hits = sequence.Hits.Select(h => new { h.AntennaNumber, h.ReadAtUtc, h.Rssi })
            });

            var deviceId = await db.RfidReaders.AsNoTracking()
                .Where(r => r.Id == sequence.ReaderId).Select(r => r.DeviceId).FirstOrDefaultAsync();

            var now = DateTime.UtcNow;
            db.RfidDeadLetters.Add(new RfidDeadLetter
            {
                DeviceId = deviceId,
                PayloadJson = payload,
                ErrorMessage = Truncate(error?.Message ?? "Unknown failure", 500),
                ErrorDetail = error?.ToString(),
                RetryCount = MaxProcessAttempts,
                FirstFailedAtUtc = now,
                LastFailedAtUtc = now
            });

            await db.SaveChangesAsync();

            _logger.LogError(error,
                "Sequence {SequenceId} for reader {ReaderId} dead-lettered after {Attempts} attempts",
                sequence.SequenceId, sequence.ReaderId, MaxProcessAttempts);
        }
        catch (Exception ex)
        {
            // If even the dead-letter write fails the database is unreachable; the structured
            // log is the last line of defence and must still carry the payload.
            _logger.LogCritical(ex,
                "Could not dead-letter sequence {SequenceId} (reader {ReaderId}, tag {Epc})",
                sequence.SequenceId, sequence.ReaderId, RfidMovementService.MaskEpc(sequence.Epc));
        }
    }

    private async Task FlushOnShutdownAsync()
    {
        var pending = _buffer.DrainAll();
        if (pending.Count == 0) return;

        _logger.LogInformation("Flushing {Count} buffered RFID sequence(s) on shutdown", pending.Count);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        foreach (var sequence in pending)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var movements = scope.ServiceProvider.GetRequiredService<IRfidMovementService>();
                await movements.ProcessSequenceAsync(sequence, cts.Token);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not flush sequence {SequenceId} during shutdown", sequence.SequenceId);
            }
        }
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}
