namespace CampusTrack.Infrastructure.Identity;

public class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "CampusTrack";
    public string Audience { get; set; } = "CampusTrackClients";

    /// <summary>
    /// Signing key. Must be at least 32 bytes. Never commit a real value: supply it through
    /// user-secrets in development and an environment variable or secret store in production.
    /// Startup refuses to run with the placeholder in a non-development environment.
    /// </summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>Deliberately short. Permission and role changes take effect within one lifetime.</summary>
    public int AccessTokenMinutes { get; set; } = 30;
    public int RefreshTokenDays { get; set; } = 30;

    /// <summary>Tolerance for clock drift between the API and its clients.</summary>
    public int ClockSkewSeconds { get; set; } = 30;

    public const string PlaceholderKey = "REPLACE-WITH-A-LONG-RANDOM-SECRET-AT-LEAST-32-CHARACTERS";
}

public class RfidDeviceOptions
{
    public const string SectionName = "RfidDevices";

    /// <summary>
    /// Shared fallback key accepted when a reader has no per-device key yet. Intended only for
    /// bringing up a new site; per-device keys are the supported production setup and a
    /// warning is logged whenever this path is used.
    /// </summary>
    public string? BootstrapApiKey { get; set; }

    /// <summary>Require every ingest call to carry a device key. Leave true in production.</summary>
    public bool RequireDeviceAuthentication { get; set; } = true;
}
