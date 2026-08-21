using CampusTrack.Application.Rfid;
using CampusTrack.Domain.Enums;
using Xunit;

namespace CampusTrack.UnitTests.Rfid;

/// <summary>
/// Direction resolution is the single most consequential decision in the product: get it
/// wrong and a child who left is recorded as present. These tests pin down every branch,
/// including the ambiguous cases that must produce no event at all.
/// </summary>
public class DirectionResolverTests
{
    private static readonly DateTime Start = new(2026, 8, 20, 7, 48, 0, DateTimeKind.Utc);

    private static AntennaHit Hit(int antenna, int offsetMs) =>
        new(antenna, Start.AddMilliseconds(offsetMs));

    // ------------------------------------------------------------ antenna order ----

    [Fact]
    public void AntennaOrder_AscendingPath_IsEntry()
    {
        var hits = new[] { Hit(1, 0), Hit(2, 200), Hit(3, 400) };

        var result = DirectionResolver.Resolve(hits, DirectionStrategy.AntennaOrder);

        Assert.Equal(MovementDirection.Entry, result.Direction);
        Assert.Equal("1,2,3", result.AntennaPath);
        Assert.True(result.Confidence > 0.9, "A clean monotonic sweep should be high confidence.");
    }

    [Fact]
    public void AntennaOrder_DescendingPath_IsExit()
    {
        var hits = new[] { Hit(3, 0), Hit(2, 200), Hit(1, 400) };

        var result = DirectionResolver.Resolve(hits, DirectionStrategy.AntennaOrder);

        Assert.Equal(MovementDirection.Exit, result.Direction);
        Assert.Equal("3,2,1", result.AntennaPath);
    }

    [Fact]
    public void RepeatedHitsOnSameAntenna_AreCollapsed()
    {
        // A UHF reader emits the same tag dozens of times per second. Without collapsing,
        // the path would be meaningless noise.
        var hits = new[]
        {
            Hit(1, 0), Hit(1, 50), Hit(1, 100), Hit(1, 150),
            Hit(2, 300), Hit(2, 350), Hit(2, 400),
            Hit(3, 600), Hit(3, 650)
        };

        var result = DirectionResolver.Resolve(hits, DirectionStrategy.AntennaOrder);

        Assert.Equal("1,2,3", result.AntennaPath);
        Assert.Equal(MovementDirection.Entry, result.Direction);
    }

