using CampusTrack.Application.Authorization;
using CampusTrack.Application.Common;
using CampusTrack.Application.Common.Interfaces;
using CampusTrack.Application.Common.Models;
using CampusTrack.Application.People;
using CampusTrack.Domain.Common;
using CampusTrack.Domain.Enums;
using CampusTrack.Domain.Identity;
using CampusTrack.Domain.People;
using CampusTrack.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CampusTrack.Infrastructure.People;

/// <summary>
/// Creates the sign-in account that sits behind a person record.
///
/// Shared by students, teachers, guardians and staff because the rules are identical in every
/// case - unique username, role assignment, forced password change - and duplicating them per
/// role is how one of them eventually drifts and stops forcing the password reset.
/// </summary>
public interface IPersonAccountFactory
{
    Task<(ApplicationUser User, string? TemporaryPassword)> CreateAsync(
        NewAccount account, string roleName, CancellationToken ct = default);

    Task<string> NextCodeAsync(string prefixSettingKey, string fallbackPrefix,
        Func<string, Task<bool>> exists, CancellationToken ct = default);
}

public record NewAccount(
    string FirstName,
    string LastName,
    string? UserName,
    string? Password,
    string? Email,
    string? PhoneNumber,
    Gender Gender = Gender.Unspecified,
    DateOnly? DateOfBirth = null,
    string? Address = null,
    string? City = null,
    string? NationalId = null,
    bool IsActive = true,
    string? CodeForFallbackUserName = null);

public class PersonAccountFactory : IPersonAccountFactory
{
    private readonly CampusTrackDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ISettingsProvider _settings;
    private readonly IDateTimeProvider _clock;
    private readonly ICurrentUser _currentUser;

    public PersonAccountFactory(
        CampusTrackDbContext db,
        UserManager<ApplicationUser> userManager,
        ISettingsProvider settings,
        IDateTimeProvider clock,
        ICurrentUser currentUser)
    {
        _db = db;
        _userManager = userManager;
        _settings = settings;
        _clock = clock;
        _currentUser = currentUser;
    }

    public async Task<(ApplicationUser User, string? TemporaryPassword)> CreateAsync(
        NewAccount account, string roleName, CancellationToken ct = default)
    {
        var userName = string.IsNullOrWhiteSpace(account.UserName)
            ? await GenerateUserNameAsync(account.FirstName, account.LastName, account.CodeForFallbackUserName, ct)
            : account.UserName.Trim();

        if (await _userManager.FindByNameAsync(userName) is not null)
            throw DomainException.Conflict($"The username '{userName}' is already taken.");

        var generated = string.IsNullOrWhiteSpace(account.Password);
        var password = generated ? GenerateTemporaryPassword() : account.Password!;

        var user = new ApplicationUser
        {
            UserName = userName,
            NormalizedUserName = userName.ToUpperInvariant(),
            Email = account.Email,
            NormalizedEmail = account.Email?.ToUpperInvariant(),
            EmailConfirmed = true,
            PhoneNumber = account.PhoneNumber,
            FirstName = account.FirstName.Trim(),
            LastName = account.LastName.Trim(),
            Gender = account.Gender,
            DateOfBirth = account.DateOfBirth,
            Address = account.Address,
            City = account.City,
            NationalId = account.NationalId,
            SchoolId = _currentUser.SchoolId,
            IsActive = account.IsActive,
            MustChangePassword = true,
            CreatedAtUtc = _clock.UtcNow,
            CreatedByUserId = _currentUser.UserId,
            SecurityStamp = Guid.NewGuid().ToString()
        };

        var result = await _userManager.CreateAsync(user, password);
        if (!result.Succeeded)
            throw DomainException.Invalid(string.Join(" ", result.Errors.Select(e => e.Description)));

        await _userManager.AddToRoleAsync(user, roleName);
        return (user, generated ? password : null);
    }

    public async Task<string> NextCodeAsync(
        string prefixSettingKey, string fallbackPrefix, Func<string, Task<bool>> exists, CancellationToken ct = default)
    {
        var prefix = await _settings.GetAsync(prefixSettingKey, fallbackPrefix, ct);

        for (var attempt = 1; attempt <= 1000; attempt++)
        {
            var candidate = $"{prefix}{attempt:D6}";
            if (!await exists(candidate)) return candidate;
        }

        return $"{prefix}{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}";
    }

