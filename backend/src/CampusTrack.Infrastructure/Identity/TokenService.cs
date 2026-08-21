using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using CampusTrack.Application.Authorization;
using CampusTrack.Application.Common.Interfaces;
using CampusTrack.Application.Identity;
using CampusTrack.Domain.Enums;
using CampusTrack.Domain.Identity;
using CampusTrack.Infrastructure.Persistence;
using CampusTrack.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace CampusTrack.Infrastructure.Identity;

public interface ITokenService
{
    Task<AuthResult> IssueAsync(ApplicationUser user, string? deviceName, DevicePlatform platform,
        string? ipAddress, CancellationToken ct = default);

    Task<AuthResult> RefreshAsync(string refreshToken, string? ipAddress, CancellationToken ct = default);

    Task RevokeAsync(string refreshToken, string? ipAddress, string reason, CancellationToken ct = default);
    Task RevokeAllForUserAsync(int userId, string reason, CancellationToken ct = default);
}

/// <summary>
/// Issues access tokens and manages the refresh-token lifecycle.
///
/// Refresh tokens are stored only as hashes and are rotated on every use. If a token that has
/// already been consumed is presented again, the entire descendant chain is revoked: either a
/// token was stolen or a client is misbehaving, and both warrant ending the session rather
/// than silently issuing new credentials.
/// </summary>
public class TokenService : ITokenService
{
    private readonly CampusTrackDbContext _db;
    private readonly IUserProfileBuilder _profiles;
    private readonly ITokenHasher _hasher;
    private readonly IDateTimeProvider _clock;
    private readonly JwtOptions _options;
    private readonly ILogger<TokenService> _logger;

    public TokenService(
        CampusTrackDbContext db,
        IUserProfileBuilder profiles,
        ITokenHasher hasher,
        IDateTimeProvider clock,
        IOptions<JwtOptions> options,
        ILogger<TokenService> logger)
    {
        _db = db;
        _profiles = profiles;
        _hasher = hasher;
        _clock = clock;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<AuthResult> IssueAsync(ApplicationUser user, string? deviceName, DevicePlatform platform,
        string? ipAddress, CancellationToken ct = default)
    {
        var profile = await _profiles.BuildAsync(user, ct);
        var now = _clock.UtcNow;

        var accessExpires = now.AddMinutes(_options.AccessTokenMinutes);
        var accessToken = CreateAccessToken(user, profile, accessExpires);

        var (refreshPlain, refreshEntity) = CreateRefreshToken(user.Id, deviceName, platform, ipAddress, now);
        _db.RefreshTokens.Add(refreshEntity);

        user.LastLoginAtUtc = now;
        user.LastLoginIp = ipAddress;
        await _db.SaveChangesAsync(ct);

        return new AuthResult
        {
            AccessToken = accessToken,
            RefreshToken = refreshPlain,
            AccessTokenExpiresAtUtc = accessExpires,
            RefreshTokenExpiresAtUtc = refreshEntity.ExpiresAtUtc,
            User = profile
        };
    }

    public async Task<AuthResult> RefreshAsync(string refreshToken, string? ipAddress, CancellationToken ct = default)
    {
        var hash = _hasher.Hash(refreshToken);
        var now = _clock.UtcNow;

        var stored = await _db.RefreshTokens
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.TokenHash == hash, ct);

        if (stored is null)
            throw new UnauthorizedAccessException("Invalid refresh token.");

        // Replay of an already-rotated token: treat the whole chain as compromised.
        if (stored.RevokedAtUtc is not null)
        {
            _logger.LogWarning(
                "Refresh token reuse detected for user {UserId} from {Ip}. Revoking the token family.",
                stored.UserId, ipAddress);

            await RevokeDescendantsAsync(stored, "Reuse of a rotated token detected", ipAddress, ct);
            await _db.SaveChangesAsync(ct);
            throw new UnauthorizedAccessException("This session has been ended for security reasons. Please sign in again.");
        }

        if (stored.IsExpired(now))
            throw new UnauthorizedAccessException("Refresh token has expired.");

        var user = stored.User ?? throw new UnauthorizedAccessException("Account no longer exists.");
        if (!user.IsActive || user.IsDeleted)
            throw new UnauthorizedAccessException("This account is not active.");

        // Rotate: retire the presented token and mint a replacement.
        var (newPlain, newEntity) = CreateRefreshToken(user.Id, stored.DeviceName, stored.Platform, ipAddress, now);
        stored.RevokedAtUtc = now;
        stored.RevokedByIp = ipAddress;
        stored.RevokedReason = "Rotated";
        stored.ReplacedByTokenHash = newEntity.TokenHash;
        _db.RefreshTokens.Add(newEntity);

        // Permissions are re-read here, which is what bounds how long a revoked grant survives.
        var profile = await _profiles.BuildAsync(user, ct);
        var accessExpires = now.AddMinutes(_options.AccessTokenMinutes);
        var accessToken = CreateAccessToken(user, profile, accessExpires);

        await _db.SaveChangesAsync(ct);

        return new AuthResult
        {
            AccessToken = accessToken,
            RefreshToken = newPlain,
            AccessTokenExpiresAtUtc = accessExpires,
            RefreshTokenExpiresAtUtc = newEntity.ExpiresAtUtc,
            User = profile
        };
    }

