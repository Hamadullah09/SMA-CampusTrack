using CampusTrack.Domain.Common;

namespace CampusTrack.Domain.People;

/// <summary>
/// The institution itself. Single-school deployments have exactly one row; the
/// tenant column on every scoped entity points here so multi-school isolation is a
/// configuration change rather than a migration.
/// </summary>
public class School : AuditableEntity<int>
{
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? Country { get; set; }
    public string? PhoneNumber { get; set; }
    public string? Email { get; set; }
    public string? Website { get; set; }
    public string? LogoPath { get; set; }

    /// <summary>Wall-clock times (gate opening, class start) are interpreted in this zone.</summary>
    public string TimeZoneId { get; set; } = "UTC";
    public bool IsActive { get; set; } = true;
}
