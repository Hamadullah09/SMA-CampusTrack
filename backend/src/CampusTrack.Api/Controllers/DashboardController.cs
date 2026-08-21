using CampusTrack.Application.Authorization;
using CampusTrack.Domain.Common;
using CampusTrack.Infrastructure.Dashboards;
using CampusTrack.Infrastructure.Identity;
using CampusTrack.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CampusTrack.Api.Controllers;

/// <summary>
/// Landing data for each portal. Each endpoint is scoped to the caller: a teacher can only
/// fetch their own dashboard, and a guardian only theirs, regardless of the id supplied.
/// </summary>
[Route("api/v1/dashboard")]
public class DashboardController : ApiControllerBase
{
    private readonly IDashboardService _dashboards;
    private readonly CampusTrackDbContext _db;

    public DashboardController(IDashboardService dashboards, CampusTrackDbContext db)
    {
        _dashboards = dashboards;
        _db = db;
    }

    [HttpGet("admin")]
    [HasPermission(Permissions.Dashboard.ViewAdmin)]
    public async Task<ActionResult<AdminDashboard>> Admin(CancellationToken ct)
        => Ok(await _dashboards.GetAdminAsync(ct));

    /// <summary>
    /// The signed-in teacher's dashboard. An admin may pass a teacherId to view another's;
    /// a teacher cannot.
    /// </summary>
    [HttpGet("teacher")]
    [HasPermission(Permissions.Dashboard.ViewTeacher)]
    public async Task<ActionResult<TeacherDashboard>> Teacher([FromQuery] int? teacherId, CancellationToken ct)
    {
        var target = ResolveTargetId(teacherId, CurrentUser.TeacherId, Permissions.Teachers.View,
            "You are not registered as a teacher.");

        return Ok(await _dashboards.GetTeacherAsync(target, ct));
    }

    [HttpGet("student")]
    [HasPermission(Permissions.Dashboard.ViewStudent)]
    public async Task<ActionResult<StudentDashboard>> Student([FromQuery] int? studentId, CancellationToken ct)
    {
        // A guardian may open a dashboard for a child they are approved to follow.
        if (studentId is { } requested && CurrentUser.GuardianId is { } guardianId
                                       && CurrentUser.StudentId != requested)
        {
            var allowed = await _db.GuardianStudents.AsNoTracking().AnyAsync(
                gs => gs.GuardianId == guardianId && gs.StudentId == requested
                      && gs.IsApproved && !gs.IsDeleted, ct);

            if (!allowed && !CurrentUser.HasPermission(Permissions.Students.View))
                throw DomainException.NotAllowed("You do not have access to this student.");

            return Ok(await _dashboards.GetStudentAsync(requested, ct));
        }

        var target = ResolveTargetId(studentId, CurrentUser.StudentId, Permissions.Students.View,
            "You are not registered as a student.");

        return Ok(await _dashboards.GetStudentAsync(target, ct));
    }

    [HttpGet("parent")]
    [HasPermission(Permissions.Dashboard.ViewGuardian)]
    public async Task<ActionResult<GuardianDashboard>> Parent([FromQuery] int? guardianId, CancellationToken ct)
    {
        var target = ResolveTargetId(guardianId, CurrentUser.GuardianId, Permissions.Guardians.View,
            "You are not registered as a guardian.");

        return Ok(await _dashboards.GetGuardianAsync(target, ct));
    }

    /// <summary>
    /// Decides which record to serve. Falls back to the caller's own profile, and only honours
    /// an explicit id when the caller holds the permission to view other people's records.
    /// </summary>
    private int ResolveTargetId(int? requested, int? own, string permissionForOthers, string missingProfileMessage)
    {
        if (requested is { } id && id != own)
        {
            if (!CurrentUser.HasPermission(permissionForOthers))
                throw DomainException.NotAllowed("You can only view your own dashboard.");
            return id;
        }

        return own ?? requested ?? throw DomainException.Invalid(missingProfileMessage);
    }
}