    public async Task RevokeAsync(string refreshToken, string? ipAddress, string reason, CancellationToken ct = default)
    {
        var hash = _hasher.Hash(refreshToken);
        var stored = await _db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (stored is null || stored.RevokedAtUtc is not null) return;

        stored.RevokedAtUtc = _clock.UtcNow;
        stored.RevokedByIp = ipAddress;
        stored.RevokedReason = reason;
        await _db.SaveChangesAsync(ct);
    }

    public async Task RevokeAllForUserAsync(int userId, string reason, CancellationToken ct = default)
    {
        var now = _clock.UtcNow;
        var active = await _db.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAtUtc == null && t.ExpiresAtUtc > now)
            .ToListAsync(ct);

        foreach (var token in active)
        {
            token.RevokedAtUtc = now;
            token.RevokedReason = reason;
        }

        await _db.SaveChangesAsync(ct);
    }

    /// <summary>Walks the replacement chain forward, revoking everything derived from a reused token.</summary>
    private async Task RevokeDescendantsAsync(RefreshToken start, string reason, string? ipAddress, CancellationToken ct)
    {
        var now = _clock.UtcNow;
        var currentHash = start.ReplacedByTokenHash;
        var guard = 0;

        while (!string.IsNullOrEmpty(currentHash) && guard++ < 64)
        {
            var next = await _db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == currentHash, ct);
            if (next is null) break;

            if (next.RevokedAtUtc is null)
            {
                next.RevokedAtUtc = now;
                next.RevokedByIp = ipAddress;
                next.RevokedReason = reason;
            }

            currentHash = next.ReplacedByTokenHash;
        }

        // Also end any other live sessions for this account - the credential is untrusted now.
        var others = await _db.RefreshTokens
            .Where(t => t.UserId == start.UserId && t.RevokedAtUtc == null && t.ExpiresAtUtc > now)
            .ToListAsync(ct);

        foreach (var token in others)
        {
            token.RevokedAtUtc = now;
            token.RevokedByIp = ipAddress;
            token.RevokedReason = reason;
        }
    }

    private (string PlainText, RefreshToken Entity) CreateRefreshToken(
        int userId, string? deviceName, DevicePlatform platform, string? ipAddress, DateTime now)
    {
        var plain = _hasher.GenerateSecureToken();
        var entity = new RefreshToken
        {
            UserId = userId,
            TokenHash = _hasher.Hash(plain),
            ExpiresAtUtc = now.AddDays(_options.RefreshTokenDays),
            CreatedAtUtc = now,
            CreatedByIp = ipAddress,
            DeviceName = deviceName,
            Platform = platform
        };
        return (plain, entity);
    }

    private string CreateAccessToken(ApplicationUser user, UserProfile profile, DateTime expiresAtUtc)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.UserName ?? string.Empty),
            new(CampusClaims.FullName, profile.FullName),
            new(CampusClaims.SchoolId, user.SchoolId.ToString())
        };

        if (!string.IsNullOrEmpty(user.Email)) claims.Add(new Claim(ClaimTypes.Email, user.Email));
        if (profile.StudentId is { } sid) claims.Add(new Claim(CampusClaims.StudentId, sid.ToString()));
        if (profile.TeacherId is { } tid) claims.Add(new Claim(CampusClaims.TeacherId, tid.ToString()));
        if (profile.GuardianId is { } gid) claims.Add(new Claim(CampusClaims.GuardianId, gid.ToString()));
        if (profile.StaffMemberId is { } fid) claims.Add(new Claim(CampusClaims.StaffId, fid.ToString()));
        if (user.MustChangePassword) claims.Add(new Claim(CampusClaims.MustChangePassword, "true"));

        claims.AddRange(profile.Roles.Select(r => new Claim(ClaimTypes.Role, r)));

        // SuperAdmin is authorised by role, so its permissions are left out of the token to
        // keep the Authorization header small. Everyone else carries their explicit grants.
        if (!profile.Roles.Contains(Permissions.RoleNames.SuperAdmin))
            claims.AddRange(profile.Permissions.Select(p => new Claim(CampusClaims.Permission, p)));

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.Key));
        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: _clock.UtcNow,
            expires: expiresAtUtc,
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

}
