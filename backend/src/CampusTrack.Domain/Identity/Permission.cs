using CampusTrack.Domain.Identity;

namespace CampusTrack.Domain.Identity;

/// <summary>
/// A single capability such as <c>students.create</c>. Authorisation is checked against
/// permissions, never against role names, so a school can invent its own roles
/// (e.g. "Head of Year") without a code change.
/// </summary>
public class Permission
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;      // students.create
    public string Group { get; set; } = string.Empty;     // Students
    public string DisplayName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsSystemPermission { get; set; } = true;

    public ICollection<RolePermission> RolePermissions { get; set; } = new List<RolePermission>();
}

public class RolePermission
{
    public int RoleId { get; set; }
    public ApplicationRole? Role { get; set; }
    public int PermissionId { get; set; }
    public Permission? Permission { get; set; }
    public DateTime GrantedAtUtc { get; set; }
    public int? GrantedByUserId { get; set; }
}

/// <summary>
/// Per-user override on top of role permissions. <see cref="IsGranted"/> false is a
/// deny that beats any role grant — used to fence off a single sensitive capability.
/// </summary>
public class UserPermission
{
    public int UserId { get; set; }
    public ApplicationUser? User { get; set; }
    public int PermissionId { get; set; }
    public Permission? Permission { get; set; }
    public bool IsGranted { get; set; } = true;
    public DateTime GrantedAtUtc { get; set; }
    public int? GrantedByUserId { get; set; }
}
