using CampusTrack.Application.Authorization;
using CampusTrack.Application.Common.Interfaces;
using CampusTrack.Application.Common.Models;
using CampusTrack.Domain.Common;
using CampusTrack.Domain.Communication;
using CampusTrack.Domain.Enums;
using CampusTrack.Infrastructure.Identity;
using CampusTrack.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CampusTrack.Api.Controllers;

/// <summary>The signed-in user's notification inbox and delivery preferences.</summary>
public class NotificationsController : ApiControllerBase
{
    private readonly CampusTrackDbContext _db;
    private readonly INotificationService _notifications;
    private readonly IDateTimeProvider _clock;

    public NotificationsController(
        CampusTrackDbContext db, INotificationService notifications, IDateTimeProvider clock)
    {
        _db = db;
        _notifications = notifications;
        _clock = clock;
    }

    [HttpGet]
    public async Task<ActionResult<PagedResult<object>>> GetMine(
        [FromQuery] PagedQuery paging, [FromQuery] bool unreadOnly = false,
        [FromQuery] NotificationCategory? category = null, [FromQuery] int? studentId = null,
        CancellationToken ct = default)
    {
        var userId = RequireUserId();
        var q = _db.Notifications.AsNoTracking().Where(n => n.UserId == userId);

        if (unreadOnly) q = q.Where(n => !n.IsRead);
        if (category is { } cat) q = q.Where(n => n.Category == cat);
        // A parent of several children can filter their inbox to one of them.
        if (studentId is { } sid) q = q.Where(n => n.StudentId == sid);

        var projected = q.OrderByDescending(n => n.CreatedAtUtc).Select(n => (object)new
        {
            n.Id, n.Category, n.Priority, n.Title, n.Body, n.DataJson,
            n.StudentId, studentName = n.Student == null ? null : n.Student.User!.FirstName,
            n.RelatedEntityType, n.RelatedEntityId, n.IsRead, n.ReadAtUtc, n.CreatedAtUtc
        });

        return Paged(await projected.ToPagedResultAsync(paging.Page, paging.PageSize, ct));
    }

    [HttpGet("unread-count")]
    public async Task<ActionResult<object>> UnreadCount(CancellationToken ct)
    {
        var userId = RequireUserId();
        return Ok(new { count = await _db.Notifications.CountAsync(n => n.UserId == userId && !n.IsRead, ct) });
    }

    [HttpPost("{id:long}/read")]
    public async Task<IActionResult> MarkRead(long id, CancellationToken ct)
    {
        var userId = RequireUserId();
        var notification = await _db.Notifications.FirstOrDefaultAsync(n => n.Id == id && n.UserId == userId, ct)
            ?? throw new KeyNotFoundException("That notification does not exist.");

        if (!notification.IsRead)
        {
            notification.IsRead = true;
            notification.ReadAtUtc = _clock.UtcNow;
            await _db.SaveChangesAsync(ct);
        }

        return NoContent();
    }

