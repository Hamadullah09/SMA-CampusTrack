/* =====================================================================
   CampusTrack - MS SQL Server schema
   RFID-based school/college attendance, scheduling & parent engagement
   Target: SQL Server 2017+
   ===================================================================== */

IF DB_ID('CampusTrack') IS NULL
    CREATE DATABASE CampusTrack;
GO
USE CampusTrack;
GO

/* ---------- Users & roles ------------------------------------------ */
CREATE TABLE Users (
    Id            INT IDENTITY(1,1) PRIMARY KEY,
    Username      NVARCHAR(64)  NOT NULL UNIQUE,
    PasswordHash  NVARCHAR(512) NOT NULL,
    Role          NVARCHAR(16)  NOT NULL,          -- Admin | Teacher | Parent | Student
    FullName      NVARCHAR(128) NOT NULL,
    Email         NVARCHAR(128) NULL,
    Phone         NVARCHAR(32)  NULL,
    FcmToken      NVARCHAR(512) NULL,              -- Firebase device token for push
    IsActive      BIT           NOT NULL DEFAULT 1,
    CreatedAt     DATETIME2     NOT NULL DEFAULT SYSUTCDATETIME()
);

/* ---------- Academic structure (modular: admin adds/removes) ------- */
CREATE TABLE Semesters (
    Id        INT IDENTITY(1,1) PRIMARY KEY,
    Name      NVARCHAR(64) NOT NULL,               -- e.g. 'Fall 2026'
    StartDate DATE NOT NULL,
    EndDate   DATE NOT NULL,
    IsCurrent BIT  NOT NULL DEFAULT 0
);

CREATE TABLE SchoolClasses (                        -- 'Grade 7', 'BSCS-3' ...
    Id       INT IDENTITY(1,1) PRIMARY KEY,
    Name     NVARCHAR(64) NOT NULL,
    IsActive BIT NOT NULL DEFAULT 1
);

CREATE TABLE Sections (                             -- 'A', 'B', 'Blue' ...
    Id       INT IDENTITY(1,1) PRIMARY KEY,
    ClassId  INT NOT NULL REFERENCES SchoolClasses(Id),
    Name     NVARCHAR(64) NOT NULL,
    IsActive BIT NOT NULL DEFAULT 1,
    CONSTRAINT UQ_Section UNIQUE (ClassId, Name)
);

/* ---------- People ------------------------------------------------- */
CREATE TABLE Parents (
    Id     INT IDENTITY(1,1) PRIMARY KEY,
    UserId INT NOT NULL UNIQUE REFERENCES Users(Id)
);

CREATE TABLE Teachers (
    Id       INT IDENTITY(1,1) PRIMARY KEY,
    UserId   INT NOT NULL UNIQUE REFERENCES Users(Id),
    Subject  NVARCHAR(128) NULL
);

CREATE TABLE Students (
    Id         INT IDENTITY(1,1) PRIMARY KEY,
    UserId     INT NOT NULL UNIQUE REFERENCES Users(Id),
    RegNo      NVARCHAR(32) NOT NULL UNIQUE,
    RfidEpc    NVARCHAR(64) NULL,                   -- EPC of tag in the ID card
    SectionId  INT NULL REFERENCES Sections(Id),
    ParentId   INT NULL REFERENCES Parents(Id)
);
CREATE UNIQUE INDEX UX_Students_Epc ON Students(RfidEpc) WHERE RfidEpc IS NOT NULL;

/* ---------- Rooms, readers & antennas ------------------------------ */
CREATE TABLE Rooms (
    Id       INT IDENTITY(1,1) PRIMARY KEY,
    Name     NVARCHAR(64) NOT NULL,
    RoomType NVARCHAR(24) NOT NULL,                 -- Gate | Classroom | Laboratory | Library | DiscussionRoom | Auditorium | Other
    IsActive BIT NOT NULL DEFAULT 1
);

CREATE TABLE RfidReaders (
    Id           INT IDENTITY(1,1) PRIMARY KEY,
    ReaderCode   NVARCHAR(64) NOT NULL UNIQUE,      -- id the physical reader sends
    RoomId       INT NOT NULL REFERENCES Rooms(Id),
    AntennaCount INT NOT NULL DEFAULT 2,            -- 3 at gates, 2 in rooms
    IsActive     BIT NOT NULL DEFAULT 1
);

/* every raw antenna hit, kept for audit / re-processing */
CREATE TABLE RawRfidReads (
    Id        BIGINT IDENTITY(1,1) PRIMARY KEY,
    ReaderId  INT NOT NULL REFERENCES RfidReaders(Id),
    AntennaNo INT NOT NULL,
    Epc       NVARCHAR(64) NOT NULL,
    ReadTime  DATETIME2 NOT NULL,
    Processed BIT NOT NULL DEFAULT 0
);
CREATE INDEX IX_RawReads_Reader_Epc_Time ON RawRfidReads(ReaderId, Epc, ReadTime);

/* resolved movement events:
   gate:  antennas 1->2->3 = Entry, 3->2->1 = Exit
   rooms: antennas 1->2    = Entry, 2->1    = Exit                     */
