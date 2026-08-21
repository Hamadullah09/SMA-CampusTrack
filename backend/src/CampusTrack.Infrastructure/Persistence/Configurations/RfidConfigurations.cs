using CampusTrack.Domain.Attendance;
using CampusTrack.Domain.Rfid;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CampusTrack.Infrastructure.Persistence.Configurations;

public class RfidTagConfiguration : IEntityTypeConfiguration<RfidTag>
{
    public void Configure(EntityTypeBuilder<RfidTag> b)
    {
        b.ToTable("rfid_tags");

        // 96-bit EPCs are 24 hex chars; 128 leaves room for longer encodings.
        b.Property(x => x.Epc).HasMaxLength(64).IsRequired();
        b.Property(x => x.TagUid).HasMaxLength(64);
        b.Property(x => x.CardNumber).HasMaxLength(40);
        b.Property(x => x.RevokedReason).HasMaxLength(300);

        // The single hottest lookup in the system: every antenna hit resolves an EPC here.
        b.HasIndex(x => new { x.SchoolId, x.Epc }).IsUnique().HasDatabaseName("ux_rfid_tag_epc");
        b.HasIndex(x => x.StudentId);
        b.HasIndex(x => x.TeacherId);
        b.HasIndex(x => x.Status);

        // Note: "one Active tag per holder" cannot be a filtered unique index because MySQL
        // has no partial indexes. It is enforced in RfidTagService and covered by a test.

        b.HasOne(x => x.Student).WithMany(s => s!.RfidTags)
            .HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.SetNull);
        b.HasOne(x => x.Teacher).WithMany()
            .HasForeignKey(x => x.TeacherId).OnDelete(DeleteBehavior.SetNull);
        b.HasOne(x => x.StaffMember).WithMany()
            .HasForeignKey(x => x.StaffMemberId).OnDelete(DeleteBehavior.SetNull);

        b.Ignore(x => x.IsAssigned);
        b.Ignore(x => x.IsUsable);
    }
}

public class RfidLocationConfiguration : IEntityTypeConfiguration<RfidLocation>
{
    public void Configure(EntityTypeBuilder<RfidLocation> b)
    {
        b.ToTable("rfid_locations");
        b.Property(x => x.Name).HasMaxLength(120).IsRequired();
        b.Property(x => x.Code).HasMaxLength(40).IsRequired();
        b.Property(x => x.Description).HasMaxLength(500);
        b.Property(x => x.Building).HasMaxLength(80);
        b.Property(x => x.Floor).HasMaxLength(40);

        b.HasIndex(x => new { x.SchoolId, x.Code }).IsUnique();
        b.HasIndex(x => x.IsCampusBoundary);
        b.HasIndex(x => x.LocationType);

        b.HasOne(x => x.Classroom).WithMany()
            .HasForeignKey(x => x.ClassroomId).OnDelete(DeleteBehavior.SetNull);
    }
}

