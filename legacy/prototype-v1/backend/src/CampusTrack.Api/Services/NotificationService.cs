using System.Text;
using System.Text.Json;
using CampusTrack.Api.Data;
using CampusTrack.Api.Domain;

namespace CampusTrack.Api.Services;

/// <summary>
/// Persists every notification to the DB (the apps poll /api/notifications)
/// and, when configured, pushes it via Firebase Cloud Messaging so it pops
/// up on the parent's / student's phone.
/// </summary>
public class NotificationService
{
    private readonly AppDbContext _db;
    private readonly IHttpClientFactory _http;
    private readonly IConfiguration _cfg;
    private readonly ILogger<NotificationService> _log;

    public NotificationService(AppDbContext db, IHttpClientFactory http,
                               IConfiguration cfg, ILogger<NotificationService> log)
    {
        _db = db; _http = http; _cfg = cfg; _log = log;
    }

    public async Task SendAsync(int userId, string type, string title, string body,
                                object? data = null, CancellationToken ct = default)
    {
        var notif = new Notification
        {
            UserId = userId, NotifType = type, Title = title, Body = body,
            DataJson = data is null ? null : JsonSerializer.Serialize(data)
        };
        _db.Notifications.Add(notif);
        await _db.SaveChangesAsync(ct);

        var token = _db.Users.Where(u => u.Id == userId).Select(u => u.FcmToken).FirstOrDefault();
        if (!string.IsNullOrEmpty(token))
            await PushFcmAsync(token, title, body, notif.NotifType, ct);
    }

    /// Sends via FCM legacy HTTP API. Set Fcm:ServerKey in appsettings
    /// (or the FCM__SERVERKEY env var). Silently no-ops when not configured.
    private async Task PushFcmAsync(string deviceToken, string title, string body,
                                    string type, CancellationToken ct)
    {
        var serverKey = _cfg["Fcm:ServerKey"];
        if (string.IsNullOrEmpty(serverKey)) return;

        try
        {
            var client = _http.CreateClient("fcm");
            var payload = new
            {
                to = deviceToken,
                notification = new { title, body },
                data = new { type },
                priority = "high"
            };
            var req = new HttpRequestMessage(HttpMethod.Post, "https://fcm.googleapis.com/fcm/send")
            {
                Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
            };
            req.Headers.TryAddWithoutValidation("Authorization", $"key={serverKey}");
            var resp = await client.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
                _log.LogWarning("FCM push failed: {Status}", resp.StatusCode);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "FCM push error");
        }
    }
}
