using CampusTrack.Api.Data;
using CampusTrack.Api.Domain;
using CampusTrack.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QRCoder;

namespace CampusTrack.Api.Controllers;

/// <summary>
/// Assignments & notes uploaded by teachers. Every document gets a QR
/// token; the QR image encodes the public download URL so students can
/// scan it (printed on a handout or shown on the class screen) and the
/// file downloads straight into the student app.
/// </summary>
[ApiController]
[Route("api/assignments")]
[Authorize]
public class AssignmentsController : ControllerBase
{
    private const long MaxFileBytes = 50 * 1024 * 1024;

    private readonly AppDbContext _db;
    private readonly FileStorageService _files;
    private readonly IConfiguration _cfg;

    public AssignmentsController(AppDbContext db, FileStorageService files, IConfiguration cfg)
    {
        _db = db; _files = files; _cfg = cfg;
    }

    [HttpPost]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Teacher}")]
    [RequestSizeLimit(MaxFileBytes)]
    public async Task<IActionResult> Create([FromForm] int sectionId, [FromForm] string docType,
        [FromForm] string title, [FromForm] string? description,
        [FromForm] DateOnly? dueDate, IFormFile file)
    {
        var teacherId = await _db.TeacherIdAsync(User);
        if (teacherId is null && !User.IsInRole(Roles.Admin)) return Forbid();
        if (docType is not ("Assignment" or "Notes"))
            return BadRequest(new { message = "docType must be Assignment or Notes" });

        var path = await _files.SaveAsync(file, $"assignments/{sectionId}");
        var a = new Assignment
        {
            SectionId = sectionId, TeacherId = teacherId ?? 0, DocType = docType,
            Title = title, Description = description, DueDate = dueDate,
            FilePath = path, OriginalName = file.FileName
        };
        _db.Assignments.Add(a);
        await _db.SaveChangesAsync();
        return Ok(new { a.Id, a.QrToken, qrImageUrl = $"/api/assignments/{a.Id}/qr" });
    }

    /// Assignments/notes list for a student's own section.
    [HttpGet("student/{studentId:int}")]
    public async Task<IActionResult> ForStudent(int studentId)
    {
        if (!await _db.CanAccessStudentAsync(User, studentId)) return Forbid();
        var sectionId = await _db.Students.Where(s => s.Id == studentId)
            .Select(s => s.SectionId).FirstOrDefaultAsync();
        if (sectionId is null) return Ok(Array.Empty<object>());

        return Ok(await _db.Assignments
            .Include(a => a.Teacher!).ThenInclude(t => t.User)
            .Where(a => a.SectionId == sectionId)
            .OrderByDescending(a => a.CreatedAt)
            .Select(a => new
            {
                a.Id, a.DocType, a.Title, a.Description, a.DueDate, a.CreatedAt,
                a.OriginalName, a.QrToken,
                Teacher = a.Teacher == null ? null : a.Teacher.User!.FullName
            }).ToListAsync());
    }

    /// QR PNG for printing / projecting. Encodes the public download URL.
    [HttpGet("{id:int}/qr")]
    [AllowAnonymous]
    public async Task<IActionResult> QrImage(int id)
    {
        var a = await _db.Assignments.FindAsync(id);
        if (a is null) return NotFound();

        var baseUrl = _cfg["PublicBaseUrl"] ?? $"{Request.Scheme}://{Request.Host}";
        var url = $"{baseUrl}/api/assignments/download/{a.QrToken}";

        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(url, QRCodeGenerator.ECCLevel.Q);
        var png = new PngByteQRCode(data).GetGraphic(pixelsPerModule: 10);
        return File(png, "image/png");
    }

    /// Token-based download used by the QR code (no login needed - the
    /// unguessable GUID token is the credential).
    [HttpGet("download/{token:guid}")]
    [AllowAnonymous]
    public async Task<IActionResult> DownloadByToken(Guid token)
    {
        var a = await _db.Assignments.FirstOrDefaultAsync(x => x.QrToken == token);
        if (a is null) return NotFound();
        var full = _files.GetFullPath(a.FilePath);
        if (!System.IO.File.Exists(full)) return NotFound();
        return PhysicalFile(full, "application/octet-stream", a.OriginalName);
    }
}
