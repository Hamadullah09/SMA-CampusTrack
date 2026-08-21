using CampusTrack.Application.Authorization;
using CampusTrack.Application.Common.Interfaces;
using CampusTrack.Application.Common.Models;
using CampusTrack.Domain.Assessment;
using CampusTrack.Domain.Common;
using CampusTrack.Domain.Enums;
using CampusTrack.Infrastructure.Identity;
using CampusTrack.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CampusTrack.Api.Controllers;

/// <summary>Assignments, their attachments, submissions and marking.</summary>
public class AssignmentsController : ApiControllerBase
{
    private readonly CampusTrackDbContext _db;
    private readonly IFileStorage _files;
    private readonly INotificationService _notifications;
    private readonly IDateTimeProvider _clock;

    public AssignmentsController(
        CampusTrackDbContext db, IFileStorage files, INotificationService notifications, IDateTimeProvider clock)
    {
        _db = db;
        _files = files;
        _notifications = notifications;
        _clock = clock;
    }

    [HttpGet]
    [HasPermission(Permissions.Assignments.View)]
    public async Task<ActionResult<PagedResult<object>>> Search(
        [FromQuery] PagedQuery paging, [FromQuery] int? sectionId, [FromQuery] int? subjectId,
        [FromQuery] AssignmentStatus? status, CancellationToken ct)
    {
        var q = _db.Assignments.AsNoTracking().AsQueryable();

        // A teacher sees only their own work unless they can view everyone's.
        if (CurrentUser.TeacherId is { } teacherId && !CurrentUser.HasPermission(Permissions.Reports.ViewAcademic))
            q = q.Where(a => a.TeacherId == teacherId);

        if (sectionId is { } sid) q = q.Where(a => a.SectionId == sid);
        if (subjectId is { } subId) q = q.Where(a => a.SubjectId == subId);
        if (status is { } st) q = q.Where(a => a.Status == st);

        if (!string.IsNullOrWhiteSpace(paging.Search))
            q = q.Where(a => a.Title.Contains(paging.Search.Trim()));

        var projected = q.OrderByDescending(a => a.DueAtUtc).Select(a => (object)new
        {
            a.Id, a.Title, a.Status, a.DueAtUtc, a.PublishedAtUtc, a.MaxScore, a.Weight, a.Category,
            a.AllowLateSubmission, a.ShareToken,
            a.SubjectId, subjectName = a.Subject!.Name, subjectColour = a.Subject.ColourHex,
            a.SectionId, sectionName = a.Section!.DisplayName,
            a.TeacherId, teacherName = a.Teacher!.User!.FirstName + " " + a.Teacher.User.LastName,
            attachmentCount = a.Attachments.Count,
            submissionCount = a.Submissions.Count(s => s.Status != SubmissionStatus.NotSubmitted),
            gradedCount = a.Submissions.Count(s => s.Status == SubmissionStatus.Graded),
            expectedCount = _db.Students.Count(s => s.CurrentSectionId == a.SectionId),
            isOverdue = a.DueAtUtc < _clock.UtcNow
        });

        return Paged(await projected.ToPagedResultAsync(paging.Page, paging.PageSize, ct));
    }

    [HttpGet("{id:int}")]
    [HasPermission(Permissions.Assignments.View)]
    public async Task<ActionResult<object>> Get(int id, CancellationToken ct)
    {
        var assignment = await _db.Assignments.AsNoTracking()
            .Where(a => a.Id == id)
            .Select(a => new
            {
                a.Id, a.Title, a.Instructions, a.Status, a.DueAtUtc, a.PublishedAtUtc,
                a.MaxScore, a.Weight, a.Category, a.AllowLateSubmission, a.LateCutoffUtc, a.ShareToken,
                a.SubjectId, subjectName = a.Subject!.Name,
                a.SectionId, sectionName = a.Section!.DisplayName,
                a.TeacherId, teacherName = a.Teacher!.User!.FirstName + " " + a.Teacher.User.LastName,
                attachments = a.Attachments.Select(f => new { f.Id, f.FileName, f.ContentType, f.SizeBytes }),
                targetedStudentIds = a.Targets.Select(t => t.StudentId)
            })
            .FirstOrDefaultAsync(ct);

        return Ok(Found(assignment, "assignment"));
    }

