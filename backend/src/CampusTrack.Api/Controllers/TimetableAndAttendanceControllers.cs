using CampusTrack.Application.Authorization;
using CampusTrack.Application.Common.Interfaces;
using CampusTrack.Application.Common.Models;
using CampusTrack.Domain.Common;
using CampusTrack.Infrastructure.Attendance;
using CampusTrack.Infrastructure.Identity;
using CampusTrack.Infrastructure.Persistence;
using CampusTrack.Infrastructure.Scheduling;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CampusTrack.Api.Controllers;

[Route("api/v1/timetable")]
public class TimetableController : ApiControllerBase
{
    private readonly ITimetableService _timetable;
    private readonly CampusTrackDbContext _db;
    private readonly IDateTimeProvider _clock;

    public TimetableController(ITimetableService timetable, CampusTrackDbContext db, IDateTimeProvider clock)
    {
        _timetable = timetable;
        _db = db;
        _clock = clock;
    }

    [HttpGet("periods")]
    [HasPermission(Permissions.Timetable.View)]
    public async Task<ActionResult<IReadOnlyList<object>>> GetPeriods(CancellationToken ct) =>
        Ok(await _db.TimetablePeriods.AsNoTracking()
            .Where(p => p.AcademicSession!.IsCurrent)
            .OrderBy(p => p.Sequence)
            .Select(p => (object)new { p.Id, p.Name, p.Sequence, p.StartTime, p.EndTime, p.IsBreak })
            .ToListAsync(ct));

    [HttpPost("periods")]
    [HasPermission(Permissions.Timetable.Manage)]
    public async Task<ActionResult<object>> CreatePeriod(PeriodRequest request, CancellationToken ct)
    {
        var sessionId = await _db.AcademicSessions.Where(s => s.IsCurrent).Select(s => s.Id).FirstOrDefaultAsync(ct);
        if (sessionId == 0) throw DomainException.Invalid("No academic session is marked as current.");

        if (request.EndTime <= request.StartTime)
            throw DomainException.Invalid("A period must end after it starts.");

        var period = new Domain.Scheduling.TimetablePeriod
        {
            AcademicSessionId = sessionId,
            Name = request.Name.Trim(),
            Sequence = request.Sequence,
            StartTime = request.StartTime,
            EndTime = request.EndTime,
            IsBreak = request.IsBreak
        };

        _db.TimetablePeriods.Add(period);
        await _db.SaveChangesAsync(ct);

        return Created($"/api/v1/timetable/periods/{period.Id}", new { period.Id, period.Name });
    }