public class RfidReaderConfiguration : IEntityTypeConfiguration<RfidReader>
{
    public void Configure(EntityTypeBuilder<RfidReader> b)
    {
        b.ToTable("rfid_readers");
        b.Property(x => x.DeviceId).HasMaxLength(64).IsRequired();
        b.Property(x => x.Name).HasMaxLength(120).IsRequired();
        b.Property(x => x.Model).HasMaxLength(60);
        b.Property(x => x.SerialNumber).HasMaxLength(80);
        b.Property(x => x.FirmwareVersion).HasMaxLength(60);
        b.Property(x => x.IpAddress).HasMaxLength(64);
        b.Property(x => x.MacAddress).HasMaxLength(32);
        b.Property(x => x.ApiKeyHash).HasMaxLength(128);
        b.Property(x => x.LastErrorMessage).HasMaxLength(500);

        // Device authentication resolves the reader by DeviceId on every ingest call.
        b.HasIndex(x => new { x.SchoolId, x.DeviceId }).IsUnique().HasDatabaseName("ux_reader_device_id");
        b.HasIndex(x => x.LocationId);
        // The health monitor sweeps by status and heartbeat age.
        b.HasIndex(x => new { x.Status, x.LastHeartbeatUtc });

        b.HasOne(x => x.Location).WithMany(l => l!.Readers)
            .HasForeignKey(x => x.LocationId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class ReaderAntennaConfiguration : IEntityTypeConfiguration<ReaderAntenna>
{
    public void Configure(EntityTypeBuilder<ReaderAntenna> b)
    {
        b.ToTable("reader_antennas");
        b.Property(x => x.Label).HasMaxLength(80);
        b.HasIndex(x => new { x.ReaderId, x.AntennaNumber }).IsUnique();

        b.HasOne(x => x.Reader).WithMany(r => r!.Antennas)
            .HasForeignKey(x => x.ReaderId).OnDelete(DeleteBehavior.Cascade);
        b.HasQueryFilter(x => !x.Reader!.IsDeleted);
    }
}

public class RfidRawReadConfiguration : IEntityTypeConfiguration<RfidRawRead>
{
    public void Configure(EntityTypeBuilder<RfidRawRead> b)
    {
        b.ToTable("rfid_raw_reads");
        b.Property(x => x.Epc).HasMaxLength(64).IsRequired();
        b.Property(x => x.IngestBatchId).HasMaxLength(64);

        // This table takes the highest write volume in the product: one row per antenna hit,
        // and a UHF reader can emit dozens per second per tag. Indexes are kept to the three
        // access patterns that actually exist, because every extra index is a write cost.
        //
        // 1) the sweeper draining pending reads for one reader/tag in time order
        b.HasIndex(x => new { x.ReaderId, x.Epc, x.ReadAtUtc }).HasDatabaseName("ix_raw_reader_epc_time");
        // 2) the background processor picking up unprocessed work
        b.HasIndex(x => new { x.State, x.ReadAtUtc }).HasDatabaseName("ix_raw_state_time");
        // 3) replay / audit of one resolved movement
        b.HasIndex(x => x.SequenceId).HasDatabaseName("ix_raw_sequence");
        // 4) idempotent re-delivery of a gateway batch after a network retry
        b.HasIndex(x => x.IngestBatchId).HasDatabaseName("ix_raw_batch");

        b.HasOne(x => x.Reader).WithMany()
            .HasForeignKey(x => x.ReaderId).OnDelete(DeleteBehavior.Restrict);
        b.HasQueryFilter(x => !x.Reader!.IsDeleted);
    }
}

public class RfidEventConfiguration : IEntityTypeConfiguration<RfidEvent>
{
    public void Configure(EntityTypeBuilder<RfidEvent> b)
    {
        b.ToTable("rfid_events");
        b.Property(x => x.Epc).HasMaxLength(64).IsRequired();
        b.Property(x => x.AntennaSequence).HasMaxLength(64);
        b.Property(x => x.RejectionReason).HasMaxLength(300);

        // "What happened to this student today" - the timeline query behind the parent app.
        b.HasIndex(x => new { x.StudentId, x.LocalDate, x.OccurredAtUtc })
            .HasDatabaseName("ix_event_student_date");
        // The live monitor: newest events, optionally filtered by location.
        b.HasIndex(x => x.OccurredAtUtc).HasDatabaseName("ix_event_time");
        b.HasIndex(x => new { x.LocationId, x.OccurredAtUtc }).HasDatabaseName("ix_event_location_time");
        b.HasIndex(x => new { x.ReaderId, x.OccurredAtUtc }).HasDatabaseName("ix_event_reader_time");
        // Deduplication checks the last event for this tag at this location and direction.
        b.HasIndex(x => new { x.TagId, x.LocationId, x.Direction, x.OccurredAtUtc })
            .HasDatabaseName("ix_event_dedupe");
        b.HasIndex(x => new { x.EventType, x.LocalDate }).HasDatabaseName("ix_event_type_date");

        b.HasOne(x => x.Tag).WithMany().HasForeignKey(x => x.TagId).OnDelete(DeleteBehavior.SetNull);
        b.HasOne(x => x.Student).WithMany().HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Teacher).WithMany().HasForeignKey(x => x.TeacherId).OnDelete(DeleteBehavior.SetNull);
        b.HasOne(x => x.StaffMember).WithMany().HasForeignKey(x => x.StaffMemberId).OnDelete(DeleteBehavior.SetNull);
        b.HasOne(x => x.Reader).WithMany().HasForeignKey(x => x.ReaderId).OnDelete(DeleteBehavior.SetNull);
        b.HasOne(x => x.Location).WithMany().HasForeignKey(x => x.LocationId).OnDelete(DeleteBehavior.SetNull);
        b.HasOne(x => x.TimetableSlot).WithMany().HasForeignKey(x => x.TimetableSlotId).OnDelete(DeleteBehavior.SetNull);
        b.HasOne(x => x.Subject).WithMany().HasForeignKey(x => x.SubjectId).OnDelete(DeleteBehavior.SetNull);
        b.HasOne(x => x.Section).WithMany().HasForeignKey(x => x.SectionId).OnDelete(DeleteBehavior.SetNull);
    }
}

public class RfidDeadLetterConfiguration : IEntityTypeConfiguration<RfidDeadLetter>
{
    public void Configure(EntityTypeBuilder<RfidDeadLetter> b)
    {
        b.ToTable("rfid_dead_letters");
        b.Property(x => x.DeviceId).HasMaxLength(64);
        b.Property(x => x.PayloadJson).HasColumnType("longtext").IsRequired();
        b.Property(x => x.ErrorMessage).HasMaxLength(500).IsRequired();
        b.Property(x => x.ErrorDetail).HasColumnType("text");
        b.Property(x => x.ResolutionNotes).HasMaxLength(500);
        b.HasIndex(x => new { x.IsResolved, x.LastFailedAtUtc });
    }
}

public class DeviceLogConfiguration : IEntityTypeConfiguration<DeviceLog>
{
    public void Configure(EntityTypeBuilder<DeviceLog> b)
    {
        b.ToTable("device_logs");
        b.Property(x => x.DeviceId).HasMaxLength(64);
        b.Property(x => x.EventName).HasMaxLength(60).IsRequired();
        b.Property(x => x.Message).HasMaxLength(1000);
        b.Property(x => x.DetailJson).HasColumnType("text");
        b.HasIndex(x => new { x.ReaderId, x.OccurredAtUtc });
        b.HasIndex(x => new { x.Level, x.OccurredAtUtc });

        b.HasOne(x => x.Reader).WithMany()
            .HasForeignKey(x => x.ReaderId).OnDelete(DeleteBehavior.Cascade);
    }
}

// ---------------------------------------------------------------- attendance ----

public class DailyAttendanceConfiguration : IEntityTypeConfiguration<DailyAttendance>
{
    public void Configure(EntityTypeBuilder<DailyAttendance> b)
    {
        b.ToTable("daily_attendance");
        b.Property(x => x.Remarks).HasMaxLength(500);

        // One row per student per day - the uniqueness the whole engine relies on when it
        // upserts a day's record from a stream of gate events.
        b.HasIndex(x => new { x.StudentId, x.Date }).IsUnique().HasDatabaseName("ux_daily_student_date");
        b.HasIndex(x => new { x.Date, x.Status }).HasDatabaseName("ix_daily_date_status");
        b.HasIndex(x => new { x.SectionId, x.Date }).HasDatabaseName("ix_daily_section_date");

        b.HasOne(x => x.Student).WithMany(s => s!.DailyAttendances)
            .HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Section).WithMany()
            .HasForeignKey(x => x.SectionId).OnDelete(DeleteBehavior.SetNull);
    }
}

public class SessionAttendanceConfiguration : IEntityTypeConfiguration<SessionAttendance>
{
    public void Configure(EntityTypeBuilder<SessionAttendance> b)
    {
        b.ToTable("session_attendance");
        b.Property(x => x.Remarks).HasMaxLength(500);

        b.HasIndex(x => new { x.StudentId, x.Date, x.TimetableSlotId })
            .IsUnique().HasDatabaseName("ux_session_student_slot");
        // The teacher's register: one lesson, all students.
        b.HasIndex(x => new { x.TimetableSlotId, x.Date }).HasDatabaseName("ix_session_slot_date");
        b.HasIndex(x => new { x.SectionId, x.Date }).HasDatabaseName("ix_session_section_date");
        b.HasIndex(x => new { x.StudentId, x.SubjectId }).HasDatabaseName("ix_session_student_subject");

        b.HasOne(x => x.Student).WithMany().HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.TimetableSlot).WithMany().HasForeignKey(x => x.TimetableSlotId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Subject).WithMany().HasForeignKey(x => x.SubjectId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Section).WithMany().HasForeignKey(x => x.SectionId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Teacher).WithMany().HasForeignKey(x => x.TeacherId).OnDelete(DeleteBehavior.SetNull);
    }
}