    [HttpPost]
    [HasPermission(Permissions.Assignments.Create)]
    public async Task<ActionResult<object>> Create(AssignmentRequest request, CancellationToken ct)
    {
        var teacherId = CurrentUser.TeacherId
                        ?? request.TeacherId
                        ?? throw DomainException.Invalid("A teacher must be specified.");

        await EnsureTeachesSectionAsync(teacherId, request.SectionId, request.SubjectId, ct);

        var sessionId = await _db.AcademicSessions.Where(s => s.IsCurrent).Select(s => s.Id).FirstOrDefaultAsync(ct);
        if (sessionId == 0) throw DomainException.Invalid("No academic session is marked as current.");

        var assignment = new Assignment
        {
            TeacherId = teacherId,
            SubjectId = request.SubjectId,
            SectionId = request.SectionId,
            AcademicSessionId = sessionId,
            Title = request.Title.Trim(),
            Instructions = request.Instructions,
            DueAtUtc = request.DueAtUtc,
            AllowLateSubmission = request.AllowLateSubmission,
            LateCutoffUtc = request.LateCutoffUtc,
            MaxScore = request.MaxScore,
            Weight = request.Weight,
            Category = request.Category,
            Status = request.PublishNow ? AssignmentStatus.Published : AssignmentStatus.Draft,
            PublishedAtUtc = request.PublishNow ? _clock.UtcNow : null
        };

        foreach (var studentId in request.TargetStudentIds ?? [])
            assignment.Targets.Add(new AssignmentTarget { StudentId = studentId });

        _db.Assignments.Add(assignment);
        await _db.SaveChangesAsync(ct);

        if (request.PublishNow) await NotifyStudentsAsync(assignment, ct);

        return CreatedAtAction(nameof(Get), new { id = assignment.Id }, new { assignment.Id, assignment.Title });
    }

    [HttpPut("{id:int}")]
    [HasPermission(Permissions.Assignments.Edit)]
    public async Task<IActionResult> Update(int id, AssignmentRequest request, CancellationToken ct)
    {
        var assignment = Found(await _db.Assignments.FirstOrDefaultAsync(a => a.Id == id, ct), "assignment");
        EnsureOwnsAssignment(assignment.TeacherId);

        assignment.Title = request.Title.Trim();
        assignment.Instructions = request.Instructions;
        assignment.DueAtUtc = request.DueAtUtc;
        assignment.AllowLateSubmission = request.AllowLateSubmission;
        assignment.LateCutoffUtc = request.LateCutoffUtc;
        assignment.MaxScore = request.MaxScore;
        assignment.Weight = request.Weight;
        assignment.Category = request.Category;

        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>Publishes a draft and notifies the students it applies to.</summary>
    [HttpPost("{id:int}/publish")]
    [HasPermission(Permissions.Assignments.Edit)]
    public async Task<IActionResult> Publish(int id, CancellationToken ct)
    {
        var assignment = Found(
            await _db.Assignments.Include(a => a.Targets).FirstOrDefaultAsync(a => a.Id == id, ct), "assignment");

        EnsureOwnsAssignment(assignment.TeacherId);

        if (assignment.Status == AssignmentStatus.Published) return NoContent();

        assignment.Status = AssignmentStatus.Published;
        assignment.PublishedAtUtc = _clock.UtcNow;
        await _db.SaveChangesAsync(ct);

        await NotifyStudentsAsync(assignment, ct);
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    [HasPermission(Permissions.Assignments.Delete)]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var assignment = Found(await _db.Assignments.FirstOrDefaultAsync(a => a.Id == id, ct), "assignment");
        EnsureOwnsAssignment(assignment.TeacherId);

        _db.Assignments.Remove(assignment);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPost("{id:int}/attachments")]
    [HasPermission(Permissions.Assignments.Edit)]
    [RequestSizeLimit(30 * 1024 * 1024)]
    public async Task<ActionResult<object>> UploadAttachment(int id, IFormFile file, CancellationToken ct)
    {
        var assignment = Found(await _db.Assignments.FirstOrDefaultAsync(a => a.Id == id, ct), "assignment");
        EnsureOwnsAssignment(assignment.TeacherId);

        if (file is null || file.Length == 0) throw DomainException.Invalid("No file was uploaded.");

        await using var stream = file.OpenReadStream();
        var stored = await _files.SaveAsync(stream, file.FileName, $"assignments/{id}", ct);

        var attachment = new AssignmentAttachment
        {
            AssignmentId = id,
            FileName = stored.FileName,
            StoredPath = stored.StoredPath,
            ContentType = stored.ContentType,
            SizeBytes = stored.SizeBytes,
            UploadedAtUtc = _clock.UtcNow,
            UploadedByUserId = CurrentUser.UserId
        };

        _db.AssignmentAttachments.Add(attachment);
        await _db.SaveChangesAsync(ct);

        return Ok(new { attachment.Id, attachment.FileName, attachment.SizeBytes });
    }

    [HttpGet("attachments/{attachmentId:int}/download")]
    [HasPermission(Permissions.Assignments.View)]
    public async Task<IActionResult> DownloadAttachment(int attachmentId, CancellationToken ct)
    {
        var attachment = Found(await _db.AssignmentAttachments.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == attachmentId, ct), "attachment");

        var stream = await _files.OpenAsync(attachment.StoredPath, ct);
        if (stream is null) return NotFound(new { message = "That file is no longer available." });

        return File(stream, attachment.ContentType ?? "application/octet-stream", attachment.FileName);
    }

