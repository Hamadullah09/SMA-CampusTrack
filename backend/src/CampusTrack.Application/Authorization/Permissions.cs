namespace CampusTrack.Application.Authorization;

/// <summary>
/// The full capability catalogue. Every protected endpoint names one of these constants;
/// nothing in the codebase authorises on a role name, so a school can define its own roles
/// ("Head of Year", "Exams Officer") and grant exactly what that job needs.
/// </summary>
public static class Permissions
{
    public const string Prefix = "permission";

    public static class Dashboard
    {
        public const string ViewAdmin = "dashboard.admin.view";
        public const string ViewTeacher = "dashboard.teacher.view";
        public const string ViewStudent = "dashboard.student.view";
        public const string ViewGuardian = "dashboard.guardian.view";
    }

    public static class Students
    {
        public const string View = "students.view";
        public const string ViewOwn = "students.view.own";
        public const string Create = "students.create";
        public const string Edit = "students.edit";
        public const string Delete = "students.delete";
        public const string Export = "students.export";
        public const string Import = "students.import";
    }

    public static class Teachers
    {
        public const string View = "teachers.view";
        public const string Create = "teachers.create";
        public const string Edit = "teachers.edit";
        public const string Delete = "teachers.delete";
        public const string Export = "teachers.export";
    }

    public static class Guardians
    {
        public const string View = "guardians.view";
        public const string Create = "guardians.create";
        public const string Edit = "guardians.edit";
        public const string Delete = "guardians.delete";
        /// <summary>Approving a guardian-child link is what unlocks a parent's access to a child.</summary>
        public const string ManageLinks = "guardians.links.manage";
    }

    public static class Staff
    {
        public const string View = "staff.view";
        public const string Create = "staff.create";
        public const string Edit = "staff.edit";
        public const string Delete = "staff.delete";
    }

    public static class Academics
    {
        public const string ViewClasses = "academics.classes.view";
        public const string ManageClasses = "academics.classes.manage";
        public const string ViewSections = "academics.sections.view";
        public const string ManageSections = "academics.sections.manage";
        public const string ViewCourses = "academics.courses.view";
        public const string ManageCourses = "academics.courses.manage";
        public const string ViewSubjects = "academics.subjects.view";
        public const string ManageSubjects = "academics.subjects.manage";
        public const string ViewSessions = "academics.sessions.view";
        public const string ManageSessions = "academics.sessions.manage";
        public const string ViewEnrollments = "academics.enrollments.view";
        public const string ManageEnrollments = "academics.enrollments.manage";
        public const string ManageTeachingAssignments = "academics.assignments.manage";
    }

    public static class Classrooms
    {
        public const string View = "classrooms.view";
        public const string Manage = "classrooms.manage";
    }

    public static class Timetable
    {
        public const string View = "timetable.view";
        public const string ViewOwn = "timetable.view.own";
        public const string Manage = "timetable.manage";
    }

    public static class Attendance
    {
        public const string View = "attendance.view";
        public const string ViewOwn = "attendance.view.own";
        public const string ViewAssigned = "attendance.view.assigned";
        public const string Mark = "attendance.mark";
        /// <summary>Overriding an RFID-derived record - always audited.</summary>
        public const string Correct = "attendance.correct";
        public const string Configure = "attendance.configure";
        public const string Export = "attendance.export";
    }

    public static class Rfid
    {
        public const string ViewEvents = "rfid.events.view";
        public const string ViewOwnEvents = "rfid.events.view.own";
        public const string ViewReaders = "rfid.readers.view";
        public const string ManageReaders = "rfid.readers.manage";
        public const string ViewLocations = "rfid.locations.view";
        public const string ManageLocations = "rfid.locations.manage";
        public const string ViewTags = "rfid.tags.view";
        public const string ManageTags = "rfid.tags.manage";
        public const string Monitor = "rfid.monitor";
        public const string Configure = "rfid.configure";
        /// <summary>Injecting synthetic events - deliberately separate from real ingestion.</summary>
        public const string Simulate = "rfid.simulate";
        public const string ReplayDeadLetters = "rfid.deadletters.replay";
    }

    public static class Assignments
    {
        public const string View = "assignments.view";
        public const string ViewOwn = "assignments.view.own";
        public const string Create = "assignments.create";
        public const string Edit = "assignments.edit";
        public const string Delete = "assignments.delete";
        public const string Submit = "assignments.submit";
        public const string Grade = "assignments.grade";
    }

