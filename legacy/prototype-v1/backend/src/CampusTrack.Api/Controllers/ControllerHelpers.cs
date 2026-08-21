using System.Security.Claims;
using CampusTrack.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace CampusTrack.Api.Controllers;

public static class ControllerHelpers
{
    public static int UserId(this ClaimsPrincipal user) =>
        int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);

    public static Task<int?> ParentIdAsync(this AppDbContext db, ClaimsPrincipal user) =>
        db.Parents.Where(p => p.UserId == user.UserId()).Select(p => (int?)p.Id).FirstOrDefaultAsync();

    public static Task<int?> StudentIdAsync(this AppDbContext db, ClaimsPrincipal user) =>
        db.Students.Where(s => s.UserId == user.UserId()).Select(s => (int?)s.Id).FirstOrDefaultAsync();

    public static Task<int?> TeacherIdAsync(this AppDbContext db, ClaimsPrincipal user) =>
        db.Teachers.Where(t => t.UserId == user.UserId()).Select(t => (int?)t.Id).FirstOrDefaultAsync();

    /// Parents may only look at their own children; students at themselves.
    public static async Task<bool> CanAccessStudentAsync(this AppDbContext db,
        ClaimsPrincipal user, int studentId)
    {
        var role = user.FindFirstValue(ClaimTypes.Role);
        if (role is Domain.Roles.Admin or Domain.Roles.Teacher) return true;
        if (role == Domain.Roles.Parent)
        {
            var pid = await db.ParentIdAsync(user);
            return pid is not null &&
                   await db.Students.AnyAsync(s => s.Id == studentId && s.ParentId == pid);
        }
        if (role == Domain.Roles.Student)
            return await db.StudentIdAsync(user) == studentId;
        return false;
    }
}