    // ------------------------------------------------------------- submissions ----

    [HttpGet("{id:int}/submissions")]
    [HasPermission(Permissions.Assignments.Grade)]
    public async Task<ActionResult<IReadOnlyList<object>>> GetSubmissions(int id, CancellationToken ct)
    {
        var assignment = Found(await _db.Assignments.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == id, ct), "assignment");

        // Every expected student appears, submitted or not - the missing ones are the point.
        var students = await _db.Students.AsNoTracking()
            .Where(s => s.CurrentSectionId == assignment.SectionId && s.Status == PersonStatus.Active)
            .Select(s => new { s.Id, Name = s.User!.FirstName + " " + s.User.LastName, s.StudentCode })
            .ToListAsync(ct);

        var submissions = await _db.AssignmentSubmissions.AsNoTracking()
            .Where(s => s.AssignmentId == id)
            .Select(s => new
            {
                s.Id, s.StudentId, s.Status, s.SubmittedAtUtc, s.Score, s.Feedback, s.IsLate,
                s.AttemptNumber, s.TextAnswer,
                files = s.Files.Select(f => new { f.Id, f.FileName, f.SizeBytes, f.ContentType })
            })
            .ToListAsync(ct);

        var byStudent = submissions.ToDictionary(s => s.StudentId);

