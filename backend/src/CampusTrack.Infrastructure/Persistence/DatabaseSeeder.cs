using CampusTrack.Application.Authorization;
using CampusTrack.Application.Common;
using CampusTrack.Domain.Assessment;
using CampusTrack.Domain.Identity;
using CampusTrack.Domain.People;
using CampusTrack.Domain.Settings;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace CampusTrack.Infrastructure.Persistence;

/// <summary>
/// Brings a database up to a usable baseline: the school record, the permission catalogue,
/// the built-in roles, the settings catalogue, a default grading scale and the first
/// administrator.
///
/// Seeding is idempotent and additive. It fills gaps but never overwrites: a school that has
/// tuned its late threshold or edited a role's grants keeps those changes through every
/// subsequent deployment. New permissions introduced by a release are added to SuperAdmin
/// automatically, because a capability nobody can grant is a capability nobody can use.
/// </summary>
public class DatabaseSeeder
{
    private readonly CampusTrackDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<ApplicationRole> _roleManager;
    private readonly IConfiguration _configuration;
    private readonly ILogger<DatabaseSeeder> _logger;

    public DatabaseSeeder(
        CampusTrackDbContext db,
        UserManager<ApplicationUser> userManager,
        RoleManager<ApplicationRole> roleManager,
        IConfiguration configuration,
        ILogger<DatabaseSeeder> logger)
    {
        _db = db;
        _userManager = userManager;
        _roleManager = roleManager;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task SeedAsync(CancellationToken ct = default)
    {
        await SeedSchoolAsync(ct);
        await SeedPermissionsAsync(ct);
        await SeedRolesAsync(ct);
        await SeedSettingsAsync(ct);
        await SeedGradeScaleAsync(ct);
        await SeedAdministratorAsync(ct);
    }

    private async Task SeedSchoolAsync(CancellationToken ct)
    {
        if (await _db.Schools.AnyAsync(ct)) return;

        _db.Schools.Add(new School
        {
            Id = 1,
            Name = _configuration["School:Name"] ?? "SMA Demonstration School",
            Code = _configuration["School:Code"] ?? "CTS",
            TimeZoneId = _configuration["SchoolTime:TimeZoneId"] ?? "UTC",
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        });

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Seeded the school record");
    }

    /// <summary>
    /// Reconciles the permission table with the catalogue in code. Permissions are code-owned:
    /// the constants are what endpoints reference, so the table follows them rather than the
    /// other way round.
    /// </summary>
    private async Task SeedPermissionsAsync(CancellationToken ct)
    {
        var existing = await _db.Permissions.Select(p => p.Name).ToListAsync(ct);
        var existingSet = existing.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var missing = Permissions.All
            .Where(p => !existingSet.Contains(p.Name))
            .Select(p => new Permission
            {
                Name = p.Name,
                Group = p.Group,
                DisplayName = p.DisplayName,
                IsSystemPermission = true
            })
            .ToList();

        if (missing.Count == 0) return;

        _db.Permissions.AddRange(missing);
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Seeded {Count} new permission(s)", missing.Count);
    }

    private async Task SeedRolesAsync(CancellationToken ct)
    {
        var permissionIds = await _db.Permissions
            .ToDictionaryAsync(p => p.Name, p => p.Id, StringComparer.OrdinalIgnoreCase, ct);

        foreach (var roleName in Permissions.RoleNames.All)
        {
            var role = await _roleManager.FindByNameAsync(roleName);
            var isNewRole = role is null;

            if (role is null)
            {
                role = new ApplicationRole
                {
                    Name = roleName,
                    NormalizedName = roleName.ToUpperInvariant(),
                    IsSystemRole = true,
                    Description = DescribeRole(roleName),
                    CreatedAtUtc = DateTime.UtcNow
                };

                var result = await _roleManager.CreateAsync(role);
                if (!result.Succeeded)
                {
                    _logger.LogError("Could not create role {Role}: {Errors}",
                        roleName, string.Join("; ", result.Errors.Select(e => e.Description)));
                    continue;
                }
            }

            if (!RolePermissionDefaults.Map.TryGetValue(roleName, out var defaults)) continue;

            var alreadyGranted = await _db.RolePermissions
                .Where(rp => rp.RoleId == role.Id)
                .Select(rp => rp.PermissionId)
                .ToListAsync(ct);

            var grantedSet = alreadyGranted.ToHashSet();

            // For an existing role, only SuperAdmin is topped up with newly introduced
            // permissions. Other roles keep exactly what the school has configured - silently
            // widening a teacher's access on upgrade would be a security regression.
            var shouldTopUp = isNewRole || roleName == Permissions.RoleNames.SuperAdmin;
            if (!shouldTopUp) continue;

            var toGrant = defaults
                .Where(name => permissionIds.TryGetValue(name, out var id) && !grantedSet.Contains(id))
                .Select(name => new RolePermission
                {
                    RoleId = role.Id,
                    PermissionId = permissionIds[name],
                    GrantedAtUtc = DateTime.UtcNow
                })
                .ToList();

            if (toGrant.Count == 0) continue;

            _db.RolePermissions.AddRange(toGrant);
            await _db.SaveChangesAsync(ct);
            _logger.LogInformation("Granted {Count} permission(s) to role {Role}", toGrant.Count, roleName);
        }
    }

    private async Task SeedSettingsAsync(CancellationToken ct)
    {
        var existing = await _db.SystemSettings.Select(s => s.Key).ToListAsync(ct);
        var existingSet = existing.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var missing = SettingKeys.Defaults
            .Where(d => !existingSet.Contains(d.Key))
            .Select((d, index) => new SystemSetting
            {
                Key = d.Key,
                Category = d.Category,
                Value = d.DefaultValue,
                DefaultValue = d.DefaultValue,
                DataType = d.DataType,
                DisplayName = d.DisplayName,
                Description = string.IsNullOrWhiteSpace(d.Description) ? null : d.Description,
                IsEditable = true,
                DisplayOrder = index,
                CreatedAtUtc = DateTime.UtcNow
            })
            .ToList();

        if (missing.Count == 0) return;

        _db.SystemSettings.AddRange(missing);
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Seeded {Count} new setting(s)", missing.Count);
    }

    private async Task SeedGradeScaleAsync(CancellationToken ct)
    {
        if (await _db.GradeScales.AnyAsync(ct)) return;

        var scale = new GradeScale
        {
            Name = "Percentage (A-F)",
            Description = "Standard percentage scale with letter grades and 4.0 grade points.",
            MaxValue = 100m,
            PassValue = 40m,
            IsDefault = true,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow,
            Bands =
            [
                new GradeBand { Letter = "A+", MinPercentage = 95, MaxPercentage = 100, GradePoint = 4.0m, Descriptor = "Outstanding", ColourHex = "#059669" },
                new GradeBand { Letter = "A",  MinPercentage = 85, MaxPercentage = 94.99m, GradePoint = 4.0m, Descriptor = "Excellent", ColourHex = "#10b981" },
                new GradeBand { Letter = "B",  MinPercentage = 75, MaxPercentage = 84.99m, GradePoint = 3.0m, Descriptor = "Good", ColourHex = "#3b82f6" },
                new GradeBand { Letter = "C",  MinPercentage = 65, MaxPercentage = 74.99m, GradePoint = 2.0m, Descriptor = "Satisfactory", ColourHex = "#f59e0b" },
                new GradeBand { Letter = "D",  MinPercentage = 50, MaxPercentage = 64.99m, GradePoint = 1.0m, Descriptor = "Needs improvement", ColourHex = "#f97316" },
                new GradeBand { Letter = "E",  MinPercentage = 40, MaxPercentage = 49.99m, GradePoint = 0.5m, Descriptor = "Marginal pass", ColourHex = "#ef4444" },
                new GradeBand { Letter = "F",  MinPercentage = 0,  MaxPercentage = 39.99m, GradePoint = 0m,   Descriptor = "Fail", ColourHex = "#dc2626" }
            ]
        };

        _db.GradeScales.Add(scale);
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Seeded the default grading scale");
    }

    /// <summary>
    /// Creates the first administrator if no account exists at all.
    ///
    /// The password comes from configuration. A development fallback exists so a fresh clone
    /// runs, but the account is flagged to force a password change on first sign-in, and a
    /// warning is logged so the default cannot quietly survive into production.
    /// </summary>
    private async Task SeedAdministratorAsync(CancellationToken ct)
    {
        if (await _db.Users.AnyAsync(ct)) return;

        var userName = _configuration["Seed:AdminUserName"] ?? "admin";
        var email = _configuration["Seed:AdminEmail"] ?? "admin@campustrack.local";
        var password = _configuration["Seed:AdminPassword"];

        var usingFallback = string.IsNullOrWhiteSpace(password);
        if (usingFallback) password = "ChangeMe!2026";

        var admin = new ApplicationUser
        {
            UserName = userName,
            NormalizedUserName = userName.ToUpperInvariant(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            EmailConfirmed = true,
            FirstName = "System",
            LastName = "Administrator",
            SchoolId = 1,
            IsActive = true,
            MustChangePassword = true,
            CreatedAtUtc = DateTime.UtcNow,
            SecurityStamp = Guid.NewGuid().ToString()
        };

        var created = await _userManager.CreateAsync(admin, password!);
        if (!created.Succeeded)
        {
            _logger.LogError("Could not create the initial administrator: {Errors}",
                string.Join("; ", created.Errors.Select(e => e.Description)));
            return;
        }

        await _userManager.AddToRoleAsync(admin, Permissions.RoleNames.SuperAdmin);

        if (usingFallback)
        {
            _logger.LogWarning(
                "Created administrator '{UserName}' with the built-in development password. " +
                "Sign in and change it immediately, or set Seed:AdminPassword before deploying.",
                userName);
        }
        else
        {
            _logger.LogInformation("Created the initial administrator '{UserName}'", userName);
        }
    }

    private static string DescribeRole(string roleName) => roleName switch
    {
        Permissions.RoleNames.SuperAdmin => "Unrestricted access. Cannot be locked out of the system.",
        Permissions.RoleNames.Admin => "Runs the school: people, academics, RFID, reports and settings.",
        Permissions.RoleNames.Teacher => "Access limited to their own classes, students and gradebook.",
        Permissions.RoleNames.Student => "Sees only their own timetable, attendance, work and results.",
        Permissions.RoleNames.Guardian => "Sees only the children they are approved to follow.",
        Permissions.RoleNames.Staff => "Non-teaching staff: attendance and monitoring, no academic records.",
        _ => string.Empty
    };
}