    public static class Quizzes
    {
        public const string View = "quizzes.view";
        public const string ViewOwn = "quizzes.view.own";
        public const string Create = "quizzes.create";
        public const string Edit = "quizzes.edit";
        public const string Delete = "quizzes.delete";
        public const string Attempt = "quizzes.attempt";
        public const string Grade = "quizzes.grade";
        public const string Publish = "quizzes.publish";
    }

    public static class Exams
    {
        public const string View = "exams.view";
        public const string Manage = "exams.manage";
        public const string EnterResults = "exams.results.enter";
        public const string PublishResults = "exams.results.publish";
    }

    public static class Grades
    {
        public const string View = "grades.view";
        public const string ViewOwn = "grades.view.own";
        public const string ViewAssigned = "grades.view.assigned";
        public const string Manage = "grades.manage";
        public const string ManageScales = "grades.scales.manage";
        public const string Publish = "grades.publish";
    }

    public static class Notifications
    {
        public const string View = "notifications.view";
        public const string Send = "notifications.send";
        public const string Configure = "notifications.configure";
    }

    public static class Announcements
    {
        public const string View = "announcements.view";
        public const string Manage = "announcements.manage";
    }

    public static class Events
    {
        public const string View = "events.view";
        public const string Manage = "events.manage";
    }

    /// <summary>Publishing builds of the mobile app that families sideload.</summary>
    public static class MobileApp
    {
        public const string Manage = "mobileapp.manage";
    }

    public static class Leave
    {
        public const string View = "leave.view";
        public const string ViewOwn = "leave.view.own";
        public const string Request = "leave.request";
        public const string Approve = "leave.approve";
    }

    public static class Reports
    {
        public const string ViewAttendance = "reports.attendance.view";
        public const string ViewAcademic = "reports.academic.view";
        public const string ViewRfid = "reports.rfid.view";
        public const string ViewSystem = "reports.system.view";
        public const string Export = "reports.export";
    }

    public static class Users
    {
        public const string View = "users.view";
        public const string Create = "users.create";
        public const string Edit = "users.edit";
        public const string Delete = "users.delete";
        public const string ResetPassword = "users.password.reset";
        public const string Activate = "users.activate";
        public const string ManageRoles = "users.roles.manage";
        public const string ManagePermissions = "users.permissions.manage";
    }

    public static class Roles
    {
        public const string View = "roles.view";
        public const string Manage = "roles.manage";
    }

    public static class Audit
    {
        public const string View = "audit.view";
        public const string Export = "audit.export";
        public const string ViewSystemLogs = "audit.system.view";
        public const string ViewDeviceLogs = "audit.device.view";
    }

    public static class Settings
    {
        public const string View = "settings.view";
        public const string Manage = "settings.manage";
        public const string Backup = "settings.backup";
        public const string Restore = "settings.restore";
    }

    /// <summary>Built-in role names. Used only for seeding and defaults, never for authorisation.</summary>
    public static class RoleNames
    {
        public const string SuperAdmin = "SuperAdmin";
        public const string Admin = "Admin";
        public const string Teacher = "Teacher";
        public const string Student = "Student";
        public const string Guardian = "Guardian";
        public const string Staff = "Staff";

        public static readonly string[] All = [SuperAdmin, Admin, Teacher, Student, Guardian, Staff];
    }

    /// <summary>Every permission constant declared above, discovered by reflection.</summary>
    public static IReadOnlyList<PermissionDefinition> All { get; } = Discover();

    private static List<PermissionDefinition> Discover()
    {
        var results = new List<PermissionDefinition>();
        var groups = typeof(Permissions).GetNestedTypes()
            .Where(t => t.IsClass && t.Name != nameof(RoleNames));

        foreach (var group in groups)
        {
            foreach (var field in group.GetFields()
                         .Where(f => f is { IsLiteral: true, IsInitOnly: false } && f.FieldType == typeof(string)))
            {
                var value = (string?)field.GetRawConstantValue();
                if (string.IsNullOrWhiteSpace(value)) continue;
                results.Add(new PermissionDefinition(value, group.Name, Humanise(field.Name, group.Name)));
            }
        }

        return results.DistinctBy(p => p.Name).OrderBy(p => p.Group).ThenBy(p => p.Name).ToList();
    }

    /// <summary>"ManageTeachingAssignments" in group "Academics" -> "Manage teaching assignments".</summary>
    private static string Humanise(string fieldName, string group)
    {
        var spaced = System.Text.RegularExpressions.Regex
            .Replace(fieldName, "(?<!^)([A-Z])", " $1")
            .ToLowerInvariant();
        return char.ToUpperInvariant(spaced[0]) + spaced[1..] + $" ({group})";
    }
}

public record PermissionDefinition(string Name, string Group, string DisplayName);
