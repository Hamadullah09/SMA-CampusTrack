using CampusTrack.Application.Common;
using CampusTrack.Application.Common.Interfaces;
using CampusTrack.Domain.Enums;
using CampusTrack.Domain.Rfid;
using CampusTrack.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CampusTrack.Infrastructure.Rfid;

/// <summary>
/// Watches for readers that have gone quiet.
///
/// A dead reader is worse than a noisy one: it fails silently, and the first sign is usually
/// a parent asking why they were never told their child arrived. This sweep turns that
/// silence into an explicit Offline status, a device log entry and a live dashboard update,
/// so the school finds out from the system rather than from a complaint.
/// </summary>
public class ReaderHealthMonitor : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(30);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ReaderHealthMonitor> _logger;

    public ReaderHealthMonitor(IServiceScopeFactory scopeFactory, ILogger<ReaderHealthMonitor> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Give readers a chance to check in before the first sweep, so a server restart does
        // not immediately declare every device offline.
        try { await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckReadersAsync(stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Reader health check failed");
            }

            try { await Task.Delay(CheckInterval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task CheckReadersAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CampusTrackDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();
        var settings = scope.ServiceProvider.GetRequiredService<ISettingsProvider>();
        var realtime = scope.ServiceProvider.GetRequiredService<IRealtimePublisher>();

        var missedAllowance = await settings.GetAsync(SettingKeys.RfidOfflineAfterMissedHeartbeats, 3, ct);
        var now = clock.UtcNow;

        var readers = await db.RfidReaders
            .Where(r => r.IsActive)
            .ToListAsync(ct);

        var changed = new List<RfidReader>();

        foreach (var reader in readers)
        {
            var allowanceSeconds = Math.Max(reader.HeartbeatIntervalSeconds * missedAllowance, 90);

            // Traffic counts as proof of life: a busy gate reader may not send a separate
            // heartbeat while it is streaming reads.
            var lastContact = Max(reader.LastHeartbeatUtc, reader.LastEventUtc);
            var isSilent = lastContact is null || (now - lastContact.Value).TotalSeconds > allowanceSeconds;

            var newStatus = isSilent
                ? (reader.Status == ReaderStatus.Maintenance ? ReaderStatus.Maintenance : ReaderStatus.Offline)
                : ReaderStatus.Online;

            if (newStatus == reader.Status) continue;

            var previous = reader.Status;
            reader.Status = newStatus;
            changed.Add(reader);

            if (newStatus == ReaderStatus.Offline)
            {
                var silentFor = lastContact is null
                    ? "since the server started"
                    : $"for {(int)(now - lastContact.Value).TotalMinutes} minute(s)";

                reader.LastErrorMessage = $"No contact {silentFor}.";
                reader.LastErrorAtUtc = now;

                db.DeviceLogs.Add(new DeviceLog
                {
                    SchoolId = reader.SchoolId,
                    ReaderId = reader.Id,
                    DeviceId = reader.DeviceId,
                    Level = DeviceLogLevel.Error,
                    EventName = "Offline",
                    Message = $"Reader '{reader.Name}' stopped responding {silentFor}.",
                    OccurredAtUtc = now
                });

                _logger.LogWarning("Reader {DeviceId} ({Name}) is offline - no contact {SilentFor}",
                    reader.DeviceId, reader.Name, silentFor);
            }
            else if (previous is ReaderStatus.Offline or ReaderStatus.Error)
            {
                reader.LastErrorMessage = null;

                db.DeviceLogs.Add(new DeviceLog
                {
                    SchoolId = reader.SchoolId,
                    ReaderId = reader.Id,
                    DeviceId = reader.DeviceId,
                    Level = DeviceLogLevel.Info,
                    EventName = "Reconnected",
                    Message = $"Reader '{reader.Name}' is responding again.",
                    OccurredAtUtc = now
                });

                _logger.LogInformation("Reader {DeviceId} ({Name}) reconnected", reader.DeviceId, reader.Name);
            }
        }

        if (changed.Count == 0) return;

        await db.SaveChangesAsync(ct);

        foreach (var reader in changed)
        {
            await realtime.PublishReaderStatusAsync(new
            {
                id = reader.Id,
                deviceId = reader.DeviceId,
                name = reader.Name,
                status = reader.Status.ToString(),
                lastHeartbeatUtc = reader.LastHeartbeatUtc,
                lastErrorMessage = reader.LastErrorMessage
            }, ct);
        }
    }

    private static DateTime? Max(DateTime? a, DateTime? b) =>
        a is null ? b : b is null ? a : (a > b ? a : b);
}
