using CampusTrack.Application.Common;
using CampusTrack.Application.Common.Interfaces;
using CampusTrack.Domain.Attendance;
using CampusTrack.Domain.Enums;
using CampusTrack.Domain.Rfid;
using CampusTrack.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CampusTrack.Infrastructure.Attendance;

public interface IAttendanceEngine
{
    /// <summary>Folds one movement into presence, daily attendance and session attendance.</summary>
    Task ApplyMovementAsync(RfidEvent movement, CancellationToken ct = default);

    /// <summary>Marks students with no arrival as absent. Run once per day by the scheduler.</summary>
    Task<int> FinaliseAbsencesAsync(DateOnly date, CancellationToken ct = default);

    /// <summary>Closes room intervals left open by a missed exit read.</summary>
    Task<int> CloseOpenPresencesAsync(DateOnly date, CancellationToken ct = default);
}

/// <summary>
/// Derives attendance from movement.
///
/// The design principle here is that attendance is <i>calculated evidence</i>, not a button a
/// teacher presses. A gate entry sets the arrival time and decides lateness against the
/// configured school-day start; a classroom entry opens a presence interval and marks the
/// lesson; the matching exit closes it and decides whether enough of the lesson was attended
/// to count as present rather than partial.
///
/// Two things are deliberately conservative:
/// <list type="bullet">
///   <item>A record a human has corrected is never silently overwritten by later RFID data -
///   the manual flag wins, because a human looked at the case and the reader did not.</item>
///   <item>Approved leave suppresses an absence rather than competing with it.</item>
/// </list>
/// </summary>
public class AttendanceEngine : IAttendanceEngine
{
    private readonly CampusTrackDbContext _db;
    private readonly ISettingsProvider _settings;
    private readonly IDateTimeProvider _clock;
    private readonly IRealtimePublisher _realtime;
    private readonly ILogger<AttendanceEngine> _logger;

    public AttendanceEngine(
        CampusTrackDbContext db,
        ISettingsProvider settings,
        IDateTimeProvider clock,
        IRealtimePublisher realtime,
        ILogger<AttendanceEngine> logger)
    {
        _db = db;
        _settings = settings;
        _clock = clock;
        _realtime = realtime;
        _logger = logger;
    }

    public async Task ApplyMovementAsync(RfidEvent movement, CancellationToken ct = default)
    {
        if (movement.StudentId is not { } studentId) return;

        switch (movement.EventType)
        {
            case RfidEventType.SchoolEntry:
                await UpdatePresenceAsync(studentId, PresenceState.OnCampus, movement, ct);
                await ApplySchoolEntryAsync(studentId, movement, ct);
                break;

            case RfidEventType.SchoolExit:
                await UpdatePresenceAsync(studentId, PresenceState.Outside, movement, ct);
                await ApplySchoolExitAsync(studentId, movement, ct);
                // Leaving the building implicitly ends any room the student was still "in".
                await CloseOpenRoomsForStudentAsync(studentId, movement.OccurredAtUtc, ct);
                break;

            case RfidEventType.ClassroomEntry:
            case RfidEventType.ZoneEntry:
                await UpdatePresenceAsync(studentId, PresenceState.InRoom, movement, ct);
                await OpenRoomPresenceAsync(studentId, movement, ct);
                if (movement.TimetableSlotId is not null) await ApplySessionEntryAsync(studentId, movement, ct);
                break;

            case RfidEventType.ClassroomExit:
            case RfidEventType.ZoneExit:
                await UpdatePresenceAsync(studentId, PresenceState.OnCampus, movement, ct);
                await CloseRoomPresenceAsync(studentId, movement, ct);
                if (movement.TimetableSlotId is not null) await ApplySessionExitAsync(studentId, movement, ct);
                break;
        }

        await _db.SaveChangesAsync(ct);
    }

    // ------------------------------------------------------------------ presence ----

    private async Task UpdatePresenceAsync(int studentId, PresenceState state, RfidEvent movement, CancellationToken ct)
    {
        var presence = await _db.StudentPresences.FirstOrDefaultAsync(p => p.StudentId == studentId, ct);

        if (presence is null)
        {
            presence = new StudentPresence { StudentId = studentId, SchoolId = movement.SchoolId };
            _db.StudentPresences.Add(presence);
        }

        presence.State = state;
        presence.SinceUtc = movement.OccurredAtUtc;
        presence.CurrentLocationId = state == PresenceState.Outside ? null : movement.LocationId;
        presence.LastEventId = movement.Id;

        if (movement.EventType == RfidEventType.SchoolEntry) presence.LastEntryAtUtc = movement.OccurredAtUtc;
        if (movement.EventType == RfidEventType.SchoolExit) presence.LastExitAtUtc = movement.OccurredAtUtc;
    }

