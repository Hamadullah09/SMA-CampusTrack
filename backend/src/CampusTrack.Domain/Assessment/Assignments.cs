using CampusTrack.Domain.Academics;
using CampusTrack.Domain.Common;
using CampusTrack.Domain.Enums;
using CampusTrack.Domain.People;

namespace CampusTrack.Domain.Assessment;

/// <summary>
/// Work set by a teacher. An assignment targets a whole section by default; adding rows to
/// <see cref="Targets"/> narrows it to named students (differentiated or catch-up work).
/// </summary>
public class Assignment : TenantEntity<int>
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

    public AssignmentStatus Status { get; set; } = AssignmentStatus.Draft;
    public DateTime? PublishedAtUtc { get; set; }
    public DateTime DueAtUtc { get; set; }

    /// <summary>Submissions after the deadline are accepted but flagged Late.</summary>
    public bool AllowLateSubmission { get; set; } = true;
    /// <summary>Hard stop; nothing is accepted after this even when late submission is on.</summary>
    public DateTime? LateCutoffUtc { get; set; }

    public decimal MaxScore { get; set; } = 100m;
    /// <summary>Weight of this assignment inside its grade category.</summary>
    public decimal Weight { get; set; } = 1m;
    public GradeCategory Category { get; set; } = GradeCategory.Assignment;

    /// <summary>Token behind the printable QR code students scan to open the brief.</summary>
    public Guid ShareToken { get; set; } = Guid.NewGuid();

    public ICollection<AssignmentAttachment> Attachments { get; set; } = new List<AssignmentAttachment>();
    public ICollection<AssignmentTarget> Targets { get; set; } = new List<AssignmentTarget>();
    public ICollection<AssignmentSubmission> Submissions { get; set; } = new List<AssignmentSubmission>();
}

/// <summary>Narrows an assignment to specific students; absent rows mean "the whole section".</summary>
public class AssignmentTarget
{
    public int Id { get; set; }
    public int AssignmentId { get; set; }
    public Assignment? Assignment { get; set; }
    public int StudentId { get; set; }
    public Student? Student { get; set; }
}

public class AssignmentAttachment
{
    public int Id { get; set; }
    public int AssignmentId { get; set; }
    public Assignment? Assignment { get; set; }

    public string FileName { get; set; } = string.Empty;
    public string StoredPath { get; set; } = string.Empty;
    public string? ContentType { get; set; }
    public long SizeBytes { get; set; }
    public DateTime UploadedAtUtc { get; set; }
    public int? UploadedByUserId { get; set; }
}

public class AssignmentSubmission : TenantEntity<int>
{
    public int AssignmentId { get; set; }
    public Assignment? Assignment { get; set; }
    public int StudentId { get; set; }
    public Student? Student { get; set; }

    public SubmissionStatus Status { get; set; } = SubmissionStatus.NotSubmitted;
    public DateTime? SubmittedAtUtc { get; set; }
    public string? TextAnswer { get; set; }
    /// <summary>Incremented on each resubmission so history is not silently overwritten.</summary>
    public int AttemptNumber { get; set; } = 1;

    public decimal? Score { get; set; }
    public string? Feedback { get; set; }
    public int? GradedByUserId { get; set; }
    public DateTime? GradedAtUtc { get; set; }

    /// <summary>Computed at submission time against the assignment deadline.</summary>
    public bool IsLate { get; set; }

    public ICollection<SubmissionFile> Files { get; set; } = new List<SubmissionFile>();
}

public class SubmissionFile
{
    public int Id { get; set; }
    public int AssignmentSubmissionId { get; set; }
    public AssignmentSubmission? Submission { get; set; }

    public string FileName { get; set; } = string.Empty;
    public string StoredPath { get; set; } = string.Empty;
    public string? ContentType { get; set; }
    public long SizeBytes { get; set; }
    public DateTime UploadedAtUtc { get; set; }
}
