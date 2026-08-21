using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CampusTrack.Application.Common.Interfaces;
using CampusTrack.Domain.Enums;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CampusTrack.Infrastructure.Notifications;

public class FirebaseOptions
{
    public const string SectionName = "Firebase";

    public string? ProjectId { get; set; }
    /// <summary>Path to the service-account JSON. Never commit the file itself.</summary>
    public string? ServiceAccountKeyPath { get; set; }
    /// <summary>Alternative to the file, for container deployments that inject secrets as env vars.</summary>
    public string? ServiceAccountJson { get; set; }
}

/// <summary>
/// Sends pushes through Firebase Cloud Messaging HTTP v1.
///
/// v1 rather than the legacy server-key endpoint because the legacy API is retired: v1 needs
/// an OAuth2 access token minted from a service account, which is what most of this class is
/// doing. Tokens are cached until shortly before they expire.
///
/// When Firebase is not configured the sender reports itself unconfigured and no-ops. That is
/// deliberate - the product must be fully usable in development and in schools that have not
/// set up Firebase, with notifications still visible in the in-app inbox.
/// </summary>
public class FirebasePushSender : IPushSender
{
    private const string TokenCacheKey = "fcm:access-token";
    private const string Scope = "https://www.googleapis.com/auth/firebase.messaging";

    private readonly IHttpClientFactory _httpFactory;
    private readonly IMemoryCache _cache;
    private readonly FirebaseOptions _options;
    private readonly ILogger<FirebasePushSender> _logger;
    private readonly ServiceAccount? _account;

    public FirebasePushSender(
        IHttpClientFactory httpFactory,
        IMemoryCache cache,
        IOptions<FirebaseOptions> options,
        ILogger<FirebasePushSender> logger)
    {
        _httpFactory = httpFactory;
        _cache = cache;
        _options = options.Value;
        _logger = logger;
        _account = LoadServiceAccount();

        if (_account is null)
            _logger.LogInformation(
                "Firebase is not configured. Notifications will still be stored and shown in-app, but no pushes will be sent.");
    }

    public bool IsConfigured => _account is not null;

    public async Task<PushResult> SendAsync(PushMessage message, CancellationToken ct = default)
    {
        if (_account is null)
            return new PushResult(false, 0, message.DeviceTokens.Count, [], "Firebase is not configured.");

        if (message.DeviceTokens.Count == 0)
            return new PushResult(true, 0, 0, []);

        string accessToken;
        try
        {
            accessToken = await GetAccessTokenAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not obtain a Firebase access token");
            return new PushResult(false, 0, message.DeviceTokens.Count, [], "Authentication with Firebase failed.");
        }

        var client = _httpFactory.CreateClient("fcm");
        client.DefaultRequestHeaders.Authorization = new("Bearer", accessToken);

        var endpoint = $"https://fcm.googleapis.com/v1/projects/{_account.ProjectId}/messages:send";

        var invalid = new List<string>();
        var success = 0;
        var failure = 0;
        string? lastError = null;
        string? messageId = null;

        // v1 sends one message per token. The batch endpoint was removed along with the legacy
        // API, and a school's fan-out is a handful of guardian devices, not thousands.
        foreach (var token in message.DeviceTokens)
        {
            var payload = BuildPayload(token, message);

            try
            {
                using var response = await client.PostAsJsonAsync(endpoint, payload, ct);

                if (response.IsSuccessStatusCode)
                {
                    success++;
                    if (messageId is null)
                    {
                        var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
                        if (body.TryGetProperty("name", out var name)) messageId = name.GetString();
                    }
                    continue;
                }

                failure++;
                var error = await response.Content.ReadAsStringAsync(ct);
                lastError = error;

                // UNREGISTERED / INVALID_ARGUMENT mean this token will never work again.
                if (response.StatusCode is System.Net.HttpStatusCode.NotFound ||
                    error.Contains("UNREGISTERED", StringComparison.OrdinalIgnoreCase) ||
                    error.Contains("INVALID_ARGUMENT", StringComparison.OrdinalIgnoreCase))
                {
                    invalid.Add(token);
                }
            }
            catch (Exception ex)
            {
                failure++;
                lastError = ex.Message;
                _logger.LogWarning(ex, "Push delivery to one device failed");
            }
        }

        return new PushResult(success > 0, success, failure, invalid, lastError, messageId);
    }