    [Fact]
    public void SingleAntennaOnly_IsAmbiguous()
    {
        // Someone standing near one antenna. Guessing here would put a false arrival in a
        // parent's timeline, so no event is produced.
        var hits = new[] { Hit(1, 0), Hit(1, 100), Hit(1, 200) };

        var result = DirectionResolver.Resolve(hits, DirectionStrategy.AntennaOrder);

        Assert.False(result.IsResolved);
        Assert.Equal(MovementDirection.Unknown, result.Direction);
        Assert.Contains("one antenna", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ApproachedAndTurnedBack_IsAmbiguous()
    {
        // Walked up to the door, then away again: starts and ends on the same antenna.
        var hits = new[] { Hit(1, 0), Hit(2, 200), Hit(1, 400) };

        var result = DirectionResolver.Resolve(hits, DirectionStrategy.AntennaOrder);

        Assert.False(result.IsResolved);
        Assert.Contains("turned back", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WanderingPath_ResolvesButWithLowerConfidence()
    {
        // 1,2,1,2,3 finishes higher than it started, so the direction is entry, but the
        // path is not a clean sweep and the confidence should say so.
        var hits = new[] { Hit(1, 0), Hit(2, 100), Hit(1, 200), Hit(2, 300), Hit(3, 400) };

        var result = DirectionResolver.Resolve(hits, DirectionStrategy.AntennaOrder);

        Assert.Equal(MovementDirection.Entry, result.Direction);
        Assert.True(result.Confidence < 0.9, "A wandering path should not claim full confidence.");
        Assert.NotNull(result.Reason);
    }

    [Fact]
    public void OutOfOrderArrival_IsSortedByReadTime()
    {
        // A gateway flushing a backlog after a reconnect can deliver reads out of order.
        var hits = new[] { Hit(3, 400), Hit(1, 0), Hit(2, 200) };

        var result = DirectionResolver.Resolve(hits, DirectionStrategy.AntennaOrder);

        Assert.Equal("1,2,3", result.AntennaPath);
        Assert.Equal(MovementDirection.Entry, result.Direction);
    }

    [Fact]
    public void NoHits_IsUnresolved()
    {
        var result = DirectionResolver.Resolve([], DirectionStrategy.AntennaOrder);
        Assert.False(result.IsResolved);
    }

    // ------------------------------------------------------------- antenna role ----

    [Fact]
    public void AntennaRole_OutsideToInside_IsEntry()
    {
        var roles = new Dictionary<int, AntennaRole>
        {
            [1] = AntennaRole.Outside,
            [2] = AntennaRole.Inside,
        };

        var result = DirectionResolver.Resolve(
            [Hit(1, 0), Hit(2, 300)], DirectionStrategy.AntennaRole, roles);

        Assert.Equal(MovementDirection.Entry, result.Direction);
        Assert.Equal(1.0, result.Confidence);
    }

    [Fact]
    public void AntennaRole_InsideToOutside_IsExit()
    {
        var roles = new Dictionary<int, AntennaRole>
        {
            [1] = AntennaRole.Outside,
            [2] = AntennaRole.Inside,
        };

        var result = DirectionResolver.Resolve(
            [Hit(2, 0), Hit(1, 300)], DirectionStrategy.AntennaRole, roles);

        Assert.Equal(MovementDirection.Exit, result.Direction);
    }

    [Fact]
    public void AntennaRole_SurvivesReversedPortNumbering()
    {
        // The installer wired the inside antenna to port 1. Numeric ordering would call this
        // an exit; declared roles get it right, which is the whole point of the strategy.
        var roles = new Dictionary<int, AntennaRole>
        {
            [1] = AntennaRole.Inside,
            [2] = AntennaRole.Outside,
        };

        var result = DirectionResolver.Resolve(
            [Hit(2, 0), Hit(1, 300)], DirectionStrategy.AntennaRole, roles);

        Assert.Equal(MovementDirection.Entry, result.Direction);
    }

    [Fact]
    public void AntennaRole_WithoutConfiguredRoles_FallsBackToOrder()
    {
        var result = DirectionResolver.Resolve(
            [Hit(1, 0), Hit(2, 300)], DirectionStrategy.AntennaRole, antennaRoles: null);

        Assert.Equal(MovementDirection.Entry, result.Direction);
    }

    [Fact]
    public void AntennaRole_SameSideOnly_IsAmbiguous()
    {
        var roles = new Dictionary<int, AntennaRole>
        {
            [1] = AntennaRole.Outside,
            [2] = AntennaRole.Outside,
        };

        var result = DirectionResolver.Resolve(
            [Hit(1, 0), Hit(2, 300)], DirectionStrategy.AntennaRole, roles);

        Assert.False(result.IsResolved);
    }

    // ------------------------------------------------------------------- fixed ----

    [Theory]
    [InlineData(MovementDirection.Entry)]
    [InlineData(MovementDirection.Exit)]
    public void Fixed_AlwaysReturnsConfiguredDirection(MovementDirection configured)
    {
        // A one-way lane: even a single read is unambiguous because the installation says so.
        var result = DirectionResolver.Resolve(
            [Hit(1, 0)], DirectionStrategy.Fixed, fixedDirection: configured);

        Assert.Equal(configured, result.Direction);
        Assert.Equal(1.0, result.Confidence);
    }

    [Fact]
    public void Fixed_WithoutConfiguredDirection_IsUnresolved()
    {
        var result = DirectionResolver.Resolve([Hit(1, 0)], DirectionStrategy.Fixed);

        Assert.False(result.IsResolved);
        Assert.Contains("fixed direction", result.Reason!, StringComparison.OrdinalIgnoreCase);
    }

    // --------------------------------------------------------- presence toggle ----

    [Fact]
    public void PresenceToggle_FromOutside_IsEntry()
    {
        var result = DirectionResolver.Resolve(
            [Hit(1, 0)], DirectionStrategy.PresenceToggle, currentPresence: PresenceState.Outside);

        Assert.Equal(MovementDirection.Entry, result.Direction);
        Assert.True(result.Confidence < 0.8, "Inferred direction must not claim high confidence.");
    }

    [Fact]
    public void PresenceToggle_FromOnCampus_IsExit()
    {
        var result = DirectionResolver.Resolve(
            [Hit(1, 0)], DirectionStrategy.PresenceToggle, currentPresence: PresenceState.OnCampus);

        Assert.Equal(MovementDirection.Exit, result.Direction);
    }

    [Fact]
    public void PresenceToggle_WithoutKnownState_IsUnresolved()
    {
        var result = DirectionResolver.Resolve([Hit(1, 0)], DirectionStrategy.PresenceToggle);
        Assert.False(result.IsResolved);
    }

    // ------------------------------------------------------------ path collapse ----

    [Fact]
    public void CollapsePath_PreservesRevisits()
    {
        // 1,1,2,2,1 collapses to 1,2,1 - the revisit is meaningful and must not be lost,
        // because it is exactly what identifies an approach-and-turn-back.
        var path = DirectionResolver.CollapsePath(
            [Hit(1, 0), Hit(1, 50), Hit(2, 200), Hit(2, 250), Hit(1, 400)]);

        Assert.Equal([1, 2, 1], path);
    }
}
