using CampusTrack.Application.Common.Interfaces;
using CampusTrack.Application.Common.Models;
using CampusTrack.Application.Rfid;
using CampusTrack.Domain.Common;
using CampusTrack.Domain.Enums;
using CampusTrack.Infrastructure.Attendance;
using CampusTrack.Infrastructure.Persistence;
using CampusTrack.Infrastructure.Reporting;
using CampusTrack.Infrastructure.Rfid;
using CampusTrack.Infrastructure.Scheduling;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CampusTrack.Api.Controllers;

/// <summary>
/// Self-service endpoints for the mobile apps.
///
/// Everything here is scoped to the caller. A student may only read their own record; a
/// guardian may only read a child they are approved for. That check happens once, in
/// <see cref="ResolveStudentAsync"/>, rather than being repeated (and eventually forgotten)
/// in each endpoint.
/// </summary>
[Route("api/v1/me")]
public class MeController : ApiControllerBase
{
    private readonly CampusTrackDbContext _db;
    private readonly ITimetableService _timetable;
    private readonly IAttendanceQueryService _attendance;
    private readonly IRfidQueryService _rfid;
    private readonly IDailyReportService _dailyReports;
    private readonly IDateTimeProvider _clock;

    public MeController(
        CampusTrackDbContext db,
        ITimetableService timetable,
        IAttendanceQueryService attendance,
        IRfidQueryService rfid,
        IDailyReportService dailyReports,
        IDateTimeProvider clock)
    {
        _db = db;
        _timetable = timetable;
        _attendance = attendance;
        _rfid = rfid;
        _dailyReports = dailyReports;
        _clock = clock;
    }

    /// <summary>The children a guardian may follow — the parent app's child switcher.</summary>
    [HttpGet("children")]
    public async Task<ActionResult<IReadOnlyList<object>>> Children(CancellationToken ct)
    {
        var guardianId = CurrentUser.GuardianId
                         ?? throw DomainException.NotAllowed("You are not registered as a guardian.");

        return Ok(await _db.GuardianStudents.AsNoTracking()
            .Where(gs => gs.GuardianId == guardianId && gs.IsApproved && !gs.IsDeleted)
            .Select(gs => (object)new
            {
                studentId = gs.StudentId,
                name = gs.Student!.User!.FirstName + " " + gs.Student.User.LastName,
                firstName = gs.Student.User.FirstName,
                studentCode = gs.Student.StudentCode,
                photoUrl = gs.Student.User.ProfileImagePath,
                sectionName = gs.Student.CurrentSection!.DisplayName,
                className = gs.Student.CurrentSection.SchoolClass!.Name,
                relationship = gs.Relationship.ToString(),
                canViewAcademics = gs.CanViewAcademics,
                presenceState = _db.StudentPresences.Where(p => p.StudentId == gs.StudentId)
                    .Select(p => p.State).FirstOrDefault().ToString(),
                hasActiveCard = gs.Student.RfidTags.Any(t => t.Status == RfidTagStatus.Active)
            })
            .ToListAsync(ct));
    }

    [HttpGet("timetable")]
    public async Task<ActionResult<IReadOnlyList<TimetableEntry>>> Timetable(
        [FromQuery] int? studentId, CancellationToken ct)
    {
        if (CurrentUser.TeacherId is { } teacherId && studentId is null)
            return Ok(await _timetable.GetForTeacherAsync(teacherId, ct));

        var student = await ResolveStudentAsync(studentId, ct);

        var sectionId = await _db.Students.AsNoTracking()
            .Where(s => s.Id == student).Select(s => s.CurrentSectionId).FirstOrDefaultAsync(ct);

        return sectionId is null
            ? Ok(Array.Empty<TimetableEntry>())
            : Ok(await _timetable.GetForSectionAsync(sectionId.Value, ct));
    }

