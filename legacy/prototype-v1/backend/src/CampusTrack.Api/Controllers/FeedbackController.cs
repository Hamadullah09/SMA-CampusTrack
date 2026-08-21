using CampusTrack.Api.Data;
using CampusTrack.Api.Domain;
using CampusTrack.Api.Dtos;
using CampusTrack.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CampusTrack.Api.Controllers;

/// Parent feedback in fixed categories; teachers/admin read and reply.
[ApiController]
[Route("api/feedback")]
[Authorize]
public class FeedbackController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly NotificationService _notifications;

    public FeedbackController(AppDbContext db, NotificationService notifications)
    {
        _db = db; _notifications = notifications;
    }

    /// The fixed feedback columns shown in the parent app.
    [HttpGet("categories")]
    [AllowAnonymous]
    public IActionResult Categories() => Ok(FeedbackCategories.All);

    [HttpPost]
    [Authorize(Roles = Roles.Parent)]
    public async Task<IActionResult> Submit(FeedbackRequest req)
    {
        var parentId = await _db.ParentIdAsync(User);
        if (parentId is null) return Forbid();
        if (!FeedbackCategories.All.Contains(req.Category))
            return BadRequest(new { message = "Invalid category" });
        if (!await _db.Students.AnyAsync(s => s.Id == req.StudentId && s.ParentId == parentId))
            return Forbid();

        var fb = new ParentFeedback
        {
            ParentId = parentId.Value, StudentId = req.StudentId,
            Category = req.Category, Message = req.Message
        };
        _db.ParentFeedback.Add(fb);
        await _db.SaveChangesAsync();
        return Ok(new { fb.Id });
    }

    /// Parent: own feedback history (with replies).
    [HttpGet("mine")]
    [Authorize(Roles = Roles.Parent)]
    public async Task<IActionResult> Mine()
    {
        var parentId = await _db.ParentIdAsync(User);
        return Ok(await _db.ParentFeedback
            .Include(f => f.Student!).ThenInclude(s => s.User)
            .Where(f => f.ParentId == parentId)
            .OrderByDescending(f => f.CreatedAt)
            .Select(f => new
            {
                f.Id, f.Category, f.Message, f.CreatedAt, f.Status, f.Reply, f.RepliedAt,
                Student = f.Student!.User!.FullName
            }).ToListAsync());
    }

    /// Teacher/admin: browse all feedback.
    [HttpGet]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Teacher}")]
    public async Task<IActionResult> All([FromQuery] string? status) =>
        Ok(await _db.ParentFeedback
            .Include(f => f.Student!).ThenInclude(s => s.User)
            .Include(f => f.Parent!).ThenInclude(p => p.User)
            .Where(f => status == null || f.Status == status)
            .OrderByDescending(f => f.CreatedAt)
            .Select(f => new
            {
                f.Id, f.Category, f.Message, f.CreatedAt, f.Status, f.Reply,
                Student = f.Student!.User!.FullName,
                Parent = f.Parent!.User!.FullName
            }).ToListAsync());

    [HttpPost("{id:int}/reply")]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Teacher}")]
    public async Task<IActionResult> Reply(int id, FeedbackReplyRequest req)
    {
        var fb = await _db.ParentFeedback.Include(f => f.Parent).FirstOrDefaultAsync(f => f.Id == id);
        if (fb is null) return NotFound();
        fb.Reply = req.Reply;
        fb.Status = req.Status ?? "Replied";
        fb.RepliedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        await _notifications.SendAsync(fb.Parent!.UserId, "FeedbackReply",
            $"Reply to your {fb.Category} feedback", req.Reply, new { feedbackId = fb.Id });
        return Ok();
    }
}