    // ------------------------------------------------------- daily attendance ----

    private async Task ApplySchoolEntryAsync(int studentId, RfidEvent movement, CancellationToken ct)
    {
        var record = await GetOrCreateDailyAsync(studentId, movement, ct);

        // Manual corrections outrank the reader; a human already adjudicated this day.
        if (record.IsManuallyAdjusted) return;

        // Only the first arrival of the day sets the arrival time. A student who steps out at
        // lunch and returns has not "arrived late" a second time.
        if (record.FirstEntryAtUtc is not null && record.FirstEntryAtUtc <= movement.OccurredAtUtc) return;

        record.FirstEntryAtUtc = movement.OccurredAtUtc;
        record.Source = EventSource.Rfid;

        var dayStart = await _settings.GetAsync(SettingKeys.SchoolDayStart, new TimeOnly(7, 45), ct);
        var lateThreshold = await _settings.GetAsync(SettingKeys.LateThresholdMinutes, 15, ct);

        var arrivalLocal = TimeOnly.FromDateTime(_clock.ToSchoolTime(movement.OccurredAtUtc).DateTime);
        var minutesLate = (int)(arrivalLocal - dayStart).TotalMinutes;

        if (minutesLate > lateThreshold)
        {
            record.LateMinutes = minutesLate;
            record.Status = AttendanceStatus.Late;
        }
        else
        {
            record.LateMinutes = 0;
            record.Status = AttendanceStatus.Present;
        }

        await _realtime.PublishAttendanceUpdateAsync(new
        {
            studentId,
            date = record.Date,
            status = record.Status.ToString(),
            lateMinutes = record.LateMinutes
        }, ct);
    }

    private async Task ApplySchoolExitAsync(int studentId, RfidEvent movement, CancellationToken ct)
    {
        var record = await GetOrCreateDailyAsync(studentId, movement, ct);
        if (record.IsManuallyAdjusted) return;

        record.LastExitAtUtc = movement.OccurredAtUtc;

        if (record.FirstEntryAtUtc is { } entry)
            record.MinutesOnCampus = (int)(movement.OccurredAtUtc - entry).TotalMinutes;

        var dayEnd = await _settings.GetAsync(SettingKeys.SchoolDayEnd, new TimeOnly(14, 30), ct);
        var earlyThreshold = await _settings.GetAsync(SettingKeys.EarlyLeaveThresholdMinutes, 20, ct);

        var exitLocal = TimeOnly.FromDateTime(_clock.ToSchoolTime(movement.OccurredAtUtc).DateTime);
        var minutesEarly = (int)(dayEnd - exitLocal).TotalMinutes;

        if (minutesEarly > earlyThreshold)
        {
            record.EarlyLeaveMinutes = minutesEarly;
            // Lateness is the more serious fact about the day, so it is not overwritten here.
            if (record.Status is AttendanceStatus.Present) record.Status = AttendanceStatus.EarlyLeave;
        }
        else
        {
            record.EarlyLeaveMinutes = 0;
        }
    }

    private async Task<DailyAttendance> GetOrCreateDailyAsync(int studentId, RfidEvent movement, CancellationToken ct)
    {
        var date = movement.LocalDate;

        var record = await _db.DailyAttendances
            .FirstOrDefaultAsync(a => a.StudentId == studentId && a.Date == date, ct);

        if (record is not null) return record;

        var sessionId = await CurrentSessionIdAsync(ct);
        var sectionId = await _db.Students.Where(s => s.Id == studentId)
            .Select(s => s.CurrentSectionId).FirstOrDefaultAsync(ct);

        record = new DailyAttendance
        {
            SchoolId = movement.SchoolId,
            StudentId = studentId,
            Date = date,
            AcademicSessionId = sessionId,
            SectionId = sectionId,
            Status = AttendanceStatus.NotRecorded,
            Source = EventSource.Rfid
        };

        _db.DailyAttendances.Add(record);
        return record;
    }

    // ----------------------------------------------------- session attendance ----

    private async Task ApplySessionEntryAsync(int studentId, RfidEvent movement, CancellationToken ct)
    {
        if (movement.TimetableSlotId is not { } slotId) return;

        var slot = await _db.TimetableSlots.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == slotId, ct);
        if (slot is null) return;

        var record = await _db.SessionAttendances
            .FirstOrDefaultAsync(a => a.StudentId == studentId
                                      && a.Date == movement.LocalDate
                                      && a.TimetableSlotId == slotId, ct);

        if (record is null)
        {
            record = new SessionAttendance
            {
                SchoolId = movement.SchoolId,
                StudentId = studentId,
                Date = movement.LocalDate,
                TimetableSlotId = slotId,
                SubjectId = slot.SubjectId,
                SectionId = slot.SectionId,
                TeacherId = slot.TeacherId,
                Source = EventSource.Rfid
            };
            _db.SessionAttendances.Add(record);
        }

