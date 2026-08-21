using CampusTrack.Application.Common;
using CampusTrack.Application.Common.Interfaces;
using CampusTrack.Application.Identity;
using CampusTrack.Domain.Common;
using CampusTrack.Domain.Communication;
using CampusTrack.Domain.Enums;
using CampusTrack.Domain.Identity;
using CampusTrack.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CampusTrack.Infrastructure.Identity;

public interface IAuthService
{
    Task<AuthResult> LoginAsync(LoginRequest request, CancellationToken ct = default);
    Task<AuthResult> RefreshAsync(RefreshRequest request, CancellationToken ct = default);
    Task LogoutAsync(string refreshToken, CancellationToken ct = default);
    Task ChangePasswordAsync(int userId, ChangePasswordRequest request, CancellationToken ct = default);
    Task<string?> GeneratePasswordResetTokenAsync(string email, CancellationToken ct = default);
    Task ResetPasswordAsync(ResetPasswordRequest request, CancellationToken ct = default);
    Task<UserProfile> GetProfileAsync(int userId, CancellationToken ct = default);
    Task RegisterDeviceTokenAsync(int userId, RegisterDeviceTokenRequest request, CancellationToken ct = default);
    Task RemoveDeviceTokenAsync(int userId, string token, CancellationToken ct = default);
}

/// <summary>
/// Sign-in, sign-out and password lifecycle.
///
/// Two things here are deliberate rather than incidental: failure messages never reveal
/// whether an account exists, and every attempt is recorded before the result is returned so
/// that lockout and the security report work off the same evidence.
/// </summary>
public class AuthService : IAuthService
{
    private readonly CampusTrackDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ITokenService _tokens;
    private readonly IUserProfileBuilder _profiles;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;
    private readonly ISettingsProvider _settings;
    private readonly IEmailSender _email;
    private readonly ILogger<AuthService> _logger;

    private const string GenericFailure = "The username or password is incorrect.";

    public AuthService(
        CampusTrackDbContext db,
        UserManager<ApplicationUser> userManager,
        ITokenService tokens,
        IUserProfileBuilder profiles,
        ICurrentUser currentUser,
        IDateTimeProvider clock,
        ISettingsProvider settings,
        IEmailSender email,
        ILogger<AuthService> logger)
    {
        _db = db;
        _userManager = userManager;
        _tokens = tokens;
        _profiles = profiles;
        _currentUser = currentUser;
        _clock = clock;
        _settings = settings;
        _email = email;
        _logger = logger;
    }

    public async Task<AuthResult> LoginAsync(LoginRequest request, CancellationToken ct = default)
    {
        var identifier = request.UserNameOrEmail.Trim();
        var ip = _currentUser.IpAddress;

        var user = await _userManager.FindByNameAsync(identifier)
                   ?? await _userManager.FindByEmailAsync(identifier);

        if (user is null)
        {
            // Still hash a dummy password so a missing account and a wrong password take a
            // similar amount of time; otherwise the endpoint enumerates users by timing.
            await Task.Delay(Random.Shared.Next(80, 160), ct);
            await RecordAttemptAsync(null, identifier, false, "Unknown account", ip, ct);
            throw new UnauthorizedAccessException(GenericFailure);
        }

        if (user.IsDeleted || !user.IsActive)
        {
            await RecordAttemptAsync(user.Id, identifier, false, "Account disabled", ip, ct);
            throw new UnauthorizedAccessException("This account has been deactivated. Please contact the school office.");
        }

        if (await _userManager.IsLockedOutAsync(user))
        {
            await RecordAttemptAsync(user.Id, identifier, false, "Locked out", ip, ct);
            var until = user.LockoutEnd?.UtcDateTime;
            var minutes = until is null ? 0 : Math.Max(1, (int)(until.Value - _clock.UtcNow).TotalMinutes);
            throw new UnauthorizedAccessException(
                $"Too many failed sign-in attempts. Please try again in {minutes} minute(s).");
        }

        var passwordValid = await _userManager.CheckPasswordAsync(user, request.Password);
        if (!passwordValid)
        {
            // Increments the failure counter and locks the account once the threshold is hit.
            await _userManager.AccessFailedAsync(user);
            await RecordAttemptAsync(user.Id, identifier, false, "Bad password", ip, ct);
            throw new UnauthorizedAccessException(GenericFailure);
        }

        await _userManager.ResetAccessFailedCountAsync(user);
        await RecordAttemptAsync(user.Id, identifier, true, null, ip, ct);

        _logger.LogInformation("User {UserId} signed in from {Ip}", user.Id, ip);
        return await _tokens.IssueAsync(user, request.DeviceName, request.Platform, ip, ct);
    }

    public Task<AuthResult> RefreshAsync(RefreshRequest request, CancellationToken ct = default)
        => _tokens.RefreshAsync(request.RefreshToken, _currentUser.IpAddress, ct);

    public Task LogoutAsync(string refreshToken, CancellationToken ct = default)
        => _tokens.RevokeAsync(refreshToken, _currentUser.IpAddress, "Signed out", ct);

