using System.Linq.Expressions;
using CampusTrack.Application.Common.Interfaces;
using CampusTrack.Domain.Academics;
using CampusTrack.Domain.Assessment;
using CampusTrack.Domain.Attendance;
using CampusTrack.Domain.Auditing;
using CampusTrack.Domain.Common;
using CampusTrack.Domain.Communication;
using CampusTrack.Domain.Facilities;
using CampusTrack.Domain.Identity;
using CampusTrack.Domain.People;
using CampusTrack.Domain.Rfid;
using CampusTrack.Domain.Scheduling;
using CampusTrack.Domain.Settings;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CampusTrack.Infrastructure.Persistence;

/// <summary>
/// The single EF Core context. Identity tables and business tables live together so that a
/// user and their profile can be created inside one transaction.
/// </summary>
public class CampusTrackDbContext
    : IdentityDbContext<ApplicationUser, ApplicationRole, int,
        Microsoft.AspNetCore.Identity.IdentityUserClaim<int>, ApplicationUserRole,
        Microsoft.AspNetCore.Identity.IdentityUserLogin<int>,
        Microsoft.AspNetCore.Identity.IdentityRoleClaim<int>,
        Microsoft.AspNetCore.Identity.IdentityUserToken<int>>,
      IApplicationDbContext
{
    public CampusTrackDbContext(DbContextOptions<CampusTrackDbContext> options) : base(options) { }

    // ---- people and identity -----------------------------------------------------
    public DbSet<School> Schools => Set<School>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<UserPermission> UserPermissions => Set<UserPermission>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<LoginAttempt> LoginAttempts => Set<LoginAttempt>();
    public DbSet<Student> Students => Set<Student>();
    public DbSet<Teacher> Teachers => Set<Teacher>();
    public DbSet<Guardian> Guardians => Set<Guardian>();
    public DbSet<GuardianStudent> GuardianStudents => Set<GuardianStudent>();
    public DbSet<StaffMember> StaffMembers => Set<StaffMember>();

    // ---- academics ---------------------------------------------------------------
    public DbSet<AcademicSession> AcademicSessions => Set<AcademicSession>();
    public DbSet<Term> Terms => Set<Term>();
    public DbSet<Course> Courses => Set<Course>();
    public DbSet<CourseSubject> CourseSubjects => Set<CourseSubject>();
    public DbSet<SchoolClass> SchoolClasses => Set<SchoolClass>();
    public DbSet<Section> Sections => Set<Section>();
    public DbSet<Subject> Subjects => Set<Subject>();
    public DbSet<Enrollment> Enrollments => Set<Enrollment>();
    public DbSet<TeachingAssignment> TeachingAssignments => Set<TeachingAssignment>();

    // ---- facilities and scheduling ------------------------------------------------
    public DbSet<Classroom> Classrooms => Set<Classroom>();
    public DbSet<TimetablePeriod> TimetablePeriods => Set<TimetablePeriod>();
    public DbSet<TimetableSlot> TimetableSlots => Set<TimetableSlot>();
    public DbSet<SchoolHoliday> SchoolHolidays => Set<SchoolHoliday>();

    // ---- RFID ---------------------------------------------------------------------
    public DbSet<RfidTag> RfidTags => Set<RfidTag>();
    public DbSet<RfidLocation> RfidLocations => Set<RfidLocation>();
    public DbSet<RfidReader> RfidReaders => Set<RfidReader>();
    public DbSet<ReaderAntenna> ReaderAntennas => Set<ReaderAntenna>();
    public DbSet<RfidRawRead> RfidRawReads => Set<RfidRawRead>();
    public DbSet<RfidEvent> RfidEvents => Set<RfidEvent>();
    public DbSet<RfidDeadLetter> RfidDeadLetters => Set<RfidDeadLetter>();
    public DbSet<DeviceLog> DeviceLogs => Set<DeviceLog>();

    // ---- attendance ---------------------------------------------------------------
    public DbSet<DailyAttendance> DailyAttendances => Set<DailyAttendance>();
    public DbSet<SessionAttendance> SessionAttendances => Set<SessionAttendance>();
    public DbSet<ClassroomPresence> ClassroomPresences => Set<ClassroomPresence>();
    public DbSet<StudentPresence> StudentPresences => Set<StudentPresence>();
    public DbSet<AttendanceCorrection> AttendanceCorrections => Set<AttendanceCorrection>();

    // ---- assessment ---------------------------------------------------------------
    public DbSet<Assignment> Assignments => Set<Assignment>();
    public DbSet<AssignmentTarget> AssignmentTargets => Set<AssignmentTarget>();
    public DbSet<AssignmentAttachment> AssignmentAttachments => Set<AssignmentAttachment>();
    public DbSet<AssignmentSubmission> AssignmentSubmissions => Set<AssignmentSubmission>();
    public DbSet<SubmissionFile> SubmissionFiles => Set<SubmissionFile>();
    public DbSet<Quiz> Quizzes => Set<Quiz>();
    public DbSet<QuizQuestion> QuizQuestions => Set<QuizQuestion>();
    public DbSet<QuizOption> QuizOptions => Set<QuizOption>();
    public DbSet<QuizAttempt> QuizAttempts => Set<QuizAttempt>();
    public DbSet<QuizAnswer> QuizAnswers => Set<QuizAnswer>();
    public DbSet<Exam> Exams => Set<Exam>();
    public DbSet<ExamSchedule> ExamSchedules => Set<ExamSchedule>();
    public DbSet<ExamResult> ExamResults => Set<ExamResult>();
    public DbSet<GradeScale> GradeScales => Set<GradeScale>();
    public DbSet<GradeBand> GradeBands => Set<GradeBand>();
    public DbSet<GradeRecord> GradeRecords => Set<GradeRecord>();
    public DbSet<ProgressNote> ProgressNotes => Set<ProgressNote>();

    // ---- communication -------------------------------------------------------------
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<NotificationDelivery> NotificationDeliveries => Set<NotificationDelivery>();
    public DbSet<NotificationPreference> NotificationPreferences => Set<NotificationPreference>();
    public DbSet<DeviceToken> DeviceTokens => Set<DeviceToken>();
    public DbSet<Announcement> Announcements => Set<Announcement>();
    public DbSet<AnnouncementTarget> AnnouncementTargets => Set<AnnouncementTarget>();
    public DbSet<SchoolEvent> SchoolEvents => Set<SchoolEvent>();
    public DbSet<LeaveRequest> LeaveRequests => Set<LeaveRequest>();
    public DbSet<DailyStudentReport> DailyStudentReports => Set<DailyStudentReport>();
    public DbSet<GuardianFeedback> GuardianFeedbacks => Set<GuardianFeedback>();

    // ---- operations -----------------------------------------------------------------
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<SystemLog> SystemLogs => Set<SystemLog>();
    public DbSet<SystemSetting> SystemSettings => Set<SystemSetting>();

    public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
        => Database.BeginTransactionAsync(cancellationToken);

    public IQueryable<TEntity> QueryIgnoringFilters<TEntity>() where TEntity : class
        => Set<TEntity>().IgnoreQueryFilters();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.ApplyConfigurationsFromAssembly(typeof(CampusTrackDbContext).Assembly);

        RenameIdentityTables(builder);
        ApplySoftDeleteFilters(builder);
        ApplyConventions(builder);
    }

    /// <summary>Identity's default names sit oddly beside the domain tables; give them house style.</summary>
    private static void RenameIdentityTables(ModelBuilder builder)
    {
        builder.Entity<ApplicationUser>().ToTable("users");
        builder.Entity<ApplicationRole>().ToTable("roles");

        builder.Entity<ApplicationUserRole>(userRole =>
        {
            userRole.ToTable("user_roles");

            // The User and Role navigations must be bound to the existing composite-key
            // columns. Left to convention, EF treats them as separate relationships and
            // silently adds shadow UserId1/RoleId1 columns alongside the real ones.
            userRole.HasOne(ur => ur.User)
                .WithMany()
                .HasForeignKey(ur => ur.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            userRole.HasOne(ur => ur.Role)
                .WithMany()
                .HasForeignKey(ur => ur.RoleId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<Microsoft.AspNetCore.Identity.IdentityUserClaim<int>>().ToTable("user_claims");
        builder.Entity<Microsoft.AspNetCore.Identity.IdentityUserLogin<int>>().ToTable("user_logins");
        builder.Entity<Microsoft.AspNetCore.Identity.IdentityRoleClaim<int>>().ToTable("role_claims");
        builder.Entity<Microsoft.AspNetCore.Identity.IdentityUserToken<int>>().ToTable("user_tokens");
    }

    /// <summary>
    /// Hides soft-deleted rows from every query automatically. Applied by reflection so a new
    /// entity cannot be added without inheriting the behaviour.
    /// </summary>
    private static void ApplySoftDeleteFilters(ModelBuilder builder)
    {
        foreach (var entityType in builder.Model.GetEntityTypes())
        {
            if (!typeof(ISoftDeletable).IsAssignableFrom(entityType.ClrType)) continue;
            if (entityType.BaseType is not null) continue;   // TPH children inherit the root's filter

            var parameter = Expression.Parameter(entityType.ClrType, "e");
            var property = Expression.Property(parameter, nameof(ISoftDeletable.IsDeleted));
            var filter = Expression.Lambda(Expression.Not(property), parameter);
            entityType.SetQueryFilter(filter);
        }

        ApplyInheritedSoftDeleteFilters(builder);
    }

    /// <summary>
    /// Extends soft deletion to child rows that are not themselves soft-deletable.
    ///
    /// Attachments, quiz options, grade bands and antenna rows have no IsDeleted column of
    /// their own - they only exist as part of their parent. Without a matching filter, EF
    /// warns (correctly) that a child can outlive a filtered-out parent, and queries starting
    /// from the child would resurrect data that the school considers deleted. This walks each
    /// required relationship to a soft-deletable parent and mirrors the parent's filter down.
    /// </summary>
    private static void ApplyInheritedSoftDeleteFilters(ModelBuilder builder)
    {
        foreach (var entityType in builder.Model.GetEntityTypes())
        {
            if (entityType.BaseType is not null) continue;
            if (typeof(ISoftDeletable).IsAssignableFrom(entityType.ClrType)) continue;
            if (entityType.GetQueryFilter() is not null) continue;      // configured by hand already
            if (entityType.IsOwned()) continue;

            // Only follow required relationships. An optional parent legitimately may not
            // exist, so its deletion should not hide the child.
            var parentNavigation = entityType.GetForeignKeys()
                .Where(fk => fk.IsRequired
                             && fk.DependentToPrincipal is not null
                             && typeof(ISoftDeletable).IsAssignableFrom(fk.PrincipalEntityType.ClrType))
                .Select(fk => fk.DependentToPrincipal!)
                .FirstOrDefault();

            if (parentNavigation is null) continue;

            // e => !e.Parent.IsDeleted
            var parameter = Expression.Parameter(entityType.ClrType, "e");
            var parent = Expression.Property(parameter, parentNavigation.PropertyInfo!);
            var isDeleted = Expression.Property(parent, nameof(ISoftDeletable.IsDeleted));
            entityType.SetQueryFilter(Expression.Lambda(Expression.Not(isDeleted), parameter));
        }
    }

    private static void ApplyConventions(ModelBuilder builder)
    {
        foreach (var entityType in builder.Model.GetEntityTypes())
        {
            // Money and marks: fixed precision, never float. 6,2 covers 0-9999.99 marks.
            foreach (var property in entityType.GetProperties()
                         .Where(p => p.ClrType == typeof(decimal) || p.ClrType == typeof(decimal?)))
            {
                if (property.GetPrecision() is null) property.SetPrecision(9);
                if (property.GetScale() is null) property.SetScale(2);
            }

            // Unbounded strings become TEXT in MySQL, which cannot be indexed without a prefix
            // length. Default them to a sane varchar so indexes and row size stay predictable.
            foreach (var property in entityType.GetProperties()
                         .Where(p => p.ClrType == typeof(string) && p.GetMaxLength() is null))
            {
                property.SetMaxLength(256);
            }

            // Restrict by default: an accidental cascade across this graph would delete a
            // student's entire history. Cascades are opted into explicitly per relationship.
            foreach (var fk in entityType.GetForeignKeys()
                         .Where(fk => fk.DeleteBehavior == DeleteBehavior.Cascade && !fk.IsOwnership))
            {
                fk.DeleteBehavior = DeleteBehavior.Restrict;
            }
        }
    }
}
