using CampusTrack.Application.Authorization;
using CampusTrack.Application.Common;
using CampusTrack.Application.Common.Interfaces;
using CampusTrack.Application.Common.Models;
using CampusTrack.Application.People;
using CampusTrack.Domain.Assessment;
using CampusTrack.Domain.Common;
using CampusTrack.Domain.Enums;
using CampusTrack.Domain.People;
using CampusTrack.Infrastructure.Identity;
using CampusTrack.Infrastructure.People;
using CampusTrack.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CampusTrack.Api.Controllers;

/// <summary>Non-teaching staff: office, security, maintenance and support roles.</summary>
public class StaffController : ApiControllerBase
{
    private readonly CampusTrackDbContext _db;
    private readonly IPersonAccountFactory _accounts;
    private readonly ICurrentUser _currentUser;

    public StaffController(
        CampusTrackDbContext db, IPersonAccountFactory accounts, ICurrentUser currentUser)
    {
        _db = db;
        _accounts = accounts;
        _currentUser = currentUser;
    }

    [HttpGet]
    [HasPermission(Permissions.Staff.View)]
    public async Task<ActionResult<PagedResult<object>>> Search(
        [FromQuery] PersonQuery query, CancellationToken ct)
    {
        var q = _db.StaffMembers.AsNoTracking().AsQueryable();

        if (query.Status is { } status) q = q.Where(s => s.Status == status);

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            q = q.Where(s => s.User!.FirstName.Contains(term)
                             || s.User.LastName.Contains(term)
                             || s.StaffCode.Contains(term)
                             || s.JobTitle.Contains(term));
        }

        var projected = q
            .OrderBy(s => s.User!.LastName)
            .Select(s => (object)new
            {
                s.Id,
                s.StaffCode,
                fullName = s.User!.FirstName + " " + s.User.LastName,
                email = s.User.Email,
                phoneNumber = s.User.PhoneNumber,
                s.JobTitle,
                s.Department,
                s.HireDate,
                s.Status,
                userId = s.UserId,
            });

        return Paged(await projected.ToPagedResultAsync(query.Page, query.PageSize, ct));
    }

    [HttpGet("{id:int}")]
    [HasPermission(Permissions.Staff.View)]
    public async Task<ActionResult<object>> Get(int id, CancellationToken ct)
    {
        var staff = await _db.StaffMembers.AsNoTracking()
            .Where(s => s.Id == id)
            .Select(s => new
            {
                s.Id, s.StaffCode, s.JobTitle, s.Department, s.HireDate, s.Status,
                firstName = s.User!.FirstName,
                lastName = s.User.LastName,
                email = s.User.Email,
                phoneNumber = s.User.PhoneNumber,
                fullName = s.User.FirstName + " " + s.User.LastName,
            })
            .FirstOrDefaultAsync(ct);

        return Ok(Found(staff, "staff member"));
    }

    [HttpPost]
    [HasPermission(Permissions.Staff.Create)]
    public async Task<ActionResult<CreatedPersonResult>> Create(CreateStaffRequest request, CancellationToken ct)
    {
        var result = await _db.InTransactionAsync(token => CreateStaffAsync(request, token), ct);
        return CreatedAtAction(nameof(Get), new { id = result.Id }, result);
    }

    private async Task<CreatedPersonResult> CreateStaffAsync(CreateStaffRequest request, CancellationToken ct)
    {
        var code = string.IsNullOrWhiteSpace(request.StaffCode)
            ? await _accounts.NextCodeAsync(SettingKeys.StaffCodePrefix, "STF-",
                candidate => _db.QueryIgnoringFilters<StaffMember>()
                    .AnyAsync(s => s.StaffCode == candidate, ct), ct)
            : request.StaffCode.Trim();

        if (await _db.StaffMembers.AnyAsync(s => s.StaffCode == code, ct))
            throw DomainException.Conflict($"Staff code '{code}' is already in use.");

        var (user, temporaryPassword) = await _accounts.CreateAsync(new NewAccount(
            request.FirstName, request.LastName, request.UserName, request.Password,
            request.Email, request.PhoneNumber, Gender.Unspecified, null,
            request.Address, null, null, request.Status == PersonStatus.Active, code),
            Permissions.RoleNames.Staff, ct);

        var staff = new StaffMember
        {
            SchoolId = _currentUser.SchoolId,
            UserId = user.Id,
            StaffCode = code,
            JobTitle = request.JobTitle.Trim(),
            Department = request.Department,
            HireDate = request.HireDate,
            Status = request.Status,
        };

        _db.StaffMembers.Add(staff);
        await _db.SaveChangesAsync(ct);

        return new CreatedPersonResult
        {
            Id = staff.Id,
            UserId = user.Id,
            Code = code,
            UserName = user.UserName!,
            TemporaryPassword = temporaryPassword,
        };
    }

    [HttpPut("{id:int}")]
    [HasPermission(Permissions.Staff.Edit)]
    public async Task<IActionResult> Update(int id, CreateStaffRequest request, CancellationToken ct)
    {
        var staff = Found(await _db.StaffMembers.Include(s => s.User)
            .FirstOrDefaultAsync(s => s.Id == id, ct), "staff member");

        var user = staff.User!;
        user.FirstName = request.FirstName.Trim();
        user.LastName = request.LastName.Trim();
        user.Email = request.Email;
        user.NormalizedEmail = request.Email?.ToUpperInvariant();
        user.PhoneNumber = request.PhoneNumber;
        user.Address = request.Address;
        user.IsActive = request.Status == PersonStatus.Active;

        staff.JobTitle = request.JobTitle.Trim();
        staff.Department = request.Department;
        staff.HireDate = request.HireDate;
        staff.Status = request.Status;

        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    [HasPermission(Permissions.Staff.Delete)]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var staff = Found(await _db.StaffMembers.Include(s => s.User)
            .FirstOrDefaultAsync(s => s.Id == id, ct), "staff member");

        staff.Status = PersonStatus.Inactive;
        if (staff.User is not null) staff.User.IsActive = false;

        _db.StaffMembers.Remove(staff);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }
}