        if (record.IsManuallyAdjusted) return;
        if (record.EnteredAtUtc is not null) return;    // first entry wins

        record.EnteredAtUtc = movement.OccurredAtUtc;

        var grace = await _settings.GetAsync(SettingKeys.SessionGraceMinutes, 10, ct);
        var entryLocal = TimeOnly.FromDateTime(_clock.ToSchoolTime(movement.OccurredAtUtc).DateTime);
        var minutesLate = (int)(entryLocal - slot.StartTime).TotalMinutes;

        record.LateMinutes = minutesLate > 0 ? minutesLate : 0;
        record.Status = minutesLate > grace ? AttendanceStatus.Late : AttendanceStatus.Present;
    }

    private async Task ApplySessionExitAsync(int studentId, RfidEvent movement, CancellationToken ct)
    {
        if (movement.TimetableSlotId is not { } slotId) return;

        var record = await _db.SessionAttendances
            .FirstOrDefaultAsync(a => a.StudentId == studentId
                                      && a.Date == movement.LocalDate
                                      && a.TimetableSlotId == slotId, ct);

        if (record is null || record.IsManuallyAdjusted || record.EnteredAtUtc is null) return;

        record.LeftAtUtc = movement.OccurredAtUtc;
        record.MinutesPresent = (int)(movement.OccurredAtUtc - record.EnteredAtUtc.Value).TotalMinutes;

        var slot = await _db.TimetableSlots.AsNoTracking().FirstOrDefaultAsync(s => s.Id == slotId, ct);
        if (slot is null) return;

        var lessonMinutes = (int)(slot.EndTime - slot.StartTime).TotalMinutes;
        if (lessonMinutes <= 0) return;

        var exitLocal = TimeOnly.FromDateTime(_clock.ToSchoolTime(movement.OccurredAtUtc).DateTime);
        var minutesEarly = (int)(slot.EndTime - exitLocal).TotalMinutes;
        record.EarlyLeaveMinutes = minutesEarly > 0 ? minutesEarly : 0;

        // Being in the room for five minutes of a forty-five minute lesson is not attendance.
        var requiredPercent = await _settings.GetAsync(SettingKeys.MinimumSessionPresencePercent, 60, ct);
        var attendedPercent = record.MinutesPresent.Value * 100.0 / lessonMinutes;

        if (attendedPercent < requiredPercent)
            record.Status = AttendanceStatus.Partial;
        else if (record.LateMinutes > 0)
            record.Status = AttendanceStatus.Late;
        else if (record.EarlyLeaveMinutes > 0)
            record.Status = AttendanceStatus.EarlyLeave;
        else
            record.Status = AttendanceStatus.Present;
    }

    // ---------------------------------------------------------- room presence ----

    private async Task OpenRoomPresenceAsync(int studentId, RfidEvent movement, CancellationToken ct)
    {
        if (movement.LocationId is not { } locationId) return;

        // A second entry without an exit means the exit read was missed; close the stale
        // interval rather than accumulating overlapping ones.
        var open = await _db.ClassroomPresences
            .Where(p => p.StudentId == studentId && p.ExitedAtUtc == null)
            .ToListAsync(ct);

        foreach (var stale in open)
        {
            stale.ExitedAtUtc = movement.OccurredAtUtc;
            stale.DurationMinutes = (int)(movement.OccurredAtUtc - stale.EnteredAtUtc).TotalMinutes;
            stale.ClosedBySystem = true;
        }

        _db.ClassroomPresences.Add(new ClassroomPresence
        {
            SchoolId = movement.SchoolId,
            StudentId = studentId,
            LocationId = locationId,
            ClassroomId = await _db.RfidLocations.Where(l => l.Id == locationId)
                .Select(l => l.ClassroomId).FirstOrDefaultAsync(ct),
            Date = movement.LocalDate,
            EnteredAtUtc = movement.OccurredAtUtc,
            EntryEventId = movement.Id,
            TimetableSlotId = movement.TimetableSlotId,
            SubjectId = movement.SubjectId
        });
    }

    private async Task CloseRoomPresenceAsync(int studentId, RfidEvent movement, CancellationToken ct)
    {
        if (movement.LocationId is not { } locationId) return;

        var open = await _db.ClassroomPresences
            .Where(p => p.StudentId == studentId && p.LocationId == locationId && p.ExitedAtUtc == null)
            .OrderByDescending(p => p.EnteredAtUtc)
            .FirstOrDefaultAsync(ct);

        // An exit with no matching entry (the entry read was missed) is not an error worth
        // failing on, but it is worth knowing about.
        if (open is null)
        {
            _logger.LogDebug("Exit event {EventId} for student {StudentId} had no open presence at location {LocationId}",
                movement.Id, studentId, locationId);
            return;
        }

        open.ExitedAtUtc = movement.OccurredAtUtc;
        open.ExitEventId = movement.Id;
        open.DurationMinutes = (int)(movement.OccurredAtUtc - open.EnteredAtUtc).TotalMinutes;
    }

    private async Task CloseOpenRoomsForStudentAsync(int studentId, DateTime atUtc, CancellationToken ct)
    {
        var open = await _db.ClassroomPresences
            .Where(p => p.StudentId == studentId && p.ExitedAtUtc == null)
            .ToListAsync(ct);

        foreach (var presence in open)
        {
            presence.ExitedAtUtc = atUtc;
            presence.DurationMinutes = (int)(atUtc - presence.EnteredAtUtc).TotalMinutes;
            presence.ClosedBySystem = true;
        }
    }

    // -------------------------------------------------------- daily finalisation ----

    public async Task<int> FinaliseAbsencesAsync(DateOnly date, CancellationToken ct = default)
    {
        // Nothing is expected on a closed day.
        var isHoliday = await _db.SchoolHolidays
            .AnyAsync(h => h.StartDate <= date && h.EndDate >= date, ct);
        if (isHoliday)
        {
            _logger.LogInformation("{Date} is a school holiday; absence finalisation skipped", date);
            return 0;
        }

        var sessionId = await CurrentSessionIdAsync(ct);

        var enrolledStudents = await _db.Enrollments
            .Where(e => e.AcademicSessionId == sessionId && e.Status == EnrollmentStatus.Active)
            .Select(e => new { e.StudentId, e.SectionId })
            .ToListAsync(ct);

        var alreadyRecorded = await _db.DailyAttendances
            .Where(a => a.Date == date)
            .Select(a => a.StudentId)
            .ToListAsync(ct);

        var recordedSet = alreadyRecorded.ToHashSet();

        var onLeave = await _db.LeaveRequests
            .Where(l => l.Status == LeaveStatus.Approved
                        && l.StudentId != null
                        && l.StartDate <= date && l.EndDate >= date)
            .Select(l => new { StudentId = l.StudentId!.Value, LeaveId = l.Id })
            .ToListAsync(ct);

        var leaveLookup = onLeave.ToDictionary(l => l.StudentId, l => l.LeaveId);

        var created = 0;
        foreach (var enrollment in enrolledStudents.Where(e => !recordedSet.Contains(e.StudentId)))
        {
            var hasLeave = leaveLookup.TryGetValue(enrollment.StudentId, out var leaveId);

            _db.DailyAttendances.Add(new DailyAttendance
            {
                StudentId = enrollment.StudentId,
                Date = date,
                AcademicSessionId = sessionId,
                SectionId = enrollment.SectionId,
                // Approved leave is an explained absence, not a truancy.
                Status = hasLeave ? AttendanceStatus.Leave : AttendanceStatus.Absent,
                Source = EventSource.System,
                LeaveRequestId = hasLeave ? leaveId : null,
                Remarks = hasLeave ? "Approved leave" : "No arrival recorded"
            });
            created++;
        }

        if (created > 0) await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Finalised absences for {Date}: {Count} record(s) created", date, created);
        return created;
    }

    public async Task<int> CloseOpenPresencesAsync(DateOnly date, CancellationToken ct = default)
    {
        var dayEnd = await _settings.GetAsync(SettingKeys.SchoolDayEnd, new TimeOnly(14, 30), ct);
        var closeAtUtc = _clock.ToUtc(date.ToDateTime(dayEnd));

        var open = await _db.ClassroomPresences
            .Where(p => p.Date == date && p.ExitedAtUtc == null)
            .ToListAsync(ct);

        foreach (var presence in open)
        {
            presence.ExitedAtUtc = closeAtUtc;
            presence.DurationMinutes = Math.Max(0, (int)(closeAtUtc - presence.EnteredAtUtc).TotalMinutes);
            presence.ClosedBySystem = true;
        }

        // Anyone still shown as on campus after closing time had their exit read missed.
        var stillInside = await _db.StudentPresences
            .Where(p => p.State != PresenceState.Outside)
            .ToListAsync(ct);

        foreach (var presence in stillInside)
        {
            presence.State = PresenceState.Outside;
            presence.CurrentLocationId = null;
            presence.SinceUtc = closeAtUtc;
        }

        if (open.Count > 0 || stillInside.Count > 0) await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Closed {Rooms} open room presence(s) and reset {Students} campus presence(s) for {Date}",
            open.Count, stillInside.Count, date);

        return open.Count;
    }

    private async Task<int> CurrentSessionIdAsync(CancellationToken ct) =>
        await _db.AcademicSessions.Where(s => s.IsCurrent).Select(s => s.Id).FirstOrDefaultAsync(ct);
}
