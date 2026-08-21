using CampusTrack.Api.Data;
using CampusTrack.Api.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CampusTrack.Api.Controllers;

/// Convenience endpoints the mobile apps call after login.
[ApiController]
[Route("api/me")]
[Authorize]
public class MeController : ControllerBase
{
    private readonly AppDbContext _db;
    public MeController(AppDbContext db) => _db = db;

    /// Parent app: the children linked to this parent account.
    [HttpGet("children")]
    [Authorize(Roles = Roles.Parent)]
    public async Task<IActionResult> Children()
    {
        var parentId = await _db.ParentIdAsync(User);
        return Ok(await _db.Students
            .Include(s => s.User).Include(s => s.Section!).ThenInclude(x => x.Class)
            .Where(s => s.ParentId == parentId)
            .Select(s => new
            {
                s.Id, s.RegNo, Name = s.User!.FullName,
                Section = s.Section == null ? null : s.Section.Class!.Name + " " + s.Section.Name,
                s.SectionId
            }).ToListAsync());
    }

    /// Student app: own profile incl. section.
    [HttpGet("student")]
    [Authorize(Roles = Roles.Student)]
    public async Task<IActionResult> StudentProfile()
    {
        var studentId = await _db.StudentIdAsync(User);
        var s = await _db.Students
            .Include(x => x.User).Include(x => x.Section!).ThenInclude(x => x.Class)
            .FirstOrDefaultAsync(x => x.Id == studentId);
        if (s is null) return NotFound();
        return Ok(new
        {
            s.Id, s.RegNo, Name = s.User!.FullName, s.SectionId,
            Section = s.Section == null ? null : s.Section.Class!.Name + " " + s.Section.Name
        });
    }

    /// Teacher portal: sections the teacher takes classes for (from timetable).
    [HttpGet("sections")]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Teacher}")]
    public async Task<IActionResult> Sections()
    {
        var teacherId = await _db.TeacherIdAsync(User);
        var query = _db.Sections.Include(s => s.Class).Where(s => s.IsActive);
        if (teacherId is not null && !User.IsInRole(Roles.Admin))
        {
            var sectionIds = await _db.ScheduleEntries.Where(e => e.TeacherId == teacherId)
                .Select(e => e.SectionId).Distinct().ToListAsync();
            if (sectionIds.Count > 0)
                query = query.Where(s => sectionIds.Contains(s.Id));
        }
        return Ok(await query
            .Select(s => new { s.Id, Name = s.Class!.Name + " " + s.Name })
            .ToListAsync());
    }
}