    [HttpPost("read-all")]
    public async Task<ActionResult<object>> MarkAllRead(CancellationToken ct)
    {
        var userId = RequireUserId();
        var now = _clock.UtcNow;

        // Set-based: a user returning from holiday may have hundreds unread, and loading them
        // all into the change tracker to flip one flag would be wasteful.
        var updated = await _db.Notifications
            .Where(n => n.UserId == userId && !n.IsRead)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.IsRead, true).SetProperty(n => n.ReadAtUtc, now), ct);

        return Ok(new { updated });
    }

    [HttpGet("preferences")]
    public async Task<ActionResult<IReadOnlyList<object>>> GetPreferences(CancellationToken ct)
    {
        var userId = RequireUserId();

        var stored = await _db.NotificationPreferences.AsNoTracking()
            .Where(p => p.UserId == userId)
            .ToDictionaryAsync(p => p.Category, ct);

        // Every category is returned, whether or not a row exists, so the settings screen can
        // render the full list with the defaults already applied.
        return Ok(Enum.GetValues<NotificationCategory>().Select(category =>
        {
            stored.TryGetValue(category, out var pref);
            return (object)new
            {
                category,
                categoryName = category.ToString(),
                inAppEnabled = pref?.InAppEnabled ?? true,
                pushEnabled = pref?.PushEnabled ?? true,
                emailEnabled = pref?.EmailEnabled ?? false,
                quietHoursStart = pref?.QuietHoursStart,
                quietHoursEnd = pref?.QuietHoursEnd
            };
        }).ToList());
    }

    [HttpPut("preferences")]
    public async Task<IActionResult> UpdatePreferences(
        [FromBody] List<PreferenceRequest> requests, CancellationToken ct)
    {
        var userId = RequireUserId();

        foreach (var request in requests)
        {
            var pref = await _db.NotificationPreferences
                .FirstOrDefaultAsync(p => p.UserId == userId && p.Category == request.Category, ct);

            if (pref is null)
            {
                pref = new NotificationPreference { UserId = userId, Category = request.Category };
                _db.NotificationPreferences.Add(pref);
            }

            pref.InAppEnabled = request.InAppEnabled;
            pref.PushEnabled = request.PushEnabled;
            pref.EmailEnabled = request.EmailEnabled;
            pref.QuietHoursStart = request.QuietHoursStart;
            pref.QuietHoursEnd = request.QuietHoursEnd;
        }

        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>Sends an ad-hoc notification. Used for emergencies and targeted messages.</summary>
    [HttpPost("send")]
    [HasPermission(Permissions.Notifications.Send)]
    public async Task<ActionResult<object>> Send(SendNotificationRequest request, CancellationToken ct)
    {
        var recipients = await ResolveRecipientsAsync(request, ct);

        if (recipients.Count == 0)
            throw DomainException.Invalid("That selection matched nobody.");

        await _notifications.NotifyManyAsync(recipients.Select(userId => new NotificationRequest
        {
            UserId = userId,
            Category = request.Category,
            Priority = request.Priority,
            Title = request.Title,
            Body = request.Body
        }), ct);

        return Ok(new { sent = recipients.Count });
    }

    private async Task<List<int>> ResolveRecipientsAsync(SendNotificationRequest request, CancellationToken ct)
    {
        if (request.UserIds is { Count: > 0 }) return request.UserIds;

        var q = _db.Users.AsNoTracking().Where(u => u.IsActive);

        if (request.SectionId is { } sectionId)
        {
            // Section-targeted messages reach the students and their approved guardians -
            // telling one without the other is almost never what the sender means.
            var studentUserIds = await _db.Students.AsNoTracking()
                .Where(s => s.CurrentSectionId == sectionId)
                .Select(s => s.UserId).ToListAsync(ct);

            var guardianUserIds = await _db.GuardianStudents.AsNoTracking()
                .Where(gs => gs.IsApproved && !gs.IsDeleted
                             && gs.Student!.CurrentSectionId == sectionId)
                .Select(gs => gs.Guardian!.UserId).ToListAsync(ct);

            return studentUserIds.Concat(guardianUserIds).Distinct().ToList();
        }

        return request.Audience switch
        {
            AnnouncementAudience.Students => await _db.Students.AsNoTracking().Select(s => s.UserId).ToListAsync(ct),
            AnnouncementAudience.Teachers => await _db.Teachers.AsNoTracking().Select(t => t.UserId).ToListAsync(ct),
            AnnouncementAudience.Guardians => await _db.Guardians.AsNoTracking().Select(g => g.UserId).ToListAsync(ct),
            AnnouncementAudience.Staff => await _db.StaffMembers.AsNoTracking().Select(s => s.UserId).ToListAsync(ct),
            _ => await q.Select(u => u.Id).ToListAsync(ct)
        };
    }
}

/// <summary>School-wide announcements and the events calendar.</summary>
public class AnnouncementsController : ApiControllerBase
{
    private readonly CampusTrackDbContext _db;
    private readonly INotificationService _notifications;
    private readonly IDateTimeProvider _clock;

    public AnnouncementsController(
        CampusTrackDbContext db, INotificationService notifications, IDateTimeProvider clock)
    {
        _db = db;
        _notifications = notifications;
        _clock = clock;
    }

