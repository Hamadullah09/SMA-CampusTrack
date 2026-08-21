using CampusTrack.Application.Common;
using CampusTrack.Application.Common.Interfaces;
using CampusTrack.Application.Common.Models;
using CampusTrack.Domain.Attendance;
using CampusTrack.Domain.Common;
using CampusTrack.Domain.Enums;
using CampusTrack.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CampusTrack.Infrastructure.Attendance;

public class AttendanceQuery : PagedQuery
{
    public int? StudentId { get; set; }
    public int? SectionId { get; set; }
    public int? SubjectId { get; set; }
    public DateOnly? FromDate { get; set; }
    public DateOnly? ToDate { get; set; }
    public AttendanceStatus? Status { get; set; }
}

public record DailyAttendanceDto
{
    public long Id { get; init; }
    public int StudentId { get; init; }
    public string StudentName { get; init; } = string.Empty;
    public string StudentCode { get; init; } = string.Empty;
    public string? SectionName { get; init; }
    public DateOnly Date { get; init; }
    public AttendanceStatus Status { get; init; }
    public DateTime? FirstEntryAtUtc { get; init; }
    public DateTime? LastExitAtUtc { get; init; }
    public int? MinutesOnCampus { get; init; }
    public int LateMinutes { get; init; }
    public int EarlyLeaveMinutes { get; init; }
    public EventSource Source { get; init; }
    public bool IsManuallyAdjusted { get; init; }
    public string? Remarks { get; init; }
}

public record SessionAttendanceDto
{
    public long Id { get; init; }
    public int StudentId { get; init; }
    public string StudentName { get; init; } = string.Empty;
    public string StudentCode { get; init; } = string.Empty;
    public string? RollNumber { get; init; }
    public DateOnly Date { get; init; }
    public int TimetableSlotId { get; init; }
    public string SubjectName { get; init; } = string.Empty;
    public TimeOnly StartTime { get; init; }
    public TimeOnly EndTime { get; init; }
    public AttendanceStatus Status { get; init; }
    public DateTime? EnteredAtUtc { get; init; }
    public DateTime? LeftAtUtc { get; init; }
    public int LateMinutes { get; init; }
    public EventSource Source { get; init; }
    public bool IsManuallyAdjusted { get; init; }
}

public record AttendanceSummary
{
    public int StudentId { get; init; }
    public string StudentName { get; init; } = string.Empty;
    public int TotalDays { get; init; }
    public int PresentDays { get; init; }
    public int AbsentDays { get; init; }
    public int LateDays { get; init; }
    public int LeaveDays { get; init; }
    public decimal AttendancePercentage { get; init; }
    public bool IsBelowRequirement { get; init; }
}

public record MarkAttendanceRequest
{
    public required int StudentId { get; init; }
    public required DateOnly Date { get; init; }
    public required AttendanceStatus Status { get; init; }
    /// <summary>When set, the correction applies to one lesson rather than the whole day.</summary>
    public int? TimetableSlotId { get; init; }
    public string? Remarks { get; init; }
    /// <summary>Required when overriding a record the RFID engine produced.</summary>
    public string? Reason { get; init; }
}

public interface IAttendanceQueryService
{
    Task<PagedResult<DailyAttendanceDto>> GetDailyAsync(AttendanceQuery query, CancellationToken ct = default);
    Task<IReadOnlyList<SessionAttendanceDto>> GetRegisterAsync(int timetableSlotId, DateOnly date, CancellationToken ct = default);
    Task<IReadOnlyList<AttendanceSummary>> GetSummaryAsync(int? sectionId, DateOnly from, DateOnly to, CancellationToken ct = default);
    Task<AttendanceSummary> GetStudentSummaryAsync(int studentId, DateOnly from, DateOnly to, CancellationToken ct = default);
    Task MarkAsync(MarkAttendanceRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<object>> GetCorrectionHistoryAsync(int studentId, CancellationToken ct = default);
}

/// <summary>
/// Reading and correcting attendance.
///
/// Correction is the sensitive operation here. Attendance drives absence notifications,
/// exam eligibility and, in some jurisdictions, funding, so every override is refused without
/// a reason, records the before and after values, and marks the row as manually adjusted so
/// the RFID engine stops overwriting it.
/// </summary>
public class AttendanceQueryService : IAttendanceQueryService
{
    private readonly CampusTrackDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;
    private readonly ISettingsProvider _settings;
    private readonly ILogger<AttendanceQueryService> _logger;

