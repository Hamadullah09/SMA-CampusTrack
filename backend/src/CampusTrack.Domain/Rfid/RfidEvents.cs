using CampusTrack.Domain.Academics;
using CampusTrack.Domain.Common;
using CampusTrack.Domain.Enums;
using CampusTrack.Domain.People;
using CampusTrack.Domain.Scheduling;

namespace CampusTrack.Domain.Rfid;

/// <summary>
/// One antenna hit exactly as the reader reported it, stored before any interpretation.
/// A UHF reader sees the same tag dozens of times per second, so this table is
/// write-heavy by design: it is append-only, never updated in place after processing,
/// and is what lets an operator re-run the engine over a disputed day.
/// </summary>
public class RfidRawRead
{
    public long Id { get; set; }
    public int SchoolId { get; set; } = 1;

    public int ReaderId { get; set; }
    public RfidReader? Reader { get; set; }
    public int AntennaNumber { get; set; }

    /// <summary>Raw EPC as received, normalised to upper-case hex. Not a foreign key:
    /// unknown tags must still be recorded, that is the point of keeping raw reads.</summary>
    public string Epc { get; set; } = string.Empty;

    /// <summary>Signal strength, when the reader reports it. Used to drop far-field noise.</summary>
    public int? Rssi { get; set; }

    /// <summary>When the reader saw the tag (device clock, normalised to UTC).</summary>
    public DateTime ReadAtUtc { get; set; }

    /// <summary>When the server accepted it. The gap exposes gateway backlog after an outage.</summary>
    public DateTime ReceivedAtUtc { get; set; }

    public RawReadState State { get; set; } = RawReadState.Pending;

    /// <summary>Groups the reads that were collapsed into one movement decision.</summary>
    public Guid? SequenceId { get; set; }

    /// <summary>Set once this read contributed to a movement event.</summary>
    public long? RfidEventId { get; set; }

    /// <summary>Idempotency key from the gateway, so a retried batch cannot double-insert.</summary>
    public string? IngestBatchId { get; set; }
}

/// <summary>
/// A resolved movement: a person crossed a boundary in a known direction at a known time.
/// This is the row every downstream feature reads - attendance, guardian notifications,
/// the activity timeline, the live dashboard and the movement reports.
/// </summary>
public class RfidEvent
{
    public long Id { get; set; }
    public int SchoolId { get; set; } = 1;

    public RfidEventType EventType { get; set; }
    public MovementDirection Direction { get; set; }
    public DateTime OccurredAtUtc { get; set; }
    /// <summary>Local calendar date of the event, precomputed so day queries stay index-friendly.</summary>
    public DateOnly LocalDate { get; set; }

    // ---- who -------------------------------------------------------------------

    public int? TagId { get; set; }
    public RfidTag? Tag { get; set; }
    /// <summary>Kept verbatim even for unknown tags, which have no TagId to point at.</summary>
    public string Epc { get; set; } = string.Empty;

    public int? StudentId { get; set; }
    public Student? Student { get; set; }
    public int? TeacherId { get; set; }
    public Teacher? Teacher { get; set; }
    public int? StaffMemberId { get; set; }
    public StaffMember? StaffMember { get; set; }

    // ---- where -----------------------------------------------------------------

    public int? ReaderId { get; set; }
    public RfidReader? Reader { get; set; }
    public int? LocationId { get; set; }
    public RfidLocation? Location { get; set; }

    // ---- academic context resolved at ingestion --------------------------------
    // Denormalised on purpose: the timetable can be edited later, and a movement record
    // must keep saying which lesson it was judged against at the time.

    public int? TimetableSlotId { get; set; }
    public TimetableSlot? TimetableSlot { get; set; }
    public int? SubjectId { get; set; }
    public Subject? Subject { get; set; }
    public int? SectionId { get; set; }
    public Section? Section { get; set; }

    public EventSource Source { get; set; } = EventSource.Rfid;

    /// <summary>The collapsed antenna path that produced the decision, e.g. "1,2,3".</summary>
    public string? AntennaSequence { get; set; }
    /// <summary>Number of raw hits behind this one event.</summary>
    public int RawReadCount { get; set; }
    /// <summary>0..1 - lower when direction had to be inferred rather than observed.</summary>
    public double Confidence { get; set; } = 1.0;

    /// <summary>Why an event was rejected (unknown tag, revoked card, inactive reader).</summary>
    public string? RejectionReason { get; set; }

    public bool NotificationSent { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

/// <summary>
/// A batch that could not be processed after all retries. Nothing is silently dropped:
/// an operator can inspect the payload, fix the cause and replay it.
/// </summary>
public class RfidDeadLetter
{
    public long Id { get; set; }
    public int SchoolId { get; set; } = 1;

    public string? DeviceId { get; set; }
    public string PayloadJson { get; set; } = string.Empty;
    public string ErrorMessage { get; set; } = string.Empty;
    public string? ErrorDetail { get; set; }
    public int RetryCount { get; set; }
    public DateTime FirstFailedAtUtc { get; set; }
    public DateTime LastFailedAtUtc { get; set; }

    public bool IsResolved { get; set; }
    public DateTime? ResolvedAtUtc { get; set; }
    public int? ResolvedByUserId { get; set; }
    public string? ResolutionNotes { get; set; }
}

/// <summary>Device-side telemetry: heartbeats, connects, disconnects, errors, firmware changes.</summary>
public class DeviceLog
{
    public long Id { get; set; }
    public int SchoolId { get; set; } = 1;

    public int? ReaderId { get; set; }
    public RfidReader? Reader { get; set; }
    public string? DeviceId { get; set; }

    public DeviceLogLevel Level { get; set; } = DeviceLogLevel.Info;
    public string EventName { get; set; } = string.Empty;   // Heartbeat, Offline, Reconnected...
    public string? Message { get; set; }
    public string? DetailJson { get; set; }
    public DateTime OccurredAtUtc { get; set; }
}