    [HttpGet]
    [HasPermission(Permissions.Announcements.View)]
    public async Task<ActionResult<PagedResult<object>>> Get(
        [FromQuery] PagedQuery paging, [FromQuery] bool includeUnpublished = false, CancellationToken ct = default)
    {
        var now = _clock.UtcNow;
        var q = _db.Announcements.AsNoTracking().AsQueryable();

        // Readers see only what is actually live; editors can see drafts and scheduled posts.
        if (!includeUnpublished || !CurrentUser.HasPermission(Permissions.Announcements.Manage))
        {
            q = q.Where(a => a.IsPublished
                             && (a.PublishAtUtc == null || a.PublishAtUtc <= now)
                             && (a.ExpiresAtUtc == null || a.ExpiresAtUtc >= now));
        }

        var projected = q.OrderByDescending(a => a.PublishAtUtc ?? a.CreatedAtUtc)
            .Select(a => (object)new
            {
                a.Id, a.Title, a.Body, a.Audience, a.Priority, a.IsPublished,
                a.PublishAtUtc, a.ExpiresAtUtc, a.AttachmentPath, a.CreatedAtUtc,
                postedBy = _db.Users.Where(u => u.Id == a.PostedByUserId)
                    .Select(u => u.FirstName + " " + u.LastName).FirstOrDefault(),
                targetSections = a.Targets.Select(t => t.Section!.DisplayName)
            });

        return Paged(await projected.ToPagedResultAsync(paging.Page, paging.PageSize, ct));
    }

    [HttpPost]
    [HasPermission(Permissions.Announcements.Manage)]
    public async Task<ActionResult<object>> Create(AnnouncementRequest request, CancellationToken ct)
    {
        var announcement = new Announcement
        {
            Title = request.Title.Trim(),
            Body = request.Body,
            Audience = request.Audience,
            Priority = request.Priority,
            IsPublished = request.PublishNow,
            PublishAtUtc = request.PublishNow ? _clock.UtcNow : request.PublishAtUtc,
            ExpiresAtUtc = request.ExpiresAtUtc,
            SendAsNotification = request.SendAsNotification,
            PostedByUserId = CurrentUser.UserId
        };

        foreach (var sectionId in request.SectionIds ?? [])
            announcement.Targets.Add(new AnnouncementTarget { SectionId = sectionId });

        _db.Announcements.Add(announcement);
        await _db.SaveChangesAsync(ct);

        if (request.PublishNow && request.SendAsNotification)
            await FanOutAsync(announcement, ct);

        return Created($"/api/v1/announcements/{announcement.Id}", new { announcement.Id, announcement.Title });
    }

    [HttpPut("{id:int}")]
    [HasPermission(Permissions.Announcements.Manage)]
    public async Task<IActionResult> Update(int id, AnnouncementRequest request, CancellationToken ct)
    {
        var announcement = Found(await _db.Announcements
            .Include(a => a.Targets)
            .FirstOrDefaultAsync(a => a.Id == id, ct), "announcement");

        // Whether this edit is the moment the announcement goes live decides whether
        // it fans out: re-saving an already-published notice must not notify twice.
        var wasPublished = announcement.IsPublished;

        announcement.Title = request.Title.Trim();
        announcement.Body = request.Body;
        announcement.Audience = request.Audience;
        announcement.Priority = request.Priority;
        announcement.IsPublished = request.PublishNow || wasPublished;
        announcement.PublishAtUtc = request.PublishNow && !wasPublished
            ? _clock.UtcNow
            : announcement.PublishAtUtc ?? request.PublishAtUtc;
        announcement.ExpiresAtUtc = request.ExpiresAtUtc;
        announcement.SendAsNotification = request.SendAsNotification;

        announcement.Targets.Clear();
        foreach (var sectionId in request.SectionIds ?? [])
            announcement.Targets.Add(new AnnouncementTarget { SectionId = sectionId });

        await _db.SaveChangesAsync(ct);

        if (!wasPublished && announcement.IsPublished && announcement.SendAsNotification)
            await FanOutAsync(announcement, ct);

        return NoContent();
    }

