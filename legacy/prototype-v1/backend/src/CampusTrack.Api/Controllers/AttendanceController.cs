using CampusTrack.Api.Data;
using CampusTrack.Api.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CampusTrack.Api.Controllers;

[ApiController]
[Route("api/attendance")]
[Authorize]
public class AttendanceController : ControllerBase
{
    private readonly AppDbContext _db;
    public AttendanceController(AppDbContext db) => _db = db;

    /// Movement timeline (gate + all rooms) for a student, newest first.
    [HttpGet("student/{studentId:int}")]
    public async Task<IActionResult> ForStudent(int studentId,
        [FromQuery] DateOnly? from, [FromQuery] DateOnly? to)
    {
        if (!await _db.CanAccessStudentAsync(User, studentId)) return Forbid();

        var fromUtc = (from ?? DateOnly.FromDateTime(DateTime.Today.AddDays(-7)))
            .ToDateTime(TimeOnly.MinValue).ToUniversalTime();
        var toUtc = (to ?? DateOnly.FromDateTime(DateTime.Today)).AddDays(1)
            .ToDateTime(TimeOnly.MinValue).ToUniversalTime();

        var events = await _db.AttendanceEvents.Include(a => a.Room)
            .Where(a => a.StudentId == studentId && a.EventTime >= fromUtc && a.EventTime < toUtc)
            .OrderByDescending(a => a.EventTime)
            .Select(a => new
            {
                a.Id, Room = a.Room!.Name, RoomType = a.Room.RoomType.ToString(),
                Direction = a.Direction.ToString(), a.EventTime, a.Source
            }).ToListAsync();
        return Ok(events);
    }

    /// Day-by-day gate summary: present / arrival / departure per day.
    [HttpGet("student/{studentId:int}/daily")]
    public async Task<IActionResult> DailyGateSummary(int studentId,
        [FromQuery] DateOnly? from, [FromQuery] DateOnly? to)
    {
        if (!await _db.CanAccessStudentAsync(User, studentId)) return Forbid();

        var fromUtc = (from ?? DateOnly.FromDateTime(DateTime.Today.AddDays(-30)))
            .ToDateTime(TimeOnly.MinValue).ToUniversalTime();
        var toUtc = (to ?? DateOnly.FromDateTime(DateTime.Today)).AddDays(1)
            .ToDateTime(TimeOnly.MinValue).ToUniversalTime();

        var gateEvents = await _db.AttendanceEvents.Include(a => a.Room)
            .Where(a => a.StudentId == studentId && a.EventTime >= fromUtc && a.EventTime < toUtc
                        && a.Room!.RoomType == RoomType.Gate)
            .OrderBy(a => a.EventTime).ToListAsync();

        var days = gateEvents
            .GroupBy(e => DateOnly.FromDateTime(e.EventTime.ToLocalTime()))
            .Select(g => new
            {
                Date = g.Key,
                Arrival = g.Where(e => e.Direction == Direction.Entry)
                           .Select(e => (DateTime?)e.EventTime).FirstOrDefault(),
                Departure = g.Where(e => e.Direction == Direction.Exit)
                             .Select(e => (DateTime?)e.EventTime).LastOrDefault(),
                Present = g.Any(e => e.Direction == Direction.Entry)
            })
            .OrderByDescending(d => d.Date).ToList();
        return Ok(days);
    }

    /// Who is currently inside a given room (last event = Entry).
    [HttpGet("room/{roomId:int}/current")]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Teacher}")]
    public async Task<IActionResult> CurrentInRoom(int roomId)
    {
        var todayUtc = DateTime.UtcNow.Date;
        var latest = await _db.AttendanceEvents
            .Include(a => a.Student!).ThenInclude(s => s.User)
            .Where(a => a.RoomId == roomId && a.EventTime >= todayUtc)
            .GroupBy(a => a.StudentId)
            .Select(g => g.OrderByDescending(a => a.EventTime).First())
            .ToListAsync();

        return Ok(latest.Where(a => a.Direction == Direction.Entry)
            .Select(a => new { a.StudentId, Name = a.Student!.User!.FullName, Since = a.EventTime }));
    }
}
