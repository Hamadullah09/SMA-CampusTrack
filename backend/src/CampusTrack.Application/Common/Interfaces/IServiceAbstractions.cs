using CampusTrack.Domain.Enums;

namespace CampusTrack.Application.Common.Interfaces;

/// <summary>
/// The authenticated caller, resolved from the JWT once per request. Services take this
/// rather than reaching for HttpContext, which keeps them testable and usable from
/// background workers (where there is no request at all).
/// </summary>
public interface ICurrentUser
{
    int? UserId { get; }
    string? UserName { get; }
    int SchoolId { get; }
    bool IsAuthenticated { get; }
    IReadOnlyCollection<string> Roles { get; }
    IReadOnlyCollection<string> Permissions { get; }

    /// <summary>Profile ids for the caller, so a teacher endpoint need not re-query them.</summary>
    int? StudentId { get; }
    int? TeacherId { get; }
    int? GuardianId { get; }
    int? StaffMemberId { get; }

    string? IpAddress { get; }
    string? UserAgent { get; }
    string? CorrelationId { get; }

    bool HasPermission(string permission);
    bool IsInRole(string role);
}

/// <summary>
/// Clock abstraction. Every timestamp in the system goes through this, which is what lets
/// the attendance and RFID tests replay a school day deterministically.
/// </summary>
public interface IDateTimeProvider
{
    DateTime UtcNow { get; }
    /// <summary>Now in the school's configured zone - the basis for "today" and bell times.</summary>
    DateTimeOffset SchoolNow { get; }
    DateOnly SchoolToday { get; }
    TimeZoneInfo SchoolTimeZone { get; }

    DateTime ToUtc(DateTime schoolLocal);
    DateTimeOffset ToSchoolTime(DateTime utc);
    DateOnly ToSchoolDate(DateTime utc);
}

/// <summary>Hashes and verifies device API keys and refresh tokens.</summary>
public interface ITokenHasher
{
    string Hash(string plainText);
    bool Verify(string plainText, string hash);
    string GenerateSecureToken(int byteLength = 48);
}

/// <summary>Stores uploaded files behind a provider-agnostic surface (local disk today, blob later).</summary>
/// <summary>
/// What a particular kind of upload is allowed to be.
///
/// The default policy is deliberately narrow -- documents and images a teacher might attach
/// to an assignment. Some uploads legitimately need different rules: a signed Android build
/// is an executable and far larger than any worksheet. Rather than widening the shared
/// allow-list until it accepts everything, each call site states the policy it needs, so
/// permission to attach a file to an assignment never becomes permission to upload a binary.
/// </summary>
public sealed record FileStoragePolicy(string[] AllowedExtensions, long MaxFileSizeBytes)
{
    /// <summary>Signed mobile builds published for families to sideload.</summary>
    public static readonly FileStoragePolicy MobileRelease =
        new([".apk", ".aab"], 200L * 1024 * 1024);
}

public interface IFileStorage
{
    /// <param name="policy">Null uses the configured default document policy.</param>
    Task<StoredFile> SaveAsync(
        Stream content, string originalFileName, string folder,
        FileStoragePolicy? policy = null, CancellationToken ct = default);
    Task<Stream?> OpenAsync(string storedPath, CancellationToken ct = default);
    Task<bool> DeleteAsync(string storedPath, CancellationToken ct = default);
    bool Exists(string storedPath);
}

public record StoredFile(string StoredPath, string FileName, string ContentType, long SizeBytes);

/// <summary>
/// Fans a notification out to the channels the recipient has enabled, after persisting it.
/// Callers never talk to FCM directly.
/// </summary>
public interface INotificationService
{
    Task<long> NotifyAsync(NotificationRequest request, CancellationToken ct = default);
    Task NotifyManyAsync(IEnumerable<NotificationRequest> requests, CancellationToken ct = default);
    /// <summary>Notifies every approved guardian of a student who has that category enabled.</summary>
    Task NotifyGuardiansOfStudentAsync(int studentId, NotificationRequest template, CancellationToken ct = default);
}

public class NotificationRequest
{
    public int UserId { get; set; }
    public NotificationCategory Category { get; set; }
    public NotificationPriority Priority { get; set; } = NotificationPriority.Normal;
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public object? Data { get; set; }
    public int? StudentId { get; set; }
    public string? RelatedEntityType { get; set; }
    public long? RelatedEntityId { get; set; }
}

/// <summary>Low-level push transport. Swapping FCM for another provider is one implementation.</summary>
public interface IPushSender
{
    Task<PushResult> SendAsync(PushMessage message, CancellationToken ct = default);
    bool IsConfigured { get; }
}

public record PushMessage(
    IReadOnlyCollection<string> DeviceTokens,
    string Title,
    string Body,
    IReadOnlyDictionary<string, string>? Data = null,
    NotificationPriority Priority = NotificationPriority.Normal);

public record PushResult(
    bool Success,
    int SuccessCount,
    int FailureCount,
    IReadOnlyCollection<string> InvalidTokens,
    string? ErrorMessage = null,
    string? ProviderMessageId = null);

/// <summary>Sends transactional email. No-ops with a warning when SMTP is unconfigured.</summary>
public interface IEmailSender
{
    Task<bool> SendAsync(string to, string subject, string htmlBody, CancellationToken ct = default);
    bool IsConfigured { get; }
}

/// <summary>
/// Pushes live updates to connected dashboards. Implemented over SignalR; abstracted so
/// domain services stay free of transport concerns and remain unit-testable.
/// </summary>
public interface IRealtimePublisher
{
    Task PublishRfidEventAsync(object payload, CancellationToken ct = default);
    Task PublishReaderStatusAsync(object payload, CancellationToken ct = default);
    Task PublishAttendanceUpdateAsync(object payload, CancellationToken ct = default);
    Task PublishDashboardCountersAsync(object payload, CancellationToken ct = default);
    Task PublishToUserAsync(int userId, string eventName, object payload, CancellationToken ct = default);
}

/// <summary>Reads runtime settings, with a short cache so hot paths do not hit the database.</summary>
public interface ISettingsProvider
{
    Task<string?> GetAsync(string key, CancellationToken ct = default);
    Task<T> GetAsync<T>(string key, T defaultValue, CancellationToken ct = default);
    Task SetAsync(string key, string? value, CancellationToken ct = default);
    void Invalidate();
}
