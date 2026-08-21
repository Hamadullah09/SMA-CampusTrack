using CampusTrack.Application.Authorization;
using CampusTrack.Application.Common.Interfaces;
using CampusTrack.Application.Common.Models;
using CampusTrack.Domain.Common;
using CampusTrack.Domain.Identity;
using CampusTrack.Infrastructure.Identity;
using CampusTrack.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CampusTrack.Api.Controllers;

/// <summary>Accounts, roles and permission grants.</summary>
public class UsersController : ApiControllerBase
{
    private readonly CampusTrackDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ITokenService _tokens;
    private readonly IDateTimeProvider _clock;

    public UsersController(
        CampusTrackDbContext db,
        UserManager<ApplicationUser> userManager,
        ITokenService tokens,
        IDateTimeProvider clock)
    {
        _db = db;
        _userManager = userManager;
        _tokens = tokens;
        _clock = clock;
    }

    [HttpGet]
    [HasPermission(Permissions.Users.View)]
    public async Task<ActionResult<PagedResult<object>>> Search(
        [FromQuery] PagedQuery paging, [FromQuery] string? role, [FromQuery] bool? isActive, CancellationToken ct)
    {
        var q = _db.Users.AsNoTracking().AsQueryable();

        if (isActive is { } active) q = q.Where(u => u.IsActive == active);

        if (!string.IsNullOrWhiteSpace(role))
        {
            var roleId = await _db.Roles.Where(r => r.Name == role).Select(r => r.Id).FirstOrDefaultAsync(ct);
            if (roleId != 0)
            {
                var userIds = await _db.Set<ApplicationUserRole>()
                    .Where(ur => ur.RoleId == roleId).Select(ur => ur.UserId).ToListAsync(ct);
                q = q.Where(u => userIds.Contains(u.Id));
            }
        }

        if (!string.IsNullOrWhiteSpace(paging.Search))
        {
            var term = paging.Search.Trim();
            q = q.Where(u => u.FirstName.Contains(term) || u.LastName.Contains(term)
                             || u.UserName!.Contains(term)
                             || (u.Email != null && u.Email.Contains(term)));
        }

        var projected = q.OrderBy(u => u.LastName).ThenBy(u => u.FirstName).Select(u => (object)new
        {
            u.Id, u.UserName, u.Email, u.PhoneNumber,
            fullName = u.FirstName + " " + u.LastName,
            u.IsActive, u.MustChangePassword, u.LastLoginAtUtc, u.CreatedAtUtc, u.LockoutEnd,
            // Never expose PasswordHash or SecurityStamp, even to administrators.
            roles = _db.Set<ApplicationUserRole>()
                .Where(ur => ur.UserId == u.Id)
                .Select(ur => ur.Role!.Name)
                .ToList(),
            profileType = _db.Students.Any(s => s.UserId == u.Id) ? "Student"
                : _db.Teachers.Any(t => t.UserId == u.Id) ? "Teacher"
                : _db.Guardians.Any(g => g.UserId == u.Id) ? "Guardian"
                : _db.StaffMembers.Any(s => s.UserId == u.Id) ? "Staff"
                : "Account"
        });

        return Paged(await projected.ToPagedResultAsync(paging.Page, paging.PageSize, ct));
    }

    /// <summary>
    /// Sets a new password on someone else's account. Ends their live sessions, because an
    /// administrator resetting a password usually means the account is suspected compromised.
    /// </summary>
    [HttpPost("{id:int}/reset-password")]
    [HasPermission(Permissions.Users.ResetPassword)]
    public async Task<ActionResult<object>> ResetPassword(int id, AdminResetPasswordDto request, CancellationToken ct)
    {
        var user = await _userManager.FindByIdAsync(id.ToString())
            ?? throw new KeyNotFoundException("That account does not exist.");

        var token = await _userManager.GeneratePasswordResetTokenAsync(user);
        var result = await _userManager.ResetPasswordAsync(user, token, request.NewPassword);

        if (!result.Succeeded)
            throw DomainException.Invalid(string.Join(" ", result.Errors.Select(e => e.Description)));

        user.MustChangePassword = request.RequireChangeOnNextLogin;
        user.PasswordChangedAtUtc = _clock.UtcNow;
        await _userManager.SetLockoutEndDateAsync(user, null);
        await _userManager.ResetAccessFailedCountAsync(user);
        await _db.SaveChangesAsync(ct);

        await _tokens.RevokeAllForUserAsync(id, "Password reset by an administrator", ct);

        return Ok(new { message = "The password has been reset and all sessions ended." });
    }

