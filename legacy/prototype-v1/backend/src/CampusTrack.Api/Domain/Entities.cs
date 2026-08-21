namespace CampusTrack.Api.Domain;

public static class Roles
{
    public const string Admin = "Admin";
    public const string Teacher = "Teacher";
    public const string Parent = "Parent";
    public const string Student = "Student";
}

public class User
{
    public int Id { get; set; }
    public string Username { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string Role { get; set; } = Roles.Student;
    public string FullName { get; set; } = "";
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? FcmToken { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class Semester
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public bool IsCurrent { get; set; }
}

public class SchoolClass
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public List<Section> Sections { get; set; } = new();
}

public class Section
{
    public int Id { get; set; }
    public int ClassId { get; set; }
    public SchoolClass? Class { get; set; }
    public string Name { get; set; } = "";
    public bool IsActive { get; set; } = true;
}

public class Parent
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public User? User { get; set; }
    public List<Student> Students { get; set; } = new();
}

public class Teacher
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public User? User { get; set; }
    public string? Subject { get; set; }
}

public class Student
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public User? User { get; set; }
    public string RegNo { get; set; } = "";
    public string? RfidEpc { get; set; }
    public int? SectionId { get; set; }
    public Section? Section { get; set; }
    public int? ParentId { get; set; }
    public Parent? Parent { get; set; }
}

public enum RoomType { Gate, Classroom, Laboratory, Library, DiscussionRoom, Auditorium, Other }

public class Room
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public RoomType RoomType { get; set; }
    public bool IsActive { get; set; } = true;
}

public class RfidReader
{
    public int Id { get; set; }
    public string ReaderCode { get; set; } = "";
    public int RoomId { get; set; }
    public Room? Room { get; set; }
    public int AntennaCount { get; set; } = 2;   // 3 at gates, 2 in rooms
    public bool IsActive { get; set; } = true;
}

public class RawRfidRead
{
    public long Id { get; set; }
    public int ReaderId { get; set; }
    public int AntennaNo { get; set; }
    public string Epc { get; set; } = "";
    public DateTime ReadTime { get; set; }
    public bool Processed { get; set; }
}

public enum Direction { Entry, Exit }

public class AttendanceEvent
{
    public long Id { get; set; }
    public int StudentId { get; set; }
    public Student? Student { get; set; }
    public int RoomId { get; set; }
    public Room? Room { get; set; }
    public Direction Direction { get; set; }
    public DateTime EventTime { get; set; }
    public string Source { get; set; } = "RFID";
}

public class ScheduleEntry
{
    public int Id { get; set; }
    public int SemesterId { get; set; }
    public Semester? Semester { get; set; }
    public int SectionId { get; set; }
    public Section? Section { get; set; }
    public int DayOfWeek { get; set; }           // 1=Mon ... 7=Sun
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
    public string Subject { get; set; } = "";
    public int? TeacherId { get; set; }
    public Teacher? Teacher { get; set; }
    public int? RoomId { get; set; }
    public Room? Room { get; set; }
}

public class ActivityReport
{
    public int Id { get; set; }
    public int StudentId { get; set; }
    public Student? Student { get; set; }
    public int TeacherId { get; set; }
    public Teacher? Teacher { get; set; }
    public DateOnly ReportDate { get; set; }
    public string Category { get; set; } = "Academic"; // Academic|Behaviour|Sports|HomeworkStatus|TestResult|Other
    public string Title { get; set; } = "";
    public string? Remarks { get; set; }
    public string? Grade { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public static class FeedbackCategories
{
    public static readonly string[] All =
        { "Teaching", "Homework", "Facilities", "Transport", "Discipline", "Suggestion", "Complaint" };
}

public class ParentFeedback
{
    public int Id { get; set; }
    public int ParentId { get; set; }
    public Parent? Parent { get; set; }
    public int StudentId { get; set; }
    public Student? Student { get; set; }
    public string Category { get; set; } = "Suggestion";
    public string Message { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string Status { get; set; } = "Open";       // Open | Replied | Closed
    public string? Reply { get; set; }
    public DateTime? RepliedAt { get; set; }
}

public class StudentUpload
{
    public int Id { get; set; }
    public int StudentId { get; set; }
    public Student? Student { get; set; }
    public string UploadType { get; set; } = "Project"; // Project | Activity | Thesis
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public string FilePath { get; set; } = "";
    public string OriginalName { get; set; } = "";
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
    public string Status { get; set; } = "Submitted";   // Submitted | Reviewed | Approved | Rejected
    public string? TeacherRemarks { get; set; }
}

public class Assignment
{
    public int Id { get; set; }
    public int SectionId { get; set; }
    public Section? Section { get; set; }
    public int TeacherId { get; set; }
    public Teacher? Teacher { get; set; }
    public string DocType { get; set; } = "Assignment"; // Assignment | Notes
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public string FilePath { get; set; } = "";
    public string OriginalName { get; set; } = "";
    public Guid QrToken { get; set; } = Guid.NewGuid();
    public DateOnly? DueDate { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class Notification
{
    public long Id { get; set; }
    public int UserId { get; set; }
    public string NotifType { get; set; } = "General";
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
    public string? DataJson { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool IsRead { get; set; }
}
