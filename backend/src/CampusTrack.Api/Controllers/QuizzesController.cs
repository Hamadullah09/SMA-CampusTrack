using System.Text.Json;
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

/// <summary>
/// Online quizzes: authoring, sitting and marking.
///
/// The rule that shapes this controller is that a student must never receive the answer key.
/// Question payloads are projected differently for the person taking the quiz and the person
/// who wrote it, rather than relying on the client to hide fields.
/// </summary>
public class QuizzesController : ApiControllerBase
{
    private readonly CampusTrackDbContext _db;
    private readonly INotificationService _notifications;
    private readonly IDateTimeProvider _clock;

    public QuizzesController(CampusTrackDbContext db, INotificationService notifications, IDateTimeProvider clock)
    {
        _db = db;
        _notifications = notifications;
        _clock = clock;
    }

    [HttpGet]
    [HasPermission(Permissions.Quizzes.View)]
    public async Task<ActionResult<PagedResult<object>>> Search(
        [FromQuery] PagedQuery paging, [FromQuery] int? sectionId, [FromQuery] QuizStatus? status, CancellationToken ct)
    {
        var q = _db.Quizzes.AsNoTracking().AsQueryable();

        if (CurrentUser.TeacherId is { } teacherId && !CurrentUser.HasPermission(Permissions.Reports.ViewAcademic))
            q = q.Where(x => x.TeacherId == teacherId);

        if (sectionId is { } sid) q = q.Where(x => x.SectionId == sid);
        if (status is { } st) q = q.Where(x => x.Status == st);

        var projected = q.OrderByDescending(x => x.OpensAtUtc ?? x.CreatedAtUtc).Select(x => (object)new
        {
            x.Id, x.Title, x.Status, x.OpensAtUtc, x.ClosesAtUtc, x.DurationMinutes,
            x.MaxScore, x.PassScore, x.MaxAttempts,
            x.SubjectId, subjectName = x.Subject!.Name,
            x.SectionId, sectionName = x.Section!.DisplayName,
            questionCount = x.Questions.Count,
            attemptCount = x.Attempts.Count,
            gradedCount = x.Attempts.Count(a => a.Status == QuizAttemptStatus.Graded),
            awaitingGrading = x.Attempts.Count(a => a.Status == QuizAttemptStatus.Submitted
                                                    || a.Status == QuizAttemptStatus.AutoSubmitted)
        });

        return Paged(await projected.ToPagedResultAsync(paging.Page, paging.PageSize, ct));
    }

    /// <summary>Full quiz including the answer key. Requires authoring rights.</summary>
    [HttpGet("{id:int}")]
    [HasPermission(Permissions.Quizzes.Edit)]
    public async Task<ActionResult<object>> Get(int id, CancellationToken ct)
    {
        var quiz = await _db.Quizzes.AsNoTracking()
            .Where(q => q.Id == id)
            .Select(q => new
            {
                q.Id, q.Title, q.Instructions, q.Status, q.OpensAtUtc, q.ClosesAtUtc, q.DurationMinutes,
                q.MaxAttempts, q.MaxScore, q.PassScore, q.Weight,
                q.ShuffleQuestions, q.ShuffleOptions, q.ShowResultImmediately, q.ShowCorrectAnswers,
                q.SubjectId, q.SectionId,
                questions = q.Questions.OrderBy(x => x.Sequence).Select(x => new
                {
                    x.Id, x.QuestionType, x.Text, x.Sequence, x.Points, x.CorrectAnswer, x.Explanation,
                    options = x.Options.OrderBy(o => o.Sequence)
                        .Select(o => new { o.Id, o.Text, o.IsCorrect, o.Sequence })
                })
            })
            .FirstOrDefaultAsync(ct);

        return Ok(Found(quiz, "quiz"));
    }

