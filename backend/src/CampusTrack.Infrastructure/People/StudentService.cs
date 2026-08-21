using CampusTrack.Application.Authorization;
using CampusTrack.Application.Common;
using CampusTrack.Application.Common.Interfaces;
using CampusTrack.Application.Common.Models;
using CampusTrack.Application.People;
using CampusTrack.Domain.Academics;
using CampusTrack.Domain.Common;
using CampusTrack.Domain.Enums;
using CampusTrack.Domain.Identity;
using CampusTrack.Domain.People;
using CampusTrack.Domain.Rfid;
using CampusTrack.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CampusTrack.Infrastructure.People;

public interface IStudentService
{
    Task<PagedResult<StudentListItem>> SearchAsync(PersonQuery query, CancellationToken ct = default);
    Task<StudentDetail> GetAsync(int id, CancellationToken ct = default);
    Task<CreatedPersonResult> CreateAsync(CreateStudentRequest request, CancellationToken ct = default);
    Task UpdateAsync(int id, UpdateStudentRequest request, CancellationToken ct = default);
    Task DeleteAsync(int id, CancellationToken ct = default);
    Task<IReadOnlyList<StudentListItem>> GetBySectionAsync(int sectionId, CancellationToken ct = default);
}

/// <summary>
/// Student records and the accounts behind them.
///
/// Creating a student touches four tables (user, student, enrollment, card) and must be all
/// or nothing: a half-created student with a login but no record, or a card assigned to
/// nobody, is worse than a clean failure. Everything here runs inside one transaction.
/// </summary>
public class StudentService : IStudentService
{
    private readonly CampusTrackDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ISettingsProvider _settings;
    private readonly IDateTimeProvider _clock;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<StudentService> _logger;

    public StudentService(
        CampusTrackDbContext db,
        UserManager<ApplicationUser> userManager,
        ISettingsProvider settings,
        IDateTimeProvider clock,
        ICurrentUser currentUser,
        ILogger<StudentService> logger)
    {
        _db = db;
        _userManager = userManager;
        _settings = settings;
        _clock = clock;
        _currentUser = currentUser;
        _logger = logger;
    }

    public async Task<PagedResult<StudentListItem>> SearchAsync(PersonQuery query, CancellationToken ct = default)
    {
        var q = _db.Students.AsNoTracking().AsQueryable();

        if (query.Status is { } status) q = q.Where(s => s.Status == status);
        if (query.SectionId is { } sectionId) q = q.Where(s => s.CurrentSectionId == sectionId);
        if (query.SchoolClassId is { } classId) q = q.Where(s => s.CurrentSection!.SchoolClassId == classId);

        if (query.HasRfidTag is { } hasTag)
        {
            q = hasTag
                ? q.Where(s => s.RfidTags.Any(t => t.Status == RfidTagStatus.Active))
                : q.Where(s => !s.RfidTags.Any(t => t.Status == RfidTagStatus.Active));
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            q = q.Where(s => s.User!.FirstName.Contains(term)
                             || s.User.LastName.Contains(term)
                             || s.StudentCode.Contains(term)
                             || (s.AdmissionNumber != null && s.AdmissionNumber.Contains(term))
                             || (s.User.Email != null && s.User.Email.Contains(term)));
        }

        q = ApplySort(q, query.SortBy, query.SortDescending);

        return await q.Select(s => new StudentListItem
        {
            Id = s.Id,
            StudentCode = s.StudentCode,
            FullName = s.User!.FirstName + " " + s.User.LastName,
            Email = s.User.Email,
            PhoneNumber = s.User.PhoneNumber,
            PhotoUrl = s.User.ProfileImagePath,
            Gender = s.User.Gender,
            DateOfBirth = s.User.DateOfBirth,
            Status = s.Status,
            SectionId = s.CurrentSectionId,
            SectionName = s.CurrentSection!.DisplayName,
            ClassName = s.CurrentSection.SchoolClass!.Name,
            RollNumber = s.Enrollments
                .Where(e => e.Status == EnrollmentStatus.Active)
                .Select(e => e.RollNumber).FirstOrDefault(),
            HasActiveCard = s.RfidTags.Any(t => t.Status == RfidTagStatus.Active),
            // Only the last six characters reach a list screen.
            RfidCard = s.RfidTags.Where(t => t.Status == RfidTagStatus.Active)
                .Select(t => "***" + t.Epc.Substring(t.Epc.Length - 6)).FirstOrDefault(),
            PresenceState = _db.StudentPresences.Where(p => p.StudentId == s.Id)
                .Select(p => p.State).FirstOrDefault(),
            GuardianCount = s.Guardians.Count(g => g.IsApproved && !g.IsDeleted)
        }).ToPagedResultAsync(query.Page, query.PageSize, ct);
    }

