using System.ComponentModel.DataAnnotations;
using CampusTrack.Domain.Enums;

namespace CampusTrack.Application.Identity;

public record LoginRequest
{
    [Required] public string UserNameOrEmail { get; init; } = string.Empty;
    [Required] public string Password { get; init; } = string.Empty;
    public string? DeviceName { get; init; }
    public DevicePlatform Platform { get; init; } = DevicePlatform.Unknown;
}

public record RefreshRequest
{
    [Required] public string RefreshToken { get; init; } = string.Empty;
}

public record AuthResult
{
    public string AccessToken { get; init; } = string.Empty;
    public string RefreshToken { get; init; } = string.Empty;
    public DateTime AccessTokenExpiresAtUtc { get; init; }
    public DateTime RefreshTokenExpiresAtUtc { get; init; }
    public string TokenType { get; init; } = "Bearer";
    public UserProfile User { get; init; } = new();
}

/// <summary>
/// What a client needs to render the right portal immediately after sign-in, without a
/// second round trip. Nothing sensitive: no password state, no internal flags beyond the
/// forced-password-change hint the UI must act on.
/// </summary>
public record UserProfile
{
    public int Id { get; init; }
    public string UserName { get; init; } = string.Empty;
    public string FullName { get; init; } = string.Empty;
    public string? Email { get; init; }
    public string? PhoneNumber { get; init; }
    public string? ProfileImageUrl { get; init; }
    public IReadOnlyList<string> Roles { get; init; } = [];
    public IReadOnlyList<string> Permissions { get; init; } = [];

    public int? StudentId { get; init; }
    public int? TeacherId { get; init; }
    public int? GuardianId { get; init; }
    public int? StaffMemberId { get; init; }

    /// <summary>Which portal the client should open by default.</summary>
    public string PrimaryPortal { get; init; } = "student";
    public bool MustChangePassword { get; init; }
    public string? TimeZoneId { get; init; }
    public string PreferredLanguage { get; init; } = "en";
    public int SchoolId { get; init; }
    public string? SchoolName { get; init; }
}

public record ChangePasswordRequest
{
    [Required] public string CurrentPassword { get; init; } = string.Empty;
    [Required, MinLength(8)] public string NewPassword { get; init; } = string.Empty;
}

public record ForgotPasswordRequest
{
    [Required, EmailAddress] public string Email { get; init; } = string.Empty;
}

public record ResetPasswordRequest
{
    [Required] public string Email { get; init; } = string.Empty;
    [Required] public string Token { get; init; } = string.Empty;
    [Required, MinLength(8)] public string NewPassword { get; init; } = string.Empty;
}

public record AdminResetPasswordRequest
{
    [Required] public int UserId { get; init; }
    [Required, MinLength(8)] public string NewPassword { get; init; } = string.Empty;
    /// <summary>Force the user to choose their own password at next sign-in.</summary>
    public bool RequireChangeOnNextLogin { get; init; } = true;
}

public record RegisterDeviceTokenRequest
{
    [Required] public string Token { get; init; } = string.Empty;
    public DevicePlatform Platform { get; init; } = DevicePlatform.Unknown;
    public string? DeviceName { get; init; }
    public string? AppVersion { get; init; }
}
