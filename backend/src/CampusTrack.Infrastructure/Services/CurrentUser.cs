using System.Security.Claims;
using CampusTrack.Application.Common.Interfaces;
using Microsoft.AspNetCore.Http;

namespace CampusTrack.Infrastructure.Services;

/// <summary>
/// Claim names this system adds on top of the standard set.
///
/// These are deliberately prefixed. ASP.NET Core rewrites inbound claim types through
/// <c>DefaultInboundClaimTypeMap</c>, and several short names are already spoken for by the
/// Microsoft identity stack: "tid" becomes the Azure tenant id, "sid" becomes ClaimTypes.Sid.
/// A token issued with those names comes back under a different type, so a lookup by the
/// original name silently returns null and the caller looks like they have no profile.
/// Prefixing keeps them ours.
/// </summary>
public static class CampusClaims
{
    public const string Permission = "perm";
    public const string SchoolId = "school";
    public const string StudentId = "ct_sid";
    public const string TeacherId = "ct_tid";
    public const string GuardianId = "ct_gid";
    public const string StaffId = "ct_fid";
    public const string FullName = "name_full";
    public const string MustChangePassword = "pwd_reset";
    /// <summary>Marks the short-lived token issued to a device rather than a person.</summary>
    public const string DeviceId = "device";
}

/// <summary>
/// Reads the caller out of the current request's claims. Permissions are carried in the
/// access token so authorisation is a set lookup rather than a database round trip on every
/// request; the token's short lifetime bounds how long a revoked permission can linger, and
/// the refresh flow re-reads them from the database.
/// </summary>
public class CurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _accessor;
    private readonly Lazy<HashSet<string>> _permissions;
    private readonly Lazy<HashSet<string>> _roles;

    public CurrentUser(IHttpContextAccessor accessor)
    {
        _accessor = accessor;
        _permissions = new Lazy<HashSet<string>>(() =>
            new HashSet<string>(
                Principal?.FindAll(CampusClaims.Permission).Select(c => c.Value) ?? [],
                StringComparer.OrdinalIgnoreCase));
        _roles = new Lazy<HashSet<string>>(() =>
            new HashSet<string>(
                Principal?.FindAll(ClaimTypes.Role).Select(c => c.Value) ?? [],
                StringComparer.OrdinalIgnoreCase));
    }

    private ClaimsPrincipal? Principal => _accessor.HttpContext?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated ?? false;

    public int? UserId => ParseInt(Principal?.FindFirstValue(ClaimTypes.NameIdentifier));
    public string? UserName => Principal?.FindFirstValue(ClaimTypes.Name);

    /// <summary>Defaults to the single seeded school for unauthenticated and device calls.</summary>
    public int SchoolId => ParseInt(Principal?.FindFirstValue(CampusClaims.SchoolId)) ?? 1;

    public int? StudentId => ParseInt(Principal?.FindFirstValue(CampusClaims.StudentId));
    public int? TeacherId => ParseInt(Principal?.FindFirstValue(CampusClaims.TeacherId));
    public int? GuardianId => ParseInt(Principal?.FindFirstValue(CampusClaims.GuardianId));
    public int? StaffMemberId => ParseInt(Principal?.FindFirstValue(CampusClaims.StaffId));

    public IReadOnlyCollection<string> Roles => _roles.Value;
    public IReadOnlyCollection<string> Permissions => _permissions.Value;

    public string? IpAddress
    {
        get
        {
            var context = _accessor.HttpContext;
            if (context is null) return null;

            // Behind a reverse proxy the socket address is the proxy; the forwarded-headers
            // middleware rewrites RemoteIpAddress, so read that rather than the raw header.
            return context.Connection.RemoteIpAddress?.ToString();
        }
    }

    public string? UserAgent => _accessor.HttpContext?.Request.Headers.UserAgent.ToString();

    public string? CorrelationId => _accessor.HttpContext?.TraceIdentifier;

    public bool HasPermission(string permission) => _permissions.Value.Contains(permission);
    public bool IsInRole(string role) => _roles.Value.Contains(role);

    private static int? ParseInt(string? value) =>
        int.TryParse(value, out var parsed) ? parsed : null;
}

/// <summary>
/// Stand-in used by background services and the seeder, where there is no request. Actions
/// taken by the system are attributed to "system" in the audit log rather than to nobody.
/// </summary>
public class SystemCurrentUser : ICurrentUser
{
    public int? UserId => null;
    public string? UserName => "system";
    public int SchoolId { get; set; } = 1;
    public bool IsAuthenticated => false;
    public IReadOnlyCollection<string> Roles => [];
    public IReadOnlyCollection<string> Permissions => [];
    public int? StudentId => null;
    public int? TeacherId => null;
    public int? GuardianId => null;
    public int? StaffMemberId => null;
    public string? IpAddress => null;
    public string? UserAgent => null;
    public string? CorrelationId => null;

    /// <summary>Background work is trusted; it never runs on behalf of a caller.</summary>
    public bool HasPermission(string permission) => true;
    public bool IsInRole(string role) => false;
}
