using CampusTrack.Domain.Academics;
using CampusTrack.Domain.Assessment;
using CampusTrack.Domain.Attendance;
using CampusTrack.Domain.Auditing;
using CampusTrack.Domain.Communication;
using CampusTrack.Domain.Facilities;
using CampusTrack.Domain.Identity;
using CampusTrack.Domain.People;
using CampusTrack.Domain.Rfid;
using CampusTrack.Domain.Scheduling;
using CampusTrack.Domain.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace CampusTrack.Application.Common.Interfaces;

/// <summary>
/// The persistence surface the application layer is allowed to see. Exposing DbSets rather
/// than a repository per entity is deliberate: EF Core is already a unit of work plus a
/// repository, and hiding LINQ behind hand-written repositories would cost the composable
/// filtering and projection that every list screen in this product depends on. Repositories
/// still appear where they earn their keep (see the RFID ingestion path).
/// </summary>
public interface IApplicationDbContext
{
    // people and identity
    DbSet<School> Schools { get; }
    DbSet<ApplicationUser> Users { get; }
    DbSet<ApplicationRole> Roles { get; }
    DbSet<Permission> Permissions { get; }
    DbSet<RolePermission> RolePermissions { get; }
    DbSet<UserPermission> UserPermissions { get; }
    DbSet<RefreshToken> RefreshTokens { get; }
    DbSet<LoginAttempt> LoginAttempts { get; }
    DbSet<Student> Students { get; }
    DbSet<Teacher> Teachers { get; }
    DbSet<Guardian> Guardians { get; }
    DbSet<GuardianStudent> GuardianStudents { get; }
    DbSet<StaffMember> StaffMembers { get; }

    // academics
    DbSet<AcademicSession> AcademicSessions { get; }
    DbSet<Term> Terms { get; }
    DbSet<Course> Courses { get; }
    DbSet<CourseSubject> CourseSubjects { get; }
    DbSet<SchoolClass> SchoolClasses { get; }
    DbSet<Section> Sections { get; }
    DbSet<Subject> Subjects { get; }
    DbSet<Enrollment> Enrollments { get; }
    DbSet<TeachingAssignment> TeachingAssignments { get; }

    // facilities and scheduling
    DbSet<Classroom> Classrooms { get; }
    DbSet<TimetablePeriod> TimetablePeriods { get; }
    DbSet<TimetableSlot> TimetableSlots { get; }
    DbSet<SchoolHoliday> SchoolHolidays { get; }

    // RFID
    DbSet<RfidTag> RfidTags { get; }
    DbSet<RfidLocation> RfidLocations { get; }
    DbSet<RfidReader> RfidReaders { get; }
    DbSet<ReaderAntenna> ReaderAntennas { get; }
    DbSet<RfidRawRead> RfidRawReads { get; }
    DbSet<RfidEvent> RfidEvents { get; }
    DbSet<RfidDeadLetter> RfidDeadLetters { get; }
    DbSet<DeviceLog> DeviceLogs { get; }

    // attendance
    DbSet<DailyAttendance> DailyAttendances { get; }
    DbSet<SessionAttendance> SessionAttendances { get; }
    DbSet<ClassroomPresence> ClassroomPresences { get; }
    DbSet<StudentPresence> StudentPresences { get; }
    DbSet<AttendanceCorrection> AttendanceCorrections { get; }

    // assessment
    DbSet<Assignment> Assignments { get; }
    DbSet<AssignmentTarget> AssignmentTargets { get; }
    DbSet<AssignmentAttachment> AssignmentAttachments { get; }
    DbSet<AssignmentSubmission> AssignmentSubmissions { get; }
    DbSet<SubmissionFile> SubmissionFiles { get; }
    DbSet<Quiz> Quizzes { get; }
    DbSet<QuizQuestion> QuizQuestions { get; }
    DbSet<QuizOption> QuizOptions { get; }
    DbSet<QuizAttempt> QuizAttempts { get; }
    DbSet<QuizAnswer> QuizAnswers { get; }
    DbSet<Exam> Exams { get; }
    DbSet<ExamSchedule> ExamSchedules { get; }
    DbSet<ExamResult> ExamResults { get; }
    DbSet<GradeScale> GradeScales { get; }
    DbSet<GradeBand> GradeBands { get; }
    DbSet<GradeRecord> GradeRecords { get; }
    DbSet<ProgressNote> ProgressNotes { get; }

    // communication
    DbSet<Notification> Notifications { get; }
    DbSet<NotificationDelivery> NotificationDeliveries { get; }
    DbSet<NotificationPreference> NotificationPreferences { get; }
    DbSet<DeviceToken> DeviceTokens { get; }
    DbSet<Announcement> Announcements { get; }
    DbSet<AnnouncementTarget> AnnouncementTargets { get; }
    DbSet<SchoolEvent> SchoolEvents { get; }
    DbSet<LeaveRequest> LeaveRequests { get; }
    DbSet<DailyStudentReport> DailyStudentReports { get; }
    DbSet<GuardianFeedback> GuardianFeedbacks { get; }

    // operations
    DbSet<AuditLog> AuditLogs { get; }
    DbSet<SystemLog> SystemLogs { get; }
    DbSet<SystemSetting> SystemSettings { get; }

    DatabaseFacade Database { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
    Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default);

    /// <summary>Escape hatch for the rare query that must bypass the soft-delete filter.</summary>
    IQueryable<TEntity> QueryIgnoringFilters<TEntity>() where TEntity : class;
}