    [HttpDelete("{id:int}")]
    [HasPermission(Permissions.Announcements.Manage)]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var announcement = Found(await _db.Announcements.FirstOrDefaultAsync(a => a.Id == id, ct), "announcement");
        _db.Announcements.Remove(announcement);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    private async Task FanOutAsync(Announcement announcement, CancellationToken ct)
    {
        List<int> userIds;

        if (announcement.Audience == AnnouncementAudience.SpecificSections)
        {
            var sectionIds = announcement.Targets.Select(t => t.SectionId).ToList();

            var students = await _db.Students.AsNoTracking()
                .Where(s => s.CurrentSectionId != null && sectionIds.Contains(s.CurrentSectionId.Value))
                .Select(s => s.UserId).ToListAsync(ct);

            var guardians = await _db.GuardianStudents.AsNoTracking()
                .Where(gs => gs.IsApproved && !gs.IsDeleted
                             && gs.Student!.CurrentSectionId != null
                             && sectionIds.Contains(gs.Student.CurrentSectionId.Value))
                .Select(gs => gs.Guardian!.UserId).ToListAsync(ct);

            userIds = students.Concat(guardians).Distinct().ToList();
        }
        else
        {
            userIds = announcement.Audience switch
            {
                AnnouncementAudience.Students => await _db.Students.AsNoTracking().Select(s => s.UserId).ToListAsync(ct),
                AnnouncementAudience.Teachers => await _db.Teachers.AsNoTracking().Select(t => t.UserId).ToListAsync(ct),
                AnnouncementAudience.Guardians => await _db.Guardians.AsNoTracking().Select(g => g.UserId).ToListAsync(ct),
                AnnouncementAudience.Staff => await _db.StaffMembers.AsNoTracking().Select(s => s.UserId).ToListAsync(ct),
                _ => await _db.Users.AsNoTracking().Where(u => u.IsActive).Select(u => u.Id).ToListAsync(ct)
            };
        }

        await _notifications.NotifyManyAsync(userIds.Select(userId => new NotificationRequest
        {
            UserId = userId,
            Category = announcement.Priority == NotificationPriority.Critical
                ? NotificationCategory.Emergency
                : NotificationCategory.Announcement,
            Priority = announcement.Priority,
            Title = announcement.Title,
            Body = Summarise(announcement.Body),
            RelatedEntityType = nameof(Announcement),
            RelatedEntityId = announcement.Id
        }), ct);
    }

    /// <summary>A push notification has no room for a full announcement body.</summary>
    private static string Summarise(string body)
    {
        var text = System.Text.RegularExpressions.Regex.Replace(body, "<.*?>", " ").Trim();
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ");
        return text.Length <= 160 ? text : text[..157] + "...";
    }
}

public class EventsController : ApiControllerBase
{
    private readonly CampusTrackDbContext _db;

    public EventsController(CampusTrackDbContext db) => _db = db;

    [HttpGet]
    [HasPermission(Permissions.Events.View)]
    public async Task<ActionResult<IReadOnlyList<object>>> Get(
        [FromQuery] DateTime? from, [FromQuery] DateTime? to, CancellationToken ct)
    {
        var start = from ?? DateTime.UtcNow.AddDays(-7);
        var end = to ?? DateTime.UtcNow.AddDays(60);

        return Ok(await _db.SchoolEvents.AsNoTracking()
            .Where(e => e.IsPublished && e.StartAtUtc <= end && e.EndAtUtc >= start)
            .OrderBy(e => e.StartAtUtc)
            .Select(e => (object)new
            {
                e.Id, e.Title, e.Description, e.Location,
                e.StartAtUtc, e.EndAtUtc, e.IsAllDay, e.Audience, e.ColourHex
            })
            .ToListAsync(ct));
    }

    [HttpPost]
    [HasPermission(Permissions.Events.Manage)]
    public async Task<ActionResult<object>> Create(EventRequest request, CancellationToken ct)
    {
        if (request.EndAtUtc < request.StartAtUtc)
            throw DomainException.Invalid("An event cannot end before it starts.");

        var schoolEvent = new SchoolEvent
        {
            Title = request.Title.Trim(),
            Description = request.Description,
            Location = request.Location,
            StartAtUtc = request.StartAtUtc,
            EndAtUtc = request.EndAtUtc,
            IsAllDay = request.IsAllDay,
            Audience = request.Audience,
            ColourHex = request.ColourHex,
            IsPublished = true,
            CreatedByUserIdRef = CurrentUser.UserId
        };

        _db.SchoolEvents.Add(schoolEvent);
        await _db.SaveChangesAsync(ct);

        return Created($"/api/v1/events/{schoolEvent.Id}", new { schoolEvent.Id, schoolEvent.Title });
    }

    [HttpPut("{id:int}")]
    [HasPermission(Permissions.Events.Manage)]
    public async Task<IActionResult> Update(int id, EventRequest request, CancellationToken ct)
    {
        if (request.EndAtUtc < request.StartAtUtc)
            throw DomainException.Invalid("An event cannot end before it starts.");

        var schoolEvent = Found(await _db.SchoolEvents.FirstOrDefaultAsync(e => e.Id == id, ct), "event");

        schoolEvent.Title = request.Title.Trim();
        schoolEvent.Description = request.Description;
        schoolEvent.Location = request.Location;
        schoolEvent.StartAtUtc = request.StartAtUtc;
        schoolEvent.EndAtUtc = request.EndAtUtc;
        schoolEvent.IsAllDay = request.IsAllDay;
        schoolEvent.Audience = request.Audience;
        schoolEvent.ColourHex = request.ColourHex;

        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    [HasPermission(Permissions.Events.Manage)]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var schoolEvent = Found(await _db.SchoolEvents.FirstOrDefaultAsync(e => e.Id == id, ct), "event");
        _db.SchoolEvents.Remove(schoolEvent);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }
}

