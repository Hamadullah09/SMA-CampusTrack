namespace CampusTrack.Application.Rfid;

/// <summary>
/// The boundary between this product and physical reader hardware.
///
/// Everything above this interface - direction resolution, attendance, notifications - is
/// hardware-agnostic and fully testable. Everything below it is vendor-specific: the D2184
/// speaks its own protocol over TCP, other fixed readers speak LLRP, and some push HTTP
/// directly. Adding a reader model means writing one adapter, not touching the engine.
///
/// The adapter's only job is to turn whatever the device emits into <see cref="RfidReadItem"/>
/// and hand it to the ingestion API. It must not interpret direction, resolve students or
/// decide attendance: those are decisions the server makes with context the device lacks.
/// </summary>
public interface IReaderHardwareAdapter
{
    /// <summary>Model this adapter serves, e.g. "D2184". Matched against RfidReader.Model.</summary>
    string ModelName { get; }

    /// <summary>Opens the connection and begins streaming. Runs until the token is cancelled.</summary>
    Task ConnectAsync(ReaderConnection connection, CancellationToken ct);

    /// <summary>Raised for each tag report the device produces.</summary>
    event EventHandler<TagReportEventArgs>? TagReported;

    /// <summary>Raised when the link goes down, so the gateway can back off and retry.</summary>
    event EventHandler<ReaderConnectionEventArgs>? ConnectionChanged;

    Task<bool> TestConnectionAsync(ReaderConnection connection, CancellationToken ct = default);
    Task DisconnectAsync(CancellationToken ct = default);
}

/// <summary>Everything needed to reach one physical reader.</summary>
public record ReaderConnection(
    string DeviceId,
    string Host,
    int Port,
    int AntennaCount = 2,
    int? PowerDbm = null,
    IReadOnlyDictionary<string, string>? VendorSettings = null);

public class TagReportEventArgs : EventArgs
{
    public required string DeviceId { get; init; }
    public required string Epc { get; init; }
    public int AntennaNumber { get; init; } = 1;
    public DateTime ReadAtUtc { get; init; } = DateTime.UtcNow;
    public int? Rssi { get; init; }
    public string? TagUid { get; init; }
}

public class ReaderConnectionEventArgs : EventArgs
{
    public required string DeviceId { get; init; }
    public bool IsConnected { get; init; }
    public string? Message { get; init; }
    public Exception? Error { get; init; }
}
