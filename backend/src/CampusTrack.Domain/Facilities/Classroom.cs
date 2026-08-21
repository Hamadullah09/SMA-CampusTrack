using CampusTrack.Domain.Common;

namespace CampusTrack.Domain.Facilities;

/// <summary>
/// A physical teaching space. A classroom may or may not be RFID-monitored; when it is,
/// an <see cref="Rfid.RfidLocation"/> points at it and movement events gain a room context.
/// </summary>
public class Classroom : TenantEntity<int>
{
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string? Building { get; set; }
    public string? Floor { get; set; }
    public int Capacity { get; set; } = 40;
    public string? RoomType { get; set; }          // Lecture, Lab, Workshop...
    public bool HasProjector { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>Normalised 0..1 position on the school floor plan, for the live RFID map.</summary>
    public double? MapX { get; set; }
    public double? MapY { get; set; }
}
