using CampusTrack.Api.Services;

namespace CampusTrack.Api.HostedServices;

/// <summary>
/// Fires the daily summary at Summaries:DailyTime (local, default 18:00)
/// and the weekly summary on Summaries:WeeklyDay (default Friday) at the
/// same time. Checks once a minute.
/// </summary>
public class SummaryScheduler : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly IConfiguration _cfg;
    private readonly ILogger<SummaryScheduler> _log;
    private DateOnly _lastDailySent = DateOnly.MinValue;
    private DateOnly _lastWeeklySent = DateOnly.MinValue;

    public SummaryScheduler(IServiceScopeFactory scopes, IConfiguration cfg, ILogger<SummaryScheduler> log)
    {
        _scopes = scopes; _cfg = cfg; _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var dailyTime = TimeOnly.TryParse(_cfg["Summaries:DailyTime"], out var t) ? t : new TimeOnly(18, 0);
        var weeklyDay = Enum.TryParse<DayOfWeek>(_cfg["Summaries:WeeklyDay"], out var d) ? d : DayOfWeek.Friday;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var now = DateTime.Now;
                var today = DateOnly.FromDateTime(now);

                if (TimeOnly.FromDateTime(now) >= dailyTime && _lastDailySent < today)
                {
                    _lastDailySent = today;
                    using var scope = _scopes.CreateScope();
                    var summaries = scope.ServiceProvider.GetRequiredService<SummaryService>();
                    await summaries.SendDailySummariesAsync(today, ct);
                    _log.LogInformation("Daily summaries sent for {Day}", today);

                    if (now.DayOfWeek == weeklyDay && _lastWeeklySent < today)
                    {
                        _lastWeeklySent = today;
                        await summaries.SendWeeklySummariesAsync(today, ct);
                        _log.LogInformation("Weekly summaries sent for week ending {Day}", today);
                    }
                }
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Summary scheduler failed");
            }
            await Task.Delay(TimeSpan.FromMinutes(1), ct);
        }
    }
}
