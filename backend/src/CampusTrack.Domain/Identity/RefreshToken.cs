using CampusTrack.Domain.Enums;

namespace CampusTrack.Domain.Identity;

/// <summary>
/// One issued refresh token. Tokens are stored hashed and rotated on every use:
/// replaying a consumed token revokes the whole descendant chain, which turns a
/// stolen token into a detectable event rather than a silent takeover.
/// </summary>
public class RefreshToken
{
    public long Id { get; set; }
    public int UserId { get; set; }
    public ApplicationUser? User { get; set; }

    public string TokenHash { get; set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public string? CreatedByIp { get; set; }
    public string? DeviceName { get; set; }
    public DevicePlatform Platform { get; set; } = DevicePlatform.Unknown;

    public DateTime? RevokedAtUtc { get; set; }
    public string? RevokedByIp { get; set; }
    public string? RevokedReason { get; set; }
    /// <summary>Hash of the token that replaced this one, forming the rotation chain.</summary>
    public string? ReplacedByTokenHash { get; set; }

    public bool IsExpired(DateTime utcNow) => utcNow >= ExpiresAtUtc;
    public bool IsActive(DateTime utcNow) => RevokedAtUtc is null && !IsExpired(utcNow);
}

/// <summary>Every sign-in attempt, successful or not — feeds lockout and the security report.</summary>
public class LoginAttempt
{
    public long Id { get; set; }
    public int? UserId { get; set; }
    public string UserNameOrEmail { get; set; } = string.Empty;
    public bool Succeeded { get; set; }
    public string? FailureReason { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public DateTime AttemptedAtUtc { get; set; }
}