/// <summary>Examination series, their papers and results.</summary>
public class ExamsController : ApiControllerBase
{
    private readonly CampusTrackDbContext _db;
    private readonly INotificationService _notifications;
    private readonly IDateTimeProvider _clock;

    public ExamsController(
        CampusTrackDbContext db, INotificationService notifications, IDateTimeProvider clock)
    {
        _db = db;
        _notifications = notifications;
        _clock = clock;
    }

    [HttpGet]
    [HasPermission(Permissions.Exams.View)]
    public async Task<ActionResult<IReadOnlyList<object>>> Get(CancellationToken ct) =>
        Ok(await _db.Exams.AsNoTracking()
            .OrderByDescending(e => e.StartDate)
            .Select(e => (object)new
            {
                e.Id, e.Name, e.Description, e.Status, e.StartDate, e.EndDate,
                e.Weight, e.ResultsPublished, e.ResultsPublishedAtUtc,
                sessionName = e.AcademicSession!.Name,
                paperCount = e.Schedules.Count,
            })
            .ToListAsync(ct));

    [HttpGet("{id:int}")]
    [HasPermission(Permissions.Exams.View)]
    public async Task<ActionResult<object>> GetOne(int id, CancellationToken ct)
    {
        var exam = await _db.Exams.AsNoTracking()
            .Where(e => e.Id == id)
            .Select(e => new
            {
                e.Id, e.Name, e.Description, e.Status, e.StartDate, e.EndDate,
                e.Weight, e.ResultsPublished,
                papers = e.Schedules.OrderBy(s => s.Date).ThenBy(s => s.StartTime).Select(s => new
                {
                    s.Id, s.Date, s.StartTime, s.EndTime, s.MaxScore, s.PassScore,
                    subjectName = s.Subject!.Name,
                    sectionName = s.Section!.DisplayName,
                    classroomName = s.Classroom!.Name,
                    resultCount = s.Results.Count,
                }),
            })
            .FirstOrDefaultAsync(ct);

        return Ok(Found(exam, "exam"));
    }

