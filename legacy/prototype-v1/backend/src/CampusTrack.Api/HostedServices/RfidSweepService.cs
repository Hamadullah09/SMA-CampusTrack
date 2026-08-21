using CampusTrack.Api.Services;

namespace CampusTrack.Api.HostedServices;

/// <summary>
/// Every second, asks the sequence engine for tag sequences that have gone
/// quiet, resolves their direction and records attendance events.
/// </summary>
public class RfidSweepService : BackgroundService
{
    private readonly RfidSequenceEngine _engine;
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<RfidSweepService> _log;

    public RfidSweepService(RfidSequenceEngine engine, IServiceScopeFactory scopes,
                            ILogger<RfidSweepService> log)
    {
        _engine = engine; _scopes = scopes; _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var completed = _engine.SweepCompleted(DateTime.UtcNow);
                if (completed.Count > 0)
                {
                    using var scope = _scopes.CreateScope();
                    var attendance = scope.ServiceProvider.GetRequiredService<AttendanceService>();
                    foreach (var (readerId, epc, direction, eventTime) in completed)
                    {
                        if (direction is null) continue;   // ambiguous pass, discarded
                        await attendance.RecordEventAsync(readerId, epc, direction.Value, eventTime, ct);
                    }
                }
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "RFID sweep failed");
            }
            await Task.Delay(TimeSpan.FromSeconds(1), ct);
        }
    }
}