    private static object BuildPayload(string token, PushMessage message)
    {
        var androidPriority = message.Priority >= NotificationPriority.High ? "high" : "normal";

        return new
        {
            message = new
            {
                token,
                notification = new { title = message.Title, body = message.Body },
                data = message.Data ?? new Dictionary<string, string>(),
                android = new
                {
                    priority = androidPriority,
                    notification = new { channel_id = "campustrack_default", sound = "default" }
                },
                apns = new
                {
                    headers = new Dictionary<string, string>
                    {
                        ["apns-priority"] = message.Priority >= NotificationPriority.High ? "10" : "5"
                    },
                    payload = new { aps = new { sound = "default", badge = 1 } }
                }
            }
        };
    }

    /// <summary>
    /// Exchanges a signed JWT assertion for an OAuth2 access token, caching it until just
    /// before expiry so the token endpoint is not called on every notification.
    /// </summary>
    private async Task<string> GetAccessTokenAsync(CancellationToken ct)
    {
        if (_cache.TryGetValue(TokenCacheKey, out string? cached) && !string.IsNullOrEmpty(cached))
            return cached;

        var assertion = CreateSignedAssertion(_account!);
        var client = _httpFactory.CreateClient("fcm-auth");

        using var response = await client.PostAsync(_account!.TokenUri,
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "urn:ietf:params:oauth:grant-type:jwt-bearer",
                ["assertion"] = assertion
            }), ct);

        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        var accessToken = body.GetProperty("access_token").GetString()
                          ?? throw new InvalidOperationException("Firebase returned no access token.");
        var expiresIn = body.TryGetProperty("expires_in", out var exp) ? exp.GetInt32() : 3600;

        // Refresh a minute early so a token never expires mid-flight.
        _cache.Set(TokenCacheKey, accessToken, TimeSpan.FromSeconds(Math.Max(60, expiresIn - 60)));
        return accessToken;
    }

    private static string CreateSignedAssertion(ServiceAccount account)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var header = Base64Url(JsonSerializer.SerializeToUtf8Bytes(new { alg = "RS256", typ = "JWT" }));
        var claims = Base64Url(JsonSerializer.SerializeToUtf8Bytes(new
        {
            iss = account.ClientEmail,
            scope = Scope,
            aud = account.TokenUri,
            exp = now + 3600,
            iat = now
        }));

        var unsigned = $"{header}.{claims}";

        using var rsa = RSA.Create();
        rsa.ImportFromPem(account.PrivateKey);
        var signature = rsa.SignData(Encoding.UTF8.GetBytes(unsigned), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        return $"{unsigned}.{Base64Url(signature)}";
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private ServiceAccount? LoadServiceAccount()
    {
        try
        {
            string? json = null;

            if (!string.IsNullOrWhiteSpace(_options.ServiceAccountJson))
                json = _options.ServiceAccountJson;
            else if (!string.IsNullOrWhiteSpace(_options.ServiceAccountKeyPath) &&
                     File.Exists(_options.ServiceAccountKeyPath))
                json = File.ReadAllText(_options.ServiceAccountKeyPath);

            if (string.IsNullOrWhiteSpace(json)) return null;

            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            var projectId = _options.ProjectId ?? root.GetProperty("project_id").GetString();
            var clientEmail = root.GetProperty("client_email").GetString();
            var privateKey = root.GetProperty("private_key").GetString();
            var tokenUri = root.TryGetProperty("token_uri", out var uri)
                ? uri.GetString()
                : "https://oauth2.googleapis.com/token";

            if (projectId is null || clientEmail is null || privateKey is null || tokenUri is null) return null;

            return new ServiceAccount(projectId, clientEmail, privateKey, tokenUri);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Firebase service account could not be read; push notifications are disabled.");
            return null;
        }
    }

    private record ServiceAccount(string ProjectId, string ClientEmail, string PrivateKey, string TokenUri);
}