    public AttendanceQueryService(
        CampusTrackDbContext db,
        ICurrentUser currentUser,
        IDateTimeProvider clock,
        ISettingsProvider settings,
        ILogger<AttendanceQueryService> logger)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
        _settings = settings;
        _logger = logger;
    }

    public async Task<PagedResult<DailyAttendanceDto>> GetDailyAsync(
        AttendanceQuery query, CancellationToken ct = default)
    {
        var q = _db.DailyAttendances.AsNoTracking().AsQueryable();

        if (query.StudentId is { } studentId) q = q.Where(a => a.StudentId == studentId);
        if (query.SectionId is { } sectionId) q = q.Where(a => a.SectionId == sectionId);
        if (query.Status is { } status) q = q.Where(a => a.Status == status);
        if (query.FromDate is { } from) q = q.Where(a => a.Date >= from);
        if (query.ToDate is { } to) q = q.Where(a => a.Date <= to);

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            q = q.Where(a => a.Student!.User!.FirstName.Contains(term)
                             || a.Student.User.LastName.Contains(term)
                             || a.Student.StudentCode.Contains(term));
        }

        var projected = q
            .OrderByDescending(a => a.Date).ThenBy(a => a.Student!.User!.LastName)
            .Select(a => new DailyAttendanceDto
            {
                Id = a.Id,
                StudentId = a.StudentId,
                StudentName = a.Student!.User!.FirstName + " " + a.Student.User.LastName,
                StudentCode = a.Student.StudentCode,
                SectionName = a.Section!.DisplayName,
                Date = a.Date,
                Status = a.Status,
                FirstEntryAtUtc = a.FirstEntryAtUtc,
                LastExitAtUtc = a.LastExitAtUtc,
                MinutesOnCampus = a.MinutesOnCampus,
                LateMinutes = a.LateMinutes,
                EarlyLeaveMinutes = a.EarlyLeaveMinutes,
                Source = a.Source,
                IsManuallyAdjusted = a.IsManuallyAdjusted,
                Remarks = a.Remarks
            });

