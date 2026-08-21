using CampusTrack.Domain.Assessment;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CampusTrack.Infrastructure.Persistence.Configurations;

public class AssignmentConfiguration : IEntityTypeConfiguration<Assignment>
{
    public void Configure(EntityTypeBuilder<Assignment> b)
    {
        b.ToTable("assignments");
        b.Property(x => x.Title).HasMaxLength(200).IsRequired();
        b.Property(x => x.Instructions).HasColumnType("text");

        b.HasIndex(x => new { x.SectionId, x.Status, x.DueAtUtc }).HasDatabaseName("ix_assignment_section_due");
        b.HasIndex(x => new { x.TeacherId, x.Status });
        b.HasIndex(x => x.ShareToken).IsUnique();

        b.HasOne(x => x.Teacher).WithMany().HasForeignKey(x => x.TeacherId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Subject).WithMany().HasForeignKey(x => x.SubjectId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Section).WithMany().HasForeignKey(x => x.SectionId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class AssignmentTargetConfiguration : IEntityTypeConfiguration<AssignmentTarget>
{
    public void Configure(EntityTypeBuilder<AssignmentTarget> b)
    {
        b.ToTable("assignment_targets");
        b.HasIndex(x => new { x.AssignmentId, x.StudentId }).IsUnique();

        b.HasOne(x => x.Assignment).WithMany(a => a!.Targets)
            .HasForeignKey(x => x.AssignmentId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Student).WithMany()
            .HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class AssignmentAttachmentConfiguration : IEntityTypeConfiguration<AssignmentAttachment>
{
    public void Configure(EntityTypeBuilder<AssignmentAttachment> b)
    {
        b.ToTable("assignment_attachments");
        b.Property(x => x.FileName).HasMaxLength(260).IsRequired();
        b.Property(x => x.StoredPath).HasMaxLength(500).IsRequired();
        b.Property(x => x.ContentType).HasMaxLength(120);
        b.HasIndex(x => x.AssignmentId);

        b.HasOne(x => x.Assignment).WithMany(a => a!.Attachments)
            .HasForeignKey(x => x.AssignmentId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class AssignmentSubmissionConfiguration : IEntityTypeConfiguration<AssignmentSubmission>
{
    public void Configure(EntityTypeBuilder<AssignmentSubmission> b)
    {
        b.ToTable("assignment_submissions");
        b.Property(x => x.TextAnswer).HasColumnType("text");
        b.Property(x => x.Feedback).HasColumnType("text");

        // One live submission row per student per assignment; resubmission bumps AttemptNumber.
        b.HasIndex(x => new { x.AssignmentId, x.StudentId }).IsUnique()
            .HasDatabaseName("ux_submission_assignment_student");
        b.HasIndex(x => new { x.StudentId, x.Status });

        b.HasOne(x => x.Assignment).WithMany(a => a!.Submissions)
            .HasForeignKey(x => x.AssignmentId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Student).WithMany()
            .HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class SubmissionFileConfiguration : IEntityTypeConfiguration<SubmissionFile>
{
    public void Configure(EntityTypeBuilder<SubmissionFile> b)
    {
        b.ToTable("submission_files");
        b.Property(x => x.FileName).HasMaxLength(260).IsRequired();
        b.Property(x => x.StoredPath).HasMaxLength(500).IsRequired();
        b.Property(x => x.ContentType).HasMaxLength(120);
        b.HasIndex(x => x.AssignmentSubmissionId);

        b.HasOne(x => x.Submission).WithMany(s => s!.Files)
            .HasForeignKey(x => x.AssignmentSubmissionId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class QuizConfiguration : IEntityTypeConfiguration<Quiz>
{
    public void Configure(EntityTypeBuilder<Quiz> b)
    {
        b.ToTable("quizzes");
        b.Property(x => x.Title).HasMaxLength(200).IsRequired();
        b.Property(x => x.Instructions).HasColumnType("text");
        b.HasIndex(x => new { x.SectionId, x.Status, x.OpensAtUtc });
        b.HasIndex(x => new { x.TeacherId, x.Status });

        b.HasOne(x => x.Teacher).WithMany().HasForeignKey(x => x.TeacherId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Subject).WithMany().HasForeignKey(x => x.SubjectId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Section).WithMany().HasForeignKey(x => x.SectionId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class QuizQuestionConfiguration : IEntityTypeConfiguration<QuizQuestion>
{
    public void Configure(EntityTypeBuilder<QuizQuestion> b)
    {
        b.ToTable("quiz_questions");
        b.Property(x => x.Text).HasColumnType("text").IsRequired();
        b.Property(x => x.CorrectAnswer).HasMaxLength(500);
        b.Property(x => x.Explanation).HasColumnType("text");
        b.Property(x => x.ImagePath).HasMaxLength(500);
        b.HasIndex(x => new { x.QuizId, x.Sequence });

        b.HasOne(x => x.Quiz).WithMany(q => q!.Questions)
            .HasForeignKey(x => x.QuizId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class QuizOptionConfiguration : IEntityTypeConfiguration<QuizOption>
{
    public void Configure(EntityTypeBuilder<QuizOption> b)
    {
        b.ToTable("quiz_options");
        b.Property(x => x.Text).HasMaxLength(500).IsRequired();
        b.HasIndex(x => x.QuizQuestionId);

        b.HasOne(x => x.Question).WithMany(q => q!.Options)
            .HasForeignKey(x => x.QuizQuestionId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class QuizAttemptConfiguration : IEntityTypeConfiguration<QuizAttempt>
{
    public void Configure(EntityTypeBuilder<QuizAttempt> b)
    {
        b.ToTable("quiz_attempts");
        b.Property(x => x.TeacherFeedback).HasColumnType("text");
        b.HasIndex(x => new { x.QuizId, x.StudentId, x.AttemptNumber }).IsUnique();
        b.HasIndex(x => new { x.StudentId, x.Status });

        b.HasOne(x => x.Quiz).WithMany(q => q!.Attempts)
            .HasForeignKey(x => x.QuizId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Student).WithMany()
            .HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class QuizAnswerConfiguration : IEntityTypeConfiguration<QuizAnswer>
{
    public void Configure(EntityTypeBuilder<QuizAnswer> b)
    {
        b.ToTable("quiz_answers");
        b.Property(x => x.SelectedOptionIdsJson).HasMaxLength(500);
        b.Property(x => x.TextAnswer).HasColumnType("text");
        b.Property(x => x.TeacherComment).HasMaxLength(1000);
        b.HasIndex(x => new { x.QuizAttemptId, x.QuizQuestionId }).IsUnique();

        b.HasOne(x => x.Attempt).WithMany(a => a!.Answers)
            .HasForeignKey(x => x.QuizAttemptId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Question).WithMany()
            .HasForeignKey(x => x.QuizQuestionId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class ExamConfiguration : IEntityTypeConfiguration<Exam>
{
    public void Configure(EntityTypeBuilder<Exam> b)
    {
        b.ToTable("exams");
        b.Property(x => x.Name).HasMaxLength(160).IsRequired();
        b.Property(x => x.Description).HasMaxLength(1000);
        b.HasIndex(x => new { x.AcademicSessionId, x.Status });

        b.HasOne(x => x.AcademicSession).WithMany()
            .HasForeignKey(x => x.AcademicSessionId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Term).WithMany()
            .HasForeignKey(x => x.TermId).OnDelete(DeleteBehavior.SetNull);
    }
}

public class ExamScheduleConfiguration : IEntityTypeConfiguration<ExamSchedule>
{
    public void Configure(EntityTypeBuilder<ExamSchedule> b)
    {
        b.ToTable("exam_schedules");
        b.HasIndex(x => new { x.ExamId, x.SectionId, x.SubjectId }).IsUnique();
        b.HasIndex(x => x.Date);

        b.HasOne(x => x.Exam).WithMany(e => e!.Schedules)
            .HasForeignKey(x => x.ExamId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Subject).WithMany().HasForeignKey(x => x.SubjectId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Section).WithMany().HasForeignKey(x => x.SectionId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Classroom).WithMany().HasForeignKey(x => x.ClassroomId).OnDelete(DeleteBehavior.SetNull);
    }
}

public class ExamResultConfiguration : IEntityTypeConfiguration<ExamResult>
{
    public void Configure(EntityTypeBuilder<ExamResult> b)
    {
        b.ToTable("exam_results");
        b.Property(x => x.Remarks).HasMaxLength(500);
        b.HasIndex(x => new { x.ExamScheduleId, x.StudentId }).IsUnique();
        b.HasIndex(x => x.StudentId);

        b.HasOne(x => x.ExamSchedule).WithMany(s => s!.Results)
            .HasForeignKey(x => x.ExamScheduleId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Student).WithMany()
            .HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class GradeScaleConfiguration : IEntityTypeConfiguration<GradeScale>
{
    public void Configure(EntityTypeBuilder<GradeScale> b)
    {
        b.ToTable("grade_scales");
        b.Property(x => x.Name).HasMaxLength(120).IsRequired();
        b.Property(x => x.Description).HasMaxLength(500);
        b.HasIndex(x => new { x.SchoolId, x.Name }).IsUnique();
    }
}

public class GradeBandConfiguration : IEntityTypeConfiguration<GradeBand>
{
    public void Configure(EntityTypeBuilder<GradeBand> b)
    {
        b.ToTable("grade_bands");
        b.Property(x => x.Letter).HasMaxLength(8).IsRequired();
        b.Property(x => x.Descriptor).HasMaxLength(80);
        b.Property(x => x.ColourHex).HasMaxLength(9);
        b.HasIndex(x => new { x.GradeScaleId, x.MinPercentage });

        b.HasOne(x => x.GradeScale).WithMany(s => s!.Bands)
            .HasForeignKey(x => x.GradeScaleId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class GradeRecordConfiguration : IEntityTypeConfiguration<GradeRecord>
{
    public void Configure(EntityTypeBuilder<GradeRecord> b)
    {
        b.ToTable("grade_records");
        b.Property(x => x.Title).HasMaxLength(200).IsRequired();
        b.Property(x => x.Letter).HasMaxLength(8);
        b.Property(x => x.Remarks).HasMaxLength(1000);

        // Report cards and subject averages both read student + session + subject.
        b.HasIndex(x => new { x.StudentId, x.AcademicSessionId, x.SubjectId })
            .HasDatabaseName("ix_grade_student_session_subject");
        b.HasIndex(x => new { x.SectionId, x.SubjectId, x.Category })
            .HasDatabaseName("ix_grade_section_subject");
        b.HasIndex(x => x.RecordedOn);

        b.HasOne(x => x.Student).WithMany().HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Subject).WithMany().HasForeignKey(x => x.SubjectId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class ProgressNoteConfiguration : IEntityTypeConfiguration<ProgressNote>
{
    public void Configure(EntityTypeBuilder<ProgressNote> b)
    {
        b.ToTable("progress_notes");
        b.Property(x => x.Category).HasMaxLength(40).IsRequired();
        b.Property(x => x.Title).HasMaxLength(200).IsRequired();
        b.Property(x => x.Body).HasColumnType("text").IsRequired();
        b.HasIndex(x => new { x.StudentId, x.NoteDate });

        b.HasOne(x => x.Student).WithMany().HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Teacher).WithMany().HasForeignKey(x => x.TeacherId).OnDelete(DeleteBehavior.Restrict);
    }
}
