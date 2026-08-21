using CampusTrack.Domain.Enums;

namespace CampusTrack.Application.Rfid;

/// <summary>One antenna hit, reduced to what direction resolution actually needs.</summary>
public readonly record struct AntennaHit(int AntennaNumber, DateTime ReadAtUtc, int? Rssi = null);

/// <summary>The decision made about one pass-through, with the evidence behind it.</summary>
public readonly record struct DirectionResult(
    MovementDirection Direction,
    double Confidence,
    string AntennaPath,
    string? Reason = null)
{
    public bool IsResolved => Direction != MovementDirection.Unknown;

    public static DirectionResult Unresolved(string path, string reason) =>
        new(MovementDirection.Unknown, 0, path, reason);
}

/// <summary>
/// Works out whether a pass-through was an entry or an exit.
///
/// A UHF reader reports the same tag many times a second across several antennas, so the raw
/// stream says nothing on its own. What carries the signal is the ORDER in which antennas saw
/// the tag: someone walking in crosses the outer antenna before the inner one, and someone
/// leaving does the reverse.
///
/// Four strategies are supported because real installations differ:
/// <list type="bullet">
///   <item><b>AntennaRole</b> - each port is declared Outside or Inside. Most reliable, and
///   the only one that survives someone renumbering the ports.</item>
///   <item><b>AntennaOrder</b> - ports are wired outermost-to-innermost, so a rising path is
///   an entry. The pragmatic default when roles have not been configured.</item>
///   <item><b>Fixed</b> - a one-way lane; the reader can only ever mean one direction.</item>
///   <item><b>PresenceToggle</b> - a single-antenna reader with no directional information at
///   all, where direction is inferred from where the person currently is.</item>
/// </list>
///
/// Pure and deterministic: no clock, no database, no I/O, so the whole decision table is
/// covered by unit tests.
/// </summary>
public static class DirectionResolver
{
    /// <summary>
    /// Collapses consecutive repeats on the same antenna. A reader emitting 1,1,1,2,2,3
    /// describes one walk past three antennas, not six events.
    /// </summary>
    public static IReadOnlyList<int> CollapsePath(IEnumerable<AntennaHit> hits)
    {
        var path = new List<int>();
        foreach (var hit in hits.OrderBy(h => h.ReadAtUtc))
        {
            if (path.Count == 0 || path[^1] != hit.AntennaNumber) path.Add(hit.AntennaNumber);
        }
        return path;
    }

    public static DirectionResult Resolve(
        IReadOnlyList<AntennaHit> hits,
        DirectionStrategy strategy,
        IReadOnlyDictionary<int, AntennaRole>? antennaRoles = null,
        MovementDirection fixedDirection = MovementDirection.Unknown,
        PresenceState? currentPresence = null)
    {
        if (hits.Count == 0) return DirectionResult.Unresolved(string.Empty, "No reads in sequence.");

        var path = CollapsePath(hits);
        var pathText = string.Join(",", path);

        return strategy switch
        {
            DirectionStrategy.Fixed => ResolveFixed(fixedDirection, pathText),
            DirectionStrategy.AntennaRole => ResolveByRole(path, pathText, antennaRoles),
            DirectionStrategy.PresenceToggle => ResolveByPresence(currentPresence, pathText),
            _ => ResolveByOrder(path, pathText)
        };
    }

    private static DirectionResult ResolveFixed(MovementDirection direction, string path)
    {
        if (direction == MovementDirection.Unknown)
            return DirectionResult.Unresolved(path, "Reader is set to a fixed direction but none was configured.");

        // Full confidence: the installation itself guarantees the direction.
        return new DirectionResult(direction, 1.0, path);
    }

    /// <summary>
    /// Outside -> Inside is an entry, Inside -> Outside an exit. Only the first and last
    /// role that differ matter; loitering between them does not change the outcome.
    /// </summary>
    private static DirectionResult ResolveByRole(
        IReadOnlyList<int> path, string pathText, IReadOnlyDictionary<int, AntennaRole>? roles)
    {
        if (roles is null || roles.Count == 0)
            return ResolveByOrder(path, pathText);   // fall back rather than refuse

        var roleSequence = path
            .Select(a => roles.TryGetValue(a, out var role) ? role : AntennaRole.Unspecified)
            .Where(r => r != AntennaRole.Unspecified)
            .ToList();

        if (roleSequence.Count < 2)
            return DirectionResult.Unresolved(pathText,
                "The tag was only seen on one side of the reader, so the direction is ambiguous.");

        var first = roleSequence[0];
        var last = roleSequence[^1];

        if (first == last)
            return DirectionResult.Unresolved(pathText,
                "The tag entered and left on the same side - the person approached and turned back.");

        var direction = first == AntennaRole.Outside ? MovementDirection.Entry : MovementDirection.Exit;
        return new DirectionResult(direction, 1.0, pathText);
    }

    /// <summary>
    /// Antennas are wired outermost to innermost, so a rising path means entry.
    /// Confidence is slightly below the role-based result: this relies on a wiring convention
    /// that nothing in the data can verify.
    /// </summary>
    private static DirectionResult ResolveByOrder(IReadOnlyList<int> path, string pathText)
    {
        if (path.Count < 2)
            return DirectionResult.Unresolved(pathText,
                "The tag was seen on only one antenna, so the direction is ambiguous.");

        var first = path[0];
        var last = path[^1];

        if (first == last)
            return DirectionResult.Unresolved(pathText,
                "The tag started and finished on the same antenna - the person approached and turned back.");

        var direction = first < last ? MovementDirection.Entry : MovementDirection.Exit;

        // A clean monotonic sweep is a stronger signal than a wandering path that happens to
        // finish higher than it started.
        var monotonic = IsMonotonic(path);
        return new DirectionResult(direction, monotonic ? 0.95 : 0.75, pathText,
            monotonic ? null : "Antenna path was not a clean sweep.");
    }

    /// <summary>
    /// Last resort for single-antenna readers: if the person is recorded as outside, this
    /// must be an entry, and vice versa. Confidence is low because a missed read leaves the
    /// stored state wrong and every subsequent decision inherits that error.
    /// </summary>
    private static DirectionResult ResolveByPresence(PresenceState? presence, string pathText)
    {
        if (presence is null)
            return DirectionResult.Unresolved(pathText, "No known presence state to toggle from.");

        var direction = presence == PresenceState.Outside
            ? MovementDirection.Entry
            : MovementDirection.Exit;

        return new DirectionResult(direction, 0.6, pathText, "Direction inferred from last known presence.");
    }

    private static bool IsMonotonic(IReadOnlyList<int> path)
    {
        var ascending = true;
        var descending = true;

        for (var i = 1; i < path.Count; i++)
        {
            if (path[i] < path[i - 1]) ascending = false;
            if (path[i] > path[i - 1]) descending = false;
        }

        return ascending || descending;
    }
}