    [HttpPut("periods/{id:int}")]
    [HasPermission(Permissions.Timetable.Manage)]
    public async Task<IActionResult> UpdatePeriod(int id, PeriodRequest request, CancellationToken ct)
    {
        var period = Found(await _db.TimetablePeriods.FirstOrDefaultAsync(p => p.Id == id, ct), "period");

        if (request.EndTime <= request.StartTime)
            throw DomainException.Invalid("A period must end after it starts.");

        period.Name = request.Name.Trim();
        period.Sequence = request.Sequence;
        period.StartTime = request.StartTime;
        period.EndTime = request.EndTime;
        period.IsBreak = request.IsBreak;

        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>
    /// Removes a period from the bell schedule. Refused while lessons are scheduled in it,
    /// since those slots would lose the times that place them on the timetable.
    /// </summary>
    [HttpDelete("periods/{id:int}")]
    [HasPermission(Permissions.Timetable.Manage)]
    public async Task<IActionResult> DeletePeriod(int id, CancellationToken ct)
    {
        var period = Found(await _db.TimetablePeriods.FirstOrDefaultAsync(p => p.Id == id, ct), "period");

        var slots = await _db.TimetableSlots.CountAsync(s => s.TimetablePeriodId == id, ct);
        if (slots > 0)
            throw DomainException.Conflict(
                $"{slots} timetable slot(s) use this period. Remove them before deleting it.");

        _db.TimetablePeriods.Remove(period);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpGet("section/{sectionId:int}")]
    [HasPermission(Permissions.Timetable.View)]
    public async Task<ActionResult<IReadOnlyList<TimetableEntry>>> ForSection(int sectionId, CancellationToken ct)
        => Ok(await _timetable.GetForSectionAsync(sectionId, ct));

    [HttpGet("teacher/{teacherId:int}")]
    [HasPermission(Permissions.Timetable.View)]
    public async Task<ActionResult<IReadOnlyList<TimetableEntry>>> ForTeacher(int teacherId, CancellationToken ct)
        => Ok(await _timetable.GetForTeacherAsync(teacherId, ct));

    [HttpGet("classroom/{classroomId:int}")]
    [HasPermission(Permissions.Timetable.View)]
    public async Task<ActionResult<IReadOnlyList<TimetableEntry>>> ForClassroom(int classroomId, CancellationToken ct)
        => Ok(await _timetable.GetForClassroomAsync(classroomId, ct));

    /// <summary>My timetable — resolves the caller's own teacher or student context.</summary>
    [HttpGet("mine")]
    public async Task<ActionResult<IReadOnlyList<TimetableEntry>>> Mine(CancellationToken ct)
    {
        if (CurrentUser.TeacherId is { } teacherId)
            return Ok(await _timetable.GetForTeacherAsync(teacherId, ct));

        if (CurrentUser.StudentId is { } studentId)
        {
            var sectionId = await _db.Students.AsNoTracking()
                .Where(s => s.Id == studentId).Select(s => s.CurrentSectionId).FirstOrDefaultAsync(ct);

            return sectionId is null
                ? Ok(Array.Empty<TimetableEntry>())
                : Ok(await _timetable.GetForSectionAsync(sectionId.Value, ct));
        }

        return Ok(Array.Empty<TimetableEntry>());
    }

    /// <summary>Checks a proposed slot for clashes without saving it — used for live UI feedback.</summary>
    [HttpPost("check-conflicts")]
    [HasPermission(Permissions.Timetable.Manage)]
    public async Task<ActionResult<IReadOnlyList<TimetableConflict>>> CheckConflicts(
        TimetableSlotRequest request, CancellationToken ct)
        => Ok(await _timetable.CheckConflictsAsync(request, ct));

    [HttpPost("slots")]
    [HasPermission(Permissions.Timetable.Manage)]
    public async Task<ActionResult<object>> SaveSlot(TimetableSlotRequest request, CancellationToken ct)
        => Ok(new { id = await _timetable.SaveSlotAsync(request, ct) });

    [HttpDelete("slots/{id:int}")]
    [HasPermission(Permissions.Timetable.Manage)]
    public async Task<IActionResult> DeleteSlot(int id, CancellationToken ct)
    {
        await _timetable.DeleteSlotAsync(id, ct);
        return NoContent();
    }
}

[Route("api/v1/attendance")]
public class AttendanceController : ApiControllerBase
{
    private readonly IAttendanceQueryService _attendance;
    private readonly IAttendanceEngine _engine;
    private readonly IDateTimeProvider _clock;

    public AttendanceController(
        IAttendanceQueryService attendance, IAttendanceEngine engine, IDateTimeProvider clock)
    {
        _attendance = attendance;
        _engine = engine;
        _clock = clock;
    }

    [HttpGet("daily")]
    [HasPermission(Permissions.Attendance.View)]
    public async Task<ActionResult<PagedResult<DailyAttendanceDto>>> GetDaily(
        [FromQuery] AttendanceQuery query, CancellationToken ct)
        => Paged(await _attendance.GetDailyAsync(query, ct));

    /// <summary>The register for one lesson, including students with nothing recorded yet.</summary>
    [HttpGet("register/{timetableSlotId:int}")]
    [HasPermission(Permissions.Attendance.ViewAssigned)]
    public async Task<ActionResult<IReadOnlyList<SessionAttendanceDto>>> GetRegister(
        int timetableSlotId, [FromQuery] DateOnly? date, CancellationToken ct)
        => Ok(await _attendance.GetRegisterAsync(timetableSlotId, date ?? _clock.SchoolToday, ct));