    [HttpGet("attendance")]
    public async Task<ActionResult<object>> Attendance(
        [FromQuery] int? studentId, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct)
    {
        var student = await ResolveStudentAsync(studentId, ct);
        var start = from ?? _clock.SchoolToday.AddDays(-30);
        var end = to ?? _clock.SchoolToday;

        var summary = await _attendance.GetStudentSummaryAsync(student, start, end, ct);

        var days = await _db.DailyAttendances.AsNoTracking()
            .Where(a => a.StudentId == student && a.Date >= start && a.Date <= end)
            .OrderByDescending(a => a.Date)
            .Select(a => new
            {
                a.Date, status = a.Status.ToString(),
                a.FirstEntryAtUtc, a.LastExitAtUtc, a.LateMinutes, a.EarlyLeaveMinutes,
                a.MinutesOnCampus, a.Remarks
            })
            .ToListAsync(ct);

        return Ok(new { summary, days });
    }

    /// <summary>Today's (or a chosen day's) movement timeline — the heart of the parent app.</summary>
    [HttpGet("activity")]
    public async Task<ActionResult<object>> Activity(
        [FromQuery] int? studentId, [FromQuery] DateOnly? date, CancellationToken ct)
    {
        var student = await ResolveStudentAsync(studentId, ct);
        var target = date ?? _clock.SchoolToday;

        var timeline = await _rfid.GetStudentTimelineAsync(student, target, ct);

        var presence = await _db.StudentPresences.AsNoTracking()
            .Where(p => p.StudentId == student)
            .Select(p => new
            {
                state = p.State.ToString(),
                location = p.CurrentLocation!.Name,
                p.SinceUtc, p.LastEntryAtUtc, p.LastExitAtUtc
            })
            .FirstOrDefaultAsync(ct);

        return Ok(new { date = target, presence, timeline });
    }

    [HttpGet("daily-report")]
    public async Task<ActionResult<object>> DailyReport(
        [FromQuery] int? studentId, [FromQuery] DateOnly? date, CancellationToken ct)
    {
        var student = await ResolveStudentAsync(studentId, ct);
        var target = date ?? _clock.SchoolToday;

        var report = await _dailyReports.GenerateForStudentAsync(student, target, send: false, ct);
        if (report is null) throw new KeyNotFoundException("No report is available for that day.");

        return Ok(new
        {
            report.Date,
            report.SchoolEntryAtUtc,
            report.SchoolExitAtUtc,
            report.ClassesAttended,
            report.ClassesMissed,
            report.LateArrivals,
            report.EarlyExits,
            report.AttendancePercentage,
            dayStatus = report.DayStatus.ToString(),
            timeline = report.TimelineJson is null
                ? null
                : System.Text.Json.JsonSerializer.Deserialize<object>(report.TimelineJson)
        });
    }

    [HttpGet("grades")]
    public async Task<ActionResult<object>> Grades([FromQuery] int? studentId, CancellationToken ct)
    {
        var student = await ResolveStudentAsync(studentId, ct, requireAcademicAccess: true);

        var grades = await _db.GradeRecords.AsNoTracking()
            .Where(g => g.StudentId == student && g.IsPublished)
            .OrderByDescending(g => g.RecordedOn)
            .Select(g => new
            {
                g.Id, g.Title, category = g.Category.ToString(),
                subjectName = g.Subject!.Name, subjectColour = g.Subject.ColourHex,
                g.Score, g.MaxScore, g.Percentage, g.Letter, g.GradePoint, g.RecordedOn, g.Remarks
            })
            .ToListAsync(ct);

        // Per-subject averages: what a parent actually looks for, rather than a flat list.
        var bySubject = grades
            .GroupBy(g => g.subjectName)
            .Select(g => new
            {
                subject = g.Key,
                colour = g.First().subjectColour,
                count = g.Count(),
                average = Math.Round(g.Average(x => x.Percentage), 1),
                best = g.Max(x => x.Percentage),
                lowest = g.Min(x => x.Percentage)
            })
            .OrderByDescending(s => s.average)
            .ToList();

        var overall = grades.Count == 0 ? 0 : Math.Round(grades.Average(g => g.Percentage), 1);

        return Ok(new { overallPercentage = overall, bySubject, grades });
    }