    public async Task<StudentDetail> GetAsync(int id, CancellationToken ct = default)
    {
        var student = await _db.Students.AsNoTracking()
            .Where(s => s.Id == id)
            .Select(s => new StudentDetail
            {
                Id = s.Id,
                UserId = s.UserId,
                StudentCode = s.StudentCode,
                AdmissionNumber = s.AdmissionNumber,
                AdmissionDate = s.AdmissionDate,
                FullName = s.User!.FirstName + " " + s.User.LastName,
                Email = s.User.Email,
                PhoneNumber = s.User.PhoneNumber,
                PhotoUrl = s.User.ProfileImagePath,
                Gender = s.User.Gender,
                DateOfBirth = s.User.DateOfBirth,
                NationalId = s.User.NationalId,
                Address = s.User.Address,
                City = s.User.City,
                Status = s.Status,
                BloodGroup = s.BloodGroup,
                MedicalNotes = s.MedicalNotes,
                EmergencyContactName = s.EmergencyContactName,
                EmergencyContactPhone = s.EmergencyContactPhone,
                TransportRoute = s.TransportRoute,
                SectionId = s.CurrentSectionId,
                SectionName = s.CurrentSection!.DisplayName,
                ClassName = s.CurrentSection.SchoolClass!.Name,
                IsAccountActive = s.User.IsActive,
                LastLoginAtUtc = s.User.LastLoginAtUtc,
                HasActiveCard = s.RfidTags.Any(t => t.Status == RfidTagStatus.Active),
                PresenceState = _db.StudentPresences.Where(p => p.StudentId == s.Id)
                    .Select(p => p.State).FirstOrDefault(),
                GuardianCount = s.Guardians.Count(g => g.IsApproved && !g.IsDeleted),

                Guardians = s.Guardians.Where(g => !g.IsDeleted).Select(g => new GuardianLink
                {
                    LinkId = g.Id,
                    GuardianId = g.GuardianId,
                    FullName = g.Guardian!.User!.FirstName + " " + g.Guardian.User.LastName,
                    PhoneNumber = g.Guardian.User.PhoneNumber,
                    Email = g.Guardian.User.Email,
                    Relationship = g.Relationship,
                    IsPrimaryContact = g.IsPrimaryContact,
                    IsAuthorisedForPickup = g.IsAuthorisedForPickup,
                    ReceivesNotifications = g.ReceivesNotifications,
                    CanViewAcademics = g.CanViewAcademics,
                    IsApproved = g.IsApproved
                }).ToList(),

                Enrollments = s.Enrollments.Select(e => new EnrollmentSummary
                {
                    Id = e.Id,
                    SectionId = e.SectionId,
                    SectionName = e.Section!.DisplayName,
                    SessionName = e.AcademicSession!.Name,
                    RollNumber = e.RollNumber,
                    EnrolledOn = e.EnrolledOn,
                    Status = e.Status
                }).ToList(),

                Cards = s.RfidTags.Select(t => new TagSummary
                {
                    Id = t.Id,
                    Epc = t.Epc,
                    CardNumber = t.CardNumber,
                    Status = t.Status,
                    IssuedAtUtc = t.IssuedAtUtc,
                    LastSeenAtUtc = t.LastSeenAtUtc,
                    LastSeenLocation = _db.RfidLocations
                        .Where(l => l.Id == t.LastSeenLocationId).Select(l => l.Name).FirstOrDefault()
                }).ToList()
            })
            .FirstOrDefaultAsync(ct);

        if (student is null) throw new KeyNotFoundException("That student does not exist.");

        // Attendance percentage is a separate aggregate; folding it into the projection above
        // would force a correlated subquery over the whole attendance history.
        var attendance = await _db.DailyAttendances.AsNoTracking()
            .Where(a => a.StudentId == id)
            .GroupBy(a => 1)
            .Select(g => new
            {
                Total = g.Count(a => a.Status != AttendanceStatus.Holiday),
                Present = g.Count(a => a.Status == AttendanceStatus.Present
                                       || a.Status == AttendanceStatus.Late
                                       || a.Status == AttendanceStatus.EarlyLeave
                                       || a.Status == AttendanceStatus.Partial)
            })
            .FirstOrDefaultAsync(ct);

        return attendance is null or { Total: 0 }
            ? student
            : student with { AttendancePercentage = Math.Round(attendance.Present * 100m / attendance.Total, 1) };
    }

