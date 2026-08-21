namespace CampusTrack.Api.Dtos;

// ---- auth ----------------------------------------------------------
public record LoginRequest(string Username, string Password);
public record LoginResponse(string Token, string Role, string FullName, int UserId,
                            int? StudentId, int? ParentId, int? TeacherId);
public record FcmTokenRequest(string Token);

// ---- admin: people -------------------------------------------------
public record CreateUserRequest(string Username, string Password, string FullName,
                                string? Email, string? Phone);
public record CreateStudentRequest(CreateUserRequest User, string RegNo, string? RfidEpc,
                                   int? SectionId, int? ParentId);
public record CreateParentRequest(CreateUserRequest User);
public record CreateTeacherRequest(CreateUserRequest User, string? Subject);

// ---- admin: structure ---------------------------------------------
public record NameRequest(string Name);
public record SectionRequest(int ClassId, string Name);
public record RoomRequest(string Name, string RoomType);
public record ReaderRequest(string ReaderCode, int RoomId, int AntennaCount);
public record SemesterRequest(string Name, DateOnly StartDate, DateOnly EndDate, bool IsCurrent);

// ---- schedule ------------------------------------------------------
public record ScheduleEntryRequest(int SemesterId, int SectionId, int DayOfWeek,
                                   TimeOnly StartTime, TimeOnly EndTime, string Subject,
                                   int? TeacherId, int? RoomId);

// ---- rfid ingest ---------------------------------------------------
/// One antenna hit as pushed by the fixed reader (or its middleware).
public record RfidReadDto(string ReaderCode, int AntennaNo, string Epc, DateTime? ReadTime);
public record RfidBatchRequest(List<RfidReadDto> Reads);

// ---- activity / feedback / uploads --------------------------------
public record ActivityReportRequest(int StudentId, DateOnly ReportDate, string Category,
                                    string Title, string? Remarks, string? Grade);
public record FeedbackRequest(int StudentId, string Category, string Message);
public record FeedbackReplyRequest(string Reply, string? Status);
public record UploadReviewRequest(string Status, string? TeacherRemarks);