    [HttpPost("{id:int}/activate")]
    [HasPermission(Permissions.Users.Activate)]
    public async Task<IActionResult> SetActive(int id, [FromQuery] bool active, CancellationToken ct)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id, ct)
            ?? throw new KeyNotFoundException("That account does not exist.");

        // An administrator locking themselves out is a support call nobody wants.
        if (!active && id == CurrentUser.UserId)
            throw DomainException.Invalid("You cannot deactivate your own account.");

        user.IsActive = active;
        await _db.SaveChangesAsync(ct);

        if (!active) await _tokens.RevokeAllForUserAsync(id, "Account deactivated", ct);

        return NoContent();
    }

    [HttpPost("{id:int}/roles")]
    [HasPermission(Permissions.Users.ManageRoles)]
    public async Task<IActionResult> SetRoles(int id, [FromBody] List<string> roles, CancellationToken ct)
    {
        var user = await _userManager.FindByIdAsync(id.ToString())
            ?? throw new KeyNotFoundException("That account does not exist.");

        var current = await _userManager.GetRolesAsync(user);

        // Removing the last SuperAdmin would leave the school unable to administer itself.
        if (current.Contains(Permissions.RoleNames.SuperAdmin) && !roles.Contains(Permissions.RoleNames.SuperAdmin))
        {
            var superAdminCount = await CountUsersInRoleAsync(Permissions.RoleNames.SuperAdmin, ct);
            if (superAdminCount <= 1)
                throw DomainException.Invalid("This is the last super administrator; the role cannot be removed.");
        }

        await _userManager.RemoveFromRolesAsync(user, current.Except(roles));
        await _userManager.AddToRolesAsync(user, roles.Except(current));

        await _tokens.RevokeAllForUserAsync(id, "Roles changed", ct);
        return NoContent();
    }

    /// <summary>Grants or denies a single permission for one user, on top of their roles.</summary>
    [HttpPost("{id:int}/permissions")]
    [HasPermission(Permissions.Users.ManagePermissions)]
    public async Task<IActionResult> SetPermissionOverride(
        int id, [FromBody] List<PermissionOverrideRequest> overrides, CancellationToken ct)
    {
        var existing = await _db.UserPermissions.Where(p => p.UserId == id).ToListAsync(ct);
        _db.UserPermissions.RemoveRange(existing);

        var permissionIds = await _db.Permissions
            .Where(p => overrides.Select(o => o.Permission).Contains(p.Name))
            .ToDictionaryAsync(p => p.Name, p => p.Id, ct);

        foreach (var request in overrides)
        {
            if (!permissionIds.TryGetValue(request.Permission, out var permissionId)) continue;

            _db.UserPermissions.Add(new UserPermission
            {
                UserId = id,
                PermissionId = permissionId,
                IsGranted = request.IsGranted,
                GrantedAtUtc = _clock.UtcNow,
                GrantedByUserId = CurrentUser.UserId
            });
        }

        await _db.SaveChangesAsync(ct);
        await _tokens.RevokeAllForUserAsync(id, "Permissions changed", ct);
        return NoContent();
    }

    private async Task<int> CountUsersInRoleAsync(string roleName, CancellationToken ct)
    {
        var roleId = await _db.Roles.Where(r => r.Name == roleName).Select(r => r.Id).FirstOrDefaultAsync(ct);
        if (roleId == 0) return 0;

        return await _db.Set<ApplicationUserRole>()
            .Where(ur => ur.RoleId == roleId)
            .Join(_db.Users.Where(u => u.IsActive), ur => ur.UserId, u => u.Id, (ur, u) => u.Id)
            .CountAsync(ct);
    }
}

public class RolesController : ApiControllerBase
{
    private readonly CampusTrackDbContext _db;
    private readonly RoleManager<ApplicationRole> _roleManager;
    private readonly IDateTimeProvider _clock;

    public RolesController(CampusTrackDbContext db, RoleManager<ApplicationRole> roleManager, IDateTimeProvider clock)
    {
        _db = db;
        _roleManager = roleManager;
        _clock = clock;
    }

