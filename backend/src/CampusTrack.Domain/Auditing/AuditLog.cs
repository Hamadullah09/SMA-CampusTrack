using CampusTrack.Domain.Enums;

namespace CampusTrack.Domain.Auditing;

/// <summary>
/// Immutable record of a state change. Written by an EF interceptor rather than by hand, so
/// nothing that touches the database can quietly skip it. Old and new values are captured
/// per column, which is what makes "who changed this grade, and from what" answerable.
/// </summary>
public class AuditLog
{
    public long Id { get; set; }
    public int SchoolId { get; set; } = 1;

    public int? UserId { get; set; }
    /// <summary>Denormalised: the log must stay readable after an account is renamed or removed.</summary>
    public string? UserName { get; set; }
    public string? UserRole { get; set; }

    public AuditAction Action { get; set; }
    public string EntityName { get; set; } = string.Empty;
    public string? EntityId { get; set; }
    /// <summary>Human-readable subject, e.g. "Ahmed Ali (STU-000123)".</summary>
    public string? EntityDisplay { get; set; }

    public string? OldValuesJson { get; set; }
    public string? NewValuesJson { get; set; }
    public string? AffectedColumns { get; set; }

    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public string? DeviceId { get; set; }
    /// <summary>Ties every log line written while serving one request together.</summary>
    public string? CorrelationId { get; set; }
    public string? RequestPath { get; set; }

    public DateTime OccurredAtUtc { get; set; }
}

/// <summary>
/// Notable application-level occurrences that are not entity changes: a background job
/// result, a failed integration, a security event. Complements the structured file log by
/// being queryable from the admin UI.
/// </summary>
public class SystemLog
{
    public long Id { get; set; }
    public int SchoolId { get; set; } = 1;

    public string Level { get; set; } = "Information";
    public string Category { get; set; } = string.Empty;    // RfidEngine, Notifications, Auth...
    public string Message { get; set; } = string.Empty;
    public string? ExceptionType { get; set; }
    public string? ExceptionDetail { get; set; }
    public string? DataJson { get; set; }
    public string? CorrelationId { get; set; }
    public int? UserId { get; set; }
    public DateTime OccurredAtUtc { get; set; }
}
