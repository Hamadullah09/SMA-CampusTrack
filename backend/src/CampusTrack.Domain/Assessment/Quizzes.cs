using CampusTrack.Domain.Academics;
using CampusTrack.Domain.Common;
using CampusTrack.Domain.Enums;
using CampusTrack.Domain.People;

namespace CampusTrack.Domain.Assessment;

/// <summary>
/// An online quiz. Objective questions are marked automatically the moment an attempt is
/// submitted; descriptive answers are left for the teacher, which is why an attempt can sit
/// in Submitted before reaching Graded.
/// </summary>
public class Quiz : TenantEntity<int>
{
    public int TeacherId { get; set; }
    public Teacher? Teacher { get; set; }
    public int SubjectId { get; set; }
    public Subject? Subject { get; set; }
    public int SectionId { get; set; }
    public Section? Section { get; set; }
    public int AcademicSessionId { get; set; }

    public string Title { get; set; } = string.Empty;
    public string? Instructions { get; set; }
    public QuizStatus Status { get; set; } = QuizStatus.Draft;

    public DateTime? OpensAtUtc { get; set; }
    public DateTime? ClosesAtUtc { get; set; }
    /// <summary>Wall-clock minutes a student gets once they start. Null means untimed.</summary>
    public int? DurationMinutes { get; set; }

    public int MaxAttempts { get; set; } = 1;
    public decimal MaxScore { get; set; }
    public decimal PassScore { get; set; }
    public decimal Weight { get; set; } = 1m;

    public bool ShuffleQuestions { get; set; }
    public bool ShuffleOptions { get; set; }
    /// <summary>Release the score as soon as the attempt is auto-marked.</summary>
    public bool ShowResultImmediately { get; set; } = true;
    /// <summary>Reveal correct answers after closing - off by default to protect reuse.</summary>
    public bool ShowCorrectAnswers { get; set; }

    public ICollection<QuizQuestion> Questions { get; set; } = new List<QuizQuestion>();
    public ICollection<QuizAttempt> Attempts { get; set; } = new List<QuizAttempt>();
}

public class QuizQuestion : TenantEntity<int>
{
    public int QuizId { get; set; }
    public Quiz? Quiz { get; set; }

    public QuestionType QuestionType { get; set; } = QuestionType.MultipleChoice;
    public string Text { get; set; } = string.Empty;
    public string? ImagePath { get; set; }
    public int Sequence { get; set; }
    public decimal Points { get; set; } = 1m;

    /// <summary>For TrueFalse and ShortAnswer, where there are no option rows.</summary>
    public string? CorrectAnswer { get; set; }
    /// <summary>Shown after marking to explain the right answer.</summary>
    public string? Explanation { get; set; }

    public ICollection<QuizOption> Options { get; set; } = new List<QuizOption>();
}

public class QuizOption
{
    public int Id { get; set; }
    public int QuizQuestionId { get; set; }
    public QuizQuestion? Question { get; set; }

    public string Text { get; set; } = string.Empty;
    public bool IsCorrect { get; set; }
    public int Sequence { get; set; }
}

public class QuizAttempt : TenantEntity<int>
{
    public int QuizId { get; set; }
    public Quiz? Quiz { get; set; }
    public int StudentId { get; set; }
    public Student? Student { get; set; }

    public int AttemptNumber { get; set; } = 1;
    public QuizAttemptStatus Status { get; set; } = QuizAttemptStatus.InProgress;

    public DateTime StartedAtUtc { get; set; }
    public DateTime? SubmittedAtUtc { get; set; }
    /// <summary>Server-side deadline for this attempt; the client clock is never trusted.</summary>
    public DateTime? ExpiresAtUtc { get; set; }

    public decimal? AutoScore { get; set; }
    public decimal? ManualScore { get; set; }
    public decimal? TotalScore { get; set; }
    public bool? IsPassed { get; set; }

    public int? GradedByUserId { get; set; }
    public DateTime? GradedAtUtc { get; set; }
    public string? TeacherFeedback { get; set; }

    public ICollection<QuizAnswer> Answers { get; set; } = new List<QuizAnswer>();
}

public class QuizAnswer
{
    public int Id { get; set; }
    public int QuizAttemptId { get; set; }
    public QuizAttempt? Attempt { get; set; }
    public int QuizQuestionId { get; set; }
    public QuizQuestion? Question { get; set; }

    /// <summary>Chosen option ids as JSON - an array so multi-answer questions fit the same shape.</summary>
    public string? SelectedOptionIdsJson { get; set; }
    public string? TextAnswer { get; set; }

    public bool? IsCorrect { get; set; }
    public decimal? PointsAwarded { get; set; }
    public string? TeacherComment { get; set; }
    public DateTime AnsweredAtUtc { get; set; }
}
