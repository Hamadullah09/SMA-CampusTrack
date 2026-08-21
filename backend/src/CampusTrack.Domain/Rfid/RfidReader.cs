using CampusTrack.Domain.Common;
using CampusTrack.Domain.Enums;

namespace CampusTrack.Domain.Rfid;

/// <summary>
/// A physical UHF reader (the D2184 in the reference deployment). The record holds both
/// inventory data (IP, MAC, firmware) and the live operational state the monitoring
/// dashboard renders: status, last heartbeat, last event, last error.
/// </summary>
public class RfidReader : TenantEntity<int>
{
    /// <summary>Stable identifier the device sends with every read. Unique per school.</summary>
    public string DeviceId { get; set; } = string.Empty;     // "MAIN-GATE-R01"
    public string Name { get; set; } = string.Empty;
    public string Model { get; set; } = "D2184";
    public string? SerialNumber { get; set; }
    public string? FirmwareVersion { get; set; }

    public string? IpAddress { get; set; }
    public string? MacAddress { get; set; }
    public int? Port { get; set; }

    public int LocationId { get; set; }
    public RfidLocation? Location { get; set; }

    // ---- direction resolution ----------------------------------------------------

    public DirectionStrategy DirectionStrategy { get; set; } = DirectionStrategy.AntennaOrder;

    /// <summary>Number of antenna ports in use (the D2184 supports up to four).</summary>
    public int AntennaCount { get; set; } = 2;

    /// <summary>Only meaningful when <see cref="DirectionStrategy"/> is Fixed.</summary>
    public MovementDirection FixedDirection { get; set; } = MovementDirection.Unknown;

    // ---- tuning ------------------------------------------------------------------

    /// <summary>
    /// A tag must go unseen for this long before its pass-through is treated as finished.
    /// Null falls back to the system-wide RFID setting.
    /// </summary>
    public int? QuietWindowMs { get; set; }

    /// <summary>
    /// Repeat events for the same tag, location and direction inside this window are
    /// suppressed. Null falls back to the system-wide setting.
    /// </summary>
    public int? DebounceSeconds { get; set; }

    /// <summary>Reads weaker than this RSSI are ignored as stray far-field reads.</summary>
    public int? MinimumRssi { get; set; }

    // ---- device authentication ---------------------------------------------------

    /// <summary>
    /// SHA-256 of the device API key. The plaintext is shown once at provisioning and
    /// never stored, so a database leak cannot be replayed as a device.
    /// </summary>
    public string? ApiKeyHash { get; set; }
    public DateTime? ApiKeyIssuedAtUtc { get; set; }

    // ---- live state --------------------------------------------------------------

    public ReaderStatus Status { get; set; } = ReaderStatus.Unknown;
    public DateTime? LastHeartbeatUtc { get; set; }
    public DateTime? LastEventUtc { get; set; }
    public string? LastErrorMessage { get; set; }
    public DateTime? LastErrorAtUtc { get; set; }

    /// <summary>
    /// Expected heartbeat cadence. The health monitor marks the reader offline once
    /// silence exceeds a multiple of this.
    /// </summary>
    public int HeartbeatIntervalSeconds { get; set; } = 60;

    public bool IsActive { get; set; } = true;

    public ICollection<ReaderAntenna> Antennas { get; set; } = new List<ReaderAntenna>();

    /// <summary>Stale once silence exceeds three heartbeat intervals, with 90s of floor.</summary>
    public bool IsHeartbeatStale(DateTime utcNow)
    {
        if (LastHeartbeatUtc is null) return true;
        var graceSeconds = Math.Max(HeartbeatIntervalSeconds * 3, 90);
        return (utcNow - LastHeartbeatUtc.Value).TotalSeconds > graceSeconds;
    }
}

/// <summary>
/// One antenna port. Declaring each port as Outside or Inside makes direction explicit and
/// survives rewiring far better than relying on port numbering alone.
/// </summary>
public class ReaderAntenna
{
    public int Id { get; set; }
    public int ReaderId { get; set; }
    public RfidReader? Reader { get; set; }

    public int AntennaNumber { get; set; }
    public AntennaRole Role { get; set; } = AntennaRole.Unspecified;
    public string? Label { get; set; }
    public int? PowerDbm { get; set; }
    public bool IsActive { get; set; } = true;
}