    private async Task<string> GenerateUserNameAsync(string first, string last, string? fallback, CancellationToken ct)
    {
        var baseName = $"{Slug(first)}.{Slug(last)}";
        if (baseName.Replace(".", "").Length == 0) baseName = fallback?.ToLowerInvariant() ?? "user";

        var candidate = baseName;
        var suffix = 2;

        while (await _db.Users.IgnoreQueryFilters()
                   .AnyAsync(u => u.NormalizedUserName == candidate.ToUpperInvariant(), ct))
        {
            candidate = $"{baseName}{suffix++}";
            if (suffix > 100) return $"{baseName}.{Guid.NewGuid().ToString("N")[..5]}";
        }

        return candidate;
    }

    private static string Slug(string value) =>
        new(value.Trim().ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

    private static string GenerateTemporaryPassword()
    {
        const string upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
        const string lower = "abcdefghijkmnpqrstuvwxyz";
        const string digits = "23456789";

        var random = Random.Shared;
        var chars = new List<char>
        {
            upper[random.Next(upper.Length)],
            lower[random.Next(lower.Length)],
            digits[random.Next(digits.Length)],
            '!'
        };

        var pool = upper + lower + digits;
        for (var i = 0; i < 6; i++) chars.Add(pool[random.Next(pool.Length)]);

        return new string(chars.OrderBy(_ => random.Next()).ToArray());
    }
}

// --------------------------------------------------------------------- teachers ----

public interface ITeacherService
{
    Task<PagedResult<TeacherListItem>> SearchAsync(PersonQuery query, CancellationToken ct = default);
    Task<TeacherListItem> GetAsync(int id, CancellationToken ct = default);
    Task<CreatedPersonResult> CreateAsync(CreateTeacherRequest request, CancellationToken ct = default);
    Task UpdateAsync(int id, CreateTeacherRequest request, CancellationToken ct = default);
    Task DeleteAsync(int id, CancellationToken ct = default);
}

public class TeacherService : ITeacherService
{
    private readonly CampusTrackDbContext _db;
    private readonly IPersonAccountFactory _accounts;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<TeacherService> _logger;

    public TeacherService(
        CampusTrackDbContext db,
        IPersonAccountFactory accounts,
        ICurrentUser currentUser,
        ILogger<TeacherService> logger)
    {
        _db = db;
        _accounts = accounts;
        _currentUser = currentUser;
        _logger = logger;
    }

    public async Task<PagedResult<TeacherListItem>> SearchAsync(PersonQuery query, CancellationToken ct = default)
    {
        var q = _db.Teachers.AsNoTracking().AsQueryable();

        if (query.Status is { } status) q = q.Where(t => t.Status == status);

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            q = q.Where(t => t.User!.FirstName.Contains(term)
                             || t.User.LastName.Contains(term)
                             || t.TeacherCode.Contains(term)
                             || (t.Specialisation != null && t.Specialisation.Contains(term)));
        }

        q = query.SortDescending
            ? q.OrderByDescending(t => t.User!.LastName)
            : q.OrderBy(t => t.User!.LastName);

        return await q.Select(Project).ToPagedResultAsync(query.Page, query.PageSize, ct);
    }

    public async Task<TeacherListItem> GetAsync(int id, CancellationToken ct = default) =>
        await _db.Teachers.AsNoTracking().Where(t => t.Id == id).Select(Project).FirstOrDefaultAsync(ct)
        ?? throw new KeyNotFoundException("That teacher does not exist.");

    public Task<CreatedPersonResult> CreateAsync(CreateTeacherRequest request, CancellationToken ct = default)
        => _db.InTransactionAsync(token => CreateInternalAsync(request, token), ct);

    private async Task<CreatedPersonResult> CreateInternalAsync(CreateTeacherRequest request, CancellationToken ct)
    {
        var code = string.IsNullOrWhiteSpace(request.TeacherCode)
            ? await _accounts.NextCodeAsync(SettingKeys.TeacherCodePrefix, "TCH-",
                candidate => _db.QueryIgnoringFilters<Teacher>().AnyAsync(t => t.TeacherCode == candidate, ct), ct)
            : request.TeacherCode.Trim();

        if (await _db.Teachers.AnyAsync(t => t.TeacherCode == code, ct))
            throw DomainException.Conflict($"Teacher code '{code}' is already in use.");

        var (user, temporaryPassword) = await _accounts.CreateAsync(new NewAccount(
            request.FirstName, request.LastName, request.UserName, request.Password,
            request.Email, request.PhoneNumber, request.Gender, request.DateOfBirth,
            request.Address, null, null, request.Status == PersonStatus.Active, code),
            Permissions.RoleNames.Teacher, ct);

        var teacher = new Teacher
        {
            SchoolId = _currentUser.SchoolId,
            UserId = user.Id,
            TeacherCode = code,
            Qualification = request.Qualification,
            Specialisation = request.Specialisation,
            HireDate = request.HireDate,
            OfficeLocation = request.OfficeLocation,
            Status = request.Status
        };

        _db.Teachers.Add(teacher);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Created teacher {Code} ({TeacherId})", code, teacher.Id);

        return new CreatedPersonResult
        {
            Id = teacher.Id,
            UserId = user.Id,
            Code = code,
            UserName = user.UserName!,
            TemporaryPassword = temporaryPassword
        };
    }

