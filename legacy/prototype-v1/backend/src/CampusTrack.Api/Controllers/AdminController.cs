using CampusTrack.Api.Data;
using CampusTrack.Api.Domain;
using CampusTrack.Api.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CampusTrack.Api.Controllers;

/// <summary>
/// Admin management: classes & sections (fully modular - add/remove any
/// time), rooms, RFID readers, semesters, and user provisioning.
/// </summary>
[ApiController]
[Route("api/admin")]
[Authorize(Roles = Roles.Admin)]
public class AdminController : ControllerBase
{
    private readonly AppDbContext _db;
    public AdminController(AppDbContext db) => _db = db;

    // ---- classes & sections (modular) ------------------------------
    [HttpGet("classes")]
    public async Task<IActionResult> GetClasses() =>
        Ok(await _db.SchoolClasses.Include(c => c.Sections)
            .Select(c => new { c.Id, c.Name, c.IsActive,
                               Sections = c.Sections.Select(s => new { s.Id, s.Name, s.IsActive }) })
            .ToListAsync());

    [HttpPost("classes")]
    public async Task<IActionResult> AddClass(NameRequest req)
    {
        var c = new SchoolClass { Name = req.Name };
        _db.SchoolClasses.Add(c);
        await _db.SaveChangesAsync();
        return Ok(new { c.Id, c.Name });
    }

    [HttpDelete("classes/{id:int}")]
    public async Task<IActionResult> DeleteClass(int id)
    {
        var c = await _db.SchoolClasses.Include(x => x.Sections).FirstOrDefaultAsync(x => x.Id == id);
        if (c is null) return NotFound();
        // soft-delete keeps history intact
        c.IsActive = false;
        foreach (var s in c.Sections) s.IsActive = false;
        await _db.SaveChangesAsync();
        return Ok();
    }

    [HttpPost("sections")]
    public async Task<IActionResult> AddSection(SectionRequest req)
    {
        var s = new Section { ClassId = req.ClassId, Name = req.Name };
        _db.Sections.Add(s);
        await _db.SaveChangesAsync();
        return Ok(new { s.Id, s.Name });
    }

    [HttpDelete("sections/{id:int}")]
    public async Task<IActionResult> DeleteSection(int id)
    {
        var s = await _db.Sections.FindAsync(id);
        if (s is null) return NotFound();
        s.IsActive = false;
        await _db.SaveChangesAsync();
        return Ok();
    }

    // ---- rooms & readers -------------------------------------------
    [HttpGet("rooms")]
    public async Task<IActionResult> GetRooms() =>
        Ok(await _db.Rooms.Where(r => r.IsActive)
            .Select(r => new { r.Id, r.Name, RoomType = r.RoomType.ToString() }).ToListAsync());

    [HttpPost("rooms")]
    public async Task<IActionResult> AddRoom(RoomRequest req)
    {
        if (!Enum.TryParse<RoomType>(req.RoomType, out var type))
            return BadRequest(new { message = "Invalid room type" });
        var room = new Room { Name = req.Name, RoomType = type };
        _db.Rooms.Add(room);
        await _db.SaveChangesAsync();
        return Ok(new { room.Id });
    }

    [HttpDelete("rooms/{id:int}")]
    public async Task<IActionResult> DeleteRoom(int id)
    {
        var room = await _db.Rooms.FindAsync(id);
        if (room is null) return NotFound();
        room.IsActive = false;
        await _db.SaveChangesAsync();
        return Ok();
    }

    [HttpGet("readers")]
    public async Task<IActionResult> GetReaders() =>
        Ok(await _db.RfidReaders.Include(r => r.Room).Where(r => r.IsActive)
            .Select(r => new { r.Id, r.ReaderCode, r.AntennaCount, Room = r.Room!.Name })
            .ToListAsync());

    [HttpPost("readers")]
    public async Task<IActionResult> AddReader(ReaderRequest req)
    {
        var reader = new RfidReader
            { ReaderCode = req.ReaderCode, RoomId = req.RoomId, AntennaCount = req.AntennaCount };
        _db.RfidReaders.Add(reader);
        await _db.SaveChangesAsync();
        return Ok(new { reader.Id });
    }