    [HttpPost]
    [HasPermission(Permissions.Exams.Manage)]
    public async Task<ActionResult<object>> Create(ExamRequest request, CancellationToken ct)
    {
        if (request.EndDate < request.StartDate)
            throw DomainException.Invalid("An exam cannot end before it starts.");

        var sessionId = await _db.AcademicSessions
            .Where(s => s.IsCurrent).Select(s => s.Id).FirstOrDefaultAsync(ct);

        if (sessionId == 0) throw DomainException.Invalid("No academic session is marked as current.");

        var exam = new Exam
        {
            AcademicSessionId = sessionId,
            Name = request.Name.Trim(),
            Description = request.Description,
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            Weight = request.Weight,
            Status = ExamStatus.Planned,
        };

        _db.Exams.Add(exam);
        await _db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(GetOne), new { id = exam.Id }, new { exam.Id, exam.Name });
    }

    [HttpPut("{id:int}")]
    [HasPermission(Permissions.Exams.Manage)]
    public async Task<IActionResult> Update(int id, ExamRequest request, CancellationToken ct)
    {
        var exam = Found(await _db.Exams.FirstOrDefaultAsync(e => e.Id == id, ct), "exam");

        exam.Name = request.Name.Trim();
        exam.Description = request.Description;
        exam.StartDate = request.StartDate;
        exam.EndDate = request.EndDate;
        exam.Weight = request.Weight;

        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    [HasPermission(Permissions.Exams.Manage)]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var exam = Found(await _db.Exams.FirstOrDefaultAsync(e => e.Id == id, ct), "exam");

        // Results are a permanent academic record; removing the exam under them would
        // orphan marks that appear on report cards.
        var resultCount = await _db.ExamResults.CountAsync(r => r.ExamSchedule!.ExamId == id, ct);
        if (resultCount > 0)
            throw DomainException.Conflict(
                $"{resultCount} result(s) have been recorded for this exam, so it cannot be removed.");

        _db.Exams.Remove(exam);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>Adds a paper: one subject, for one section, at a time and place.</summary>
    [HttpPost("{id:int}/papers")]
    [HasPermission(Permissions.Exams.Manage)]
    public async Task<ActionResult<object>> AddPaper(int id, ExamPaperRequest request, CancellationToken ct)
    {
        Found(await _db.Exams.FirstOrDefaultAsync(e => e.Id == id, ct), "exam");

        if (request.EndTime <= request.StartTime)
            throw DomainException.Invalid("A paper must finish after it starts.");

        var duplicate = await _db.ExamSchedules.AnyAsync(
            s => s.ExamId == id && s.SectionId == request.SectionId && s.SubjectId == request.SubjectId, ct);

        if (duplicate)
            throw DomainException.Conflict("That section already has a paper for this subject in this exam.");

        var paper = new ExamSchedule
        {
            ExamId = id,
            SubjectId = request.SubjectId,
            SectionId = request.SectionId,
            ClassroomId = request.ClassroomId,
            Date = request.Date,
            StartTime = request.StartTime,
            EndTime = request.EndTime,
            MaxScore = request.MaxScore,
            PassScore = request.PassScore,
            InvigilatorTeacherId = request.InvigilatorTeacherId,
        };

        _db.ExamSchedules.Add(paper);
        await _db.SaveChangesAsync(ct);

        return Ok(new { paper.Id });
    }

    /// <summary>The mark sheet for one paper, listing every student whether marked or not.</summary>
    [HttpGet("papers/{paperId:int}/results")]
    [HasPermission(Permissions.Exams.View)]
    public async Task<ActionResult<IReadOnlyList<object>>> GetResults(int paperId, CancellationToken ct)
    {
        var paper = Found(await _db.ExamSchedules.AsNoTracking()
            .Where(s => s.Id == paperId)
            .Select(s => new { s.Id, s.SectionId, s.MaxScore })
            .FirstOrDefaultAsync(ct), "paper");

        var students = await _db.Students.AsNoTracking()
            .Where(s => s.CurrentSectionId == paper.SectionId && s.Status == PersonStatus.Active)
            .Select(s => new { s.Id, name = s.User!.FirstName + " " + s.User.LastName, s.StudentCode })
            .OrderBy(s => s.name)
            .ToListAsync(ct);

        var results = await _db.ExamResults.AsNoTracking()
            .Where(r => r.ExamScheduleId == paperId)
            .ToDictionaryAsync(r => r.StudentId, ct);

        return Ok(students.Select(s =>
        {
            results.TryGetValue(s.Id, out var result);
            return (object)new
            {
                studentId = s.Id,
                studentName = s.name,
                studentCode = s.StudentCode,
                score = result?.Score,
                isAbsent = result?.IsAbsent ?? false,
                remarks = result?.Remarks,
                maxScore = paper.MaxScore,
            };
        }).ToList());
    }

    /// <summary>Enters or updates marks for a paper in one call.</summary>
    [HttpPost("papers/{paperId:int}/results")]
    [HasPermission(Permissions.Exams.EnterResults)]
    public async Task<ActionResult<object>> SaveResults(
        int paperId, [FromBody] List<ExamResultEntry> entries, CancellationToken ct)
    {
        var paper = Found(await _db.ExamSchedules
            .FirstOrDefaultAsync(s => s.Id == paperId, ct), "paper");

        var existing = await _db.ExamResults
            .Where(r => r.ExamScheduleId == paperId)
            .ToDictionaryAsync(r => r.StudentId, ct);

        var saved = 0;

        foreach (var entry in entries)
        {
            if (entry.Score is { } score && (score < 0 || score > paper.MaxScore))
                throw DomainException.Invalid(
                    $"A score of {score} is outside the range 0 to {paper.MaxScore}.");

            if (!existing.TryGetValue(entry.StudentId, out var result))
            {
                result = new ExamResult { ExamScheduleId = paperId, StudentId = entry.StudentId };
                _db.ExamResults.Add(result);
            }

            result.Score = entry.IsAbsent ? null : entry.Score;
            result.IsAbsent = entry.IsAbsent;
            result.Remarks = entry.Remarks;
            result.EnteredByUserId = CurrentUser.UserId;
            result.EnteredAtUtc = _clock.UtcNow;
            saved++;
        }

        await _db.SaveChangesAsync(ct);
        return Ok(new { saved });
    }

    /// <summary>
    /// Releases results to students and parents, and notifies them. Kept separate from
    /// entering marks so a teacher can work through a paper without partial results leaking.
    /// </summary>
    [HttpPost("{id:int}/publish-results")]
    [HasPermission(Permissions.Exams.PublishResults)]
    public async Task<ActionResult<object>> PublishResults(int id, CancellationToken ct)
    {
        var exam = Found(await _db.Exams.FirstOrDefaultAsync(e => e.Id == id, ct), "exam");

        exam.ResultsPublished = true;
        exam.ResultsPublishedAtUtc = _clock.UtcNow;
        exam.Status = ExamStatus.ResultsPublished;

        var students = await _db.ExamResults.AsNoTracking()
            .Where(r => r.ExamSchedule!.ExamId == id)
            .Select(r => new { r.StudentId, userId = r.Student!.UserId })
            .Distinct()
            .ToListAsync(ct);

        await _db.SaveChangesAsync(ct);

        await _notifications.NotifyManyAsync(students.Select(s => new NotificationRequest
        {
            UserId = s.userId,
            Category = NotificationCategory.Exam,
            Title = $"{exam.Name} results are available",
            Body = $"Results for {exam.Name} have been published.",
            StudentId = s.StudentId,
            RelatedEntityType = nameof(Exam),
            RelatedEntityId = exam.Id,
        }), ct);

        foreach (var student in students)
        {
            await _notifications.NotifyGuardiansOfStudentAsync(student.StudentId, new NotificationRequest
            {
                Category = NotificationCategory.Exam,
                Title = $"{exam.Name} results are available",
                Body = $"Results for {exam.Name} have been published.",
                StudentId = student.StudentId,
            }, ct);
        }

        return Ok(new { published = students.Count });
    }
}