public class ClassroomPresenceConfiguration : IEntityTypeConfiguration<ClassroomPresence>
{
    public void Configure(EntityTypeBuilder<ClassroomPresence> b)
    {
        b.ToTable("classroom_presence");
        b.Ignore(x => x.IsOpen);

        // Closing an interval means finding this student's open row at this location.
        b.HasIndex(x => new { x.StudentId, x.LocationId, x.ExitedAtUtc })
            .HasDatabaseName("ix_presence_open_lookup");
        b.HasIndex(x => new { x.StudentId, x.Date }).HasDatabaseName("ix_presence_student_date");
        b.HasIndex(x => new { x.LocationId, x.Date }).HasDatabaseName("ix_presence_location_date");

        b.HasOne(x => x.Student).WithMany().HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Location).WithMany().HasForeignKey(x => x.LocationId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Classroom).WithMany().HasForeignKey(x => x.ClassroomId).OnDelete(DeleteBehavior.SetNull);
    }
}

public class StudentPresenceConfiguration : IEntityTypeConfiguration<StudentPresence>
{
    public void Configure(EntityTypeBuilder<StudentPresence> b)
    {
        b.ToTable("student_presence");

        // Exactly one live row per student: "who is on campus right now" is then an index scan.
        b.HasIndex(x => x.StudentId).IsUnique();
        b.HasIndex(x => x.State).HasDatabaseName("ix_presence_state");

        b.HasOne(x => x.Student).WithMany().HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.CurrentLocation).WithMany()
            .HasForeignKey(x => x.CurrentLocationId).OnDelete(DeleteBehavior.SetNull);
    }
}

public class AttendanceCorrectionConfiguration : IEntityTypeConfiguration<AttendanceCorrection>
{
    public void Configure(EntityTypeBuilder<AttendanceCorrection> b)
    {
        b.ToTable("attendance_corrections");
        b.Property(x => x.RecordType).HasMaxLength(16).IsRequired();
        b.Property(x => x.Reason).HasMaxLength(500).IsRequired();
        b.Property(x => x.IpAddress).HasMaxLength(64);

        b.HasIndex(x => new { x.StudentId, x.Date });
        b.HasIndex(x => x.CorrectedAtUtc);

        b.HasOne(x => x.Student).WithMany().HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Cascade);
    }
}
