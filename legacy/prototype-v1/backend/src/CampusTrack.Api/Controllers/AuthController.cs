using System.Security.Claims;
using CampusTrack.Api.Data;
using CampusTrack.Api.Domain;
using CampusTrack.Api.Dtos;
using CampusTrack.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CampusTrack.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly JwtService _jwt;

    public AuthController(AppDbContext db, JwtService jwt) { _db = db; _jwt = jwt; }

    [HttpPost("login")]
    public async Task<ActionResult<LoginResponse>> Login(LoginRequest req)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Username == req.Username && u.IsActive);
        if (user is null) return Unauthorized(new { message = "Invalid username or password" });

        var hasher = new PasswordHasher<User>();
        if (hasher.VerifyHashedPassword(user, user.PasswordHash, req.Password)
            == PasswordVerificationResult.Failed)
            return Unauthorized(new { message = "Invalid username or password" });

        var studentId = await _db.Students.Where(s => s.UserId == user.Id).Select(s => (int?)s.Id).FirstOrDefaultAsync();
        var parentId = await _db.Parents.Where(p => p.UserId == user.Id).Select(p => (int?)p.Id).FirstOrDefaultAsync();
        var teacherId = await _db.Teachers.Where(t => t.UserId == user.Id).Select(t => (int?)t.Id).FirstOrDefaultAsync();

        return new LoginResponse(_jwt.CreateToken(user), user.Role, user.FullName, user.Id,
                                 studentId, parentId, teacherId);
    }

    /// Mobile apps register their Firebase device token here after login.
    [Authorize]
    [HttpPost("fcm-token")]
    public async Task<IActionResult> SaveFcmToken(FcmTokenRequest req)
    {
        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var user = await _db.Users.FindAsync(userId);
        if (user is null) return NotFound();
        user.FcmToken = req.Token;
        await _db.SaveChangesAsync();
        return Ok();
    }
}
