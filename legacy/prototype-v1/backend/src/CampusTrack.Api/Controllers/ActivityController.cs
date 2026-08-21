using CampusTrack.Api.Data;
using CampusTrack.Api.Domain;
using CampusTrack.Api.Dtos;
using CampusTrack.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CampusTrack.Api.Controllers;

/// Teacher-entered progress / activity reports; parents & students read them.
[ApiController]
[Route("api/activity")]
[Authorize]
public class ActivityController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly NotificationService _notifications;

    public ActivityController(AppDbContext db, NotificationService notifications)
    {
        _db = db; _notifications = notifications;
    }

    [HttpPost]
    [Authorize(Roles = Roles.Teacher)]
    public async Task<IActionResult> Create(ActivityReportRequest req)
    {
        var teacherId = await _db.TeacherIdAsync(User);
        if (teacherId is null) return Forbid();

        var report = new ActivityReport
        {
            StudentId = req.StudentId, TeacherId = teacherId.Value,
            ReportDate = req.ReportDate, Category = req.Category,
            Title = req.Title, Remarks = req.Remarks, Grade = req.Grade
        };
        _db.ActivityReports.Add(report);
        await _db.SaveChangesAsync();

        // notify the parent right away
        var student = await _db.Students.Include(s => s.User).Include(s => s.Parent)
            .FirstOrDefaultAsync(s => s.Id == req.StudentId);
        if (student?.Parent is not null)
            await _notifications.SendAsync(student.Parent.UserId, "Activity",
                $"New {req.Category} update for {student.User?.FullName}",
                $"{req.Title}" + (req.Grade is null ? "" : $" – {req.Grade}") +
                (req.Remarks is null ? "" : $"\n{req.Remarks}"),
                new { reportId = report.Id, studentId = student.Id });

        return Ok(new { report.Id });
    }

    [HttpGet("student/{studentId:int}")]
    public async Task<IActionResult> ForStudent(int studentId)
    {
        if (!await _db.CanAccessStudentAsync(User, studentId)) return Forbid();
        return Ok(await _db.ActivityReports
            .Include(r => r.Teacher!).ThenInclude(t => t.User)
            .Where(r => r.StudentId == studentId)
            .OrderByDescending(r => r.ReportDate).ThenByDescending(r => r.CreatedAt)
            .Select(r => new
            {
                r.Id, r.ReportDate, r.Category, r.Title, r.Remarks, r.Grade,
                Teacher = r.Teacher!.User!.FullName
            }).ToListAsync());
    }

    /// Teacher view: all students of a section, for entering reports.
    [HttpGet("section/{sectionId:int}/students")]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Teacher}")]
    public async Task<IActionResult> SectionStudents(int sectionId) =>
        Ok(await _db.Students.Include(s => s.User)
            .Where(s => s.SectionId == sectionId)
            .Select(s => new { s.Id, s.RegNo, Name = s.User!.FullName })
            .ToListAsync());
}