    public async Task UpdateAsync(int id, CreateTeacherRequest request, CancellationToken ct = default)
    {
        var teacher = await _db.Teachers.Include(t => t.User).FirstOrDefaultAsync(t => t.Id == id, ct)
            ?? throw new KeyNotFoundException("That teacher does not exist.");

        var user = teacher.User!;
        user.FirstName = request.FirstName.Trim();
        user.LastName = request.LastName.Trim();
        user.Email = request.Email;
        user.NormalizedEmail = request.Email?.ToUpperInvariant();
        user.PhoneNumber = request.PhoneNumber;
        user.Gender = request.Gender;
        user.DateOfBirth = request.DateOfBirth;
        user.Address = request.Address;
        user.IsActive = request.Status == PersonStatus.Active;

        teacher.Qualification = request.Qualification;
        teacher.Specialisation = request.Specialisation;
        teacher.HireDate = request.HireDate;
        teacher.OfficeLocation = request.OfficeLocation;
        teacher.Status = request.Status;

        await _db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        var teacher = await _db.Teachers.Include(t => t.User).FirstOrDefaultAsync(t => t.Id == id, ct)
            ?? throw new KeyNotFoundException("That teacher does not exist.");

        // Refuse rather than silently orphan lessons: an admin must reassign the timetable
        // first, or a class would be left with no teacher and no warning.
        var activeAssignments = await _db.TeachingAssignments
            .CountAsync(a => a.TeacherId == id && a.IsActive, ct);

        if (activeAssignments > 0)
            throw DomainException.Conflict(
                $"This teacher still has {activeAssignments} active class assignment(s). Reassign them first.");

        teacher.Status = PersonStatus.Inactive;
        if (teacher.User is not null) teacher.User.IsActive = false;
        _db.Teachers.Remove(teacher);
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Held as an Expression, not a method. A method call inside Select() cannot be translated
    /// to SQL, so EF would materialise entities and map on the client - where every navigation
    /// it did not eagerly load is null.
    /// </summary>
    private static readonly System.Linq.Expressions.Expression<Func<Teacher, TeacherListItem>>
        Project = t => new TeacherListItem
    {
        Id = t.Id,
        TeacherCode = t.TeacherCode,
        FullName = t.User!.FirstName + " " + t.User.LastName,
        Email = t.User.Email,
        PhoneNumber = t.User.PhoneNumber,
        PhotoUrl = t.User.ProfileImagePath,
        Qualification = t.Qualification,
        Specialisation = t.Specialisation,
        HireDate = t.HireDate,
        Status = t.Status,
        SectionCount = t.TeachingAssignments.Where(a => a.IsActive).Select(a => a.SectionId).Distinct().Count(),
        SubjectCount = t.TeachingAssignments.Where(a => a.IsActive).Select(a => a.SubjectId).Distinct().Count(),
        Subjects = t.TeachingAssignments.Where(a => a.IsActive).Select(a => a.Subject!.Name).Distinct().ToList()
    };
}

// -------------------------------------------------------------------- guardians ----

public interface IGuardianService
{
    Task<PagedResult<GuardianListItem>> SearchAsync(PersonQuery query, CancellationToken ct = default);
    Task<GuardianListItem> GetAsync(int id, CancellationToken ct = default);
    Task<CreatedPersonResult> CreateAsync(CreateGuardianRequest request, CancellationToken ct = default);
    Task UpdateAsync(int id, CreateGuardianRequest request, CancellationToken ct = default);
    Task DeleteAsync(int id, CancellationToken ct = default);
    Task LinkChildAsync(int guardianId, LinkChildRequest request, CancellationToken ct = default);
    Task UnlinkChildAsync(int guardianId, int studentId, CancellationToken ct = default);
    Task ApproveLinkAsync(int linkId, bool approved, CancellationToken ct = default);
}

public class GuardianService : IGuardianService
{
    private readonly CampusTrackDbContext _db;
    private readonly IPersonAccountFactory _accounts;
    private readonly IDateTimeProvider _clock;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<GuardianService> _logger;

