using CampusTrack.Domain.Academics;
using CampusTrack.Domain.Facilities;
using CampusTrack.Domain.Scheduling;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CampusTrack.Infrastructure.Persistence.Configurations;

public class AcademicSessionConfiguration : IEntityTypeConfiguration<AcademicSession>
{
    public void Configure(EntityTypeBuilder<AcademicSession> b)
    {
        b.ToTable("academic_sessions");
        b.Property(x => x.Name).HasMaxLength(80).IsRequired();
        b.Property(x => x.Code).HasMaxLength(32).IsRequired();
        b.HasIndex(x => new { x.SchoolId, x.Code }).IsUnique();
        b.HasIndex(x => x.IsCurrent);
    }
}

public class TermConfiguration : IEntityTypeConfiguration<Term>
{
    public void Configure(EntityTypeBuilder<Term> b)
    {
        b.ToTable("terms");
        b.Property(x => x.Name).HasMaxLength(80).IsRequired();
        b.HasIndex(x => new { x.AcademicSessionId, x.Sequence }).IsUnique();

        b.HasOne(x => x.AcademicSession).WithMany(s => s!.Terms)
            .HasForeignKey(x => x.AcademicSessionId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class CourseConfiguration : IEntityTypeConfiguration<Course>
{
    public void Configure(EntityTypeBuilder<Course> b)
    {
        b.ToTable("courses");
        b.Property(x => x.Name).HasMaxLength(160).IsRequired();
        b.Property(x => x.Code).HasMaxLength(32).IsRequired();
        b.Property(x => x.Description).HasMaxLength(1000);
        b.HasIndex(x => new { x.SchoolId, x.Code }).IsUnique();
    }
}

public class SubjectConfiguration : IEntityTypeConfiguration<Subject>
{
    public void Configure(EntityTypeBuilder<Subject> b)
    {
        b.ToTable("subjects");
        b.Property(x => x.Name).HasMaxLength(160).IsRequired();
        b.Property(x => x.Code).HasMaxLength(32).IsRequired();
        b.Property(x => x.Description).HasMaxLength(1000);
        b.Property(x => x.ColourHex).HasMaxLength(9);
        b.HasIndex(x => new { x.SchoolId, x.Code }).IsUnique();
    }
}

public class CourseSubjectConfiguration : IEntityTypeConfiguration<CourseSubject>
{
    public void Configure(EntityTypeBuilder<CourseSubject> b)
    {
        b.ToTable("course_subjects");
        b.HasKey(x => new { x.CourseId, x.SubjectId });

        b.HasOne(x => x.Course).WithMany(c => c!.CourseSubjects)
            .HasForeignKey(x => x.CourseId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Subject).WithMany(s => s!.CourseSubjects)
            .HasForeignKey(x => x.SubjectId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class SchoolClassConfiguration : IEntityTypeConfiguration<SchoolClass>
{
    public void Configure(EntityTypeBuilder<SchoolClass> b)
    {
        b.ToTable("school_classes");
        b.Property(x => x.Name).HasMaxLength(80).IsRequired();
        b.Property(x => x.Code).HasMaxLength(32).IsRequired();
        b.HasIndex(x => new { x.SchoolId, x.Code }).IsUnique();
        b.HasIndex(x => x.Level);

        b.HasOne(x => x.Course).WithMany(c => c!.Classes)
            .HasForeignKey(x => x.CourseId).OnDelete(DeleteBehavior.SetNull);
    }
}

public class SectionConfiguration : IEntityTypeConfiguration<Section>
{
    public void Configure(EntityTypeBuilder<Section> b)
    {
        b.ToTable("sections");
        b.Property(x => x.Name).HasMaxLength(40).IsRequired();
        b.Property(x => x.DisplayName).HasMaxLength(120).IsRequired();

        b.HasIndex(x => new { x.SchoolClassId, x.Name }).IsUnique();

        b.HasOne(x => x.SchoolClass).WithMany(c => c!.Sections)
            .HasForeignKey(x => x.SchoolClassId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.HomeroomTeacher).WithMany(t => t!.HomeroomSections)
            .HasForeignKey(x => x.HomeroomTeacherId).OnDelete(DeleteBehavior.SetNull);
        b.HasOne(x => x.DefaultClassroom).WithMany()
            .HasForeignKey(x => x.DefaultClassroomId).OnDelete(DeleteBehavior.SetNull);
    }
}

public class EnrollmentConfiguration : IEntityTypeConfiguration<Enrollment>
{
    public void Configure(EntityTypeBuilder<Enrollment> b)
    {
        b.ToTable("enrollments");
        b.Property(x => x.RollNumber).HasMaxLength(20);
        b.Property(x => x.Notes).HasMaxLength(500);

        // A student joins a given section once per session; re-joining after a withdrawal
        // reuses the row rather than creating a duplicate history.
        b.HasIndex(x => new { x.StudentId, x.SectionId, x.AcademicSessionId }).IsUnique();
        b.HasIndex(x => new { x.SectionId, x.Status });

        b.HasOne(x => x.Student).WithMany(s => s!.Enrollments)
            .HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Section).WithMany(s => s!.Enrollments)
            .HasForeignKey(x => x.SectionId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.AcademicSession).WithMany()
            .HasForeignKey(x => x.AcademicSessionId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class TeachingAssignmentConfiguration : IEntityTypeConfiguration<TeachingAssignment>
{
    public void Configure(EntityTypeBuilder<TeachingAssignment> b)
    {
        b.ToTable("teaching_assignments");

        b.HasIndex(x => new { x.TeacherId, x.SectionId, x.SubjectId, x.AcademicSessionId })
            .IsUnique()
            .HasDatabaseName("ux_teaching_assignment");
        // Every teacher-portal query starts from "what am I assigned to this session".
        b.HasIndex(x => new { x.TeacherId, x.AcademicSessionId, x.IsActive });
        b.HasIndex(x => new { x.SectionId, x.SubjectId });

        b.HasOne(x => x.Teacher).WithMany(t => t!.TeachingAssignments)
            .HasForeignKey(x => x.TeacherId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Section).WithMany(s => s!.TeachingAssignments)
            .HasForeignKey(x => x.SectionId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Subject).WithMany()
            .HasForeignKey(x => x.SubjectId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.AcademicSession).WithMany()
            .HasForeignKey(x => x.AcademicSessionId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class ClassroomConfiguration : IEntityTypeConfiguration<Classroom>
{
    public void Configure(EntityTypeBuilder<Classroom> b)
    {
        b.ToTable("classrooms");
        b.Property(x => x.Name).HasMaxLength(120).IsRequired();
        b.Property(x => x.Code).HasMaxLength(32).IsRequired();
        b.Property(x => x.Building).HasMaxLength(80);
        b.Property(x => x.Floor).HasMaxLength(40);
        b.Property(x => x.RoomType).HasMaxLength(60);
        b.HasIndex(x => new { x.SchoolId, x.Code }).IsUnique();
    }
}

public class TimetablePeriodConfiguration : IEntityTypeConfiguration<TimetablePeriod>
{
    public void Configure(EntityTypeBuilder<TimetablePeriod> b)
    {
        b.ToTable("timetable_periods");
        b.Property(x => x.Name).HasMaxLength(60).IsRequired();
        b.Ignore(x => x.DurationMinutes);
        b.HasIndex(x => new { x.AcademicSessionId, x.Sequence }).IsUnique();

        b.HasOne(x => x.AcademicSession).WithMany()
            .HasForeignKey(x => x.AcademicSessionId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class TimetableSlotConfiguration : IEntityTypeConfiguration<TimetableSlot>
{
    public void Configure(EntityTypeBuilder<TimetableSlot> b)
    {
        b.ToTable("timetable_slots");
        b.Property(x => x.Notes).HasMaxLength(400);

        // A section cannot be in two lessons in the same period on the same day.
        b.HasIndex(x => new { x.SectionId, x.DayOfWeek, x.TimetablePeriodId, x.AcademicSessionId })
            .IsUnique()
            .HasDatabaseName("ux_slot_section_period");

        // The RFID engine's hot path: "which lesson is section X in, at this room, right now".
        b.HasIndex(x => new { x.DayOfWeek, x.StartTime, x.EndTime });
        b.HasIndex(x => new { x.ClassroomId, x.DayOfWeek });
        b.HasIndex(x => new { x.TeacherId, x.DayOfWeek });

        b.HasOne(x => x.Section).WithMany()
            .HasForeignKey(x => x.SectionId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Subject).WithMany()
            .HasForeignKey(x => x.SubjectId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Teacher).WithMany()
            .HasForeignKey(x => x.TeacherId).OnDelete(DeleteBehavior.SetNull);
        b.HasOne(x => x.Classroom).WithMany()
            .HasForeignKey(x => x.ClassroomId).OnDelete(DeleteBehavior.SetNull);
        b.HasOne(x => x.TimetablePeriod).WithMany()
            .HasForeignKey(x => x.TimetablePeriodId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.AcademicSession).WithMany()
            .HasForeignKey(x => x.AcademicSessionId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class SchoolHolidayConfiguration : IEntityTypeConfiguration<SchoolHoliday>
{
    public void Configure(EntityTypeBuilder<SchoolHoliday> b)
    {
        b.ToTable("school_holidays");
        b.Property(x => x.Name).HasMaxLength(160).IsRequired();
        b.Property(x => x.Description).HasMaxLength(500);
        b.HasIndex(x => new { x.AcademicSessionId, x.StartDate, x.EndDate });

        b.HasOne(x => x.AcademicSession).WithMany()
            .HasForeignKey(x => x.AcademicSessionId).OnDelete(DeleteBehavior.Cascade);
    }
}
