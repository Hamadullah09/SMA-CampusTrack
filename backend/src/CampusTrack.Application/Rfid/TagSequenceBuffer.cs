using System.Collections.Concurrent;

namespace CampusTrack.Application.Rfid;

/// <summary>A pass-through that has gone quiet and is ready to be turned into an event.</summary>
public sealed record CompletedSequence(
    Guid SequenceId,
    int ReaderId,
    string Epc,
    IReadOnlyList<AntennaHit> Hits,
    DateTime FirstReadUtc,
    DateTime LastReadUtc,
    bool ClosedByTimeout);

/// <summary>
/// Groups raw antenna hits into discrete pass-throughs.
///
/// The problem this solves: a UHF reader does not report "a person walked through", it
/// reports a continuous stream of detections for as long as the tag is in the field - often
/// 20-50 per second. Turning that into one movement means deciding when the walk ended.
///
/// A pass-through is closed when either
/// <list type="bullet">
///   <item>the tag has not been seen for the quiet window (the person has walked on), or</item>
///   <item>the sequence has run longer than the maximum span (someone is standing in the
///   field, and waiting longer would delay the event indefinitely).</item>
/// </list>
///
/// Thread-safe: the ingestion endpoint adds from request threads while the background
/// processor sweeps on its own timer.
/// </summary>
public class TagSequenceBuffer
{
    private sealed class Buffer
    {
        public readonly List<AntennaHit> Hits = [];
        public Guid SequenceId = Guid.NewGuid();
        public DateTime FirstReadUtc;
        public DateTime LastReadUtc;
    }

    private readonly ConcurrentDictionary<(int ReaderId, string Epc), Buffer> _buffers = new();

    /// <summary>Number of pass-throughs currently in flight - surfaced on the health endpoint.</summary>
    public int PendingSequences => _buffers.Count;

    public void Add(int readerId, string epc, int antennaNumber, DateTime readAtUtc, int? rssi = null)
    {
        var key = (readerId, epc);
        var buffer = _buffers.GetOrAdd(key, _ => new Buffer { FirstReadUtc = readAtUtc });

        lock (buffer)
        {
            if (buffer.Hits.Count == 0)
            {
                buffer.FirstReadUtc = readAtUtc;
                buffer.SequenceId = Guid.NewGuid();
            }

            buffer.Hits.Add(new AntennaHit(antennaNumber, readAtUtc, rssi));

            // Reads can arrive out of order after a gateway reconnects and flushes a backlog,
            // so track the true latest rather than assuming the newest arrival is the newest read.
            if (readAtUtc > buffer.LastReadUtc) buffer.LastReadUtc = readAtUtc;
        }
    }

    /// <summary>
    /// Returns every pass-through that has finished, removing it from the buffer.
    /// Called on a short timer by the background processor.
    /// </summary>
    public IReadOnlyList<CompletedSequence> Sweep(DateTime utcNow, TimeSpan quietWindow, TimeSpan maxSpan)
    {
        var completed = new List<CompletedSequence>();

        foreach (var (key, buffer) in _buffers)
        {
            bool ready;
            bool byTimeout;
            List<AntennaHit> snapshot;
            Guid sequenceId;
            DateTime first, last;

            lock (buffer)
            {
                if (buffer.Hits.Count == 0) continue;

                var quiet = utcNow - buffer.LastReadUtc >= quietWindow;
                byTimeout = utcNow - buffer.FirstReadUtc >= maxSpan;
                ready = quiet || byTimeout;
                if (!ready) continue;

                snapshot = [.. buffer.Hits];
                sequenceId = buffer.SequenceId;
                first = buffer.FirstReadUtc;
                last = buffer.LastReadUtc;
            }

            // Only the thread that removes the buffer emits the sequence, so a concurrent
            // sweep cannot produce the same movement twice.
            if (_buffers.TryRemove(key, out _))
            {
                completed.Add(new CompletedSequence(
                    sequenceId, key.ReaderId, key.Epc, snapshot, first, last, byTimeout));
            }
        }

        return completed;
    }

    /// <summary>Drains everything regardless of age - used on graceful shutdown.</summary>
    public IReadOnlyList<CompletedSequence> DrainAll()
    {
        var completed = new List<CompletedSequence>();

        foreach (var key in _buffers.Keys.ToList())
        {
            if (!_buffers.TryRemove(key, out var buffer)) continue;

            lock (buffer)
            {
                if (buffer.Hits.Count == 0) continue;
                completed.Add(new CompletedSequence(
                    buffer.SequenceId, key.ReaderId, key.Epc, [.. buffer.Hits],
                    buffer.FirstReadUtc, buffer.LastReadUtc, false));
            }
        }

        return completed;
    }

    public void Clear() => _buffers.Clear();
}
