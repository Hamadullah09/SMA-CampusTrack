using CampusTrack.Domain.Academics;
using CampusTrack.Domain.Common;
using CampusTrack.Domain.Enums;
using CampusTrack.Domain.Identity;
using CampusTrack.Domain.People;

namespace CampusTrack.Domain.Communication;

/// <summary>
/// One notification for one user. Persisted first and pushed second, so the in-app history
/// stays complete even when FCM is down or the phone is offline for a week.
/// </summary>
public class Notification
{
    public long Id { get; set; }
    public int SchoolId { get; set; } = 1;

    public int UserId { get; set; }
    public ApplicationUser? User { get; set; }

    public NotificationCategory Category { get; set; }
    public NotificationPriority Priority { get; set; } = NotificationPriority.Normal;
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;

    /// <summary>Structured payload so the app can deep-link instead of parsing the text.</summary>
    public string? DataJson { get; set; }

    /// <summary>The child this concerns, when the recipient is a guardian of several.</summary>
    public int? StudentId { get; set; }
    public Student? Student { get; set; }

    public string? RelatedEntityType { get; set; }
    public long? RelatedEntityId { get; set; }

    public bool IsRead { get; set; }
    public DateTime? ReadAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }

    public ICollection<NotificationDelivery> Deliveries { get; set; } = new List<NotificationDelivery>();
}

/// <summary>
/// Per-channel delivery attempt. Separating this from the notification means a failed push
/// can be retried without duplicating what the user sees in their inbox.
/// </summary>
public class NotificationDelivery
{
    public long Id { get; set; }
    public long NotificationId { get; set; }
    public Notification? Notification { get; set; }

    public NotificationChannel Channel { get; set; }
    public DeliveryStatus Status { get; set; } = DeliveryStatus.Pending;
    public int AttemptCount { get; set; }
    public DateTime? SentAtUtc { get; set; }
    public DateTime? NextRetryAtUtc { get; set; }
    public string? ErrorMessage { get; set; }
    /// <summary>Provider-side id, for tracing a delivery in the FCM console.</summary>
    public string? ProviderMessageId { get; set; }
}

/// <summary>
/// What a user wants to hear about, per category and channel. A parent who works nights can
/// silence pushes without losing the in-app record.
/// </summary>
public class NotificationPreference
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public ApplicationUser? User { get; set; }

    public NotificationCategory Category { get; set; }
    public bool InAppEnabled { get; set; } = true;
    public bool PushEnabled { get; set; } = true;
    public bool EmailEnabled { get; set; }
    public bool SmsEnabled { get; set; }

    /// <summary>Quiet hours in the user's local time; Critical notifications ignore them.</summary>
    public TimeOnly? QuietHoursStart { get; set; }
    public TimeOnly? QuietHoursEnd { get; set; }
}

/// <summary>A registered push endpoint. One user may have several devices.</summary>
public class DeviceToken
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public ApplicationUser? User { get; set; }

    public string Token { get; set; } = string.Empty;
    public DevicePlatform Platform { get; set; } = DevicePlatform.Unknown;
    public string? DeviceName { get; set; }
    public string? AppVersion { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime LastSeenAtUtc { get; set; }
    public bool IsActive { get; set; } = true;
    /// <summary>Set when the provider reports the token as unregistered, so it stops being retried.</summary>
    public DateTime? InvalidatedAtUtc { get; set; }
}

public class Announcement : TenantEntity<int>
{
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public AnnouncementAudience Audience { get; set; } = AnnouncementAudience.Everyone;
    public NotificationPriority Priority { get; set; } = NotificationPriority.Normal;

    public bool IsPublished { get; set; }
    public DateTime? PublishAtUtc { get; set; }
    public DateTime? ExpiresAtUtc { get; set; }
    public int? PostedByUserId { get; set; }
    public string? AttachmentPath { get; set; }