    public GuardianService(
        CampusTrackDbContext db,
        IPersonAccountFactory accounts,
        IDateTimeProvider clock,
        ICurrentUser currentUser,
        ILogger<GuardianService> logger)
    {
        _db = db;
        _accounts = accounts;
        _clock = clock;
        _currentUser = currentUser;
        _logger = logger;
    }

    public async Task<PagedResult<GuardianListItem>> SearchAsync(PersonQuery query, CancellationToken ct = default)
    {
        var q = _db.Guardians.AsNoTracking().AsQueryable();

        if (query.Status is { } status) q = q.Where(g => g.Status == status);

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            q = q.Where(g => g.User!.FirstName.Contains(term)
                             || g.User.LastName.Contains(term)
                             || g.GuardianCode.Contains(term)
                             || (g.User.PhoneNumber != null && g.User.PhoneNumber.Contains(term))
                             // Searching by the child's name is how office staff actually
                             // find a parent when a pupil is unwell.
                             || g.Students.Any(s => s.Student!.User!.FirstName.Contains(term)
                                                    || s.Student.User.LastName.Contains(term)));
        }

        q = query.SortDescending
            ? q.OrderByDescending(g => g.User!.LastName)
            : q.OrderBy(g => g.User!.LastName);

        return await q.Select(Project).ToPagedResultAsync(query.Page, query.PageSize, ct);
    }

    public async Task<GuardianListItem> GetAsync(int id, CancellationToken ct = default) =>
        await _db.Guardians.AsNoTracking().Where(g => g.Id == id).Select(Project).FirstOrDefaultAsync(ct)
        ?? throw new KeyNotFoundException("That guardian does not exist.");

    public Task<CreatedPersonResult> CreateAsync(CreateGuardianRequest request, CancellationToken ct = default)
        => _db.InTransactionAsync(token => CreateInternalAsync(request, token), ct);

    private async Task<CreatedPersonResult> CreateInternalAsync(CreateGuardianRequest request, CancellationToken ct)
    {
        var code = string.IsNullOrWhiteSpace(request.GuardianCode)
            ? await _accounts.NextCodeAsync(SettingKeys.GuardianCodePrefix, "GRD-",
                candidate => _db.QueryIgnoringFilters<Guardian>().AnyAsync(g => g.GuardianCode == candidate, ct), ct)
            : request.GuardianCode.Trim();

        var (user, temporaryPassword) = await _accounts.CreateAsync(new NewAccount(
            request.FirstName, request.LastName, request.UserName, request.Password,
            request.Email, request.PhoneNumber, request.Gender, null,
            request.Address, null, null, true, code),
            Permissions.RoleNames.Guardian, ct);

        var guardian = new Guardian
        {
            SchoolId = _currentUser.SchoolId,
            UserId = user.Id,
            GuardianCode = code,
            Occupation = request.Occupation,
            WorkplacePhone = request.WorkplacePhone,
            AlternatePhone = request.AlternatePhone,
            Status = PersonStatus.Active
        };

        _db.Guardians.Add(guardian);
        await _db.SaveChangesAsync(ct);

        foreach (var child in request.Children ?? [])
            await LinkChildInternalAsync(guardian.Id, child, approved: true, ct);

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Created guardian {Code} with {ChildCount} linked child(ren)",
            code, request.Children?.Count ?? 0);

