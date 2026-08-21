using CampusTrack.Domain.Common;
using CampusTrack.Domain.Enums;
using Microsoft.AspNetCore.Identity;

namespace CampusTrack.Domain.Identity;

/// <summary>
/// The single account record behind every human in the system. Role-specific data
/// (student, teacher, guardian, staff) hangs off this via one-to-one profiles, so a
/// person who is both a teacher and a parent still has exactly one login.
/// </summary>
public class ApplicationUser : IdentityUser<int>, IAuditableEntity, ISoftDeletable, ITenantScoped
{
    public int SchoolId { get; set; } = 1;

    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string FullName => $"{FirstName} {LastName}".Trim();

    public Gender Gender { get; set; } = Gender.Unspecified;
    public DateOnly? DateOfBirth { get; set; }
    public string? NationalId { get; set; }
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? ProfileImagePath { get; set; }

    /// <summary>IANA id (e.g. "Asia/Riyadh"). Used to render event times in the user's own day.</summary>
    public string? TimeZoneId { get; set; }
    public string PreferredLanguage { get; set; } = "en";

    public bool IsActive { get; set; } = true;
    /// <summary>Forces a password change on next sign-in — set for admin-created accounts.</summary>
    public bool MustChangePassword { get; set; }
    public DateTime? LastLoginAtUtc { get; set; }
    public string? LastLoginIp { get; set; }
    public DateTime? PasswordChangedAtUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public int? CreatedByUserId { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
    public int? UpdatedByUserId { get; set; }

    public bool IsDeleted { get; set; }
    public DateTime? DeletedAtUtc { get; set; }
    public int? DeletedByUserId { get; set; }

    public ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();
}

public class ApplicationRole : IdentityRole<int>
{
    /// <summary>Built-in roles cannot be renamed or deleted from the UI.</summary>
    public bool IsSystemRole { get; set; }
    public string? Description { get; set; }
    public DateTime CreatedAtUtc { get; set; }

    public ICollection<RolePermission> RolePermissions { get; set; } = new List<RolePermission>();
}

public class ApplicationUserRole : IdentityUserRole<int>
{
    public ApplicationUser? User { get; set; }
    public ApplicationRole? Role { get; set; }
}