    [HttpGet("assignments")]
    public async Task<ActionResult<IReadOnlyList<object>>> Assignments(
        [FromQuery] int? studentId, [FromQuery] bool includeCompleted = true, CancellationToken ct = default)
    {
        var student = await ResolveStudentAsync(studentId, ct, requireAcademicAccess: true);

        var sectionId = await _db.Students.AsNoTracking()
            .Where(s => s.Id == student).Select(s => s.CurrentSectionId).FirstOrDefaultAsync(ct);

        if (sectionId is null) return Ok(Array.Empty<object>());

        var q = _db.Assignments.AsNoTracking()
            .Where(a => a.SectionId == sectionId && a.Status == AssignmentStatus.Published)
            // Targeted assignments only appear for the students they were set for.
            .Where(a => !a.Targets.Any() || a.Targets.Any(t => t.StudentId == student));

        var assignments = await q.OrderBy(a => a.DueAtUtc)
            .Select(a => new
            {
                a.Id, a.Title, a.Instructions, a.DueAtUtc, a.MaxScore, a.AllowLateSubmission,
                subjectName = a.Subject!.Name, subjectColour = a.Subject.ColourHex,
                teacherName = a.Teacher!.User!.FirstName + " " + a.Teacher.User.LastName,
                attachments = a.Attachments.Select(f => new { f.Id, f.FileName, f.SizeBytes }),
                submission = a.Submissions.Where(s => s.StudentId == student)
                    .Select(s => new
                    {
                        s.Id, status = s.Status.ToString(), s.SubmittedAtUtc,
                        s.Score, s.Feedback, s.IsLate
                    }).FirstOrDefault()
            })
            .ToListAsync(ct);

        var result = includeCompleted
            ? assignments
            : assignments.Where(a => a.submission is null || a.submission.status == "NotSubmitted").ToList();

        return Ok(result.Cast<object>().ToList());
    }

    [HttpGet("quizzes")]
    public async Task<ActionResult<IReadOnlyList<object>>> Quizzes([FromQuery] int? studentId, CancellationToken ct)
    {
        var student = await ResolveStudentAsync(studentId, ct, requireAcademicAccess: true);

        var sectionId = await _db.Students.AsNoTracking()
            .Where(s => s.Id == student).Select(s => s.CurrentSectionId).FirstOrDefaultAsync(ct);

        if (sectionId is null) return Ok(Array.Empty<object>());

        return Ok(await _db.Quizzes.AsNoTracking()
            .Where(q => q.SectionId == sectionId && q.Status == QuizStatus.Published)
            .OrderByDescending(q => q.OpensAtUtc)
            .Select(q => (object)new
            {
                q.Id, q.Title, q.Instructions, q.OpensAtUtc, q.ClosesAtUtc,
                q.DurationMinutes, q.MaxScore, q.PassScore, q.MaxAttempts,
                subjectName = q.Subject!.Name,
                questionCount = q.Questions.Count,
                attempts = q.Attempts.Where(a => a.StudentId == student)
                    .Select(a => new
                    {
                        a.Id, a.AttemptNumber, status = a.Status.ToString(),
                        a.SubmittedAtUtc,
                        // Results are withheld until the teacher releases them.
                        score = q.ShowResultImmediately ? a.TotalScore : null,
                        isPassed = q.ShowResultImmediately ? a.IsPassed : null
                    }).ToList()
            })
            .ToListAsync(ct));
    }

    [HttpGet("exams")]
    public async Task<ActionResult<IReadOnlyList<object>>> Exams([FromQuery] int? studentId, CancellationToken ct)
    {
        var student = await ResolveStudentAsync(studentId, ct, requireAcademicAccess: true);

        var sectionId = await _db.Students.AsNoTracking()
            .Where(s => s.Id == student).Select(s => s.CurrentSectionId).FirstOrDefaultAsync(ct);

        if (sectionId is null) return Ok(Array.Empty<object>());

        return Ok(await _db.ExamSchedules.AsNoTracking()
            .Where(e => e.SectionId == sectionId)
            .OrderBy(e => e.Date).ThenBy(e => e.StartTime)
            .Select(e => (object)new
            {
                e.Id, e.Date, e.StartTime, e.EndTime, e.MaxScore, e.PassScore,
                examName = e.Exam!.Name, examStatus = e.Exam.Status.ToString(),
                subjectName = e.Subject!.Name,
                classroomName = e.Classroom!.Name,
                result = e.Exam.ResultsPublished
                    ? e.Results.Where(r => r.StudentId == student)
                        .Select(r => new { r.Score, r.IsAbsent, r.Remarks }).FirstOrDefault()
                    : null
            })
            .ToListAsync(ct));
    }