    public async Task ChangePasswordAsync(int userId, ChangePasswordRequest request, CancellationToken ct = default)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString())
            ?? throw new DomainException("not_found", "Account not found.");

        var result = await _userManager.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
        if (!result.Succeeded)
            throw new DomainException("password_change_failed", string.Join(" ", result.Errors.Select(e => e.Description)));

        user.MustChangePassword = false;
        user.PasswordChangedAtUtc = _clock.UtcNow;
        await _db.SaveChangesAsync(ct);

        // A password change ends every other session; a changed password should mean
        // "whoever else was signed in is now signed out".
        await _tokens.RevokeAllForUserAsync(userId, "Password changed", ct);
        _logger.LogInformation("User {UserId} changed their password", userId);
    }

    public async Task<string?> GeneratePasswordResetTokenAsync(string email, CancellationToken ct = default)
    {
        var user = await _userManager.FindByEmailAsync(email.Trim());

        // Returns null for unknown addresses; the caller responds identically either way so
        // the endpoint cannot be used to discover which addresses are registered.
        if (user is null || user.IsDeleted || !user.IsActive) return null;

        var token = await _userManager.GeneratePasswordResetTokenAsync(user);

        if (_email.IsConfigured && !string.IsNullOrEmpty(user.Email))
        {
            await _email.SendAsync(user.Email, "Reset your CampusTrack password",
                $"<p>Hello {user.FirstName},</p><p>Use this code to reset your password:</p>" +
                $"<p style=\"font-size:18px\"><strong>{token}</strong></p>" +
                "<p>If you did not request this, you can ignore this message.</p>", ct);
        }

        return token;
    }

    public async Task ResetPasswordAsync(ResetPasswordRequest request, CancellationToken ct = default)
    {
        var user = await _userManager.FindByEmailAsync(request.Email.Trim())
            ?? throw new DomainException("invalid_token", "This reset link is not valid.");

        var result = await _userManager.ResetPasswordAsync(user, request.Token, request.NewPassword);
        if (!result.Succeeded)
            throw new DomainException("invalid_token", string.Join(" ", result.Errors.Select(e => e.Description)));

        user.MustChangePassword = false;
        user.PasswordChangedAtUtc = _clock.UtcNow;
        await _userManager.ResetAccessFailedCountAsync(user);
        await _userManager.SetLockoutEndDateAsync(user, null);
        await _db.SaveChangesAsync(ct);

        await _tokens.RevokeAllForUserAsync(user.Id, "Password reset", ct);
        _logger.LogInformation("Password reset completed for user {UserId}", user.Id);
    }

    public async Task<UserProfile> GetProfileAsync(int userId, CancellationToken ct = default)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw new DomainException("not_found", "Account not found.");

        // Same builder the token service uses, so the profile endpoint and the sign-in
        // response can never disagree about what this user may do.
        return await _profiles.BuildAsync(user, ct);
    }

    public async Task RegisterDeviceTokenAsync(int userId, RegisterDeviceTokenRequest request, CancellationToken ct = default)
    {
        var now = _clock.UtcNow;

        var existing = await _db.DeviceTokens.FirstOrDefaultAsync(d => d.Token == request.Token, ct);
        if (existing is not null)
        {
            // The same handset can be handed to a different user (a shared family phone).
            existing.UserId = userId;
            existing.Platform = request.Platform;
            existing.DeviceName = request.DeviceName;
            existing.AppVersion = request.AppVersion;
            existing.LastSeenAtUtc = now;
            existing.IsActive = true;
            existing.InvalidatedAtUtc = null;
        }
        else
        {
            _db.DeviceTokens.Add(new DeviceToken
            {
                UserId = userId,
                Token = request.Token,
                Platform = request.Platform,
                DeviceName = request.DeviceName,
                AppVersion = request.AppVersion,
                CreatedAtUtc = now,
                LastSeenAtUtc = now,
                IsActive = true
            });
        }

        await _db.SaveChangesAsync(ct);
    }

    public async Task RemoveDeviceTokenAsync(int userId, string token, CancellationToken ct = default)
    {
        var existing = await _db.DeviceTokens
            .FirstOrDefaultAsync(d => d.Token == token && d.UserId == userId, ct);
        if (existing is null) return;

        existing.IsActive = false;
        existing.InvalidatedAtUtc = _clock.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    private async Task RecordAttemptAsync(int? userId, string identifier, bool success,
        string? reason, string? ip, CancellationToken ct)
    {
        _db.LoginAttempts.Add(new LoginAttempt
        {
            UserId = userId,
            UserNameOrEmail = identifier.Length > 256 ? identifier[..256] : identifier,
            Succeeded = success,
            FailureReason = reason,
            IpAddress = ip,
            UserAgent = _currentUser.UserAgent?.Length > 400 ? _currentUser.UserAgent[..400] : _currentUser.UserAgent,
            AttemptedAtUtc = _clock.UtcNow
        });

        await _db.SaveChangesAsync(ct);
    }
}
