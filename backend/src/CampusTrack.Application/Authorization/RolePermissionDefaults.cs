namespace CampusTrack.Application.Authorization;

/// <summary>
/// What each built-in role can do out of the box. These are seeded once; afterwards an
/// administrator edits grants in the UI and the seeder leaves existing roles alone, so a
/// school's customisation is never overwritten by a deployment.
/// </summary>
public static class RolePermissionDefaults
{
    public static IReadOnlyDictionary<string, string[]> Map { get; } = new Dictionary<string, string[]>
    {
        // SuperAdmin is granted every permission at seed time, including ones added later.
        [Permissions.RoleNames.SuperAdmin] = Permissions.All.Select(p => p.Name).ToArray(),

        [Permissions.RoleNames.Admin] =
        [
            Permissions.Dashboard.ViewAdmin,

            Permissions.Students.View, Permissions.Students.Create, Permissions.Students.Edit,
            Permissions.Students.Delete, Permissions.Students.Export, Permissions.Students.Import,

            Permissions.Teachers.View, Permissions.Teachers.Create, Permissions.Teachers.Edit,
            Permissions.Teachers.Delete, Permissions.Teachers.Export,

            Permissions.Guardians.View, Permissions.Guardians.Create, Permissions.Guardians.Edit,
            Permissions.Guardians.Delete, Permissions.Guardians.ManageLinks,

            Permissions.Staff.View, Permissions.Staff.Create, Permissions.Staff.Edit, Permissions.Staff.Delete,

            Permissions.Academics.ViewClasses, Permissions.Academics.ManageClasses,
            Permissions.Academics.ViewSections, Permissions.Academics.ManageSections,
            Permissions.Academics.ViewCourses, Permissions.Academics.ManageCourses,
            Permissions.Academics.ViewSubjects, Permissions.Academics.ManageSubjects,
            Permissions.Academics.ViewSessions, Permissions.Academics.ManageSessions,
            Permissions.Academics.ViewEnrollments, Permissions.Academics.ManageEnrollments,
            Permissions.Academics.ManageTeachingAssignments,

            Permissions.Classrooms.View, Permissions.Classrooms.Manage,
            Permissions.Timetable.View, Permissions.Timetable.Manage,

            Permissions.Attendance.View, Permissions.Attendance.Mark, Permissions.Attendance.Correct,
            Permissions.Attendance.Configure, Permissions.Attendance.Export,

            Permissions.Rfid.ViewEvents, Permissions.Rfid.ViewReaders, Permissions.Rfid.ManageReaders,
            Permissions.Rfid.ViewLocations, Permissions.Rfid.ManageLocations,
            Permissions.Rfid.ViewTags, Permissions.Rfid.ManageTags,
            Permissions.Rfid.Monitor, Permissions.Rfid.Configure, Permissions.Rfid.ReplayDeadLetters,

            Permissions.Assignments.View, Permissions.Assignments.Create, Permissions.Assignments.Edit,
            Permissions.Assignments.Delete, Permissions.Assignments.Grade,

            Permissions.Quizzes.View, Permissions.Quizzes.Create, Permissions.Quizzes.Edit,
            Permissions.Quizzes.Delete, Permissions.Quizzes.Grade, Permissions.Quizzes.Publish,

            Permissions.Exams.View, Permissions.Exams.Manage,
            Permissions.Exams.EnterResults, Permissions.Exams.PublishResults,

            Permissions.Grades.View, Permissions.Grades.Manage,
            Permissions.Grades.ManageScales, Permissions.Grades.Publish,

            Permissions.Notifications.View, Permissions.Notifications.Send, Permissions.Notifications.Configure,
            Permissions.Announcements.View, Permissions.Announcements.Manage,
            Permissions.Events.View, Permissions.Events.Manage,
            Permissions.MobileApp.Manage,
            Permissions.Leave.View, Permissions.Leave.Approve,

            Permissions.Reports.ViewAttendance, Permissions.Reports.ViewAcademic,
            Permissions.Reports.ViewRfid, Permissions.Reports.ViewSystem, Permissions.Reports.Export,

            Permissions.Users.View, Permissions.Users.Create, Permissions.Users.Edit, Permissions.Users.Delete,
            Permissions.Users.ResetPassword, Permissions.Users.Activate,
            Permissions.Users.ManageRoles, Permissions.Users.ManagePermissions,
            Permissions.Roles.View, Permissions.Roles.Manage,

            Permissions.Audit.View, Permissions.Audit.Export,
            Permissions.Audit.ViewSystemLogs, Permissions.Audit.ViewDeviceLogs,

            Permissions.Settings.View, Permissions.Settings.Manage, Permissions.Settings.Backup
        ],

        // A teacher sees only what they are assigned to teach. The ".assigned" and ".own"
        // permissions are the hook the services use to scope every query.
        [Permissions.RoleNames.Teacher] =
        [
            Permissions.Dashboard.ViewTeacher,
            Permissions.Students.View,
            Permissions.Timetable.ViewOwn,
            Permissions.Attendance.ViewAssigned, Permissions.Attendance.Mark,
            Permissions.Attendance.Correct, Permissions.Attendance.Export,
            Permissions.Rfid.ViewEvents,
            Permissions.Assignments.View, Permissions.Assignments.Create, Permissions.Assignments.Edit,
            Permissions.Assignments.Delete, Permissions.Assignments.Grade,
            Permissions.Quizzes.View, Permissions.Quizzes.Create, Permissions.Quizzes.Edit,
            Permissions.Quizzes.Delete, Permissions.Quizzes.Grade, Permissions.Quizzes.Publish,
            Permissions.Exams.View, Permissions.Exams.EnterResults,
            Permissions.Grades.ViewAssigned, Permissions.Grades.Manage, Permissions.Grades.Publish,
            Permissions.Notifications.View,
            Permissions.Announcements.View,
            Permissions.Events.View,
            Permissions.Leave.ViewOwn, Permissions.Leave.Request,
            Permissions.Reports.ViewAttendance, Permissions.Reports.ViewAcademic, Permissions.Reports.Export,
            Permissions.Classrooms.View,
            Permissions.Academics.ViewSections, Permissions.Academics.ViewSubjects
        ],

        [Permissions.RoleNames.Student] =
        [
            Permissions.Dashboard.ViewStudent,
            Permissions.Students.ViewOwn,
            Permissions.Timetable.ViewOwn,
            Permissions.Attendance.ViewOwn,
            Permissions.Rfid.ViewOwnEvents,
            Permissions.Assignments.ViewOwn, Permissions.Assignments.Submit,
            Permissions.Quizzes.ViewOwn, Permissions.Quizzes.Attempt,
            Permissions.Exams.View,
            Permissions.Grades.ViewOwn,
            Permissions.Notifications.View,
            Permissions.Announcements.View,
            Permissions.Events.View,
            Permissions.Leave.ViewOwn, Permissions.Leave.Request
        ],

        // A guardian's reach is bounded by their approved child links, enforced in the
        // services; the permissions here only say which screens exist for them at all.
        [Permissions.RoleNames.Guardian] =
        [
            Permissions.Dashboard.ViewGuardian,
            Permissions.Students.ViewOwn,
            Permissions.Timetable.ViewOwn,
            Permissions.Attendance.ViewOwn,
            Permissions.Rfid.ViewOwnEvents,
            Permissions.Assignments.ViewOwn,
            Permissions.Quizzes.ViewOwn,
            Permissions.Exams.View,
            Permissions.Grades.ViewOwn,
            Permissions.Notifications.View,
            Permissions.Announcements.View,
            Permissions.Events.View,
            Permissions.Leave.ViewOwn, Permissions.Leave.Request
        ],

        [Permissions.RoleNames.Staff] =
        [
            Permissions.Students.View,
            Permissions.Attendance.View,
            Permissions.Rfid.ViewEvents, Permissions.Rfid.ViewReaders, Permissions.Rfid.Monitor,
            Permissions.Notifications.View,
            Permissions.Announcements.View,
            Permissions.Events.View,
            Permissions.Leave.ViewOwn, Permissions.Leave.Request,
            Permissions.Reports.ViewAttendance
        ]
    };
}