    /// <summary>The student's own card status — answers "why did the gate not register me".</summary>
    [HttpGet("rfid-status")]
    public async Task<ActionResult<object>> RfidStatus([FromQuery] int? studentId, CancellationToken ct)
    {
        var student = await ResolveStudentAsync(studentId, ct);

        var card = await _db.RfidTags.AsNoTracking()
            .Where(t => t.StudentId == student && t.Status == RfidTagStatus.Active)
            .Select(t => new
            {
                // Only the tail of the EPC — enough to match a card, useless for cloning.
                maskedEpc = "***" + t.Epc.Substring(t.Epc.Length - 6),
                t.CardNumber,
                status = t.Status.ToString(),
                t.IssuedAtUtc,
                t.LastSeenAtUtc,
                lastSeenLocation = _db.RfidLocations.Where(l => l.Id == t.LastSeenLocationId)
                    .Select(l => l.Name).FirstOrDefault()
            })
            .FirstOrDefaultAsync(ct);

        return Ok(new
        {
            hasActiveCard = card is not null,
            card,
            message = card is null
                ? "No active card is assigned. Please contact the school office."
                : "Your card is active."
        });
    }

    [HttpGet("announcements")]
    public async Task<ActionResult<PagedResult<object>>> Announcements(
        [FromQuery] PagedQuery paging, CancellationToken ct)
    {
        var now = _clock.UtcNow;

        var projected = _db.Announcements.AsNoTracking()
            .Where(a => a.IsPublished
                        && (a.PublishAtUtc == null || a.PublishAtUtc <= now)
                        && (a.ExpiresAtUtc == null || a.ExpiresAtUtc >= now))
            .OrderByDescending(a => a.PublishAtUtc ?? a.CreatedAtUtc)
            .Select(a => (object)new
            {
                a.Id, a.Title, a.Body, priority = a.Priority.ToString(),
                a.PublishAtUtc, a.AttachmentPath
            });

        return Paged(await projected.ToPagedResultAsync(paging.Page, paging.PageSize, ct));
    }

    /// <summary>
    /// Resolves which student the caller may act for.
    ///
    /// A student is always themselves. A guardian must have an approved link, and academic
    /// screens additionally require the link to grant academic access - a guardian authorised
    /// only for pickup can see arrivals but not grades.
    /// </summary>
    private async Task<int> ResolveStudentAsync(
        int? requested, CancellationToken ct, bool requireAcademicAccess = false)
    {
        if (CurrentUser.StudentId is { } ownId)
        {
            if (requested is { } asked && asked != ownId)
                throw DomainException.NotAllowed("You can only view your own records.");
            return ownId;
        }

        if (CurrentUser.GuardianId is { } guardianId)
        {
            var links = await _db.GuardianStudents.AsNoTracking()
                .Where(gs => gs.GuardianId == guardianId && gs.IsApproved && !gs.IsDeleted)
                .Select(gs => new { gs.StudentId, gs.CanViewAcademics })
                .ToListAsync(ct);

            if (links.Count == 0)
                throw DomainException.NotAllowed("No children are linked to your account yet.");

            // With one child, the app need not send an id at all.
            var link = requested is { } asked
                ? links.FirstOrDefault(l => l.StudentId == asked)
                : links[0];

            if (link is null)
                throw DomainException.NotAllowed("You do not have access to that student.");

            if (requireAcademicAccess && !link.CanViewAcademics)
                throw DomainException.NotAllowed("Your access to this child does not include academic records.");

            return link.StudentId;
        }

        throw DomainException.NotAllowed("This area is for students and guardians.");
    }
}