    [HttpPost]
    [HasPermission(Permissions.Quizzes.Create)]
    public async Task<ActionResult<object>> Create(QuizRequest request, CancellationToken ct)
    {
        var teacherId = CurrentUser.TeacherId ?? request.TeacherId
            ?? throw DomainException.Invalid("A teacher must be specified.");

        var sessionId = await _db.AcademicSessions.Where(s => s.IsCurrent).Select(s => s.Id).FirstOrDefaultAsync(ct);
        if (sessionId == 0) throw DomainException.Invalid("No academic session is marked as current.");

        var quiz = new Quiz
        {
            TeacherId = teacherId,
            SubjectId = request.SubjectId,
            SectionId = request.SectionId,
            AcademicSessionId = sessionId,
            Title = request.Title.Trim(),
            Instructions = request.Instructions,
            OpensAtUtc = request.OpensAtUtc,
            ClosesAtUtc = request.ClosesAtUtc,
            DurationMinutes = request.DurationMinutes,
            MaxAttempts = Math.Max(1, request.MaxAttempts),
            PassScore = request.PassScore,
            Weight = request.Weight,
            ShuffleQuestions = request.ShuffleQuestions,
            ShuffleOptions = request.ShuffleOptions,
            ShowResultImmediately = request.ShowResultImmediately,
            ShowCorrectAnswers = request.ShowCorrectAnswers,
            Status = QuizStatus.Draft
        };

        AddQuestions(quiz, request.Questions);
        // Total is derived from the questions, so it can never disagree with them.
        quiz.MaxScore = quiz.Questions.Sum(q => q.Points);

        _db.Quizzes.Add(quiz);
        await _db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(Get), new { id = quiz.Id }, new { quiz.Id, quiz.Title, quiz.MaxScore });
    }

    [HttpPut("{id:int}")]
    [HasPermission(Permissions.Quizzes.Edit)]
    public async Task<IActionResult> Update(int id, QuizRequest request, CancellationToken ct)
    {
        var quiz = Found(await _db.Quizzes
            .Include(q => q.Questions).ThenInclude(q => q.Options)
            .Include(q => q.Attempts)
            .FirstOrDefaultAsync(q => q.Id == id, ct), "quiz");

        EnsureOwns(quiz.TeacherId);

        // Changing questions after students have answered would invalidate their marks.
        if (quiz.Attempts.Count > 0 && request.Questions is { Count: > 0 })
            throw DomainException.Conflict(
                "Students have already attempted this quiz, so its questions can no longer be changed.");

        quiz.Title = request.Title.Trim();
        quiz.Instructions = request.Instructions;
        quiz.OpensAtUtc = request.OpensAtUtc;
        quiz.ClosesAtUtc = request.ClosesAtUtc;
        quiz.DurationMinutes = request.DurationMinutes;
        quiz.MaxAttempts = Math.Max(1, request.MaxAttempts);
        quiz.PassScore = request.PassScore;
        quiz.Weight = request.Weight;
        quiz.ShuffleQuestions = request.ShuffleQuestions;
        quiz.ShuffleOptions = request.ShuffleOptions;
        quiz.ShowResultImmediately = request.ShowResultImmediately;
        quiz.ShowCorrectAnswers = request.ShowCorrectAnswers;

        if (request.Questions is { Count: > 0 })
        {
            _db.QuizQuestions.RemoveRange(quiz.Questions);
            quiz.Questions.Clear();
            AddQuestions(quiz, request.Questions);
            quiz.MaxScore = quiz.Questions.Sum(q => q.Points);
        }

        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPost("{id:int}/publish")]
    [HasPermission(Permissions.Quizzes.Publish)]
    public async Task<IActionResult> Publish(int id, [FromQuery] bool publish = true, CancellationToken ct = default)
    {
        var quiz = Found(await _db.Quizzes.Include(q => q.Questions)
            .FirstOrDefaultAsync(q => q.Id == id, ct), "quiz");

        EnsureOwns(quiz.TeacherId);

        if (publish && quiz.Questions.Count == 0)
            throw DomainException.Invalid("Add at least one question before publishing.");

        quiz.Status = publish ? QuizStatus.Published : QuizStatus.Draft;
        await _db.SaveChangesAsync(ct);

        if (!publish) return NoContent();

        var students = await _db.Students.AsNoTracking()
            .Where(s => s.CurrentSectionId == quiz.SectionId && s.Status == PersonStatus.Active)
            .Select(s => new { s.Id, s.UserId })
            .ToListAsync(ct);

        var subjectName = await _db.Subjects.Where(s => s.Id == quiz.SubjectId)
            .Select(s => s.Name).FirstOrDefaultAsync(ct) ?? "your class";

        await _notifications.NotifyManyAsync(students.Select(s => new NotificationRequest
        {
            UserId = s.UserId,
            Category = NotificationCategory.Quiz,
            Title = $"New {subjectName} quiz",
            Body = quiz.ClosesAtUtc is { } closes
                ? $"'{quiz.Title}' is open until {_clock.ToSchoolTime(closes):ddd d MMM, h:mm tt}."
                : $"'{quiz.Title}' is now available.",
            StudentId = s.Id,
            RelatedEntityType = nameof(Quiz),
            RelatedEntityId = quiz.Id
        }), ct);

        return NoContent();
    }

    [HttpDelete("{id:int}")]
    [HasPermission(Permissions.Quizzes.Delete)]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var quiz = Found(await _db.Quizzes.Include(q => q.Attempts)
            .FirstOrDefaultAsync(q => q.Id == id, ct), "quiz");

        EnsureOwns(quiz.TeacherId);

        // Attempts are a student's work and part of their record; removing the quiz would
        // take their marks with it.
        if (quiz.Attempts.Count > 0)
            throw DomainException.Conflict(
                $"{quiz.Attempts.Count} student(s) have attempted this quiz, so it cannot be removed.");

        _db.Quizzes.Remove(quiz);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    // ------------------------------------------------------------------ taking ----

    /// <summary>
    /// The student's view of a quiz: questions and options with the answer key stripped out.
    /// </summary>
    [HttpGet("{id:int}/take")]
    [HasPermission(Permissions.Quizzes.Attempt)]
    public async Task<ActionResult<object>> Take(int id, CancellationToken ct)
    {
        var studentId = CurrentUser.StudentId
                        ?? throw DomainException.NotAllowed("Only students can sit a quiz.");

        var quiz = Found(await _db.Quizzes.AsNoTracking()
            .Include(q => q.Questions).ThenInclude(q => q.Options)
            .FirstOrDefaultAsync(q => q.Id == id, ct), "quiz");

        if (quiz.Status != QuizStatus.Published)
            throw DomainException.Invalid("This quiz is not currently open.");

        var now = _clock.UtcNow;
        if (quiz.OpensAtUtc is { } opens && now < opens)
            throw DomainException.Invalid($"This quiz opens at {_clock.ToSchoolTime(opens):ddd d MMM, h:mm tt}.");
        if (quiz.ClosesAtUtc is { } closes && now > closes)
            throw DomainException.Invalid("This quiz has closed.");

        var attemptCount = await _db.QuizAttempts
            .CountAsync(a => a.QuizId == id && a.StudentId == studentId, ct);

        if (attemptCount >= quiz.MaxAttempts)
            throw DomainException.Invalid($"You have used all {quiz.MaxAttempts} attempt(s) for this quiz.");

        var inProgress = await _db.QuizAttempts
            .FirstOrDefaultAsync(a => a.QuizId == id && a.StudentId == studentId
                                      && a.Status == QuizAttemptStatus.InProgress, ct);

        if (inProgress is null)
        {
            inProgress = new QuizAttempt
            {
                QuizId = id,
                StudentId = studentId,
                AttemptNumber = attemptCount + 1,
                Status = QuizAttemptStatus.InProgress,
                StartedAtUtc = now,
                // The deadline is fixed server-side at start; the client clock is never trusted.
                ExpiresAtUtc = quiz.DurationMinutes is { } minutes ? now.AddMinutes(minutes) : quiz.ClosesAtUtc
            };

            _db.QuizAttempts.Add(inProgress);
            await _db.SaveChangesAsync(ct);
        }

        var questions = quiz.Questions.OrderBy(q => quiz.ShuffleQuestions ? Random.Shared.Next() : q.Sequence)
            .Select(q => new
            {
                q.Id,
                q.QuestionType,
                q.Text,
                q.Points,
                q.Sequence,
                // IsCorrect and CorrectAnswer are deliberately absent from this shape.
                options = q.Options
                    .OrderBy(o => quiz.ShuffleOptions ? Random.Shared.Next() : o.Sequence)
                    .Select(o => new { o.Id, o.Text })
            });

        return Ok(new
        {
            quiz.Id,
            quiz.Title,
            quiz.Instructions,
            quiz.DurationMinutes,
            quiz.MaxScore,
            attemptId = inProgress.Id,
            attemptNumber = inProgress.AttemptNumber,
            startedAtUtc = inProgress.StartedAtUtc,
            expiresAtUtc = inProgress.ExpiresAtUtc,
            questions
        });
    }

    /// <summary>Submits an attempt. Objective questions are marked immediately.</summary>
    [HttpPost("attempts/{attemptId:int}/submit")]
    [HasPermission(Permissions.Quizzes.Attempt)]
    public async Task<ActionResult<object>> SubmitAttempt(
        int attemptId, SubmitAttemptRequest request, CancellationToken ct)
    {
        var attempt = Found(await _db.QuizAttempts
            .Include(a => a.Quiz).ThenInclude(q => q!.Questions).ThenInclude(q => q.Options)
            .FirstOrDefaultAsync(a => a.Id == attemptId, ct), "attempt");

        if (attempt.StudentId != CurrentUser.StudentId)
            throw DomainException.NotAllowed("This is not your attempt.");

        if (attempt.Status != QuizAttemptStatus.InProgress)
            throw DomainException.Invalid("This attempt has already been submitted.");

        var now = _clock.UtcNow;
        var quiz = attempt.Quiz!;

        // Late arrivals are still accepted and marked, but recorded as auto-submitted so the
        // teacher can see the student ran out of time rather than stopped early.
        var expired = attempt.ExpiresAtUtc is { } expiry && now > expiry;

        decimal autoScore = 0;
        var needsManualMarking = false;

        foreach (var question in quiz.Questions)
        {
            var answer = request.Answers.FirstOrDefault(a => a.QuestionId == question.Id);

            var record = new QuizAnswer
            {
                QuizAttemptId = attempt.Id,
                QuizQuestionId = question.Id,
                TextAnswer = answer?.TextAnswer,
                SelectedOptionIdsJson = answer?.SelectedOptionIds is { Count: > 0 }
                    ? JsonSerializer.Serialize(answer.SelectedOptionIds)
                    : null,
                AnsweredAtUtc = now
            };

            switch (question.QuestionType)
            {
                case QuestionType.MultipleChoice:
                case QuestionType.MultipleAnswer:
                {
                    var correct = question.Options.Where(o => o.IsCorrect).Select(o => o.Id).OrderBy(x => x).ToList();
                    var chosen = (answer?.SelectedOptionIds ?? []).OrderBy(x => x).ToList();

                    // Exact-set match: partial credit on multiple-answer questions is a policy
                    // choice a school should make deliberately, not a silent default.
                    record.IsCorrect = correct.Count > 0 && correct.SequenceEqual(chosen);
                    record.PointsAwarded = record.IsCorrect == true ? question.Points : 0;
                    break;
                }

                case QuestionType.TrueFalse:
                case QuestionType.ShortAnswer:
                {
                    var expected = question.CorrectAnswer?.Trim();
                    var given = answer?.TextAnswer?.Trim();

                    record.IsCorrect = !string.IsNullOrEmpty(expected) && !string.IsNullOrEmpty(given)
                                       && string.Equals(expected, given, StringComparison.OrdinalIgnoreCase);
                    record.PointsAwarded = record.IsCorrect == true ? question.Points : 0;
                    break;
                }

                default:
                    // Descriptive answers wait for a human.
                    needsManualMarking = true;
                    record.IsCorrect = null;
                    record.PointsAwarded = null;
                    break;
            }

            autoScore += record.PointsAwarded ?? 0;
            _db.QuizAnswers.Add(record);
        }

        attempt.SubmittedAtUtc = now;
        attempt.Status = expired ? QuizAttemptStatus.AutoSubmitted : QuizAttemptStatus.Submitted;
        attempt.AutoScore = autoScore;

        if (!needsManualMarking)
        {
            attempt.TotalScore = autoScore;
            attempt.IsPassed = autoScore >= quiz.PassScore;
            attempt.Status = QuizAttemptStatus.Graded;
            attempt.GradedAtUtc = now;
        }

        await _db.SaveChangesAsync(ct);

        return Ok(new
        {
            attempt.Id,
            status = attempt.Status.ToString(),
            score = quiz.ShowResultImmediately && !needsManualMarking ? attempt.TotalScore : null,
            maxScore = quiz.MaxScore,
            isPassed = quiz.ShowResultImmediately && !needsManualMarking ? attempt.IsPassed : null,
            awaitingTeacherMarking = needsManualMarking,
            message = needsManualMarking
                ? "Your answers have been submitted. Written questions will be marked by your teacher."
                : quiz.ShowResultImmediately
                    ? "Your quiz has been marked."
                    : "Your answers have been submitted. Results will be released by your teacher."
        });
    }

    [HttpGet("{id:int}/attempts")]
    [HasPermission(Permissions.Quizzes.Grade)]
    public async Task<ActionResult<IReadOnlyList<object>>> GetAttempts(int id, CancellationToken ct) =>
        Ok(await _db.QuizAttempts.AsNoTracking()
            .Where(a => a.QuizId == id)
            .OrderBy(a => a.Student!.User!.LastName)
            .Select(a => (object)new
            {
                a.Id, a.StudentId,
                studentName = a.Student!.User!.FirstName + " " + a.Student.User.LastName,
                studentCode = a.Student.StudentCode,
                a.AttemptNumber, a.Status, a.StartedAtUtc, a.SubmittedAtUtc,
                a.AutoScore, a.ManualScore, a.TotalScore, a.IsPassed,
                answers = a.Answers.Select(x => new
                {
                    x.Id, x.QuizQuestionId, questionText = x.Question!.Text,
                    questionType = x.Question.QuestionType, x.Question.Points,
                    x.TextAnswer, x.SelectedOptionIdsJson, x.IsCorrect, x.PointsAwarded, x.TeacherComment
                })
            })
            .ToListAsync(ct));

    /// <summary>Awards marks for the written answers and finalises the attempt.</summary>
    [HttpPost("attempts/{attemptId:int}/grade")]
    [HasPermission(Permissions.Quizzes.Grade)]
    public async Task<IActionResult> GradeAttempt(int attemptId, GradeAttemptRequest request, CancellationToken ct)
    {
        var attempt = Found(await _db.QuizAttempts
            .Include(a => a.Quiz)
            .Include(a => a.Answers)
            .FirstOrDefaultAsync(a => a.Id == attemptId, ct), "attempt");

        EnsureOwns(attempt.Quiz!.TeacherId);

        decimal manualScore = 0;

        foreach (var mark in request.Marks)
        {
            var answer = attempt.Answers.FirstOrDefault(a => a.Id == mark.AnswerId);
            if (answer is null) continue;

            answer.PointsAwarded = mark.Points;
            answer.TeacherComment = mark.Comment;
            answer.IsCorrect = mark.Points > 0;
            manualScore += mark.Points;
        }

        attempt.ManualScore = manualScore;
        attempt.TotalScore = (attempt.AutoScore ?? 0) + manualScore;
        attempt.IsPassed = attempt.TotalScore >= attempt.Quiz.PassScore;
        attempt.Status = QuizAttemptStatus.Graded;
        attempt.GradedByUserId = CurrentUser.UserId;
        attempt.GradedAtUtc = _clock.UtcNow;
        attempt.TeacherFeedback = request.Feedback;

        await _db.SaveChangesAsync(ct);

        var studentUserId = await _db.Students.Where(s => s.Id == attempt.StudentId)
            .Select(s => s.UserId).FirstOrDefaultAsync(ct);

        if (studentUserId != 0)
        {
            await _notifications.NotifyAsync(new NotificationRequest
            {
                UserId = studentUserId,
                Category = NotificationCategory.Quiz,
                Title = $"'{attempt.Quiz.Title}' has been marked",
                Body = $"You scored {attempt.TotalScore} out of {attempt.Quiz.MaxScore}.",
                StudentId = attempt.StudentId,
                RelatedEntityType = nameof(Quiz),
                RelatedEntityId = attempt.QuizId
            }, ct);
        }

        return NoContent();
    }

    // ----------------------------------------------------------------- helpers ----

    private static void AddQuestions(Quiz quiz, List<QuestionRequest>? questions)
    {
        var sequence = 1;

        foreach (var q in questions ?? [])
        {
            var question = new QuizQuestion
            {
                QuestionType = q.QuestionType,
                Text = q.Text.Trim(),
                Points = q.Points <= 0 ? 1 : q.Points,
                Sequence = sequence++,
                CorrectAnswer = q.CorrectAnswer,
                Explanation = q.Explanation
            };

            var optionSequence = 1;
            foreach (var option in q.Options ?? [])
            {
                question.Options.Add(new QuizOption
                {
                    Text = option.Text.Trim(),
                    IsCorrect = option.IsCorrect,
                    Sequence = optionSequence++
                });
            }

            quiz.Questions.Add(question);
        }
    }

    private void EnsureOwns(int teacherId)
    {
        if (CurrentUser.TeacherId is { } mine && mine != teacherId
            && !CurrentUser.HasPermission(Permissions.Reports.ViewAcademic))
        {
            throw DomainException.NotAllowed("You can only change your own quizzes.");
        }
    }
}

