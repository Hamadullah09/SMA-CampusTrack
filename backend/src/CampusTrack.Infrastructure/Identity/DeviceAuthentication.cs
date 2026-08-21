using System.Security.Claims;
using System.Text.Encodings.Web;
using CampusTrack.Application.Common.Interfaces;
using CampusTrack.Domain.Rfid;
using CampusTrack.Infrastructure.Persistence;
using CampusTrack.Infrastructure.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CampusTrack.Infrastructure.Identity;

public class DeviceAuthenticationOptions : AuthenticationSchemeOptions
{
    public const string SchemeName = "ReaderApiKey";
    public const string HeaderName = "X-Device-Key";
    public const string DeviceIdHeader = "X-Device-Id";
}

/// <summary>
/// Authenticates RFID readers and gateways.
///
/// Readers are devices, not people: they cannot complete an interactive sign-in or refresh a
/// token, so they present a per-device key over TLS. The key is stored only as a SHA-256
/// hash, is compared in constant time, and is scoped to a single reader - a key lifted from
/// one gate cannot be used to fabricate movement anywhere else on site.
/// </summary>
public class DeviceAuthenticationHandler : AuthenticationHandler<DeviceAuthenticationOptions>
{
    private readonly CampusTrackDbContext _db;
    private readonly ITokenHasher _hasher;
    private readonly RfidDeviceOptions _deviceOptions;

    public DeviceAuthenticationHandler(
        IOptionsMonitor<DeviceAuthenticationOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        CampusTrackDbContext db,
        ITokenHasher hasher,
        IOptions<RfidDeviceOptions> deviceOptions)
        : base(options, logger, encoder)
    {
        _db = db;
        _hasher = hasher;
        _deviceOptions = deviceOptions.Value;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(DeviceAuthenticationOptions.HeaderName, out var keyValues))
            return AuthenticateResult.NoResult();

        var presentedKey = keyValues.ToString();
        if (string.IsNullOrWhiteSpace(presentedKey))
            return AuthenticateResult.Fail("Device key missing.");

        var deviceId = Request.Headers.TryGetValue(DeviceAuthenticationOptions.DeviceIdHeader, out var idValues)
            ? idValues.ToString()
            : null;

        if (string.IsNullOrWhiteSpace(deviceId))
            return AuthenticateResult.Fail("Device id missing.");

        var reader = await _db.RfidReaders
            .AsNoTracking()
            .Where(r => r.DeviceId == deviceId)
            .Select(r => new { r.Id, r.DeviceId, r.Name, r.SchoolId, r.ApiKeyHash, r.IsActive, r.LocationId })
            .FirstOrDefaultAsync();

        if (reader is null)
        {
            Logger.LogWarning("Ingest attempt from unregistered device {DeviceId}", deviceId);
            return AuthenticateResult.Fail("Unknown device.");
        }

        if (!reader.IsActive)
            return AuthenticateResult.Fail("This device is disabled.");

        var authenticated = false;

        if (!string.IsNullOrEmpty(reader.ApiKeyHash))
        {
            authenticated = _hasher.Verify(presentedKey, reader.ApiKeyHash);
        }
        else if (!string.IsNullOrWhiteSpace(_deviceOptions.BootstrapApiKey))
        {
            // Site bring-up only: a reader that has not been issued its own key yet may use
            // the shared bootstrap key. Logged every time so it cannot quietly become the norm.
            authenticated = _hasher.Verify(presentedKey, _hasher.Hash(_deviceOptions.BootstrapApiKey));
            if (authenticated)
                Logger.LogWarning(
                    "Device {DeviceId} authenticated with the shared bootstrap key. Issue it a dedicated key.",
                    deviceId);
        }

        if (!authenticated)
        {
            Logger.LogWarning("Invalid device key presented for {DeviceId}", deviceId);
            return AuthenticateResult.Fail("Invalid device key.");
        }

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, $"device:{reader.Id}"),
            new Claim(ClaimTypes.Name, reader.Name),
            new Claim(CampusClaims.DeviceId, reader.DeviceId),
            new Claim(CampusClaims.SchoolId, reader.SchoolId.ToString()),
            new Claim("reader_id", reader.Id.ToString()),
            new Claim("location_id", reader.LocationId.ToString())
        };

        var identity = new ClaimsIdentity(claims, DeviceAuthenticationOptions.SchemeName);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), DeviceAuthenticationOptions.SchemeName);
        return AuthenticateResult.Success(ticket);
    }
}

/// <summary>Convenience accessors for the reader identity established above.</summary>
public static class DevicePrincipalExtensions
{
    public static int? GetReaderId(this ClaimsPrincipal principal) =>
        int.TryParse(principal.FindFirstValue("reader_id"), out var id) ? id : null;

    public static string? GetDeviceId(this ClaimsPrincipal principal) =>
        principal.FindFirstValue(CampusClaims.DeviceId);
}
