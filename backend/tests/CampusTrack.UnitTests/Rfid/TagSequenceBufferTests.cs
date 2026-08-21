using CampusTrack.Application.Rfid;
using Xunit;

namespace CampusTrack.UnitTests.Rfid;

/// <summary>
/// The buffer is what turns a continuous stream of detections into discrete pass-throughs.
/// These tests cover the timing rules and the concurrency guarantee that a movement is
/// emitted exactly once.
/// </summary>
public class TagSequenceBufferTests
{
    private static readonly DateTime Start = new(2026, 8, 20, 7, 48, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Quiet = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan MaxSpan = TimeSpan.FromSeconds(30);

    [Fact]
    public void SequenceIsNotEmittedWhileTagIsStillBeingSeen()
    {
        var buffer = new TagSequenceBuffer();
        buffer.Add(1, "EPC1", 1, Start);
        buffer.Add(1, "EPC1", 2, Start.AddSeconds(1));

        // Only two seconds of silence: the person may still be walking through.
        var completed = buffer.Sweep(Start.AddSeconds(3), Quiet, MaxSpan);

        Assert.Empty(completed);
        Assert.Equal(1, buffer.PendingSequences);
    }

    [Fact]
    public void SequenceIsEmittedOnceTheTagGoesQuiet()
    {
        var buffer = new TagSequenceBuffer();
        buffer.Add(1, "EPC1", 1, Start);
        buffer.Add(1, "EPC1", 2, Start.AddSeconds(1));

        var completed = buffer.Sweep(Start.AddSeconds(6), Quiet, MaxSpan);

        var sequence = Assert.Single(completed);
        Assert.Equal("EPC1", sequence.Epc);
        Assert.Equal(2, sequence.Hits.Count);
        Assert.False(sequence.ClosedByTimeout);
        Assert.Equal(0, buffer.PendingSequences);
    }

    [Fact]
    public void LoiteringTagIsClosedByTheMaximumSpan()
    {
        // Someone standing in the read field would otherwise keep resetting the quiet timer
        // and never produce an event at all.
        var buffer = new TagSequenceBuffer();

        for (var second = 0; second < 40; second++)
        {
            buffer.Add(1, "EPC1", 1 + (second % 2), Start.AddSeconds(second));
        }

        var completed = buffer.Sweep(Start.AddSeconds(40), Quiet, MaxSpan);

        var sequence = Assert.Single(completed);
        Assert.True(sequence.ClosedByTimeout);
    }

    [Fact]
    public void DifferentTagsOnSameReaderAreTrackedSeparately()
    {
        var buffer = new TagSequenceBuffer();
        buffer.Add(1, "EPC1", 1, Start);
        buffer.Add(1, "EPC2", 1, Start);

        Assert.Equal(2, buffer.PendingSequences);

        var completed = buffer.Sweep(Start.AddSeconds(6), Quiet, MaxSpan);
        Assert.Equal(2, completed.Count);
    }

    [Fact]
    public void SameTagOnDifferentReadersIsTrackedSeparately()
    {
        // A student passing two doorways at once is impossible, but two readers with
        // overlapping fields is not; each must produce its own decision.
        var buffer = new TagSequenceBuffer();
        buffer.Add(1, "EPC1", 1, Start);
        buffer.Add(2, "EPC1", 1, Start);

        Assert.Equal(2, buffer.PendingSequences);
    }

    [Fact]
    public void OutOfOrderReadsDoNotPrematurelyCloseASequence()
    {
        // A late-arriving read from earlier in the walk must not make the buffer think the
        // tag has been quiet longer than it has.
        var buffer = new TagSequenceBuffer();
        buffer.Add(1, "EPC1", 2, Start.AddSeconds(2));
        buffer.Add(1, "EPC1", 1, Start);   // arrives second, but happened first

        var completed = buffer.Sweep(Start.AddSeconds(5), Quiet, MaxSpan);

        // Last read was at +2s, so at +5s only three seconds have passed: still open.
        Assert.Empty(completed);
    }

    [Fact]
    public void ConcurrentSweepsEmitEachSequenceExactlyOnce()
    {
        // Two sweeps racing must not both emit the same pass-through, which would create a
        // duplicate arrival for the same child.
        var buffer = new TagSequenceBuffer();

        for (var i = 0; i < 200; i++)
        {
            buffer.Add(1, $"EPC{i}", 1, Start);
            buffer.Add(1, $"EPC{i}", 2, Start.AddMilliseconds(200));
        }

        var sweepAt = Start.AddSeconds(6);
        var results = new List<CompletedSequence>[4];

        Parallel.For(0, 4, index =>
        {
            results[index] = buffer.Sweep(sweepAt, Quiet, MaxSpan).ToList();
        });

        var all = results.SelectMany(r => r).ToList();

        Assert.Equal(200, all.Count);
        Assert.Equal(200, all.Select(s => s.Epc).Distinct().Count());
        Assert.Equal(0, buffer.PendingSequences);
    }

    [Fact]
    public void DrainAllFlushesEverythingRegardlessOfAge()
    {
        // Used on shutdown: whatever is mid-flight must be processed rather than lost.
        var buffer = new TagSequenceBuffer();
        buffer.Add(1, "EPC1", 1, Start);
        buffer.Add(1, "EPC2", 1, Start);

        var drained = buffer.DrainAll();

        Assert.Equal(2, drained.Count);
        Assert.Equal(0, buffer.PendingSequences);
    }

    [Fact]
    public void SequenceIdIsStableAcrossTheWholePassThrough()
    {
        var buffer = new TagSequenceBuffer();
        buffer.Add(1, "EPC1", 1, Start);
        buffer.Add(1, "EPC1", 2, Start.AddMilliseconds(300));
        buffer.Add(1, "EPC1", 3, Start.AddMilliseconds(600));

        var sequence = Assert.Single(buffer.Sweep(Start.AddSeconds(6), Quiet, MaxSpan));

        Assert.NotEqual(Guid.Empty, sequence.SequenceId);
        Assert.Equal(3, sequence.Hits.Count);
        Assert.Equal(Start, sequence.FirstReadUtc);
        Assert.Equal(Start.AddMilliseconds(600), sequence.LastReadUtc);
    }
}
