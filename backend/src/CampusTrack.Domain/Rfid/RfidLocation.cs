using CampusTrack.Domain.Common;
using CampusTrack.Domain.Enums;
using CampusTrack.Domain.Facilities;

namespace CampusTrack.Domain.Rfid;

/// <summary>
/// A monitored place - a gate, a classroom door, the library entrance. Readers are
/// mounted at locations, and it is the location (not the reader) that gives an event its
/// meaning: crossing a boundary location changes campus presence, while entering a
/// classroom location changes room presence and feeds session attendance.
/// </summary>
public class RfidLocation : TenantEntity<int>
{
    public string Name { get; set; } = string.Empty;        // "Main Gate"
    public string Code { get; set; } = string.Empty;        // "MAIN-GATE"
    public LocationType LocationType { get; set; } = LocationType.Other;
    public string? Description { get; set; }
    public string? Building { get; set; }
    public string? Floor { get; set; }

    /// <summary>
    /// True for gates: passing here moves the student on or off campus and triggers the
    /// arrival/departure notification to guardians.
    /// </summary>
    public bool IsCampusBoundary { get; set; }

    /// <summary>When set, events here are attributed to this teaching room.</summary>
    public int? ClassroomId { get; set; }
    public Classroom? Classroom { get; set; }

    /// <summary>Normalised 0..1 coordinates for the live floor-plan monitor.</summary>
    public double? MapX { get; set; }
    public double? MapY { get; set; }

    /// <summary>Guardians are notified about movement here (gates always; rooms optionally).</summary>
    public bool NotifyGuardians { get; set; }

    /// <summary>Feed movements here into classroom and session attendance.</summary>
    public bool AffectsAttendance { get; set; } = true;

    public bool IsActive { get; set; } = true;

    public ICollection<RfidReader> Readers { get; set; } = new List<RfidReader>();
}