    [HttpGet]
    [HasPermission(Permissions.Roles.View)]
    public async Task<ActionResult<IReadOnlyList<object>>> Get(CancellationToken ct) =>
        Ok(await _db.Roles.AsNoTracking()
            .OrderBy(r => r.Name)
            .Select(r => (object)new
            {
                r.Id, r.Name, r.Description, r.IsSystemRole,
                userCount = _db.Set<ApplicationUserRole>().Count(ur => ur.RoleId == r.Id),
                permissions = r.RolePermissions.Select(rp => rp.Permission!.Name).ToList()
            })
            .ToListAsync(ct));

    /// <summary>The full permission catalogue, grouped for the role editor.</summary>
    [HttpGet("permissions")]
    [HasPermission(Permissions.Roles.View)]
    public ActionResult<IReadOnlyList<object>> GetPermissionCatalogue() =>
        Ok(Permissions.All
            .GroupBy(p => p.Group)
            .Select(g => (object)new
            {
                group = g.Key,
                permissions = g.Select(p => new { p.Name, p.DisplayName })
            })
            .ToList());

    [HttpPost]
    [HasPermission(Permissions.Roles.Manage)]
    public async Task<ActionResult<object>> Create(RoleRequest request, CancellationToken ct)
    {
        if (await _roleManager.RoleExistsAsync(request.Name))
            throw DomainException.Conflict($"A role named '{request.Name}' already exists.");

        var role = new ApplicationRole
        {
            Name = request.Name.Trim(),
            NormalizedName = request.Name.Trim().ToUpperInvariant(),
            Description = request.Description,
            IsSystemRole = false,
            CreatedAtUtc = _clock.UtcNow
        };

        var result = await _roleManager.CreateAsync(role);
        if (!result.Succeeded)
            throw DomainException.Invalid(string.Join(" ", result.Errors.Select(e => e.Description)));

        await SetPermissionsInternalAsync(role.Id, request.Permissions ?? [], ct);
        return Created($"/api/v1/roles/{role.Id}", new { role.Id, role.Name });
    }

    [HttpPut("{id:int}")]
    [HasPermission(Permissions.Roles.Manage)]
    public async Task<IActionResult> Update(int id, RoleRequest request, CancellationToken ct)
    {
        var role = await _db.Roles.FirstOrDefaultAsync(r => r.Id == id, ct)
            ?? throw new KeyNotFoundException("That role does not exist.");

        // Built-in roles are referenced by name throughout the codebase, so renaming one
        // would silently detach every check that names it. Their description is free to edit.
        if (role.IsSystemRole && !string.Equals(role.Name, request.Name.Trim(), StringComparison.Ordinal))
            throw DomainException.Invalid($"'{role.Name}' is a built-in role and cannot be renamed.");

        if (!role.IsSystemRole)
        {
            var name = request.Name.Trim();
            var clash = await _db.Roles.AnyAsync(r => r.Id != id && r.NormalizedName == name.ToUpperInvariant(), ct);
            if (clash) throw DomainException.Conflict($"A role named '{name}' already exists.");

            role.Name = name;
            role.NormalizedName = name.ToUpperInvariant();
        }

        role.Description = request.Description;
        await _db.SaveChangesAsync(ct);

        if (request.Permissions is not null)
            await SetPermissionsInternalAsync(role.Id, request.Permissions, ct);

        return NoContent();
    }

    /// <summary>
    /// Deletes a custom role. Built-in roles stay, and a role still assigned to someone
    /// is refused rather than silently stripping those users of their access.
    /// </summary>
    [HttpDelete("{id:int}")]
    [HasPermission(Permissions.Roles.Manage)]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var role = await _db.Roles.FirstOrDefaultAsync(r => r.Id == id, ct)
            ?? throw new KeyNotFoundException("That role does not exist.");

        if (role.IsSystemRole)
            throw DomainException.Invalid($"'{role.Name}' is a built-in role and cannot be deleted.");

        var assigned = await _db.Set<ApplicationUserRole>().CountAsync(ur => ur.RoleId == id, ct);
        if (assigned > 0)
            throw DomainException.Conflict(
                $"{assigned} user(s) still hold this role. Reassign them before deleting it.");

        // The role's permission grants are pure join rows with no meaning once the role is
        // gone, so they are cleared here rather than blocking the delete on a foreign key.
        var grants = await _db.RolePermissions.Where(rp => rp.RoleId == id).ToListAsync(ct);
        _db.RolePermissions.RemoveRange(grants);

