using CampusTrack.Application.Authorization;
using CampusTrack.Application.Common.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace CampusTrack.Infrastructure.Realtime;

/// <summary>
/// The live channel behind the dashboards.
///
/// Clients are placed into groups on connect rather than being allowed to subscribe to
/// whatever they ask for. That matters: without server-side grouping, a parent could join the
/// monitoring group and watch every child in the school move around the building.
/// </summary>
[Authorize]
public class CampusHub : Hub
{
    public const string MonitoringGroup = "rfid-monitor";
    public const string DashboardGroup = "admin-dashboard";

    private readonly ICurrentUser _currentUser;
    private readonly ILogger<CampusHub> _logger;

    public CampusHub(ICurrentUser currentUser, ILogger<CampusHub> logger)
    {
        _currentUser = currentUser;
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        var userId = _currentUser.UserId;
        if (userId is null)
        {
            Context.Abort();
            return;
        }

        // Personal group: how a user receives their own notifications.
        await Groups.AddToGroupAsync(Context.ConnectionId, UserGroup(userId.Value));

        // Operational feeds are permission-gated, evaluated here once per connection.
        if (_currentUser.HasPermission(Permissions.Rfid.Monitor) ||
            _currentUser.IsInRole(Permissions.RoleNames.SuperAdmin))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, MonitoringGroup);
        }

        if (_currentUser.HasPermission(Permissions.Dashboard.ViewAdmin) ||
            _currentUser.IsInRole(Permissions.RoleNames.SuperAdmin))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, DashboardGroup);
        }

        _logger.LogDebug("User {UserId} connected to the live hub ({ConnectionId})", userId, Context.ConnectionId);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (exception is not null)
            _logger.LogDebug(exception, "Hub connection {ConnectionId} dropped", Context.ConnectionId);

        await base.OnDisconnectedAsync(exception);
    }

    public static string UserGroup(int userId) => $"user-{userId}";
}

/// <summary>
/// Publishes hub messages from services that must not know about SignalR.
///
/// Every method swallows its exceptions on purpose: a dropped dashboard update is a cosmetic
/// problem, and it must never roll back the RFID event or attendance write that triggered it.
/// </summary>
public class SignalRPublisher : IRealtimePublisher
{
    private readonly IHubContext<CampusHub> _hub;
    private readonly ILogger<SignalRPublisher> _logger;

    public SignalRPublisher(IHubContext<CampusHub> hub, ILogger<SignalRPublisher> logger)
    {
        _hub = hub;
        _logger = logger;
    }

    public Task PublishRfidEventAsync(object payload, CancellationToken ct = default) =>
        SendAsync(CampusHub.MonitoringGroup, "rfidEvent", payload, ct);

    public Task PublishReaderStatusAsync(object payload, CancellationToken ct = default) =>
        SendAsync(CampusHub.MonitoringGroup, "readerStatus", payload, ct);

    public Task PublishAttendanceUpdateAsync(object payload, CancellationToken ct = default) =>
        SendAsync(CampusHub.DashboardGroup, "attendanceUpdate", payload, ct);

    public Task PublishDashboardCountersAsync(object payload, CancellationToken ct = default) =>
        SendAsync(CampusHub.DashboardGroup, "dashboardCounters", payload, ct);

    public Task PublishToUserAsync(int userId, string eventName, object payload, CancellationToken ct = default) =>
        SendAsync(CampusHub.UserGroup(userId), eventName, payload, ct);

    private async Task SendAsync(string group, string eventName, object payload, CancellationToken ct)
    {
        try
        {
            await _hub.Clients.Group(group).SendAsync(eventName, payload, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Live update '{Event}' to group '{Group}' could not be delivered", eventName, group);
        }
    }
}