CREATE TABLE AttendanceEvents (
    Id        BIGINT IDENTITY(1,1) PRIMARY KEY,
    StudentId INT NOT NULL REFERENCES Students(Id),
    RoomId    INT NOT NULL REFERENCES Rooms(Id),
    Direction NVARCHAR(8) NOT NULL,                 -- Entry | Exit
    EventTime DATETIME2 NOT NULL,
    Source    NVARCHAR(16) NOT NULL DEFAULT 'RFID'  -- RFID | Manual
);
CREATE INDEX IX_Att_Student_Time ON AttendanceEvents(StudentId, EventTime);

/* ---------- Timetable (full semester) ------------------------------ */
CREATE TABLE ScheduleEntries (
    Id         INT IDENTITY(1,1) PRIMARY KEY,
    SemesterId INT NOT NULL REFERENCES Semesters(Id),
    SectionId  INT NOT NULL REFERENCES Sections(Id),
    DayOfWeek  INT NOT NULL,                        -- 1=Mon ... 7=Sun
    StartTime  TIME NOT NULL,
    EndTime    TIME NOT NULL,
    Subject    NVARCHAR(128) NOT NULL,
    TeacherId  INT NULL REFERENCES Teachers(Id),
    RoomId     INT NULL REFERENCES Rooms(Id)
);

/* ---------- Teacher-entered activity / progress reports ------------ */
CREATE TABLE ActivityReports (
    Id         INT IDENTITY(1,1) PRIMARY KEY,
    StudentId  INT NOT NULL REFERENCES Students(Id),
    TeacherId  INT NOT NULL REFERENCES Teachers(Id),
    ReportDate DATE NOT NULL,
    Category   NVARCHAR(32) NOT NULL,               -- Academic | Behaviour | Sports | HomeworkStatus | TestResult | Other
    Title      NVARCHAR(128) NOT NULL,
    Remarks    NVARCHAR(MAX) NULL,
    Grade      NVARCHAR(16) NULL,
    CreatedAt  DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
);

/* ---------- Parent feedback (fixed columns/categories) ------------- */
CREATE TABLE ParentFeedback (
    Id        INT IDENTITY(1,1) PRIMARY KEY,
    ParentId  INT NOT NULL REFERENCES Parents(Id),
    StudentId INT NOT NULL REFERENCES Students(Id),
    Category  NVARCHAR(32) NOT NULL,                -- Teaching | Homework | Facilities | Transport | Discipline | Suggestion | Complaint
    Message   NVARCHAR(MAX) NOT NULL,
    CreatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    Status    NVARCHAR(16) NOT NULL DEFAULT 'Open', -- Open | Replied | Closed
    Reply     NVARCHAR(MAX) NULL,
    RepliedAt DATETIME2 NULL
);

/* ---------- Student uploads (projects / activities / theses) ------- */
CREATE TABLE StudentUploads (
    Id             INT IDENTITY(1,1) PRIMARY KEY,
    StudentId      INT NOT NULL REFERENCES Students(Id),
    UploadType     NVARCHAR(16) NOT NULL,           -- Project | Activity | Thesis
    Title          NVARCHAR(128) NOT NULL,
    Description    NVARCHAR(MAX) NULL,
    FilePath       NVARCHAR(512) NOT NULL,
    OriginalName   NVARCHAR(256) NOT NULL,
    UploadedAt     DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    Status         NVARCHAR(16) NOT NULL DEFAULT 'Submitted', -- Submitted | Reviewed | Approved | Rejected
    TeacherRemarks NVARCHAR(MAX) NULL
);

/* ---------- Assignments & notes (QR-code downloadable) ------------- */
CREATE TABLE Assignments (
    Id           INT IDENTITY(1,1) PRIMARY KEY,
    SectionId    INT NOT NULL REFERENCES Sections(Id),
    TeacherId    INT NOT NULL REFERENCES Teachers(Id),
    DocType      NVARCHAR(16) NOT NULL,             -- Assignment | Notes
    Title        NVARCHAR(128) NOT NULL,
    Description  NVARCHAR(MAX) NULL,
    FilePath     NVARCHAR(512) NOT NULL,
    OriginalName NVARCHAR(256) NOT NULL,
    QrToken      UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(), -- encoded in the QR image
    DueDate      DATE NULL,
    CreatedAt    DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
);
CREATE UNIQUE INDEX UX_Assignments_Qr ON Assignments(QrToken);

/* ---------- Notifications (mirror of every push sent) -------------- */
CREATE TABLE Notifications (
    Id        BIGINT IDENTITY(1,1) PRIMARY KEY,
    UserId    INT NOT NULL REFERENCES Users(Id),
    NotifType NVARCHAR(24) NOT NULL,                -- GateEntry | GateExit | DailySummary | WeeklySummary | Activity | FeedbackReply | Assignment | General
    Title     NVARCHAR(128) NOT NULL,
    Body      NVARCHAR(MAX) NOT NULL,
    DataJson  NVARCHAR(MAX) NULL,
    CreatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    IsRead    BIT NOT NULL DEFAULT 0
);
CREATE INDEX IX_Notif_User ON Notifications(UserId, CreatedAt DESC);
GO