        _db.Roles.Remove(role);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPut("{id:int}/permissions")]
    [HasPermission(Permissions.Roles.Manage)]
    public async Task<IActionResult> SetPermissions(int id, [FromBody] List<string> permissions, CancellationToken ct)
    {
        var role = await _db.Roles.FirstOrDefaultAsync(r => r.Id == id, ct)
            ?? throw new KeyNotFoundException("That role does not exist.");

        // SuperAdmin's authority is enforced in code; editing its grants would create the
        // illusion of restricting it.
        if (role.Name == Permissions.RoleNames.SuperAdmin)
            throw DomainException.Invalid("The super administrator role always holds every permission.");

        await SetPermissionsInternalAsync(id, permissions, ct);
        return NoContent();
    }

    private async Task SetPermissionsInternalAsync(int roleId, List<string> permissions, CancellationToken ct)
    {
        var existing = await _db.RolePermissions.Where(rp => rp.RoleId == roleId).ToListAsync(ct);
        _db.RolePermissions.RemoveRange(existing);

        var ids = await _db.Permissions
            .Where(p => permissions.Contains(p.Name))
            .Select(p => p.Id)
            .ToListAsync(ct);

        foreach (var permissionId in ids)
        {
            _db.RolePermissions.Add(new RolePermission
            {
                RoleId = roleId,
                PermissionId = permissionId,
                GrantedAtUtc = _clock.UtcNow,
                GrantedByUserId = CurrentUser.UserId
            });
        }

        await _db.SaveChangesAsync(ct);
    }
}

/// <summary>Audit trail, system logs and device logs.</summary>
public class AuditController : ApiControllerBase
{
    private readonly CampusTrackDbContext _db;

    public AuditController(CampusTrackDbContext db) => _db = db;

    [HttpGet]
    [HasPermission(Permissions.Audit.View)]
    public async Task<ActionResult<PagedResult<object>>> Get(
        [FromQuery] PagedQuery paging, [FromQuery] string? entityName, [FromQuery] int? userId,
        [FromQuery] DateTime? from, [FromQuery] DateTime? to, CancellationToken ct)
    {
        var q = _db.AuditLogs.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(entityName)) q = q.Where(a => a.EntityName == entityName);
        if (userId is { } uid) q = q.Where(a => a.UserId == uid);
        if (from is { } f) q = q.Where(a => a.OccurredAtUtc >= f);
        if (to is { } t) q = q.Where(a => a.OccurredAtUtc <= t);

        if (!string.IsNullOrWhiteSpace(paging.Search))
        {
            var term = paging.Search.Trim();
            q = q.Where(a => a.EntityName.Contains(term)
                             || (a.UserName != null && a.UserName.Contains(term))
                             || (a.EntityId != null && a.EntityId.Contains(term)));
        }

        var projected = q.OrderByDescending(a => a.OccurredAtUtc).Select(a => (object)new
        {
            a.Id, a.Action, a.EntityName, a.EntityId, a.EntityDisplay,
            a.UserId, a.UserName, a.UserRole,
            a.OldValuesJson, a.NewValuesJson, a.AffectedColumns,
            a.IpAddress, a.CorrelationId, a.OccurredAtUtc
        });

        return Paged(await projected.ToPagedResultAsync(paging.Page, paging.PageSize, ct));
    }

    [HttpGet("system-logs")]
    [HasPermission(Permissions.Audit.ViewSystemLogs)]
    public async Task<ActionResult<PagedResult<object>>> SystemLogs(
        [FromQuery] PagedQuery paging, [FromQuery] string? level, CancellationToken ct)
    {
        var q = _db.SystemLogs.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(level)) q = q.Where(l => l.Level == level);

        var projected = q.OrderByDescending(l => l.OccurredAtUtc).Select(l => (object)new
        {
            l.Id, l.Level, l.Category, l.Message, l.ExceptionType, l.CorrelationId, l.OccurredAtUtc
        });

        return Paged(await projected.ToPagedResultAsync(paging.Page, paging.PageSize, ct));
    }

    [HttpGet("device-logs")]
    [HasPermission(Permissions.Audit.ViewDeviceLogs)]
    public async Task<ActionResult<PagedResult<object>>> DeviceLogs(
        [FromQuery] PagedQuery paging, [FromQuery] int? readerId, CancellationToken ct)
    {
        var q = _db.DeviceLogs.AsNoTracking().AsQueryable();
        if (readerId is { } id) q = q.Where(l => l.ReaderId == id);

        var projected = q.OrderByDescending(l => l.OccurredAtUtc).Select(l => (object)new
        {
            l.Id, l.Level, l.EventName, l.Message, l.DeviceId,
            readerName = l.Reader!.Name, l.OccurredAtUtc
        });

        return Paged(await projected.ToPagedResultAsync(paging.Page, paging.PageSize, ct));
    }

    [HttpGet("login-attempts")]
    [HasPermission(Permissions.Audit.View)]
    public async Task<ActionResult<PagedResult<object>>> LoginAttempts(
        [FromQuery] PagedQuery paging, [FromQuery] bool failedOnly = false, CancellationToken ct = default)
    {
        var q = _db.LoginAttempts.AsNoTracking().AsQueryable();
        if (failedOnly) q = q.Where(a => !a.Succeeded);

        var projected = q.OrderByDescending(a => a.AttemptedAtUtc).Select(a => (object)new
        {
            a.Id, a.UserNameOrEmail, a.Succeeded, a.FailureReason, a.IpAddress, a.AttemptedAtUtc
        });

        return Paged(await projected.ToPagedResultAsync(paging.Page, paging.PageSize, ct));
    }
}

