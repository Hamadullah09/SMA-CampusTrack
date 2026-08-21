using CampusTrack.Domain.Auditing;
using CampusTrack.Domain.Communication;
using CampusTrack.Domain.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CampusTrack.Infrastructure.Persistence.Configurations;

public class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> b)
    {
        b.ToTable("notifications");
        b.Property(x => x.Title).HasMaxLength(200).IsRequired();
        b.Property(x => x.Body).HasMaxLength(1000).IsRequired();
        b.Property(x => x.DataJson).HasColumnType("text");
        b.Property(x => x.RelatedEntityType).HasMaxLength(60);

        // The inbox query: this user's newest notifications, unread first.
        b.HasIndex(x => new { x.UserId, x.CreatedAtUtc }).HasDatabaseName("ix_notification_user_time");
        b.HasIndex(x => new { x.UserId, x.IsRead }).HasDatabaseName("ix_notification_user_unread");
        b.HasIndex(x => new { x.StudentId, x.CreatedAtUtc });

        b.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Student).WithMany().HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.SetNull);

        // Mirrors the owner's soft-delete filter. Without this, a deactivated account's
        // notifications would still surface in queries that join through it.
        b.HasQueryFilter(x => !x.User!.IsDeleted);
    }
}

public class NotificationDeliveryConfiguration : IEntityTypeConfiguration<NotificationDelivery>
{
    public void Configure(EntityTypeBuilder<NotificationDelivery> b)
    {
        b.ToTable("notification_deliveries");
        b.Property(x => x.ErrorMessage).HasMaxLength(500);
        b.Property(x => x.ProviderMessageId).HasMaxLength(200);

        // The retry worker sweeps pending/failed deliveries whose backoff has elapsed.
        b.HasIndex(x => new { x.Status, x.NextRetryAtUtc }).HasDatabaseName("ix_delivery_retry");
        b.HasIndex(x => x.NotificationId);

        b.HasOne(x => x.Notification).WithMany(n => n!.Deliveries)
            .HasForeignKey(x => x.NotificationId).OnDelete(DeleteBehavior.Cascade);

        // Notification is filtered by its owner rather than by an IsDeleted column of its
        // own, so the automatic convention cannot infer this one; mirror it explicitly.
        b.HasQueryFilter(x => !x.Notification!.User!.IsDeleted);
    }
}

public class NotificationPreferenceConfiguration : IEntityTypeConfiguration<NotificationPreference>
{
    public void Configure(EntityTypeBuilder<NotificationPreference> b)
    {
        b.ToTable("notification_preferences");
        b.HasIndex(x => new { x.UserId, x.Category }).IsUnique();

        b.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        b.HasQueryFilter(x => !x.User!.IsDeleted);
    }
}

public class DeviceTokenConfiguration : IEntityTypeConfiguration<DeviceToken>
{
    public void Configure(EntityTypeBuilder<DeviceToken> b)
    {
        b.ToTable("device_tokens");
        // FCM registration tokens run to ~160+ chars and are not fixed-length; 512 is safe,
        // but MySQL caps a utf8mb4 unique index key at 191 chars per column, so the unique
        // index is on a prefix (configured in the migration) rather than the whole column.
        b.Property(x => x.Token).HasMaxLength(512).IsRequired();
        b.Property(x => x.DeviceName).HasMaxLength(160);
        b.Property(x => x.AppVersion).HasMaxLength(40);

        b.HasIndex(x => new { x.UserId, x.IsActive });

        b.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        // A removed account must stop receiving pushes on its old devices.
        b.HasQueryFilter(x => !x.User!.IsDeleted);
    }
}

public class AnnouncementConfiguration : IEntityTypeConfiguration<Announcement>
{
    public void Configure(EntityTypeBuilder<Announcement> b)
    {
        b.ToTable("announcements");
        b.Property(x => x.Title).HasMaxLength(200).IsRequired();
        b.Property(x => x.Body).HasColumnType("text").IsRequired();
        b.Property(x => x.AttachmentPath).HasMaxLength(500);
        b.HasIndex(x => new { x.IsPublished, x.PublishAtUtc });
    }
}

public class AnnouncementTargetConfiguration : IEntityTypeConfiguration<AnnouncementTarget>
{
    public void Configure(EntityTypeBuilder<AnnouncementTarget> b)
    {
        b.ToTable("announcement_targets");
        b.HasIndex(x => new { x.AnnouncementId, x.SectionId }).IsUnique();

        b.HasOne(x => x.Announcement).WithMany(a => a!.Targets)
            .HasForeignKey(x => x.AnnouncementId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Section).WithMany()
            .HasForeignKey(x => x.SectionId).OnDelete(DeleteBehavior.Cascade);
        b.HasQueryFilter(x => !x.Announcement!.IsDeleted);
    }
}

public class SchoolEventConfiguration : IEntityTypeConfiguration<SchoolEvent>
{
    public void Configure(EntityTypeBuilder<SchoolEvent> b)
    {
        b.ToTable("school_events");
        b.Property(x => x.Title).HasMaxLength(200).IsRequired();
        b.Property(x => x.Description).HasColumnType("text");
        b.Property(x => x.Location).HasMaxLength(200);
        b.Property(x => x.ColourHex).HasMaxLength(9);
        b.HasIndex(x => new { x.StartAtUtc, x.EndAtUtc });
    }
}