        return Ok(students.Select(s =>
        {
            byStudent.TryGetValue(s.Id, out var submission);
            return (object)new
            {
                studentId = s.Id,
                studentName = s.Name,
                studentCode = s.StudentCode,
                submissionId = submission?.Id,
                status = submission?.Status ?? SubmissionStatus.NotSubmitted,
                submission?.SubmittedAtUtc,
                submission?.Score,
                submission?.Feedback,
                isLate = submission?.IsLate ?? false,
                textAnswer = submission?.TextAnswer,
                files = submission?.files ?? []
            };
        }).ToList());
    }

    /// <summary>Student submits their work. Late submissions are accepted but flagged.</summary>
    [HttpPost("{id:int}/submit")]
    [HasPermission(Permissions.Assignments.Submit)]
    public async Task<ActionResult<object>> Submit(
        int id, [FromForm] string? textAnswer, IFormFile? file, CancellationToken ct)
    {
        var studentId = CurrentUser.StudentId
                        ?? throw DomainException.NotAllowed("Only students can submit work.");

        var assignment = Found(await _db.Assignments.FirstOrDefaultAsync(a => a.Id == id, ct), "assignment");

        if (assignment.Status != AssignmentStatus.Published)
            throw DomainException.Invalid("This assignment is not open for submission.");

        var now = _clock.UtcNow;
        var isLate = now > assignment.DueAtUtc;

        if (isLate && !assignment.AllowLateSubmission)
            throw DomainException.Invalid("The deadline for this assignment has passed.");

        if (assignment.LateCutoffUtc is { } cutoff && now > cutoff)
            throw DomainException.Invalid("This assignment is closed and no longer accepts submissions.");

        var submission = await _db.AssignmentSubmissions
            .Include(s => s.Files)
            .FirstOrDefaultAsync(s => s.AssignmentId == id && s.StudentId == studentId, ct);

        if (submission is null)
        {
            submission = new AssignmentSubmission { AssignmentId = id, StudentId = studentId };
            _db.AssignmentSubmissions.Add(submission);
        }
        else if (submission.Status is SubmissionStatus.Submitted or SubmissionStatus.Graded)
        {
            // Resubmission keeps the same row but records that it happened, so a teacher can
            // see the work changed after they marked it.
            submission.AttemptNumber++;
            submission.Status = SubmissionStatus.Resubmitted;
        }

        submission.TextAnswer = textAnswer;
        submission.SubmittedAtUtc = now;
        submission.IsLate = isLate;
        if (submission.Status is not SubmissionStatus.Resubmitted)
            submission.Status = isLate ? SubmissionStatus.Late : SubmissionStatus.Submitted;

        await _db.SaveChangesAsync(ct);

        if (file is { Length: > 0 })
        {
            await using var stream = file.OpenReadStream();
            var stored = await _files.SaveAsync(stream, file.FileName, $"submissions/{id}", ct);

            _db.SubmissionFiles.Add(new SubmissionFile
            {
                AssignmentSubmissionId = submission.Id,
                FileName = stored.FileName,
                StoredPath = stored.StoredPath,
                ContentType = stored.ContentType,
                SizeBytes = stored.SizeBytes,
                UploadedAtUtc = now
            });

            await _db.SaveChangesAsync(ct);
        }

        return Ok(new { submission.Id, submission.Status, submission.IsLate, submission.AttemptNumber });
    }

    /// <summary>Marks a submission and records the grade in the shared gradebook.</summary>
    [HttpPost("submissions/{submissionId:int}/grade")]
    [HasPermission(Permissions.Assignments.Grade)]
    public async Task<IActionResult> Grade(int submissionId, GradeSubmissionRequest request, CancellationToken ct)
    {
        var submission = Found(await _db.AssignmentSubmissions
            .Include(s => s.Assignment)
            .FirstOrDefaultAsync(s => s.Id == submissionId, ct), "submission");

        var assignment = submission.Assignment!;
        EnsureOwnsAssignment(assignment.TeacherId);

        if (request.Score < 0 || request.Score > assignment.MaxScore)
            throw DomainException.Invalid($"The score must be between 0 and {assignment.MaxScore}.");

        submission.Score = request.Score;
        submission.Feedback = request.Feedback;
        submission.Status = SubmissionStatus.Graded;
        submission.GradedByUserId = CurrentUser.UserId;
        submission.GradedAtUtc = _clock.UtcNow;

        await UpsertGradeRecordAsync(
            submission.StudentId, assignment.SubjectId, assignment.SectionId, assignment.AcademicSessionId,
            GradeCategory.Assignment, assignment.Title, request.Score, assignment.MaxScore, assignment.Weight,
            assignmentId: assignment.Id, ct: ct);

        await _db.SaveChangesAsync(ct);

        var studentUserId = await _db.Students.Where(s => s.Id == submission.StudentId)
            .Select(s => s.UserId).FirstOrDefaultAsync(ct);

        if (studentUserId != 0)
        {
            await _notifications.NotifyAsync(new NotificationRequest
            {
                UserId = studentUserId,
                Category = NotificationCategory.Grade,
                Title = $"'{assignment.Title}' has been marked",
                Body = $"You scored {request.Score} out of {assignment.MaxScore}.",
                StudentId = submission.StudentId,
                RelatedEntityType = nameof(Assignment),
                RelatedEntityId = assignment.Id
            }, ct);
        }

        return NoContent();
    }

    // ---------------------------------------------------------------- helpers ----

    /// <summary>
    /// Writes the mark into the single gradebook table so report cards, subject averages and
    /// GPA read from one place regardless of what produced the mark.
    /// </summary>
    private async Task UpsertGradeRecordAsync(
        int studentId, int subjectId, int sectionId, int sessionId, GradeCategory category,
        string title, decimal score, decimal maxScore, decimal weight,
        int? assignmentId = null, int? quizId = null, int? examScheduleId = null, CancellationToken ct = default)
    {
        var record = await _db.GradeRecords.FirstOrDefaultAsync(g =>
            g.StudentId == studentId
            && (assignmentId != null ? g.AssignmentId == assignmentId
                : quizId != null ? g.QuizId == quizId
                : g.ExamScheduleId == examScheduleId), ct);

        if (record is null)
        {
            record = new GradeRecord
            {
                StudentId = studentId,
                SubjectId = subjectId,
                SectionId = sectionId,
                AcademicSessionId = sessionId,
                AssignmentId = assignmentId,
                QuizId = quizId,
                ExamScheduleId = examScheduleId,
                Category = category
            };
            _db.GradeRecords.Add(record);
        }

        var percentage = maxScore <= 0 ? 0 : Math.Round(score * 100m / maxScore, 2);

        record.Title = title;
        record.Score = score;
        record.MaxScore = maxScore;
        record.Weight = weight;
        record.Percentage = percentage;
        record.RecordedOn = _clock.SchoolToday;
        record.RecordedByUserId = CurrentUser.UserId;
        record.IsPublished = true;

        // Letter and grade point come from the school's configured scale rather than from
        // hard-coded thresholds, so a school on a 0-20 scale gets correct letters too.
        var band = await _db.GradeBands.AsNoTracking()
            .Where(b => b.GradeScale!.IsDefault
                        && b.MinPercentage <= percentage && b.MaxPercentage >= percentage)
            .Select(b => new { b.Letter, b.GradePoint })
            .FirstOrDefaultAsync(ct);

        record.Letter = band?.Letter;
        record.GradePoint = band?.GradePoint;
    }

    private async Task NotifyStudentsAsync(Assignment assignment, CancellationToken ct)
    {
        var targeted = assignment.Targets.Select(t => t.StudentId).ToList();

        var recipients = await _db.Students.AsNoTracking()
            .Where(s => targeted.Count > 0
                ? targeted.Contains(s.Id)
                : s.CurrentSectionId == assignment.SectionId && s.Status == PersonStatus.Active)
            .Select(s => new { s.Id, s.UserId })
            .ToListAsync(ct);

        var subjectName = await _db.Subjects.Where(s => s.Id == assignment.SubjectId)
            .Select(s => s.Name).FirstOrDefaultAsync(ct) ?? "your class";

        var dueLocal = _clock.ToSchoolTime(assignment.DueAtUtc);

        await _notifications.NotifyManyAsync(recipients.Select(r => new NotificationRequest
        {
            UserId = r.UserId,
            Category = NotificationCategory.Assignment,
            Title = $"New {subjectName} assignment",
            Body = $"'{assignment.Title}' is due on {dueLocal:ddd d MMM} at {dueLocal:h:mm tt}.",
            StudentId = r.Id,
            RelatedEntityType = nameof(Assignment),
            RelatedEntityId = assignment.Id
        }), ct);
    }

    private void EnsureOwnsAssignment(int teacherId)
    {
        if (CurrentUser.TeacherId is { } mine && mine != teacherId
            && !CurrentUser.HasPermission(Permissions.Reports.ViewAcademic))
        {
            throw DomainException.NotAllowed("You can only change your own assignments.");
        }
    }

    private async Task EnsureTeachesSectionAsync(int teacherId, int sectionId, int subjectId, CancellationToken ct)
    {
        // Administrators create work on a teacher's behalf during setup; teachers themselves
        // are held to their assignments.
        if (CurrentUser.TeacherId is null) return;

        var assigned = await _db.TeachingAssignments.AnyAsync(
            a => a.TeacherId == teacherId && a.SectionId == sectionId
                 && a.SubjectId == subjectId && a.IsActive, ct);

        if (!assigned)
            throw DomainException.NotAllowed("You are not assigned to teach that subject to that section.");
    }
}

public record AssignmentRequest
{
    public required string Title { get; init; }
    public string? Instructions { get; init; }
    public required int SubjectId { get; init; }
    public required int SectionId { get; init; }
    public int? TeacherId { get; init; }
    public DateTime DueAtUtc { get; init; }
    public bool AllowLateSubmission { get; init; } = true;
    public DateTime? LateCutoffUtc { get; init; }
    public decimal MaxScore { get; init; } = 100m;
    public decimal Weight { get; init; } = 1m;
    public GradeCategory Category { get; init; } = GradeCategory.Assignment;
    public bool PublishNow { get; init; }
    public List<int>? TargetStudentIds { get; init; }
}

public record GradeSubmissionRequest
{
    public decimal Score { get; init; }
    public string? Feedback { get; init; }
}
