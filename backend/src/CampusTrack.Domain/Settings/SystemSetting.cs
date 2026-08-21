using CampusTrack.Domain.Common;
using CampusTrack.Domain.Enums;

namespace CampusTrack.Domain.Settings;

/// <summary>
/// A runtime-editable setting. Anything a school might reasonably want to change - the late
/// threshold, the debounce window, the daily-report send time - lives here rather than in
/// appsettings, so changing it does not need a redeploy. Secrets stay in configuration.
/// </summary>
public class SystemSetting : TenantEntity<int>
{
    public string Key { get; set; } = string.Empty;          // "Attendance.LateThresholdMinutes"
    public string Category { get; set; } = string.Empty;     // Attendance, Rfid, Notifications...
    public string? Value { get; set; }
    public string? DefaultValue { get; set; }
    public SettingDataType DataType { get; set; } = SettingDataType.String;

    public string DisplayName { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>Rendered as a masked field and redacted in audit logs and exports.</summary>
    public bool IsSecret { get; set; }
    /// <summary>Editable by admins from the settings screen.</summary>
    public bool IsEditable { get; set; } = true;
    public int DisplayOrder { get; set; }
}