    [HttpGet("summary")]
    [HasPermission(Permissions.Attendance.View)]
    public async Task<ActionResult<IReadOnlyList<AttendanceSummary>>> GetSummary(
        [FromQuery] int? sectionId, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct)
    {
        var start = from ?? _clock.SchoolToday.AddDays(-30);
        var end = to ?? _clock.SchoolToday;
        return Ok(await _attendance.GetSummaryAsync(sectionId, start, end, ct));
    }

    [HttpGet("students/{studentId:int}/summary")]
    [HasPermission(Permissions.Attendance.View)]
    public async Task<ActionResult<AttendanceSummary>> GetStudentSummary(
        int studentId, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct)
        => Ok(await _attendance.GetStudentSummaryAsync(
            studentId, from ?? _clock.SchoolToday.AddDays(-90), to ?? _clock.SchoolToday, ct));

    /// <summary>
    /// Records or corrects attendance. Overriding an RFID-derived record requires a reason and
    /// is written to the correction audit trail.
    /// </summary>
    [HttpPost("mark")]
    [HasPermission(Permissions.Attendance.Mark)]
    public async Task<IActionResult> Mark(MarkAttendanceRequest request, CancellationToken ct)
    {
        await _attendance.MarkAsync(request, ct);
        return NoContent();
    }

    /// <summary>Marks a whole register in one call — how a teacher actually takes attendance.</summary>
    [HttpPost("mark-bulk")]
    [HasPermission(Permissions.Attendance.Mark)]
    public async Task<ActionResult<object>> MarkBulk(BulkMarkRequest request, CancellationToken ct)
    {
        var marked = 0;
        var failures = new List<string>();

        foreach (var entry in request.Entries)
        {
            try
            {
                await _attendance.MarkAsync(new MarkAttendanceRequest
                {
                    StudentId = entry.StudentId,
                    Date = request.Date,
                    Status = entry.Status,
                    TimetableSlotId = request.TimetableSlotId,
                    Remarks = entry.Remarks,
                    Reason = request.Reason
                }, ct);
                marked++;
            }
            catch (DomainException ex)
            {
                // One student's record failing must not discard the rest of the register.
                failures.Add($"Student {entry.StudentId}: {ex.Message}");
            }
        }

        return Ok(new { marked, failed = failures.Count, failures });
    }

    [HttpGet("students/{studentId:int}/corrections")]
    [HasPermission(Permissions.Attendance.View)]
    public async Task<ActionResult<IReadOnlyList<object>>> GetCorrections(int studentId, CancellationToken ct)
        => Ok(await _attendance.GetCorrectionHistoryAsync(studentId, ct));

    /// <summary>
    /// Re-runs absence finalisation for a date. Normally the scheduler does this; exposed for
    /// the case where the server was down at the scheduled time.
    /// </summary>
    [HttpPost("finalise")]
    [HasPermission(Permissions.Attendance.Configure)]
    public async Task<ActionResult<object>> Finalise([FromQuery] DateOnly? date, CancellationToken ct)
    {
        var target = date ?? _clock.SchoolToday;
        var created = await _engine.FinaliseAbsencesAsync(target, ct);
        return Ok(new { date = target, recordsCreated = created });
    }
}

public record PeriodRequest
{
    public required string Name { get; init; }
    public int Sequence { get; init; }
    public TimeOnly StartTime { get; init; }
    public TimeOnly EndTime { get; init; }
    public bool IsBreak { get; init; }
}

public record BulkMarkRequest
{
    public required DateOnly Date { get; init; }
    public int? TimetableSlotId { get; init; }
    public string? Reason { get; init; }
    public required List<BulkMarkEntry> Entries { get; init; }
}

public record BulkMarkEntry
{
    public int StudentId { get; init; }
    public Domain.Enums.AttendanceStatus Status { get; init; }
    public string? Remarks { get; init; }
}