public record QuizRequest
{
    public required string Title { get; init; }
    public string? Instructions { get; init; }
    public required int SubjectId { get; init; }
    public required int SectionId { get; init; }
    public int? TeacherId { get; init; }
    public DateTime? OpensAtUtc { get; init; }
    public DateTime? ClosesAtUtc { get; init; }
    public int? DurationMinutes { get; init; }
    public int MaxAttempts { get; init; } = 1;
    public decimal PassScore { get; init; }
    public decimal Weight { get; init; } = 1m;
    public bool ShuffleQuestions { get; init; }
    public bool ShuffleOptions { get; init; }
    public bool ShowResultImmediately { get; init; } = true;
    public bool ShowCorrectAnswers { get; init; }
    public List<QuestionRequest>? Questions { get; init; }
}

public record QuestionRequest
{
    public QuestionType QuestionType { get; init; } = QuestionType.MultipleChoice;
    public required string Text { get; init; }
    public decimal Points { get; init; } = 1m;
    public string? CorrectAnswer { get; init; }
    public string? Explanation { get; init; }
    public List<OptionRequest>? Options { get; init; }
}

public record OptionRequest
{
    public required string Text { get; init; }
    public bool IsCorrect { get; init; }
}

public record SubmitAttemptRequest
{
    public required List<AnswerRequest> Answers { get; init; }
}

public record AnswerRequest
{
    public int QuestionId { get; init; }
    public List<int>? SelectedOptionIds { get; init; }
    public string? TextAnswer { get; init; }
}

public record GradeAttemptRequest
{
    public required List<AnswerMark> Marks { get; init; }
    public string? Feedback { get; init; }
}

public record AnswerMark
{
    public int AnswerId { get; init; }
    public decimal Points { get; init; }
    public string? Comment { get; init; }
}
