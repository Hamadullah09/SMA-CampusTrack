namespace CampusTrack.Domain.Enums;

// ---------------------------------------------------------------- people ----

public enum PersonStatus { Pending = 0, Active = 1, Suspended = 2, Inactive = 3, Graduated = 4, Transferred = 5 }

public enum Gender { Unspecified = 0, Male = 1, Female = 2, Other = 3 }

/// <summary>How a guardian relates to a student. Drives wording in notifications.</summary>
public enum GuardianRelationship { Parent = 0, Father = 1, Mother = 2, Guardian = 3, Grandparent = 4, Sibling = 5, Other = 6 }

// ------------------------------------------------------------- academics ----

public enum EnrollmentStatus { Active = 0, Completed = 1, Withdrawn = 2, Transferred = 3, Suspended = 4 }

public enum AcademicSessionStatus { Planned = 0, Active = 1, Closed = 2, Archived = 3 }

public enum TermType { FullYear = 0, Semester = 1, Trimester = 2, Quarter = 3 }

// -------------------------------------------------------------- RFID ---------

/// <summary>Physical role of a monitored place. Boundary locations move a student in/out of campus.</summary>
public enum LocationType
{
    MainGate = 0, ExitGate = 1, Classroom = 2, Laboratory = 3, Library = 4,
    ComputerLab = 5, StaffRoom = 6, Cafeteria = 7, Auditorium = 8,
    Playground = 9, Hostel = 10, Transport = 11, Other = 12
}

public enum ReaderStatus { Unknown = 0, Online = 1, Offline = 2, Degraded = 3, Maintenance = 4, Error = 5 }

/// <summary>How a reader decides entry vs exit.</summary>
public enum DirectionStrategy
{
    /// <summary>Antenna numbers ascend inward: first &lt; last antenna = entry. Works with any antenna count.</summary>
    AntennaOrder = 0,
    /// <summary>Each antenna is explicitly declared Outside or Inside; the transition decides direction.</summary>
    AntennaRole = 1,
    /// <summary>Reader only ever produces one direction (a dedicated in-only or out-only lane).</summary>
    Fixed = 2,
    /// <summary>Direction toggles against the student's last known presence at this location.</summary>
    PresenceToggle = 3
}

public enum AntennaRole { Unspecified = 0, Outside = 1, Inside = 2 }

public enum MovementDirection { Unknown = 0, Entry = 1, Exit = 2 }

public enum RfidTagStatus { Unassigned = 0, Active = 1, Lost = 2, Damaged = 3, Revoked = 4, Replaced = 5 }

public enum RfidEventType
{
    SchoolEntry = 0, SchoolExit = 1,
    ClassroomEntry = 2, ClassroomExit = 3,
    ZoneEntry = 4, ZoneExit = 5,
    UnknownTag = 6, Rejected = 7
}

/// <summary>Where a movement or attendance fact came from — never inferred, always recorded.</summary>
public enum EventSource { Rfid = 0, Manual = 1, Simulator = 2, Import = 3, System = 4, MobileCheckIn = 5 }

public enum RawReadState { Pending = 0, Buffered = 1, Processed = 2, Discarded = 3, Failed = 4 }

// ---------------------------------------------------------- attendance ------

public enum AttendanceStatus
{
    NotRecorded = 0, Present = 1, Absent = 2, Late = 3, EarlyLeave = 4,
    Excused = 5, Unexcused = 6, Partial = 7, Holiday = 8, Leave = 9
}

public enum PresenceState { Outside = 0, OnCampus = 1, InRoom = 2 }

// ---------------------------------------------------------- assessment -----

public enum AssignmentStatus { Draft = 0, Published = 1, Closed = 2, Archived = 3 }

public enum SubmissionStatus { NotSubmitted = 0, Submitted = 1, Late = 2, Graded = 3, Returned = 4, Resubmitted = 5 }

public enum QuizStatus { Draft = 0, Scheduled = 1, Published = 2, InProgress = 3, Closed = 4, Archived = 5 }

public enum QuestionType { MultipleChoice = 0, MultipleAnswer = 1, TrueFalse = 2, ShortAnswer = 3, Descriptive = 4 }

public enum QuizAttemptStatus { InProgress = 0, Submitted = 1, AutoSubmitted = 2, Graded = 3, Abandoned = 4 }

public enum ExamStatus { Planned = 0, Scheduled = 1, Ongoing = 2, Completed = 3, ResultsPublished = 4, Cancelled = 5 }

public enum GradeCategory { Assignment = 0, Quiz = 1, Exam = 2, Project = 3, Participation = 4, Practical = 5, Other = 6 }

// -------------------------------------------------------- communication ----

public enum NotificationCategory
{
    SchoolEntry = 0, SchoolExit = 1, ClassroomEntry = 2, ClassroomExit = 3,
    Absence = 4, LateArrival = 5, EarlyLeave = 6,
    Assignment = 7, Quiz = 8, Grade = 9, Exam = 10,
    Announcement = 11, Emergency = 12, DailyReport = 13,
    LeaveRequest = 14, Account = 15, System = 16, Feedback = 17
}

public enum NotificationPriority { Low = 0, Normal = 1, High = 2, Critical = 3 }

public enum NotificationChannel { InApp = 0, Push = 1, Email = 2, Sms = 3 }

public enum DeliveryStatus { Pending = 0, Sent = 1, Failed = 2, Skipped = 3, Retrying = 4 }

public enum DevicePlatform { Unknown = 0, Android = 1, IOS = 2, Web = 3 }

public enum AnnouncementAudience { Everyone = 0, Staff = 1, Teachers = 2, Students = 3, Guardians = 4, SpecificSections = 5 }

public enum LeaveStatus { Pending = 0, Approved = 1, Rejected = 2, Cancelled = 3 }

public enum LeaveRequesterType { Student = 0, Teacher = 1, Staff = 2 }

// ------------------------------------------------------------- auditing ----

public enum AuditAction { Create = 0, Update = 1, Delete = 2, Read = 3, Login = 4, Logout = 5, Export = 6, Execute = 7 }

public enum DeviceLogLevel { Info = 0, Warning = 1, Error = 2, Critical = 3 }

public enum SettingDataType { String = 0, Integer = 1, Decimal = 2, Boolean = 3, Time = 4, Json = 5 }
