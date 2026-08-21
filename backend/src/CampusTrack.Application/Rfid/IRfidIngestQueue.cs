namespace CampusTrack.Application.Rfid;

/// <summary>One antenna hit, already resolved to a known reader, waiting to be processed.</summary>
public readonly record struct QueuedRead(
    int ReaderId,
    string DeviceId,
    string Epc,
    int AntennaNumber,
    DateTime ReadAtUtc,
    DateTime ReceivedAtUtc,
    int? Rssi,
    string? TagUid,
    string? BatchId,
    int SchoolId);

/// <summary>
/// Hands reads from the HTTP thread to the background processor.
///
/// The ingestion endpoint must return in single-digit milliseconds: a gate reader that has to
/// wait on database writes, timetable lookups and push notifications will fall behind during
/// the morning rush, and a backed-up reader loses reads. So the endpoint does the minimum
/// (authenticate, validate, enqueue) and everything else happens off the request path.
///
/// The queue is bounded. An unbounded queue does not remove backpressure, it just relocates
/// the failure to memory exhaustion; when full, the oldest read is dropped and counted so the
/// condition is visible rather than silent.
/// </summary>
public interface IRfidIngestQueue
{
    /// <summary>Enqueues a read. Returns false when the queue is saturated.</summary>
    bool TryEnqueue(QueuedRead read);

    /// <summary>Reads until the token is cancelled. Consumed by the background processor.</summary>
    IAsyncEnumerable<QueuedRead> DequeueAllAsync(CancellationToken ct);

    int Depth { get; }
    int Capacity { get; }
    long TotalEnqueued { get; }
    long TotalDropped { get; }
}
