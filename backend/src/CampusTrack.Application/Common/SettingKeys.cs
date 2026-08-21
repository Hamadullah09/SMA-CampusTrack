using CampusTrack.Domain.Enums;

namespace CampusTrack.Application.Common;

/// <summary>
/// Every runtime-tunable knob, with the default a fresh install starts from. Keeping the
/// catalogue in one place means the settings screen, the seeder and the code that reads a
/// value can never drift apart.
/// </summary>
public static class SettingKeys
{
    // ---- attendance ---------------------------------------------------------------
    /// <summary>Arriving more than this many minutes after the day starts counts as Late.</summary>
    public const string LateThresholdMinutes = "Attendance.LateThresholdMinutes";
    /// <summary>Grace period after a lesson starts before a classroom entry counts as late.</summary>
    public const string SessionGraceMinutes = "Attendance.SessionGraceMinutes";
    /// <summary>Leaving more than this many minutes before the day ends counts as an early exit.</summary>
    public const string EarlyLeaveThresholdMinutes = "Attendance.EarlyLeaveThresholdMinutes";
    /// <summary>Fraction of a lesson a student must be present for to be marked Present.</summary>
    public const string MinimumSessionPresencePercent = "Attendance.MinimumSessionPresencePercent";
    public const string SchoolDayStart = "Attendance.SchoolDayStart";
    public const string SchoolDayEnd = "Attendance.SchoolDayEnd";
    /// <summary>Mark students with no entry event absent at this time each day.</summary>
    public const string AbsenceFinalisationTime = "Attendance.AbsenceFinalisationTime";
    public const string AutoCloseOpenPresences = "Attendance.AutoCloseOpenPresences";

    // ---- RFID ---------------------------------------------------------------------
    /// <summary>Silence after which a tag's pass-through is considered complete.</summary>
    public const string RfidQuietWindowMs = "Rfid.QuietWindowMs";
    /// <summary>Hard ceiling on one pass-through, for someone loitering in the field.</summary>
    public const string RfidMaxSequenceMs = "Rfid.MaxSequenceMs";
    /// <summary>Identical movement (same tag, location, direction) inside this window is dropped.</summary>
    public const string RfidDebounceSeconds = "Rfid.DebounceSeconds";
    public const string RfidMinimumRssi = "Rfid.MinimumRssi";
    /// <summary>Multiple of the heartbeat interval before a reader is declared offline.</summary>
    public const string RfidOfflineAfterMissedHeartbeats = "Rfid.OfflineAfterMissedHeartbeats";
    public const string RfidRetainRawReadsDays = "Rfid.RetainRawReadsDays";
    public const string RfidMaxIngestBatchSize = "Rfid.MaxIngestBatchSize";
    /// <summary>Reject reads whose device clock is further than this from the server's.</summary>
    public const string RfidMaxClockSkewMinutes = "Rfid.MaxClockSkewMinutes";

    // ---- notifications -------------------------------------------------------------
    public const string NotifyOnSchoolEntry = "Notifications.NotifyOnSchoolEntry";
    public const string NotifyOnSchoolExit = "Notifications.NotifyOnSchoolExit";
    public const string NotifyOnClassroomMovement = "Notifications.NotifyOnClassroomMovement";
    public const string NotifyOnAbsence = "Notifications.NotifyOnAbsence";
    public const string NotifyOnLateArrival = "Notifications.NotifyOnLateArrival";
    public const string DailyReportTime = "Notifications.DailyReportTime";
    public const string DailyReportEnabled = "Notifications.DailyReportEnabled";
    public const string PushMaxRetries = "Notifications.PushMaxRetries";

    // ---- academic -------------------------------------------------------------------
    public const string DefaultGradeScaleId = "Academic.DefaultGradeScaleId";
    public const string AttendanceRequiredPercent = "Academic.AttendanceRequiredPercent";
    public const string StudentCodePrefix = "Academic.StudentCodePrefix";
    public const string TeacherCodePrefix = "Academic.TeacherCodePrefix";
    public const string GuardianCodePrefix = "Academic.GuardianCodePrefix";
    public const string StaffCodePrefix = "Academic.StaffCodePrefix";

    // ---- security ---------------------------------------------------------------------
    public const string MaxFailedLoginAttempts = "Security.MaxFailedLoginAttempts";
    public const string LockoutMinutes = "Security.LockoutMinutes";
    public const string AccessTokenMinutes = "Security.AccessTokenMinutes";
    public const string RefreshTokenDays = "Security.RefreshTokenDays";
    public const string AuditRetentionDays = "Security.AuditRetentionDays";