        return new CreatedPersonResult
        {
            Id = guardian.Id,
            UserId = user.Id,
            Code = code,
            UserName = user.UserName!,
            TemporaryPassword = temporaryPassword
        };
    }

    public async Task UpdateAsync(int id, CreateGuardianRequest request, CancellationToken ct = default)
    {
        var guardian = await _db.Guardians.Include(g => g.User)
            .FirstOrDefaultAsync(g => g.Id == id, ct)
            ?? throw new KeyNotFoundException("That guardian does not exist.");

        var user = guardian.User!;
        user.FirstName = request.FirstName.Trim();
        user.LastName = request.LastName.Trim();
        user.Email = request.Email;
        user.NormalizedEmail = request.Email?.ToUpperInvariant();
        user.PhoneNumber = request.PhoneNumber;
        user.Address = request.Address;
        user.Gender = request.Gender;

        guardian.Occupation = request.Occupation;
        guardian.WorkplacePhone = request.WorkplacePhone;
        guardian.AlternatePhone = request.AlternatePhone;

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Updated guardian {GuardianId}", id);
    }

    /// <summary>
    /// Soft-deletes a guardian and disables their sign-in. The child links go with them,
    /// so a removed parent immediately stops receiving that child's notifications.
    /// </summary>
    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        var guardian = await _db.Guardians.Include(g => g.User).Include(g => g.Students)
            .FirstOrDefaultAsync(g => g.Id == id, ct)
            ?? throw new KeyNotFoundException("That guardian does not exist.");

        guardian.Status = PersonStatus.Inactive;
        if (guardian.User is not null) guardian.User.IsActive = false;

        _db.Guardians.Remove(guardian);
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Removed guardian {GuardianId} and {LinkCount} child link(s)",
            id, guardian.Students.Count);
    }

    public async Task LinkChildAsync(int guardianId, LinkChildRequest request, CancellationToken ct = default)
    {
        // A link created by school staff is authoritative and takes effect at once. A link a
        // parent requests for themselves would arrive here unapproved and wait for review.
        var approved = _currentUser.HasPermission(Permissions.Guardians.ManageLinks);

        await LinkChildInternalAsync(guardianId, request, approved, ct);
        await _db.SaveChangesAsync(ct);
    }

    public async Task UnlinkChildAsync(int guardianId, int studentId, CancellationToken ct = default)
    {
        var link = await _db.GuardianStudents
            .FirstOrDefaultAsync(g => g.GuardianId == guardianId && g.StudentId == studentId, ct)
            ?? throw new KeyNotFoundException("That link does not exist.");

        // Soft-removed so the audit trail keeps who could see this child, and when.
        link.IsDeleted = true;
        await _db.SaveChangesAsync(ct);
    }

    public async Task ApproveLinkAsync(int linkId, bool approved, CancellationToken ct = default)
    {
        var link = await _db.GuardianStudents.FirstOrDefaultAsync(g => g.Id == linkId, ct)
            ?? throw new KeyNotFoundException("That link does not exist.");

        link.IsApproved = approved;
        link.ApprovedAtUtc = approved ? _clock.UtcNow : null;
        link.ApprovedByUserId = approved ? _currentUser.UserId : null;

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Guardian link {LinkId} {Decision} by user {UserId}",
            linkId, approved ? "approved" : "rejected", _currentUser.UserId);
    }

    private async Task LinkChildInternalAsync(int guardianId, LinkChildRequest request, bool approved, CancellationToken ct)
    {
        var studentExists = await _db.Students.AnyAsync(s => s.Id == request.StudentId, ct);
        if (!studentExists) throw DomainException.Invalid("That student does not exist.");

        var existing = await _db.GuardianStudents.IgnoreQueryFilters()
            .FirstOrDefaultAsync(g => g.GuardianId == guardianId && g.StudentId == request.StudentId, ct);

        if (existing is not null)
        {
            // Re-linking a previously removed guardian reuses the row; the unique index on the
            // pair means a second insert would fail.
            existing.IsDeleted = false;
            existing.Relationship = request.Relationship;
            existing.IsPrimaryContact = request.IsPrimaryContact;
            existing.IsAuthorisedForPickup = request.IsAuthorisedForPickup;
            existing.ReceivesNotifications = request.ReceivesNotifications;
            existing.CanViewAcademics = request.CanViewAcademics;
            existing.IsApproved = approved;
            existing.ApprovedAtUtc = approved ? _clock.UtcNow : null;
            return;
        }

        _db.GuardianStudents.Add(new GuardianStudent
        {
            GuardianId = guardianId,
            StudentId = request.StudentId,
            Relationship = request.Relationship,
            IsPrimaryContact = request.IsPrimaryContact,
            IsAuthorisedForPickup = request.IsAuthorisedForPickup,
            ReceivesNotifications = request.ReceivesNotifications,
            CanViewAcademics = request.CanViewAcademics,
            IsApproved = approved,
            ApprovedAtUtc = approved ? _clock.UtcNow : null,
            ApprovedByUserId = approved ? _currentUser.UserId : null,
            CreatedAtUtc = _clock.UtcNow,
            CreatedByUserId = _currentUser.UserId
        });
    }

    /// <summary>See the note on TeacherService.Project - this must stay an Expression.</summary>
    private static readonly System.Linq.Expressions.Expression<Func<Guardian, GuardianListItem>>
        Project = g => new GuardianListItem
    {
        Id = g.Id,
        GuardianCode = g.GuardianCode,
        FullName = g.User!.FirstName + " " + g.User.LastName,
        Email = g.User.Email,
        PhoneNumber = g.User.PhoneNumber,
        AlternatePhone = g.AlternatePhone,
        Occupation = g.Occupation,
        Status = g.Status,
        ChildCount = g.Students.Count(s => s.IsApproved && !s.IsDeleted),
        Children = g.Students.Where(s => !s.IsDeleted)
            .Select(s => s.Student!.User!.FirstName + " " + s.Student.User.LastName).ToList(),
        HasPendingLinks = g.Students.Any(s => !s.IsApproved && !s.IsDeleted)
    };
}
