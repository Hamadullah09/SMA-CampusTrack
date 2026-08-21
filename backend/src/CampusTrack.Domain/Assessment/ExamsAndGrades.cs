using CampusTrack.Domain.Academics;
using CampusTrack.Domain.Common;
using CampusTrack.Domain.Enums;
using CampusTrack.Domain.Facilities;
using CampusTrack.Domain.People;

namespace CampusTrack.Domain.Assessment;

/// <summary>A formal examination series, e.g. "Mid-Term, Term 1".</summary>
public class Exam : TenantEntity<int>
{
    public int AcademicSessionId { get; set; }
    public AcademicSession? AcademicSession { get; set; }
    public int? TermId { get; set; }
    public Term? Term { get; set; }

    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public ExamStatus Status { get; set; } = ExamStatus.Planned;
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }

    public decimal Weight { get; set; } = 1m;
    public bool ResultsPublished { get; set; }
    public DateTime? ResultsPublishedAtUtc { get; set; }

    public ICollection<ExamSchedule> Schedules { get; set; } = new List<ExamSchedule>();
}

/// <summary>One paper: a subject, for a section, at a time, in a room.</summary>
public class ExamSchedule : TenantEntity<int>
{
    public int ExamId { get; set; }
    public Exam? Exam { get; set; }
    public int SubjectId { get; set; }
    public Subject? Subject { get; set; }
    public int SectionId { get; set; }
    public Section? Section { get; set; }
    public int? ClassroomId { get; set; }
    public Classroom? Classroom { get; set; }

    public DateOnly Date { get; set; }
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
    public decimal MaxScore { get; set; } = 100m;
    public decimal PassScore { get; set; } = 40m;
    public int? InvigilatorTeacherId { get; set; }

    public ICollection<ExamResult> Results { get; set; } = new List<ExamResult>();
}

public class ExamResult : TenantEntity<int>
{
    public int ExamScheduleId { get; set; }
    public ExamSchedule? ExamSchedule { get; set; }
    public int StudentId { get; set; }
    public Student? Student { get; set; }

    public decimal? Score { get; set; }
    public bool IsAbsent { get; set; }
    public string? Remarks { get; set; }
    public int? EnteredByUserId { get; set; }
    public DateTime? EnteredAtUtc { get; set; }
}

/// <summary>
/// A named grading system (letter grades, 0-20, GPA bands). Schools differ, so the bands
/// are data rather than code and a school can run more than one scale at a time.
/// </summary>
public class GradeScale : TenantEntity<int>
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal MaxValue { get; set; } = 100m;
    public decimal PassValue { get; set; } = 40m;
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; } = true;

    public ICollection<GradeBand> Bands { get; set; } = new List<GradeBand>();
}

/// <summary>One band of a scale: 80-89 = "A", 4.0 points.</summary>
public class GradeBand
{
    public int Id { get; set; }
    public int GradeScaleId { get; set; }
    public GradeScale? GradeScale { get; set; }

    public string Letter { get; set; } = string.Empty;
    public decimal MinPercentage { get; set; }
    public decimal MaxPercentage { get; set; }
    public decimal? GradePoint { get; set; }
    public string? Descriptor { get; set; }        // "Excellent"
    public string? ColourHex { get; set; }
}

/// <summary>
/// A single recorded mark, whatever produced it. Assignments, quizzes and exams all funnel
/// here so a report card, a subject average and a GPA read from one table instead of three.
/// </summary>
public class GradeRecord : TenantEntity<long>
{
    public int StudentId { get; set; }
    public Student? Student { get; set; }
    public int SubjectId { get; set; }
    public Subject? Subject { get; set; }
    public int SectionId { get; set; }
    public int AcademicSessionId { get; set; }
    public int? TermId { get; set; }

    public GradeCategory Category { get; set; } = GradeCategory.Other;
    public string Title { get; set; } = string.Empty;

    /// <summary>Back-pointer to whichever artefact produced the mark.</summary>
    public int? AssignmentId { get; set; }
    public int? QuizId { get; set; }
    public int? ExamScheduleId { get; set; }

    public decimal Score { get; set; }
    public decimal MaxScore { get; set; } = 100m;
    public decimal Weight { get; set; } = 1m;
    public decimal Percentage { get; set; }
    public string? Letter { get; set; }
    public decimal? GradePoint { get; set; }

    public DateOnly RecordedOn { get; set; }
    public int? RecordedByUserId { get; set; }
    public string? Remarks { get; set; }
    /// <summary>Hidden from students and guardians until the teacher releases it.</summary>
    public bool IsPublished { get; set; } = true;
}

/// <summary>Narrative feedback a teacher writes about a student, visible to guardians.</summary>
public class ProgressNote : TenantEntity<int>
{
    public int StudentId { get; set; }
    public Student? Student { get; set; }
    public int TeacherId { get; set; }
    public Teacher? Teacher { get; set; }
    public int? SubjectId { get; set; }

    public string Category { get; set; } = "Academic";   // Academic, Behaviour, Attendance...
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public DateOnly NoteDate { get; set; }
    public bool IsVisibleToGuardian { get; set; } = true;
}
