using CampusTrack.Api.Data;
using CampusTrack.Api.Domain;
using CampusTrack.Api.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CampusTrack.Api.Controllers;

[ApiController]
[Route("api/schedule")]
[Authorize]
public class ScheduleController : ControllerBase
{
    private readonly AppDbContext _db;
    public ScheduleController(AppDbContext db) => _db = db;

    /// Full-semester timetable for a section (parents & students use the
    /// student variant below; teachers/admin can query any section).
    [HttpGet("section/{sectionId:int}")]
    public async Task<IActionResult> ForSection(int sectionId, [FromQuery] int? semesterId)
    {
        semesterId ??= await CurrentSemesterIdAsync();
        var entries = await _db.ScheduleEntries
            .Include(e => e.Teacher!).ThenInclude(t => t.User)
            .Include(e => e.Room).Include(e => e.Semester)
            .Where(e => e.SectionId == sectionId && e.SemesterId == semesterId)
            .OrderBy(e => e.DayOfWeek).ThenBy(e => e.StartTime)
            .Select(e => new
            {
                e.Id, e.DayOfWeek, e.Subject,
                StartTime = e.StartTime.ToString("HH:mm"),
                EndTime = e.EndTime.ToString("HH:mm"),
                Teacher = e.Teacher == null ? null : e.Teacher.User!.FullName,
                Room = e.Room == null ? null : e.Room.Name,
                Semester = e.Semester!.Name
            }).ToListAsync();
        return Ok(entries);
    }

    /// Timetable for a specific student (resolves their section).
    [HttpGet("student/{studentId:int}")]
    public async Task<IActionResult> ForStudent(int studentId, [FromQuery] int? semesterId)
    {
        if (!await _db.CanAccessStudentAsync(User, studentId)) return Forbid();
        var sectionId = await _db.Students.Where(s => s.Id == studentId)
            .Select(s => s.SectionId).FirstOrDefaultAsync();
        if (sectionId is null) return Ok(Array.Empty<object>());
        return await ForSection(sectionId.Value, semesterId);
    }

    [HttpPost]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Teacher}")]
    public async Task<IActionResult> Add(ScheduleEntryRequest req)
    {
        var e = new ScheduleEntry
        {
            SemesterId = req.SemesterId, SectionId = req.SectionId, DayOfWeek = req.DayOfWeek,
            StartTime = req.StartTime, EndTime = req.EndTime, Subject = req.Subject,
            TeacherId = req.TeacherId, RoomId = req.RoomId
        };
        _db.ScheduleEntries.Add(e);
        await _db.SaveChangesAsync();
        return Ok(new { e.Id });
    }

    [HttpDelete("{id:int}")]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Teacher}")]
    public async Task<IActionResult> Delete(int id)
    {
        var e = await _db.ScheduleEntries.FindAsync(id);
        if (e is null) return NotFound();
        _db.ScheduleEntries.Remove(e);
        await _db.SaveChangesAsync();
        return Ok();
    }

    private async Task<int> CurrentSemesterIdAsync() =>
        await _db.Semesters.Where(s => s.IsCurrent).Select(s => s.Id).FirstOrDefaultAsync();
}
