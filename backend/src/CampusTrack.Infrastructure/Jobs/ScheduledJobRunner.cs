using CampusTrack.Application.Common;
using CampusTrack.Application.Common.Interfaces;
using CampusTrack.Domain.Enums;
using CampusTrack.Infrastructure.Attendance;
using CampusTrack.Infrastructure.Persistence;
using CampusTrack.Infrastructure.Reporting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CampusTrack.Infrastructure.Rfid;

/// <summary>
/// Runs the school's daily rhythm: finalise absences mid-morning, send guardians their
/// end-of-day report, close presence intervals at closing time, and prune old raw reads
/// overnight.
///
/// It ticks once a minute and asks "is it time yet" rather than sleeping until a target time,
/// because a process that sleeps for eight hours misses its window entirely if the server is
/// restarted or the clock shifts for daylight saving. Each job records the date it last ran
/// for, so a restart at 18:05 still sends the 18:00 report exactly once.
/// </summary>
public class ScheduledJobRunner : BackgroundService
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromMinutes(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ScheduledJobRunner> _logger;

    private DateOnly? _absencesFinalisedFor;
    private DateOnly? _reportsSentFor;
    private DateOnly? _presencesClosedFor;
    private DateOnly? _cleanupRanFor;

    public ScheduledJobRunner(IServiceScopeFactory scopeFactory, ILogger<ScheduledJobRunner> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Scheduled job runner started");

        try { await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await TickAsync(stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                // One failing job must not stop the schedule for the rest of the term.
                _logger.LogError(ex, "Scheduled job tick failed");
            }

            try { await Task.Delay(TickInterval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task TickAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var provider = scope.ServiceProvider;

        var clock = provider.GetRequiredService<IDateTimeProvider>();
        var settings = provider.GetRequiredService<ISettingsProvider>();

        var today = clock.SchoolToday;
        var now = TimeOnly.FromDateTime(clock.SchoolNow.DateTime);

        // ---- finalise absences ------------------------------------------------------
        var absenceTime = await settings.GetAsync(SettingKeys.AbsenceFinalisationTime, new TimeOnly(11, 0), ct);
        if (_absencesFinalisedFor != today && now >= absenceTime)
        {
            _absencesFinalisedFor = today;
            var attendance = provider.GetRequiredService<IAttendanceEngine>();
            var created = await attendance.FinaliseAbsencesAsync(today, ct);

            if (created > 0 && await settings.GetAsync(SettingKeys.NotifyOnAbsence, true, ct))
                await NotifyAbsencesAsync(provider, today, ct);
        }

        // ---- close open presences ---------------------------------------------------
        var dayEnd = await settings.GetAsync(SettingKeys.SchoolDayEnd, new TimeOnly(14, 30), ct);
        var closeAt = dayEnd.AddHours(2);   // leave room for after-school activities
        if (_presencesClosedFor != today && now >= closeAt &&
            await settings.GetAsync(SettingKeys.AutoCloseOpenPresences, true, ct))
        {
            _presencesClosedFor = today;
            var attendance = provider.GetRequiredService<IAttendanceEngine>();
            await attendance.CloseOpenPresencesAsync(today, ct);
        }

        // ---- daily guardian report ----------------------------------------------------
        if (await settings.GetAsync(SettingKeys.DailyReportEnabled, true, ct))
        {
            var reportTime = await settings.GetAsync(SettingKeys.DailyReportTime, new TimeOnly(18, 0), ct);
            if (_reportsSentFor != today && now >= reportTime)
            {
                _reportsSentFor = today;
                var reports = provider.GetRequiredService<IDailyReportService>();
                var sent = await reports.GenerateAndSendAsync(today, ct);
                _logger.LogInformation("Daily reports generated and sent for {Date}: {Count}", today, sent);
            }
        }

        // ---- overnight cleanup ----------------------------------------------------------
        if (_cleanupRanFor != today && now >= new TimeOnly(2, 30))
        {
            _cleanupRanFor = today;
            await CleanupAsync(provider, settings, clock, ct);
        }
    }

    /// <summary>Tells guardians their child did not arrive, once absences are final.</summary>
    private async Task NotifyAbsencesAsync(IServiceProvider provider, DateOnly date, CancellationToken ct)
    {
        var db = provider.GetRequiredService<CampusTrackDbContext>();
        var notifications = provider.GetRequiredService<INotificationService>();

        var absentees = await db.DailyAttendances
            .Where(a => a.Date == date && a.Status == AttendanceStatus.Absent)
            .Select(a => new
            {
                a.StudentId,
                FirstName = a.Student!.User!.FirstName,
                FullName = a.Student.User.FirstName + " " + a.Student.User.LastName
            })
            .ToListAsync(ct);

        foreach (var student in absentees)
        {
            await notifications.NotifyGuardiansOfStudentAsync(student.StudentId, new NotificationRequest
            {
                Category = NotificationCategory.Absence,
                Priority = NotificationPriority.High,
                Title = $"{student.FirstName} was marked absent",
                Body = $"{student.FullName} has not been recorded at school today ({date:d}). " +
                       "Please contact the school office if this is unexpected.",
                StudentId = student.StudentId
            }, ct);
        }

        if (absentees.Count > 0)
            _logger.LogInformation("Notified guardians of {Count} absence(s) for {Date}", absentees.Count, date);
    }

    /// <summary>
    /// Prunes raw antenna reads past their retention window. Resolved movement events and
    /// attendance are never touched - only the high-volume per-hit rows whose purpose (replay
    /// and dispute resolution) has a limited shelf life.
    /// </summary>
    private async Task CleanupAsync(
        IServiceProvider provider, ISettingsProvider settings, IDateTimeProvider clock, CancellationToken ct)
    {
        var db = provider.GetRequiredService<CampusTrackDbContext>();

        var retentionDays = await settings.GetAsync(SettingKeys.RfidRetainRawReadsDays, 90, ct);
        var cutoff = clock.UtcNow.AddDays(-retentionDays);

        // Deleted in batches so a long-running statement does not hold locks on the busiest
        // table in the database for minutes at a time.
        //
        // Raw SQL rather than ExecuteDeleteAsync with Take(): EF renders that as a DELETE with
        // an IN (subquery with LIMIT), which MySQL rejects outright. A plain DELETE ... LIMIT
        // is both supported and the more efficient statement here.
        var totalDeleted = 0;
        const int batchSize = 5000;

        while (!ct.IsCancellationRequested)
        {
            var deleted = await db.Database.ExecuteSqlRawAsync(
                "DELETE FROM rfid_raw_reads WHERE ReadAtUtc < {0} ORDER BY Id LIMIT {1}",
                [cutoff, batchSize], ct);

            totalDeleted += deleted;
            if (deleted < batchSize) break;

            await Task.Delay(TimeSpan.FromMilliseconds(200), ct);
        }

        if (totalDeleted > 0)
            _logger.LogInformation("Pruned {Count} raw RFID read(s) older than {Days} day(s)", totalDeleted, retentionDays);

        var auditRetention = await settings.GetAsync(SettingKeys.AuditRetentionDays, 730, ct);
        var auditCutoff = clock.UtcNow.AddDays(-auditRetention);
        var auditDeleted = await db.SystemLogs.Where(l => l.OccurredAtUtc < auditCutoff).ExecuteDeleteAsync(ct);

        if (auditDeleted > 0)
            _logger.LogInformation("Pruned {Count} system log entr(ies)", auditDeleted);
    }
}
