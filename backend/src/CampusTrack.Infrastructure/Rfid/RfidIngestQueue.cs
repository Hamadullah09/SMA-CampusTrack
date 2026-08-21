using System.Threading.Channels;
using CampusTrack.Application.Rfid;
using Microsoft.Extensions.Logging;

namespace CampusTrack.Infrastructure.Rfid;

/// <summary>
/// Bounded in-memory queue between the ingestion endpoint and the event processor.
///
/// Capacity is sized for a burst, not a steady state: a 1,200-student school arriving over
/// twenty minutes with four antennas reporting is a few thousand reads per second at peak,
/// and the processor drains far faster than that. The bound exists so a stuck processor or a
/// misconfigured reader flooding the endpoint degrades visibly instead of exhausting memory.
///
/// DropOldest is the right policy here: when the queue is saturated, a read from ten seconds
/// ago has almost certainly been superseded by a newer one for the same tag, whereas dropping
/// the newest would discard the freshest evidence of where someone is.
/// </summary>
public class RfidIngestQueue : IRfidIngestQueue
{
    private readonly Channel<QueuedRead> _channel;
    private readonly ILogger<RfidIngestQueue> _logger;
    private long _enqueued;
    private long _dropped;
    private int _depth;
    private DateTime _lastDropWarningUtc = DateTime.MinValue;

    public RfidIngestQueue(ILogger<RfidIngestQueue> logger, int capacity = 50_000)
    {
        _logger = logger;
        Capacity = capacity;

        _channel = Channel.CreateBounded<QueuedRead>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,     // exactly one processor drains this
            SingleWriter = false     // many request threads write
        });
    }

    public int Capacity { get; }
    public int Depth => Volatile.Read(ref _depth);
    public long TotalEnqueued => Interlocked.Read(ref _enqueued);
    public long TotalDropped => Interlocked.Read(ref _dropped);

    public bool TryEnqueue(QueuedRead read)
    {
        if (_channel.Writer.TryWrite(read))
        {
            Interlocked.Increment(ref _enqueued);
            Interlocked.Increment(ref _depth);
            return true;
        }

        Interlocked.Increment(ref _dropped);

        // Rate-limit the warning: a saturated queue would otherwise flood the log with
        // thousands of identical lines per second and bury the cause.
        var now = DateTime.UtcNow;
        if (now - _lastDropWarningUtc > TimeSpan.FromSeconds(10))
        {
            _lastDropWarningUtc = now;
            _logger.LogError(
                "RFID ingest queue is saturated (capacity {Capacity}, dropped {Dropped} reads in total). " +
                "The event processor is not keeping up.", Capacity, TotalDropped);
        }

        return false;
    }

    public async IAsyncEnumerable<QueuedRead> DequeueAllAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var read in _channel.Reader.ReadAllAsync(ct))
        {
            Interlocked.Decrement(ref _depth);
            yield return read;
        }
    }
}