/// <summary>Runtime settings a school can change without a redeploy.</summary>
public class SettingsController : ApiControllerBase
{
    private readonly CampusTrackDbContext _db;
    private readonly ISettingsProvider _settings;

    public SettingsController(CampusTrackDbContext db, ISettingsProvider settings)
    {
        _db = db;
        _settings = settings;
    }

    [HttpGet]
    [HasPermission(Permissions.Settings.View)]
    public async Task<ActionResult<IReadOnlyList<object>>> Get(CancellationToken ct) =>
        Ok(await _db.SystemSettings.AsNoTracking()
            .OrderBy(s => s.Category).ThenBy(s => s.DisplayOrder)
            .Select(s => (object)new
            {
                s.Id, s.Key, s.Category, s.DisplayName, s.Description, s.DataType,
                s.DefaultValue, s.IsEditable,
                // Secrets are never returned, only whether one is set.
                value = s.IsSecret ? null : s.Value,
                s.IsSecret,
                hasValue = s.Value != null
            })
            .ToListAsync(ct));

    [HttpPut]
    [HasPermission(Permissions.Settings.Manage)]
    public async Task<IActionResult> Update([FromBody] List<SettingUpdate> updates, CancellationToken ct)
    {
        foreach (var update in updates)
        {
            var setting = await _db.SystemSettings.FirstOrDefaultAsync(s => s.Key == update.Key, ct);
            if (setting is null) continue;

            if (!setting.IsEditable)
                throw DomainException.Invalid($"'{setting.DisplayName}' cannot be changed from here.");

            ValidateValue(setting.DataType, update.Value, setting.DisplayName);
            setting.Value = update.Value;
        }

        await _db.SaveChangesAsync(ct);

        // The provider caches values for a minute; an explicit edit should take effect now.
        _settings.Invalidate();
        return NoContent();
    }

    /// <summary>
    /// Rejects a value the running system could not parse. Without this, a mistyped time would
    /// only surface later as the daily report silently failing to send.
    /// </summary>
    private static void ValidateValue(Domain.Enums.SettingDataType type, string? value, string displayName)
    {
        if (string.IsNullOrWhiteSpace(value)) return;

        var valid = type switch
        {
            Domain.Enums.SettingDataType.Integer => int.TryParse(value, out _),
            Domain.Enums.SettingDataType.Decimal => decimal.TryParse(value, out _),
            Domain.Enums.SettingDataType.Boolean =>
                bool.TryParse(value, out _) || value is "1" or "0",
            Domain.Enums.SettingDataType.Time => TimeOnly.TryParse(value, out _),
            Domain.Enums.SettingDataType.Json => IsValidJson(value),
            _ => true
        };

        if (!valid)
            throw DomainException.Invalid($"'{value}' is not a valid {type} value for {displayName}.");
    }

    private static bool IsValidJson(string value)
    {
        try
        {
            System.Text.Json.JsonDocument.Parse(value);
            return true;
        }
        catch (System.Text.Json.JsonException)
        {
            return false;
        }
    }
}

public record AdminResetPasswordDto
{
    public required string NewPassword { get; init; }
    public bool RequireChangeOnNextLogin { get; init; } = true;
}

public record PermissionOverrideRequest
{
    public required string Permission { get; init; }
    public bool IsGranted { get; init; } = true;
}

public record RoleRequest
{
    public required string Name { get; init; }
    public string? Description { get; init; }
    public List<string>? Permissions { get; init; }
}

public record SettingUpdate
{
    public required string Key { get; init; }
    public string? Value { get; init; }
}