    /// <summary>Seed definitions: key, category, default, type, label, description.</summary>
    public static readonly SettingSeed[] Defaults =
    [
        new(LateThresholdMinutes, "Attendance", "15", SettingDataType.Integer, "Late threshold (minutes)",
            "How many minutes after the school day starts a student may arrive before being marked Late."),
        new(SessionGraceMinutes, "Attendance", "10", SettingDataType.Integer, "Lesson grace period (minutes)",
            "Minutes after a lesson starts during which a classroom entry still counts as on time."),
        new(EarlyLeaveThresholdMinutes, "Attendance", "20", SettingDataType.Integer, "Early leave threshold (minutes)",
            "Leaving more than this many minutes before the day ends is recorded as an early exit."),
        new(MinimumSessionPresencePercent, "Attendance", "60", SettingDataType.Integer, "Minimum presence for a lesson (%)",
            "Share of a lesson a student must be in the room to be counted Present rather than Partial."),
        new(SchoolDayStart, "Attendance", "07:45", SettingDataType.Time, "School day starts", "First bell."),
        new(SchoolDayEnd, "Attendance", "14:30", SettingDataType.Time, "School day ends", "Last bell."),
        new(AbsenceFinalisationTime, "Attendance", "11:00", SettingDataType.Time, "Absence finalisation time",
            "Students with no entry event by this time are marked absent and guardians are notified."),
        new(AutoCloseOpenPresences, "Attendance", "true", SettingDataType.Boolean, "Auto-close open room presences",
            "Close room presence intervals left open at the end of the day (a missed exit read)."),

        new(RfidQuietWindowMs, "Rfid", "4000", SettingDataType.Integer, "Quiet window (ms)",
            "Silence from a tag after which its pass-through is treated as finished."),
        new(RfidMaxSequenceMs, "Rfid", "30000", SettingDataType.Integer, "Maximum sequence span (ms)",
            "Upper bound on one pass-through, for a tag idling inside the read field."),
        new(RfidDebounceSeconds, "Rfid", "60", SettingDataType.Integer, "Duplicate suppression window (s)",
            "Repeat movements for the same tag, place and direction inside this window are discarded."),
        new(RfidMinimumRssi, "Rfid", "-70", SettingDataType.Integer, "Minimum signal strength (dBm)",
            "Weaker reads are ignored as stray far-field detections."),
        new(RfidOfflineAfterMissedHeartbeats, "Rfid", "3", SettingDataType.Integer, "Missed heartbeats before offline",
            "How many heartbeats a reader may miss before it is shown as offline."),
        new(RfidRetainRawReadsDays, "Rfid", "90", SettingDataType.Integer, "Raw read retention (days)",
            "How long individual antenna hits are kept before the cleanup job removes them."),
        new(RfidMaxIngestBatchSize, "Rfid", "500", SettingDataType.Integer, "Maximum reads per ingest call",
            "Larger batches from a gateway are rejected to bound request size."),
        new(RfidMaxClockSkewMinutes, "Rfid", "10", SettingDataType.Integer, "Maximum device clock skew (minutes)",
            "Reads timestamped further than this from server time are clamped and flagged."),

        new(NotifyOnSchoolEntry, "Notifications", "true", SettingDataType.Boolean, "Notify on arrival",
            "Push a notification to guardians when a child enters the school."),
        new(NotifyOnSchoolExit, "Notifications", "true", SettingDataType.Boolean, "Notify on departure",
            "Push a notification to guardians when a child leaves the school."),
        new(NotifyOnClassroomMovement, "Notifications", "false", SettingDataType.Boolean, "Notify on room movement",
            "Push a notification for every monitored room a child enters. Off by default: it is a lot of messages."),
        new(NotifyOnAbsence, "Notifications", "true", SettingDataType.Boolean, "Notify on absence",
            "Tell guardians when a child is marked absent."),
        new(NotifyOnLateArrival, "Notifications", "true", SettingDataType.Boolean, "Notify on late arrival",
            "Tell guardians when a child arrives after the late threshold."),
        new(DailyReportEnabled, "Notifications", "true", SettingDataType.Boolean, "Send daily report", ""),
        new(DailyReportTime, "Notifications", "18:00", SettingDataType.Time, "Daily report time",
            "When the end-of-day summary is generated and sent to guardians."),
        new(PushMaxRetries, "Notifications", "3", SettingDataType.Integer, "Push retry attempts", ""),

        new(AttendanceRequiredPercent, "Academic", "75", SettingDataType.Integer, "Required attendance (%)",
            "Attendance below this is highlighted as at risk on dashboards and reports."),
        new(StudentCodePrefix, "Academic", "STU-", SettingDataType.String, "Student code prefix", ""),
        new(TeacherCodePrefix, "Academic", "TCH-", SettingDataType.String, "Teacher code prefix", ""),
        new(GuardianCodePrefix, "Academic", "GRD-", SettingDataType.String, "Guardian code prefix", ""),
        new(StaffCodePrefix, "Academic", "STF-", SettingDataType.String, "Staff code prefix", ""),

        new(MaxFailedLoginAttempts, "Security", "5", SettingDataType.Integer, "Failed sign-ins before lockout", ""),
        new(LockoutMinutes, "Security", "15", SettingDataType.Integer, "Lockout duration (minutes)", ""),
        new(AccessTokenMinutes, "Security", "30", SettingDataType.Integer, "Access token lifetime (minutes)",
            "Short by design: permission changes take effect within one token lifetime."),
        new(RefreshTokenDays, "Security", "30", SettingDataType.Integer, "Refresh token lifetime (days)", ""),
        new(AuditRetentionDays, "Security", "730", SettingDataType.Integer, "Audit log retention (days)", "")
    ];
}

public record SettingSeed(
    string Key,
    string Category,
    string DefaultValue,
    SettingDataType DataType,
    string DisplayName,
    string Description);