/// <summary>Leave requests, raised by guardians or staff and reviewed by the office.</summary>
public class LeaveController : ApiControllerBase
{
    private readonly CampusTrackDbContext _db;
    private readonly INotificationService _notifications;
    private readonly IDateTimeProvider _clock;

    public LeaveController(CampusTrackDbContext db, INotificationService notifications, IDateTimeProvider clock)
    {
        _db = db;
        _notifications = notifications;
        _clock = clock;
    }

    [HttpGet]
    [HasPermission(Permissions.Leave.ViewOwn)]
    public async Task<ActionResult<PagedResult<object>>> Get(
        [FromQuery] PagedQuery paging, [FromQuery] LeaveStatus? status, CancellationToken ct)
    {
        var q = _db.LeaveRequests.AsNoTracking().AsQueryable();

        // Without approval rights, a caller sees only what they filed or what concerns their
        // own children.
        if (!CurrentUser.HasPermission(Permissions.Leave.Approve))
        {
            var userId = RequireUserId();
            var childIds = CurrentUser.GuardianId is { } guardianId
                ? await _db.GuardianStudents.AsNoTracking()
                    .Where(gs => gs.GuardianId == guardianId && gs.IsApproved && !gs.IsDeleted)
                    .Select(gs => gs.StudentId).ToListAsync(ct)
                : [];

            q = q.Where(l => l.RequestedByUserId == userId
                             || (l.StudentId != null && childIds.Contains(l.StudentId.Value))
                             || (CurrentUser.StudentId != null && l.StudentId == CurrentUser.StudentId)
                             || (CurrentUser.TeacherId != null && l.TeacherId == CurrentUser.TeacherId));
        }

        if (status is { } s) q = q.Where(l => l.Status == s);

        var projected = q.OrderByDescending(l => l.CreatedAtUtc).Select(l => (object)new
        {
            l.Id, l.RequesterType, l.StudentId,
            studentName = l.Student == null ? null : l.Student.User!.FirstName + " " + l.Student.User.LastName,
            l.TeacherId,
            teacherName = l.Teacher == null ? null : l.Teacher.User!.FirstName + " " + l.Teacher.User.LastName,
            l.StartDate, l.EndDate, l.Reason, l.Status, l.ReviewNotes, l.ReviewedAtUtc, l.CreatedAtUtc,
            totalDays = l.EndDate.DayNumber - l.StartDate.DayNumber + 1
        });

        return Paged(await projected.ToPagedResultAsync(paging.Page, paging.PageSize, ct));
    }

    [HttpPost]
    [HasPermission(Permissions.Leave.Request)]
    public async Task<ActionResult<object>> Create(LeaveRequestDto request, CancellationToken ct)
    {
        if (request.EndDate < request.StartDate)
            throw DomainException.Invalid("The end date cannot be before the start date.");

        // A guardian may only file leave for a child they are approved for.
        if (request.StudentId is { } studentId && CurrentUser.GuardianId is { } guardianId)
        {
            var allowed = await _db.GuardianStudents.AnyAsync(
                gs => gs.GuardianId == guardianId && gs.StudentId == studentId
                      && gs.IsApproved && !gs.IsDeleted, ct);

            if (!allowed) throw DomainException.NotAllowed("You are not authorised to request leave for that student.");
        }

        var leave = new LeaveRequest
        {
            RequesterType = request.StudentId is not null ? LeaveRequesterType.Student : LeaveRequesterType.Teacher,
            StudentId = request.StudentId ?? CurrentUser.StudentId,
            TeacherId = request.StudentId is null ? CurrentUser.TeacherId : null,
            RequestedByUserId = RequireUserId(),
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            Reason = request.Reason.Trim(),
            Status = LeaveStatus.Pending
        };

        _db.LeaveRequests.Add(leave);
        await _db.SaveChangesAsync(ct);

        return Created($"/api/v1/leave/{leave.Id}", new { leave.Id, leave.Status });
    }

