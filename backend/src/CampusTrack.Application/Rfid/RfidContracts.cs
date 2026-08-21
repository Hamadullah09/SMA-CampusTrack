using System.ComponentModel.DataAnnotations;
using CampusTrack.Domain.Enums;

namespace CampusTrack.Application.Rfid;

/// <summary>
/// The wire format a reader or local gateway posts to the ingestion endpoint. Kept
/// deliberately small and flat: it is produced by embedded firmware and by a thin gateway
/// process, neither of which should have to build anything elaborate.
/// </summary>
public class RfidReadBatch
{
    /// <summary>Device identifier; must match the authenticated device header.</summary>
    [Required] public string DeviceId { get; set; } = string.Empty;

    /// <summary>
    /// Gateway-generated id for this batch. Resending the same batch after a network timeout
    /// is a no-op, so an unreliable link cannot inflate attendance.
    /// </summary>
    public string? BatchId { get; set; }

    [Required, MinLength(1)]
    public List<RfidReadItem> Reads { get; set; } = [];

    /// <summary>Optional device telemetry piggybacked on the batch.</summary>
    public ReaderTelemetry? Telemetry { get; set; }
}

public class RfidReadItem
{
    /// <summary>EPC as read from the air. Case and whitespace are normalised server-side.</summary>
    [Required] public string Epc { get; set; } = string.Empty;

    [Range(1, 32)] public int AntennaNumber { get; set; } = 1;

    /// <summary>Reader clock, in UTC. Omitted means "now" at the server.</summary>
    public DateTime? ReadAtUtc { get; set; }

    /// <summary>Signal strength in dBm, when the reader reports it.</summary>
    public int? Rssi { get; set; }

    /// <summary>Chip serial, when the reader is configured to read TID as well as EPC.</summary>
    public string? TagUid { get; set; }
}

public class ReaderTelemetry
{
    public string? FirmwareVersion { get; set; }
    public int? TemperatureCelsius { get; set; }
    public int? UptimeSeconds { get; set; }
    public int? QueuedReads { get; set; }
    public string? Status { get; set; }
}

public class RfidHeartbeat
{
    [Required] public string DeviceId { get; set; } = string.Empty;
    public string? FirmwareVersion { get; set; }
    public string? IpAddress { get; set; }
    public ReaderTelemetry? Telemetry { get; set; }
}

/// <summary>What the ingestion endpoint returns. The gateway uses this to decide what to retry.</summary>
public class RfidIngestResponse
{
    public int Received { get; set; }
    public int Accepted { get; set; }
    public int Rejected { get; set; }
    public bool Duplicate { get; set; }
    public List<string> Warnings { get; set; } = [];

    /// <summary>Queue depth, so a gateway can back off before it overwhelms the server.</summary>
    public int QueueDepth { get; set; }
}

/// <summary>A movement event as presented to dashboards, timelines and the mobile apps.</summary>
public class RfidEventDto
{
    public long Id { get; set; }
    public RfidEventType EventType { get; set; }
    public string EventTypeName { get; set; } = string.Empty;
    public MovementDirection Direction { get; set; }
    public DateTime OccurredAtUtc { get; set; }
    public DateOnly LocalDate { get; set; }

    public int? StudentId { get; set; }
    public string? StudentName { get; set; }
    public string? StudentCode { get; set; }
    public string? StudentPhotoUrl { get; set; }
    public string? SectionName { get; set; }

    public int? LocationId { get; set; }
    public string? LocationName { get; set; }
    public LocationType? LocationType { get; set; }
    public string? ReaderName { get; set; }
    public string? DeviceId { get; set; }

    public string? SubjectName { get; set; }
    public string? TeacherName { get; set; }

    public EventSource Source { get; set; }
    public double Confidence { get; set; }
    public string? AntennaSequence { get; set; }
    public string? RejectionReason { get; set; }

    /// <summary>Masked for display: only the last six characters, so screenshots do not leak a card id.</summary>
    public string? MaskedEpc { get; set; }
}

/// <summary>An entry in a student's day, as the parent app renders it.</summary>
public class ActivityTimelineEntry
{
    public DateTime OccurredAtUtc { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Detail { get; set; }
    public string Icon { get; set; } = "location";
    public RfidEventType? EventType { get; set; }
    public string? LocationName { get; set; }
    public string? SubjectName { get; set; }
    public int? DurationMinutes { get; set; }
}

/// <summary>Reader state for the monitoring screen and the floor plan.</summary>
public class ReaderStatusDto
{
    public int Id { get; set; }
    public string DeviceId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public ReaderStatus Status { get; set; }
    public string StatusName { get; set; } = string.Empty;
    public int LocationId { get; set; }
    public string LocationName { get; set; } = string.Empty;
    public LocationType LocationType { get; set; }
    public string? IpAddress { get; set; }
    public string? FirmwareVersion { get; set; }
    public DateTime? LastHeartbeatUtc { get; set; }
    public DateTime? LastEventUtc { get; set; }
    public int? SecondsSinceHeartbeat { get; set; }
    public string? LastErrorMessage { get; set; }
    public int AntennaCount { get; set; }
    public int EventsToday { get; set; }
    public double? MapX { get; set; }
    public double? MapY { get; set; }
}