public record CreateStaffRequest
{
    public required string FirstName { get; init; }
    public required string LastName { get; init; }
    public required string JobTitle { get; init; }
    public string? StaffCode { get; init; }
    public string? Department { get; init; }
    public string? Email { get; init; }
    public string? PhoneNumber { get; init; }
    public string? Address { get; init; }
    public DateOnly? HireDate { get; init; }
    public PersonStatus Status { get; init; } = PersonStatus.Active;
    public string? UserName { get; init; }
    public string? Password { get; init; }
}

public record ExamRequest
{
    public required string Name { get; init; }
    public string? Description { get; init; }
    public DateOnly StartDate { get; init; }
    public DateOnly EndDate { get; init; }
    public decimal Weight { get; init; } = 1m;
}

public record ExamPaperRequest
{
    public required int SubjectId { get; init; }
    public required int SectionId { get; init; }
    public int? ClassroomId { get; init; }
    public DateOnly Date { get; init; }
    public TimeOnly StartTime { get; init; }
    public TimeOnly EndTime { get; init; }
    public decimal MaxScore { get; init; } = 100m;
    public decimal PassScore { get; init; } = 40m;
    public int? InvigilatorTeacherId { get; init; }
}

public record ExamResultEntry
{
    public int StudentId { get; init; }
    public decimal? Score { get; init; }
    public bool IsAbsent { get; init; }
    public string? Remarks { get; init; }
}
