using System.Collections.Concurrent;
using CampusTrack.Api.Domain;

namespace CampusTrack.Api.Services;

/// <summary>
/// Buffers raw antenna reads per (reader, tag) and resolves them into
/// Entry / Exit events from the antenna ORDER:
///
///   Gate reader, 3 antennas:   1 -> 2 -> 3  = ENTRY,   3 -> 2 -> 1 = EXIT
///   Room readers, 2 antennas:  1 -> 2       = ENTRY,   2 -> 1      = EXIT
///
/// The rule generalises to "first antenna lower than last antenna = entry".
/// A sequence is finalised when no new read for that tag arrives on the
/// reader for <see cref="QuietWindow"/> (the person has walked through).
/// UHF readers report the same tag many times per second, so consecutive
/// duplicate antenna hits are collapsed before the direction is decided.
/// </summary>
public class RfidSequenceEngine
{
    /// no further reads for this long => the pass-through is complete
    public static readonly TimeSpan QuietWindow = TimeSpan.FromSeconds(4);
    /// a sequence can never span longer than this (someone loitering at the gate)
    public static readonly TimeSpan MaxSequenceSpan = TimeSpan.FromSeconds(30);
    /// suppress an identical event (same student/room/direction) inside this window
    public static readonly TimeSpan DuplicateSuppression = TimeSpan.FromSeconds(60);

    private sealed class TagBuffer
    {
        public readonly List<(int Antenna, DateTime Time)> Reads = new();
        public DateTime FirstRead;
        public DateTime LastRead;
    }

    private readonly ConcurrentDictionary<(int ReaderId, string Epc), TagBuffer> _buffers = new();

    public void AddRead(int readerId, string epc, int antennaNo, DateTime readTime)
    {
        var buf = _buffers.GetOrAdd((readerId, epc), _ => new TagBuffer { FirstRead = readTime });
        lock (buf)
        {
            if (buf.Reads.Count == 0) buf.FirstRead = readTime;
            buf.Reads.Add((antennaNo, readTime));
            buf.LastRead = readTime;
        }
    }

    /// <summary>
    /// Called periodically by the sweeper. Returns every finished sequence
    /// with its resolved direction (null direction = ambiguous, discarded).
    /// </summary>
    public List<(int ReaderId, string Epc, Direction? Direction, DateTime EventTime)> SweepCompleted(DateTime utcNow)
    {
        var results = new List<(int, string, Direction?, DateTime)>();

        foreach (var (key, buf) in _buffers)
        {
            bool done;
            List<(int Antenna, DateTime Time)> snapshot;
            lock (buf)
            {
                done = utcNow - buf.LastRead >= QuietWindow ||
                       utcNow - buf.FirstRead >= MaxSequenceSpan;
                if (!done) continue;
                snapshot = new List<(int, DateTime)>(buf.Reads);
            }

            if (_buffers.TryRemove(key, out _))
                results.Add((key.ReaderId, key.Epc, ResolveDirection(snapshot), snapshot[^1].Time));
        }
        return results;
    }

    /// <summary>Collapse duplicates, then compare first vs last antenna.</summary>
    public static Direction? ResolveDirection(IReadOnlyList<(int Antenna, DateTime Time)> reads)
    {
        if (reads.Count == 0) return null;

        var ordered = reads.OrderBy(r => r.Time).ToList();

        // collapse consecutive repeats: 1,1,1,2,2,3 -> 1,2,3
        var seq = new List<int>();
        foreach (var (antenna, _) in ordered)
            if (seq.Count == 0 || seq[^1] != antenna)
                seq.Add(antenna);

        // a single antenna can't tell direction (tag idled near one antenna)
        if (seq.Count < 2) return null;

        int first = seq[0], last = seq[^1];
        if (first == last) return null;          // walked in and backed out
        return first < last ? Direction.Entry : Direction.Exit;
    }
}