    // ---- semesters -------------------------------------------------
    [HttpGet("semesters")]
    public async Task<IActionResult> GetSemesters() => Ok(await _db.Semesters.ToListAsync());

    [HttpPost("semesters")]
    public async Task<IActionResult> AddSemester(SemesterRequest req)
    {
        if (req.IsCurrent)
            await _db.Semesters.ExecuteUpdateAsync(s => s.SetProperty(x => x.IsCurrent, false));
        var sem = new Semester { Name = req.Name, StartDate = req.StartDate, EndDate = req.EndDate, IsCurrent = req.IsCurrent };
        _db.Semesters.Add(sem);
        await _db.SaveChangesAsync();
        return Ok(new { sem.Id });
    }

    // ---- user provisioning -----------------------------------------
    [HttpPost("parents")]
    public async Task<IActionResult> AddParent(CreateParentRequest req)
    {
        var user = await CreateUserAsync(req.User, Roles.Parent);
        var parent = new Parent { UserId = user.Id };
        _db.Parents.Add(parent);
        await _db.SaveChangesAsync();
        return Ok(new { parent.Id, user.Username });
    }

    [HttpPost("teachers")]
    public async Task<IActionResult> AddTeacher(CreateTeacherRequest req)
    {
        var user = await CreateUserAsync(req.User, Roles.Teacher);
        var teacher = new Teacher { UserId = user.Id, Subject = req.Subject };
        _db.Teachers.Add(teacher);
        await _db.SaveChangesAsync();
        return Ok(new { teacher.Id, user.Username });
    }

    [HttpPost("students")]
    public async Task<IActionResult> AddStudent(CreateStudentRequest req)
    {
        var user = await CreateUserAsync(req.User, Roles.Student);
        var student = new Student
        {
            UserId = user.Id, RegNo = req.RegNo, RfidEpc = req.RfidEpc,
            SectionId = req.SectionId, ParentId = req.ParentId
        };
        _db.Students.Add(student);
        await _db.SaveChangesAsync();
        return Ok(new { student.Id, user.Username });
    }

    [HttpGet("students")]
    public async Task<IActionResult> GetStudents() =>
        Ok(await _db.Students.Include(s => s.User).Include(s => s.Section!).ThenInclude(x => x.Class)
            .Select(s => new
            {
                s.Id, s.RegNo, s.RfidEpc, Name = s.User!.FullName, s.ParentId,
                Section = s.Section == null ? null : s.Section.Class!.Name + " " + s.Section.Name
            }).ToListAsync());

    [HttpGet("parents")]
    public async Task<IActionResult> GetParents() =>
        Ok(await _db.Parents.Include(p => p.User)
            .Select(p => new { p.Id, Name = p.User!.FullName, p.User.Phone }).ToListAsync());

    [HttpGet("teachers")]
    public async Task<IActionResult> GetTeachers() =>
        Ok(await _db.Teachers.Include(t => t.User)
            .Select(t => new { t.Id, Name = t.User!.FullName, t.Subject }).ToListAsync());

    /// Assign / replace the RFID card EPC on a student's ID card.
    [HttpPut("students/{id:int}/rfid")]
    public async Task<IActionResult> SetStudentEpc(int id, NameRequest req)
    {
        var s = await _db.Students.FindAsync(id);
        if (s is null) return NotFound();
        s.RfidEpc = req.Name;
        await _db.SaveChangesAsync();
        return Ok();
    }

    private async Task<User> CreateUserAsync(CreateUserRequest req, string role)
    {
        var hasher = new PasswordHasher<User>();
        var user = new User
        {
            Username = req.Username, Role = role, FullName = req.FullName,
            Email = req.Email, Phone = req.Phone
        };
        user.PasswordHash = hasher.HashPassword(user, req.Password);
        _db.Users.Add(user);
        await _db.SaveChangesAsync();
        return user;
    }
}