public class LeaveRequestConfiguration : IEntityTypeConfiguration<LeaveRequest>
{
    public void Configure(EntityTypeBuilder<LeaveRequest> b)
    {
        b.ToTable("leave_requests");
        b.Property(x => x.Reason).HasMaxLength(1000).IsRequired();
        b.Property(x => x.ReviewNotes).HasMaxLength(500);
        b.Property(x => x.AttachmentPath).HasMaxLength(500);
        b.Ignore(x => x.TotalDays);

        b.HasIndex(x => new { x.Status, x.StartDate });
        // The attendance engine asks "is this student on approved leave on this date".
        b.HasIndex(x => new { x.StudentId, x.StartDate, x.EndDate })
            .HasDatabaseName("ix_leave_student_range");

        b.HasOne(x => x.Student).WithMany().HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Teacher).WithMany().HasForeignKey(x => x.TeacherId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class DailyStudentReportConfiguration : IEntityTypeConfiguration<DailyStudentReport>
{
    public void Configure(EntityTypeBuilder<DailyStudentReport> b)
    {
        b.ToTable("daily_student_reports");
        b.Property(x => x.TimelineJson).HasColumnType("text");
        b.Property(x => x.HighlightsJson).HasColumnType("text");

        b.HasIndex(x => new { x.StudentId, x.Date }).IsUnique().HasDatabaseName("ux_daily_report");
        b.HasIndex(x => new { x.Date, x.IsSent });

        b.HasOne(x => x.Student).WithMany().HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class GuardianFeedbackConfiguration : IEntityTypeConfiguration<GuardianFeedback>
{
    public void Configure(EntityTypeBuilder<GuardianFeedback> b)
    {
        b.ToTable("guardian_feedback");
        b.Property(x => x.Category).HasMaxLength(40).IsRequired();
        b.Property(x => x.Subject).HasMaxLength(200).IsRequired();
        b.Property(x => x.Message).HasColumnType("text").IsRequired();
        b.Property(x => x.Reply).HasColumnType("text");
        b.Property(x => x.Status).HasMaxLength(20).IsRequired();
        b.HasIndex(x => new { x.Status, x.CreatedAtUtc });

        b.HasOne(x => x.Guardian).WithMany().HasForeignKey(x => x.GuardianId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Student).WithMany().HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.SetNull);
    }
}

public class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> b)
    {
        b.ToTable("audit_logs");
        b.Property(x => x.UserName).HasMaxLength(256);
        b.Property(x => x.UserRole).HasMaxLength(120);
        b.Property(x => x.EntityName).HasMaxLength(120).IsRequired();
        b.Property(x => x.EntityId).HasMaxLength(64);
        b.Property(x => x.EntityDisplay).HasMaxLength(300);
        b.Property(x => x.OldValuesJson).HasColumnType("longtext");
        b.Property(x => x.NewValuesJson).HasColumnType("longtext");
        b.Property(x => x.AffectedColumns).HasMaxLength(1000);
        b.Property(x => x.IpAddress).HasMaxLength(64);
        b.Property(x => x.UserAgent).HasMaxLength(400);
        b.Property(x => x.DeviceId).HasMaxLength(64);
        b.Property(x => x.CorrelationId).HasMaxLength(64);
        b.Property(x => x.RequestPath).HasMaxLength(300);

        // The three ways an auditor actually searches: by time, by actor, by record.
        b.HasIndex(x => x.OccurredAtUtc).HasDatabaseName("ix_audit_time");
        b.HasIndex(x => new { x.UserId, x.OccurredAtUtc }).HasDatabaseName("ix_audit_user_time");
        b.HasIndex(x => new { x.EntityName, x.EntityId }).HasDatabaseName("ix_audit_entity");
        b.HasIndex(x => x.CorrelationId);
    }
}

public class SystemLogConfiguration : IEntityTypeConfiguration<SystemLog>
{
    public void Configure(EntityTypeBuilder<SystemLog> b)
    {
        b.ToTable("system_logs");
        b.Property(x => x.Level).HasMaxLength(20).IsRequired();
        b.Property(x => x.Category).HasMaxLength(80).IsRequired();
        b.Property(x => x.Message).HasMaxLength(2000).IsRequired();
        b.Property(x => x.ExceptionType).HasMaxLength(200);
        b.Property(x => x.ExceptionDetail).HasColumnType("longtext");
        b.Property(x => x.DataJson).HasColumnType("text");
        b.Property(x => x.CorrelationId).HasMaxLength(64);

        b.HasIndex(x => new { x.Level, x.OccurredAtUtc });
        b.HasIndex(x => new { x.Category, x.OccurredAtUtc });
    }
}

public class SystemSettingConfiguration : IEntityTypeConfiguration<SystemSetting>
{
    public void Configure(EntityTypeBuilder<SystemSetting> b)
    {
        b.ToTable("system_settings");
        b.Property(x => x.Key).HasMaxLength(160).IsRequired();
        b.Property(x => x.Category).HasMaxLength(60).IsRequired();
        b.Property(x => x.Value).HasMaxLength(2000);
        b.Property(x => x.DefaultValue).HasMaxLength(2000);
        b.Property(x => x.DisplayName).HasMaxLength(200).IsRequired();
        b.Property(x => x.Description).HasMaxLength(600);

        b.HasIndex(x => new { x.SchoolId, x.Key }).IsUnique();
        b.HasIndex(x => x.Category);
    }
}