    /// <summary>
    /// Withdraws a leave request. Staff with the approve permission may remove any
    /// request; everyone else may only withdraw their own, and only while it is
    /// still pending -- an approved absence is part of the attendance record.
    /// </summary>
    [HttpDelete("{id:int}")]
    [HasPermission(Permissions.Leave.Request)]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var leave = Found(await _db.LeaveRequests.FirstOrDefaultAsync(l => l.Id == id, ct), "leave request");

        var canManageAny = CurrentUser.HasPermission(Permissions.Leave.Approve);

        if (!canManageAny)
        {
            if (leave.RequestedByUserId != CurrentUser.UserId)
                throw DomainException.NotAllowed("You can only withdraw your own leave requests.");

            if (leave.Status != LeaveStatus.Pending)
                throw DomainException.Invalid("Only a pending leave request can be withdrawn.");
        }

        _db.LeaveRequests.Remove(leave);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPost("{id:int}/review")]
    [HasPermission(Permissions.Leave.Approve)]
    public async Task<IActionResult> Review(int id, ReviewLeaveRequest request, CancellationToken ct)
    {
        var leave = Found(await _db.LeaveRequests.FirstOrDefaultAsync(l => l.Id == id, ct), "request");

        leave.Status = request.Approved ? LeaveStatus.Approved : LeaveStatus.Rejected;
        leave.ReviewedByUserId = CurrentUser.UserId;
        leave.ReviewedAtUtc = _clock.UtcNow;
        leave.ReviewNotes = request.Notes;

        await _db.SaveChangesAsync(ct);

        // Approved leave is what stops the attendance engine reporting these days as truancy;
        // the finalisation job reads these rows.
        await _notifications.NotifyAsync(new NotificationRequest
        {
            UserId = leave.RequestedByUserId,
            Category = NotificationCategory.LeaveRequest,
            Title = request.Approved ? "Leave approved" : "Leave not approved",
            Body = request.Approved
                ? $"Leave from {leave.StartDate:d} to {leave.EndDate:d} has been approved."
                : $"Leave from {leave.StartDate:d} to {leave.EndDate:d} was not approved."
                  + (string.IsNullOrWhiteSpace(request.Notes) ? "" : $" {request.Notes}"),
            StudentId = leave.StudentId,
            RelatedEntityType = nameof(LeaveRequest),
            RelatedEntityId = leave.Id
        }, ct);

        return NoContent();
    }
}

// -------------------------------------------------------------------- requests ----

public record PreferenceRequest
{
    public NotificationCategory Category { get; init; }
    public bool InAppEnabled { get; init; } = true;
    public bool PushEnabled { get; init; } = true;
    public bool EmailEnabled { get; init; }
    public TimeOnly? QuietHoursStart { get; init; }
    public TimeOnly? QuietHoursEnd { get; init; }
}

public record SendNotificationRequest
{
    public required string Title { get; init; }
    public required string Body { get; init; }
    public NotificationCategory Category { get; init; } = NotificationCategory.System;
    public NotificationPriority Priority { get; init; } = NotificationPriority.Normal;
    public AnnouncementAudience Audience { get; init; } = AnnouncementAudience.Everyone;
    public int? SectionId { get; init; }
    public List<int>? UserIds { get; init; }
}

public record AnnouncementRequest
{
    public required string Title { get; init; }
    public required string Body { get; init; }
    public AnnouncementAudience Audience { get; init; } = AnnouncementAudience.Everyone;
    public NotificationPriority Priority { get; init; } = NotificationPriority.Normal;
    public bool PublishNow { get; init; } = true;
    public DateTime? PublishAtUtc { get; init; }
    public DateTime? ExpiresAtUtc { get; init; }
    public bool SendAsNotification { get; init; } = true;
    public List<int>? SectionIds { get; init; }
}

public record EventRequest
{
    public required string Title { get; init; }
    public string? Description { get; init; }
    public string? Location { get; init; }
    public DateTime StartAtUtc { get; init; }
    public DateTime EndAtUtc { get; init; }
    public bool IsAllDay { get; init; }
    public AnnouncementAudience Audience { get; init; } = AnnouncementAudience.Everyone;
    public string? ColourHex { get; init; }
}

public record LeaveRequestDto
{
    public int? StudentId { get; init; }
    public DateOnly StartDate { get; init; }
    public DateOnly EndDate { get; init; }
    public required string Reason { get; init; }
}

public record ReviewLeaveRequest
{
    public bool Approved { get; init; }
    public string? Notes { get; init; }
}