    public Task<CreatedPersonResult> CreateAsync(CreateStudentRequest request, CancellationToken ct = default)
        => _db.InTransactionAsync(token => CreateInternalAsync(request, token), ct);

    private async Task<CreatedPersonResult> CreateInternalAsync(CreateStudentRequest request, CancellationToken ct)
    {
        var code = string.IsNullOrWhiteSpace(request.StudentCode)
            ? await GenerateStudentCodeAsync(ct)
            : request.StudentCode.Trim();

        if (await _db.Students.AnyAsync(s => s.StudentCode == code, ct))
            throw DomainException.Conflict($"Student code '{code}' is already in use.");

        var userName = string.IsNullOrWhiteSpace(request.UserName)
            ? await GenerateUserNameAsync(request.FirstName, request.LastName, code, ct)
            : request.UserName.Trim();

        if (await _userManager.FindByNameAsync(userName) is not null)
            throw DomainException.Conflict($"The username '{userName}' is already taken.");

        var generatedPassword = string.IsNullOrWhiteSpace(request.Password);
        var password = generatedPassword ? GenerateTemporaryPassword() : request.Password!;

        var user = new ApplicationUser
        {
            UserName = userName,
            NormalizedUserName = userName.ToUpperInvariant(),
            Email = request.Email,
            NormalizedEmail = request.Email?.ToUpperInvariant(),
            EmailConfirmed = true,
            PhoneNumber = request.PhoneNumber,
            FirstName = request.FirstName.Trim(),
            LastName = request.LastName.Trim(),
            Gender = request.Gender,
            DateOfBirth = request.DateOfBirth,
            NationalId = request.NationalId,
            Address = request.Address,
            City = request.City,
            SchoolId = _currentUser.SchoolId,
            IsActive = request.Status == PersonStatus.Active,
            // A password an administrator typed or the system generated is not the student's
            // own; they must set one they alone know at first sign-in.
            MustChangePassword = true,
            CreatedAtUtc = _clock.UtcNow,
            CreatedByUserId = _currentUser.UserId,
            SecurityStamp = Guid.NewGuid().ToString()
        };

        var created = await _userManager.CreateAsync(user, password);
        if (!created.Succeeded)
            throw DomainException.Invalid(string.Join(" ", created.Errors.Select(e => e.Description)));

        await _userManager.AddToRoleAsync(user, Permissions.RoleNames.Student);

        var student = new Student
        {
            SchoolId = _currentUser.SchoolId,
            UserId = user.Id,
            StudentCode = code,
            AdmissionNumber = request.AdmissionNumber,
            AdmissionDate = request.AdmissionDate ?? _clock.SchoolToday,
            Status = request.Status,
            BloodGroup = request.BloodGroup,
            MedicalNotes = request.MedicalNotes,
            EmergencyContactName = request.EmergencyContactName,
            EmergencyContactPhone = request.EmergencyContactPhone,
            TransportRoute = request.TransportRoute,
            CurrentSectionId = request.SectionId
        };

        _db.Students.Add(student);
        await _db.SaveChangesAsync(ct);

        if (request.SectionId is { } section) await EnrollAsync(student.Id, section, request.RollNumber, ct);

        if (!string.IsNullOrWhiteSpace(request.RfidEpc))
            await AssignCardAsync(student.Id, request.RfidEpc, request.CardNumber, ct);

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Created student {Code} ({StudentId}) with account {UserName}",
            code, student.Id, userName);