        return await projected.ToPagedResultAsync(query.Page, query.PageSize, ct);
    }

    /// <summary>
    /// The teacher's register for one lesson. Every enrolled student appears, including those
    /// with no record yet - an empty register is not the same as an absent class, and the
    /// teacher needs to see the full list to mark it.
    /// </summary>
    public async Task<IReadOnlyList<SessionAttendanceDto>> GetRegisterAsync(
        int timetableSlotId, DateOnly date, CancellationToken ct = default)
    {
        var slot = await _db.TimetableSlots.AsNoTracking()
            .Where(s => s.Id == timetableSlotId)
            .Select(s => new { s.Id, s.SectionId, s.SubjectId, s.StartTime, s.EndTime, SubjectName = s.Subject!.Name })
            .FirstOrDefaultAsync(ct)
            ?? throw new KeyNotFoundException("That timetable slot does not exist.");

        var students = await _db.Students.AsNoTracking()
            .Where(s => s.CurrentSectionId == slot.SectionId && s.Status == PersonStatus.Active)
            .Select(s => new
            {
                s.Id,
                Name = s.User!.FirstName + " " + s.User.LastName,
                s.StudentCode,
                RollNumber = s.Enrollments.Where(e => e.Status == EnrollmentStatus.Active)
                    .Select(e => e.RollNumber).FirstOrDefault()
            })
            .OrderBy(s => s.RollNumber).ThenBy(s => s.Name)
            .ToListAsync(ct);

        var records = await _db.SessionAttendances.AsNoTracking()
            .Where(a => a.TimetableSlotId == timetableSlotId && a.Date == date)
            .ToDictionaryAsync(a => a.StudentId, ct);

        return students.Select(s =>
        {
            records.TryGetValue(s.Id, out var record);

            return new SessionAttendanceDto
            {
                Id = record?.Id ?? 0,
                StudentId = s.Id,
                StudentName = s.Name,
                StudentCode = s.StudentCode,
                RollNumber = s.RollNumber,
                Date = date,
                TimetableSlotId = timetableSlotId,
                SubjectName = slot.SubjectName,
                StartTime = slot.StartTime,
                EndTime = slot.EndTime,
                Status = record?.Status ?? AttendanceStatus.NotRecorded,
                EnteredAtUtc = record?.EnteredAtUtc,
                LeftAtUtc = record?.LeftAtUtc,
                LateMinutes = record?.LateMinutes ?? 0,
                Source = record?.Source ?? EventSource.System,
                IsManuallyAdjusted = record?.IsManuallyAdjusted ?? false
            };
        }).ToList();
    }

    public async Task<IReadOnlyList<AttendanceSummary>> GetSummaryAsync(
        int? sectionId, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var required = await _settings.GetAsync(SettingKeys.AttendanceRequiredPercent, 75, ct);

        var q = _db.DailyAttendances.AsNoTracking()
            .Where(a => a.Date >= from && a.Date <= to && a.Status != AttendanceStatus.Holiday);

        if (sectionId is { } section) q = q.Where(a => a.SectionId == section);

        var grouped = await q
            .GroupBy(a => new { a.StudentId, Name = a.Student!.User!.FirstName + " " + a.Student.User.LastName })
            .Select(g => new
            {
                g.Key.StudentId,
                g.Key.Name,
                Total = g.Count(),
                Present = g.Count(a => a.Status == AttendanceStatus.Present
                                       || a.Status == AttendanceStatus.Late
                                       || a.Status == AttendanceStatus.EarlyLeave
                                       || a.Status == AttendanceStatus.Partial),
                Absent = g.Count(a => a.Status == AttendanceStatus.Absent
                                      || a.Status == AttendanceStatus.Unexcused),
                Late = g.Count(a => a.Status == AttendanceStatus.Late),
                Leave = g.Count(a => a.Status == AttendanceStatus.Leave
                                     || a.Status == AttendanceStatus.Excused)
            })
            .ToListAsync(ct);

        return grouped.Select(g =>
        {
            var percentage = g.Total == 0 ? 0m : Math.Round(g.Present * 100m / g.Total, 1);

            return new AttendanceSummary
            {
                StudentId = g.StudentId,
                StudentName = g.Name,
                TotalDays = g.Total,
                PresentDays = g.Present,
                AbsentDays = g.Absent,
                LateDays = g.Late,
                LeaveDays = g.Leave,
                AttendancePercentage = percentage,
                IsBelowRequirement = percentage < required
            };
        })
        .OrderBy(s => s.AttendancePercentage)   // students at risk surface first
        .ToList();
    }

    public async Task<AttendanceSummary> GetStudentSummaryAsync(
        int studentId, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var summaries = await GetSummaryAsync(null, from, to, ct);
        var found = summaries.FirstOrDefault(s => s.StudentId == studentId);

        if (found is not null) return found;

        var name = await _db.Students.AsNoTracking()
            .Where(s => s.Id == studentId)
            .Select(s => s.User!.FirstName + " " + s.User.LastName)
            .FirstOrDefaultAsync(ct) ?? "Unknown";

        return new AttendanceSummary { StudentId = studentId, StudentName = name };
    }

    public async Task MarkAsync(MarkAttendanceRequest request, CancellationToken ct = default)
    {
        if (request.TimetableSlotId is { } slotId)
            await MarkSessionAsync(request, slotId, ct);
        else
            await MarkDailyAsync(request, ct);

        await _db.SaveChangesAsync(ct);
    }

    private async Task MarkDailyAsync(MarkAttendanceRequest request, CancellationToken ct)
    {
        var record = await _db.DailyAttendances
            .FirstOrDefaultAsync(a => a.StudentId == request.StudentId && a.Date == request.Date, ct);

        var isOverride = record is not null && record.Source == EventSource.Rfid;

        // Overriding evidence the readers produced needs a stated reason. Filling a gap where
        // nothing was recorded does not.
        if (isOverride && string.IsNullOrWhiteSpace(request.Reason))
            throw DomainException.Invalid(
                "This record came from the RFID readers. Please give a reason for changing it.");

        var previousStatus = record?.Status ?? AttendanceStatus.NotRecorded;

        if (record is null)
        {
            var sessionId = await _db.AcademicSessions.Where(s => s.IsCurrent).Select(s => s.Id).FirstOrDefaultAsync(ct);
            var sectionId = await _db.Students.Where(s => s.Id == request.StudentId)
                .Select(s => s.CurrentSectionId).FirstOrDefaultAsync(ct);

            record = new DailyAttendance
            {
                StudentId = request.StudentId,
                Date = request.Date,
                AcademicSessionId = sessionId,
                SectionId = sectionId
            };
            _db.DailyAttendances.Add(record);
        }

        record.Status = request.Status;
        record.Remarks = request.Remarks;
        record.Source = EventSource.Manual;
        // Stops the attendance engine from silently reverting a human decision on the next read.
        record.IsManuallyAdjusted = true;

        await _db.SaveChangesAsync(ct);

        _db.AttendanceCorrections.Add(new AttendanceCorrection
        {
            StudentId = request.StudentId,
            Date = request.Date,
            RecordType = "Daily",
            RecordId = record.Id,
            OldStatus = previousStatus,
            NewStatus = request.Status,
            Reason = request.Reason ?? request.Remarks ?? "Recorded manually",
            CorrectedByUserId = _currentUser.UserId ?? 0,
            CorrectedAtUtc = _clock.UtcNow,
            IpAddress = _currentUser.IpAddress
        });

        _logger.LogInformation(
            "Attendance for student {StudentId} on {Date} changed from {Old} to {New} by user {UserId}",
            request.StudentId, request.Date, previousStatus, request.Status, _currentUser.UserId);
    }

    private async Task MarkSessionAsync(MarkAttendanceRequest request, int slotId, CancellationToken ct)
    {
        var slot = await _db.TimetableSlots.AsNoTracking().FirstOrDefaultAsync(s => s.Id == slotId, ct)
            ?? throw new KeyNotFoundException("That timetable slot does not exist.");

        var record = await _db.SessionAttendances.FirstOrDefaultAsync(
            a => a.StudentId == request.StudentId && a.Date == request.Date && a.TimetableSlotId == slotId, ct);

        var isOverride = record is not null && record.Source == EventSource.Rfid;
        if (isOverride && string.IsNullOrWhiteSpace(request.Reason))
            throw DomainException.Invalid(
                "This record came from the RFID readers. Please give a reason for changing it.");

        var previousStatus = record?.Status ?? AttendanceStatus.NotRecorded;

        if (record is null)
        {
            record = new SessionAttendance
            {
                StudentId = request.StudentId,
                Date = request.Date,
                TimetableSlotId = slotId,
                SubjectId = slot.SubjectId,
                SectionId = slot.SectionId,
                TeacherId = slot.TeacherId
            };
            _db.SessionAttendances.Add(record);
        }

        record.Status = request.Status;
        record.Remarks = request.Remarks;
        record.Source = EventSource.Manual;
        record.IsManuallyAdjusted = true;
        record.MarkedByUserId = _currentUser.UserId;
        record.MarkedAtUtc = _clock.UtcNow;

        await _db.SaveChangesAsync(ct);

        _db.AttendanceCorrections.Add(new AttendanceCorrection
        {
            StudentId = request.StudentId,
            Date = request.Date,
            RecordType = "Session",
            RecordId = record.Id,
            TimetableSlotId = slotId,
            OldStatus = previousStatus,
            NewStatus = request.Status,
            Reason = request.Reason ?? request.Remarks ?? "Recorded manually",
            CorrectedByUserId = _currentUser.UserId ?? 0,
            CorrectedAtUtc = _clock.UtcNow,
            IpAddress = _currentUser.IpAddress
        });
    }

    public async Task<IReadOnlyList<object>> GetCorrectionHistoryAsync(int studentId, CancellationToken ct = default) =>
        await _db.AttendanceCorrections.AsNoTracking()
            .Where(c => c.StudentId == studentId)
            .OrderByDescending(c => c.CorrectedAtUtc)
            .Take(100)
            .Select(c => (object)new
            {
                c.Id, c.Date, c.RecordType, c.OldStatus, c.NewStatus, c.Reason, c.CorrectedAtUtc,
                correctedBy = _db.Users.Where(u => u.Id == c.CorrectedByUserId)
                    .Select(u => u.FirstName + " " + u.LastName).FirstOrDefault()
            })
            .ToListAsync(ct);
}
