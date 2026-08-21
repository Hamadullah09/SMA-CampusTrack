using CampusTrack.Application.Authorization;
using CampusTrack.Application.Identity;
using CampusTrack.Domain.Identity;
using CampusTrack.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace CampusTrack.Infrastructure.Identity;

public interface IUserProfileBuilder
{
    Task<UserProfile> BuildAsync(ApplicationUser user, CancellationToken ct = default);
}

/// <summary>
/// Assembles a user's roles, effective permissions and profile ids.
///
/// Shared by the token service and the profile endpoint so the two can never disagree about
/// what a user may do - a mismatch there would show a menu the API then refuses to serve.
/// </summary>
public class UserProfileBuilder : IUserProfileBuilder
{
    private readonly CampusTrackDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;

    public UserProfileBuilder(CampusTrackDbContext db, UserManager<ApplicationUser> userManager)
    {
        _db = db;
        _userManager = userManager;
    }

    public async Task<UserProfile> BuildAsync(ApplicationUser user, CancellationToken ct = default)
    {
        var roles = await _userManager.GetRolesAsync(user);
        var permissions = await ResolvePermissionsAsync(user.Id, roles, ct);

        var ids = await _db.Users
            .Where(u => u.Id == user.Id)
            .Select(u => new
            {
                StudentId = _db.Students.Where(s => s.UserId == u.Id).Select(s => (int?)s.Id).FirstOrDefault(),
                TeacherId = _db.Teachers.Where(t => t.UserId == u.Id).Select(t => (int?)t.Id).FirstOrDefault(),
                GuardianId = _db.Guardians.Where(g => g.UserId == u.Id).Select(g => (int?)g.Id).FirstOrDefault(),
                StaffId = _db.StaffMembers.Where(s => s.UserId == u.Id).Select(s => (int?)s.Id).FirstOrDefault(),
                SchoolName = _db.Schools.Where(s => s.Id == u.SchoolId).Select(s => s.Name).FirstOrDefault()
            })
            .FirstOrDefaultAsync(ct);

        return new UserProfile
        {
            Id = user.Id,
            UserName = user.UserName ?? string.Empty,
            FullName = user.FullName,
            Email = user.Email,
            PhoneNumber = user.PhoneNumber,
            ProfileImageUrl = user.ProfileImagePath,
            Roles = roles.ToList(),
            Permissions = permissions,
            StudentId = ids?.StudentId,
            TeacherId = ids?.TeacherId,
            GuardianId = ids?.GuardianId,
            StaffMemberId = ids?.StaffId,
            PrimaryPortal = DeterminePortal(roles, ids?.StudentId, ids?.TeacherId, ids?.GuardianId),
            MustChangePassword = user.MustChangePassword,
            TimeZoneId = user.TimeZoneId,
            PreferredLanguage = user.PreferredLanguage,
            SchoolId = user.SchoolId,
            SchoolName = ids?.SchoolName
        };
    }

    /// <summary>
    /// Effective permissions = role grants + per-user grants - per-user denials.
    /// A denial always wins, so a single capability can be withdrawn from one person without
    /// inventing a bespoke role for them.
    /// </summary>
    private async Task<List<string>> ResolvePermissionsAsync(int userId, IList<string> roles, CancellationToken ct)
    {
        // SuperAdmin holds everything, including permissions added by a future release.
        if (roles.Contains(Permissions.RoleNames.SuperAdmin))
            return Permissions.All.Select(p => p.Name).ToList();

        var roleIds = await _db.Roles
            .Where(r => r.Name != null && roles.Contains(r.Name))
            .Select(r => r.Id)
            .ToListAsync(ct);

        var fromRoles = await _db.RolePermissions
            .Where(rp => roleIds.Contains(rp.RoleId))
            .Select(rp => rp.Permission!.Name)
            .ToListAsync(ct);

        var overrides = await _db.UserPermissions
            .Where(up => up.UserId == userId)
            .Select(up => new { up.Permission!.Name, up.IsGranted })
            .ToListAsync(ct);

        var effective = new HashSet<string>(fromRoles, StringComparer.OrdinalIgnoreCase);
        foreach (var o in overrides.Where(x => x.IsGranted)) effective.Add(o.Name);
        foreach (var o in overrides.Where(x => !x.IsGranted)) effective.Remove(o.Name);

        return effective.ToList();
    }

    /// <summary>
    /// Which portal opens first. Ordered by privilege so a teacher who is also a parent lands
    /// in the teacher portal and switches to the parent view deliberately.
    /// </summary>
    private static string DeterminePortal(IList<string> roles, int? studentId, int? teacherId, int? guardianId)
    {
        if (roles.Contains(Permissions.RoleNames.SuperAdmin) || roles.Contains(Permissions.RoleNames.Admin))
            return "admin";
        if (roles.Contains(Permissions.RoleNames.Teacher) || teacherId is not null) return "teacher";
        if (roles.Contains(Permissions.RoleNames.Guardian) || guardianId is not null) return "parent";
        if (roles.Contains(Permissions.RoleNames.Student) || studentId is not null) return "student";
        if (roles.Contains(Permissions.RoleNames.Staff)) return "staff";
        return "student";
    }
}