        return new CreatedPersonResult
        {
            Id = student.Id,
            UserId = user.Id,
            Code = code,
            UserName = userName,
            TemporaryPassword = generatedPassword ? password : null
        };
    }

    public async Task UpdateAsync(int id, UpdateStudentRequest request, CancellationToken ct = default)
    {
        var student = await _db.Students.Include(s => s.User)
            .FirstOrDefaultAsync(s => s.Id == id, ct)
            ?? throw new KeyNotFoundException("That student does not exist.");

        var user = student.User!;
        user.FirstName = request.FirstName.Trim();
        user.LastName = request.LastName.Trim();
        user.Email = request.Email;
        user.NormalizedEmail = request.Email?.ToUpperInvariant();
        user.PhoneNumber = request.PhoneNumber;
        user.Gender = request.Gender;
        user.DateOfBirth = request.DateOfBirth;
        user.NationalId = request.NationalId;
        user.Address = request.Address;
        user.City = request.City;
        user.IsActive = request.Status == PersonStatus.Active;

        student.AdmissionNumber = request.AdmissionNumber;
        student.AdmissionDate = request.AdmissionDate;
        student.Status = request.Status;
        student.BloodGroup = request.BloodGroup;
        student.MedicalNotes = request.MedicalNotes;
        student.EmergencyContactName = request.EmergencyContactName;
        student.EmergencyContactPhone = request.EmergencyContactPhone;
        student.TransportRoute = request.TransportRoute;

        // Moving section means closing the old enrollment and opening a new one, not just
        // repointing a column - otherwise last term's attendance would appear under the new
        // class and the register history would be wrong.
        if (request.SectionId != student.CurrentSectionId)
        {
            var previous = await _db.Enrollments
                .Where(e => e.StudentId == id && e.Status == EnrollmentStatus.Active)
                .ToListAsync(ct);

            foreach (var enrollment in previous)
            {
                enrollment.Status = EnrollmentStatus.Transferred;
                enrollment.EndedOn = _clock.SchoolToday;
            }

            student.CurrentSectionId = request.SectionId;
            if (request.SectionId is { } newSection) await EnrollAsync(id, newSection, null, ct);
        }

        await _db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        var student = await _db.Students.Include(s => s.User)
            .FirstOrDefaultAsync(s => s.Id == id, ct)
            ?? throw new KeyNotFoundException("That student does not exist.");

        // Soft delete (handled by the interceptor) keeps attendance and movement history
        // intact. The account is deactivated and any card revoked so neither can be used.
        student.Status = PersonStatus.Inactive;
        if (student.User is not null) student.User.IsActive = false;

        var cards = await _db.RfidTags.Where(t => t.StudentId == id && t.Status == RfidTagStatus.Active)
            .ToListAsync(ct);

        foreach (var card in cards)
        {
            card.Status = RfidTagStatus.Revoked;
            card.RevokedAtUtc = _clock.UtcNow;
            card.RevokedByUserId = _currentUser.UserId;
            card.RevokedReason = "Student record removed";
        }

        _db.Students.Remove(student);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Student {StudentId} removed and {CardCount} card(s) revoked", id, cards.Count);
    }

    public async Task<IReadOnlyList<StudentListItem>> GetBySectionAsync(int sectionId, CancellationToken ct = default)
    {
        var result = await SearchAsync(
            new PersonQuery { SectionId = sectionId, PageSize = 200, SortBy = "name" }, ct);
        return result.Items;
    }

    // ------------------------------------------------------------------ helpers ----

    private async Task EnrollAsync(int studentId, int sectionId, string? rollNumber, CancellationToken ct)
    {
        var sessionId = await _db.AcademicSessions
            .Where(s => s.IsCurrent).Select(s => s.Id).FirstOrDefaultAsync(ct);

        if (sessionId == 0)
            throw DomainException.Invalid("No academic session is marked as current. Create one before enrolling students.");

        var existing = await _db.Enrollments.FirstOrDefaultAsync(
            e => e.StudentId == studentId && e.SectionId == sectionId && e.AcademicSessionId == sessionId, ct);

        if (existing is not null)
        {
            // Re-enrolling into a section the student previously left reuses the row rather
            // than colliding with the unique index.
            existing.Status = EnrollmentStatus.Active;
            existing.EndedOn = null;
            if (rollNumber is not null) existing.RollNumber = rollNumber;
            return;
        }

        _db.Enrollments.Add(new Enrollment
        {
            SchoolId = _currentUser.SchoolId,
            StudentId = studentId,
            SectionId = sectionId,
            AcademicSessionId = sessionId,
            RollNumber = rollNumber,
            EnrolledOn = _clock.SchoolToday,
            Status = EnrollmentStatus.Active
        });
    }

    private async Task AssignCardAsync(int studentId, string rawEpc, string? cardNumber, CancellationToken ct)
    {
        var epc = Rfid.RfidIngestionService.NormaliseEpc(rawEpc)
                  ?? throw DomainException.Invalid("That RFID card number is not valid hexadecimal.");

        var existing = await _db.RfidTags.FirstOrDefaultAsync(t => t.Epc == epc, ct);
        if (existing is not null && existing.StudentId is not null && existing.StudentId != studentId)
            throw DomainException.Conflict("That card is already assigned to another person.");

        if (existing is null)
        {
            existing = new RfidTag { SchoolId = _currentUser.SchoolId, Epc = epc };
            _db.RfidTags.Add(existing);
        }

        existing.StudentId = studentId;
        existing.CardNumber = cardNumber;
        existing.Status = RfidTagStatus.Active;
        existing.IssuedAtUtc = _clock.UtcNow;
        existing.IssuedByUserId = _currentUser.UserId;
    }

    /// <summary>Next code in the configured series, e.g. STU-000124.</summary>
    private async Task<string> GenerateStudentCodeAsync(CancellationToken ct)
    {
        var prefix = await _settings.GetAsync(SettingKeys.StudentCodePrefix, "STU-", ct);

        // Ignores the soft-delete filter: a removed student's code must not be handed out
        // again, or their historical records would appear to belong to someone new.
        var highest = await _db.QueryIgnoringFilters<Student>()
            .Where(s => s.StudentCode.StartsWith(prefix))
            .Select(s => s.StudentCode)
            .ToListAsync(ct);

        var next = highest
            .Select(c => int.TryParse(c[prefix.Length..], out var n) ? n : 0)
            .DefaultIfEmpty(0)
            .Max() + 1;

        return $"{prefix}{next:D6}";
    }

    private async Task<string> GenerateUserNameAsync(string first, string last, string code, CancellationToken ct)
    {
        var baseName = $"{Slug(first)}.{Slug(last)}";
        if (string.IsNullOrWhiteSpace(baseName.Replace(".", ""))) baseName = code.ToLowerInvariant();

        var candidate = baseName;
        var suffix = 1;

        // Names collide often in a school; fall back to the student code, which cannot.
        while (await _db.Users.IgnoreQueryFilters().AnyAsync(u => u.NormalizedUserName == candidate.ToUpperInvariant(), ct))
        {
            candidate = suffix == 1 ? $"{baseName}.{code.ToLowerInvariant()}" : $"{baseName}{suffix}";
            suffix++;
            if (suffix > 50) return code.ToLowerInvariant();
        }

        return candidate;
    }

    private static string Slug(string value) =>
        new(value.Trim().ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

    /// <summary>
    /// A readable temporary password that still satisfies the Identity policy. Shown once to
    /// the administrator, never stored in clear, and must be changed at first sign-in.
    /// </summary>
    private static string GenerateTemporaryPassword()
    {
        const string upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";   // no I or O
        const string lower = "abcdefghijkmnpqrstuvwxyz";   // no l
        const string digits = "23456789";                  // no 0 or 1

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

    private static IQueryable<Student> ApplySort(IQueryable<Student> query, string? sortBy, bool descending) =>
        (sortBy?.ToLowerInvariant()) switch
        {
            "code" => descending ? query.OrderByDescending(s => s.StudentCode) : query.OrderBy(s => s.StudentCode),
            "section" => descending
                ? query.OrderByDescending(s => s.CurrentSection!.DisplayName)
                : query.OrderBy(s => s.CurrentSection!.DisplayName),
            "status" => descending ? query.OrderByDescending(s => s.Status) : query.OrderBy(s => s.Status),
            "created" => descending ? query.OrderByDescending(s => s.CreatedAtUtc) : query.OrderBy(s => s.CreatedAtUtc),
            _ => descending
                ? query.OrderByDescending(s => s.User!.LastName).ThenByDescending(s => s.User!.FirstName)
                : query.OrderBy(s => s.User!.LastName).ThenBy(s => s.User!.FirstName)
        };
}