    /// <summary>Also deliver as a push/in-app notification rather than only appearing in the feed.</summary>
    public bool SendAsNotification { get; set; } = true;

    public ICollection<AnnouncementTarget> Targets { get; set; } = new List<AnnouncementTarget>();
}

/// <summary>Restricts an announcement to specific sections when the audience is SpecificSections.</summary>
public class AnnouncementTarget
{
    public int Id { get; set; }
    public int AnnouncementId { get; set; }
    public Announcement? Announcement { get; set; }
    public int SectionId { get; set; }
    public Section? Section { get; set; }
}

public class SchoolEvent : TenantEntity<int>
{
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Location { get; set; }
    public DateTime StartAtUtc { get; set; }
    public DateTime EndAtUtc { get; set; }
    public bool IsAllDay { get; set; }
    public AnnouncementAudience Audience { get; set; } = AnnouncementAudience.Everyone;
    public string? ColourHex { get; set; }
    public bool IsPublished { get; set; } = true;
    public int? CreatedByUserIdRef { get; set; }
}

/// <summary>
/// Planned absence. Approved leave is what stops the attendance engine from reporting a
/// student as truant when the school already knows why they are away.
/// </summary>
public class LeaveRequest : TenantEntity<int>
{
    public LeaveRequesterType RequesterType { get; set; } = LeaveRequesterType.Student;
    public int? StudentId { get; set; }
    public Student? Student { get; set; }
    public int? TeacherId { get; set; }
    public Teacher? Teacher { get; set; }
    public int? StaffMemberId { get; set; }

    /// <summary>The account that filed it - typically a guardian on a child's behalf.</summary>
    public int RequestedByUserId { get; set; }

    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string? AttachmentPath { get; set; }

    public LeaveStatus Status { get; set; } = LeaveStatus.Pending;
    public int? ReviewedByUserId { get; set; }
    public DateTime? ReviewedAtUtc { get; set; }
    public string? ReviewNotes { get; set; }

    public int TotalDays => EndDate.DayNumber - StartDate.DayNumber + 1;
}

/// <summary>
/// The generated end-of-day digest for one child, stored so a guardian can reopen last
/// Tuesday's report rather than only catching the push that evening.
/// </summary>
public class DailyStudentReport : TenantEntity<long>
{
    public int StudentId { get; set; }
    public Student? Student { get; set; }
    public DateOnly Date { get; set; }

    public DateTime? SchoolEntryAtUtc { get; set; }
    public DateTime? SchoolExitAtUtc { get; set; }
    public DateTime? FirstClassroomEntryAtUtc { get; set; }
    public DateTime? LastClassroomExitAtUtc { get; set; }

    public int ClassesAttended { get; set; }
    public int ClassesMissed { get; set; }
    public int LateArrivals { get; set; }
    public int EarlyExits { get; set; }
    public decimal AttendancePercentage { get; set; }
    public AttendanceStatus DayStatus { get; set; } = AttendanceStatus.NotRecorded;

    /// <summary>Room-by-room timeline, pre-rendered so the app opens it without recomputing.</summary>
    public string? TimelineJson { get; set; }
    public string? HighlightsJson { get; set; }

    public bool IsSent { get; set; }
    public DateTime? SentAtUtc { get; set; }
    public DateTime GeneratedAtUtc { get; set; }
}

/// <summary>Guardian-raised feedback, kept as a threadable record with a school reply.</summary>
public class GuardianFeedback : TenantEntity<int>
{
    public int GuardianId { get; set; }
    public Guardian? Guardian { get; set; }
    public int? StudentId { get; set; }
    public Student? Student { get; set; }

    public string Category { get; set; } = "Suggestion";
    public string Subject { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string Status { get; set; } = "Open";          // Open, InProgress, Replied, Closed

    public string? Reply { get; set; }
    public int? RepliedByUserId { get; set; }
    public DateTime? RepliedAtUtc { get; set; }
}
