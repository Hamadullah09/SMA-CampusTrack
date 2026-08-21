using CampusTrack.Api.Data;
using CampusTrack.Api.Domain;
using CampusTrack.Api.Dtos;
using CampusTrack.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CampusTrack.Api.Controllers;

/// Students upload projects / activities / theses; teachers review them.
[ApiController]
[Route("api/uploads")]
[Authorize]
public class UploadsController : ControllerBase
{
    private static readonly string[] AllowedTypes = { "Project", "Activity", "Thesis" };
    private const long MaxFileBytes = 50 * 1024 * 1024;

    private readonly AppDbContext _db;
    private readonly FileStorageService _files;

    public UploadsController(AppDbContext db, FileStorageService files)
    {
        _db = db; _files = files;
    }

    [HttpPost]
    [Authorize(Roles = Roles.Student)]
    [RequestSizeLimit(MaxFileBytes)]
    public async Task<IActionResult> Upload([FromForm] string uploadType, [FromForm] string title,
        [FromForm] string? description, IFormFile file)
    {
        var studentId = await _db.StudentIdAsync(User);
        if (studentId is null) return Forbid();
        if (!AllowedTypes.Contains(uploadType))
            return BadRequest(new { message = "uploadType must be Project, Activity or Thesis" });
        if (file.Length == 0 || file.Length > MaxFileBytes)
            return BadRequest(new { message = "File missing or larger than 50 MB" });

        var path = await _files.SaveAsync(file, $"student-uploads/{studentId}");
        var upload = new StudentUpload
        {
            StudentId = studentId.Value, UploadType = uploadType, Title = title,
            Description = description, FilePath = path, OriginalName = file.FileName
        };
        _db.StudentUploads.Add(upload);
        await _db.SaveChangesAsync();
        return Ok(new { upload.Id });
    }

    [HttpGet("student/{studentId:int}")]
    public async Task<IActionResult> ForStudent(int studentId)
    {
        if (!await _db.CanAccessStudentAsync(User, studentId)) return Forbid();
        return Ok(await _db.StudentUploads
            .Where(u => u.StudentId == studentId)
            .OrderByDescending(u => u.UploadedAt)
            .Select(u => new
            {
                u.Id, u.UploadType, u.Title, u.Description, u.OriginalName,
                u.UploadedAt, u.Status, u.TeacherRemarks
            }).ToListAsync());
    }

    /// Teacher: pending uploads to review.
    [HttpGet("pending")]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Teacher}")]
    public async Task<IActionResult> Pending() =>
        Ok(await _db.StudentUploads
            .Include(u => u.Student!).ThenInclude(s => s.User)
            .Where(u => u.Status == "Submitted")
            .OrderBy(u => u.UploadedAt)
            .Select(u => new
            {
                u.Id, u.UploadType, u.Title, u.OriginalName, u.UploadedAt,
                Student = u.Student!.User!.FullName
            }).ToListAsync());

    [HttpPost("{id:int}/review")]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Teacher}")]
    public async Task<IActionResult> Review(int id, UploadReviewRequest req)
    {
        var upload = await _db.StudentUploads.FindAsync(id);
        if (upload is null) return NotFound();
        upload.Status = req.Status;
        upload.TeacherRemarks = req.TeacherRemarks;
        await _db.SaveChangesAsync();
        return Ok();
    }

    [HttpGet("{id:int}/file")]
    public async Task<IActionResult> Download(int id)
    {
        var upload = await _db.StudentUploads.FindAsync(id);
        if (upload is null) return NotFound();
        if (!await _db.CanAccessStudentAsync(User, upload.StudentId)) return Forbid();
        var full = _files.GetFullPath(upload.FilePath);
        if (!System.IO.File.Exists(full)) return NotFound();
        return PhysicalFile(full, "application/octet-stream", upload.OriginalName);
    }
}
