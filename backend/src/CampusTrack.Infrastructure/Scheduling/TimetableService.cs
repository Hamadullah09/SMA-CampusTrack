using CampusTrack.Application.Common.Interfaces;
using CampusTrack.Domain.Common;
using CampusTrack.Domain.Scheduling;
using CampusTrack.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CampusTrack.Infrastructure.Scheduling;

public record TimetableSlotRequest
{
    public int? Id { get; init; }
    public required int SectionId { get; init; }
    public required int SubjectId { get; init; }
    public int? TeacherId { get; init; }
    public int? ClassroomId { get; init; }
    public required int TimetablePeriodId { get; init; }
    public required int DayOfWeek { get; init; }
    public DateOnly? EffectiveFrom { get; init; }
    public DateOnly? EffectiveTo { get; init; }
    public string? Notes { get; init; }
}

public record TimetableEntry
{
    public int Id { get; init; }
    public int DayOfWeek { get; init; }
    public string DayName { get; init; } = string.Empty;
    public int PeriodId { get; init; }
    public string PeriodName { get; init; } = string.Empty;
    public int PeriodSequence { get; init; }
    public TimeOnly StartTime { get; init; }
    public TimeOnly EndTime { get; init; }
    public bool IsBreak { get; init; }
    public int SectionId { get; init; }
    public string SectionName { get; init; } = string.Empty;
    public int SubjectId { get; init; }
    public string SubjectName { get; init; } = string.Empty;
    public string? SubjectColour { get; init; }
    public int? TeacherId { get; init; }
    public string? TeacherName { get; init; }
    public int? ClassroomId { get; init; }
    public string? ClassroomName { get; init; }
    public bool IsMonitored { get; init; }
    public string? Notes { get; init; }
}

/// <summary>A clash the caller must resolve before the slot can be saved.</summary>
public record TimetableConflict(string Kind, string Message, int ConflictingSlotId);

public interface ITimetableService
{
    Task<IReadOnlyList<TimetableEntry>> GetForSectionAsync(int sectionId, CancellationToken ct = default);
    Task<IReadOnlyList<TimetableEntry>> GetForTeacherAsync(int teacherId, CancellationToken ct = default);
    Task<IReadOnlyList<TimetableEntry>> GetForClassroomAsync(int classroomId, CancellationToken ct = default);
    Task<IReadOnlyList<TimetableEntry>> GetDayForSectionAsync(int sectionId, DateOnly date, CancellationToken ct = default);
    Task<IReadOnlyList<TimetableConflict>> CheckConflictsAsync(TimetableSlotRequest request, CancellationToken ct = default);
    Task<int> SaveSlotAsync(TimetableSlotRequest request, CancellationToken ct = default);
    Task DeleteSlotAsync(int id, CancellationToken ct = default);
}

/// <summary>
/// Builds and validates the timetable.
///
/// The value here is the conflict check. A timetable that double-books a teacher or a room is
/// not merely untidy: the RFID engine uses these slots to decide which lesson a student was
/// expected in, so an inconsistent timetable silently produces wrong attendance. The three
/// physical impossibilities are refused outright rather than warned about.
/// </summary>
public class TimetableService : ITimetableService
{
    private static readonly string[] DayNames =
        ["", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday"];

    private readonly CampusTrackDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<TimetableService> _logger;

    public TimetableService(CampusTrackDbContext db, ICurrentUser currentUser, ILogger<TimetableService> logger)
    {
        _db = db;
        _currentUser = currentUser;
        _logger = logger;
    }

    public Task<IReadOnlyList<TimetableEntry>> GetForSectionAsync(int sectionId, CancellationToken ct = default)
        => QueryAsync(s => s.SectionId == sectionId, ct);

    public Task<IReadOnlyList<TimetableEntry>> GetForTeacherAsync(int teacherId, CancellationToken ct = default)
        => QueryAsync(s => s.TeacherId == teacherId, ct);

    public Task<IReadOnlyList<TimetableEntry>> GetForClassroomAsync(int classroomId, CancellationToken ct = default)
        => QueryAsync(s => s.ClassroomId == classroomId, ct);

    public async Task<IReadOnlyList<TimetableEntry>> GetDayForSectionAsync(
        int sectionId, DateOnly date, CancellationToken ct = default)
    {
        var isoDay = ToIsoDay(date);

        // Respects the effective-from/to window so a mid-year timetable change shows the
        // version that applied on the date being asked about, not today's.
        return await QueryAsync(s => s.SectionId == sectionId
                                     && s.DayOfWeek == isoDay
                                     && (s.EffectiveFrom == null || s.EffectiveFrom <= date)
                                     && (s.EffectiveTo == null || s.EffectiveTo >= date), ct);
    }

    public async Task<IReadOnlyList<TimetableConflict>> CheckConflictsAsync(
        TimetableSlotRequest request, CancellationToken ct = default)
    {
        var conflicts = new List<TimetableConflict>();

        var candidates = await _db.TimetableSlots.AsNoTracking()
            .Where(s => s.IsActive
                        && s.DayOfWeek == request.DayOfWeek
                        && s.TimetablePeriodId == request.TimetablePeriodId
                        && s.Id != (request.Id ?? 0))
            .Select(s => new
            {
                s.Id, s.SectionId, s.TeacherId, s.ClassroomId,
                SectionName = s.Section!.DisplayName,
                SubjectName = s.Subject!.Name,
                TeacherName = s.Teacher == null ? null : s.Teacher.User!.FirstName + " " + s.Teacher.User.LastName,
                ClassroomName = s.Classroom == null ? null : s.Classroom.Name
            })
            .ToListAsync(ct);

        var day = DayNames[Math.Clamp(request.DayOfWeek, 1, 7)];

        // A section cannot attend two lessons at once.
        var sectionClash = candidates.FirstOrDefault(c => c.SectionId == request.SectionId);
        if (sectionClash is not null)
            conflicts.Add(new TimetableConflict("section",
                $"{sectionClash.SectionName} already has {sectionClash.SubjectName} in this period on {day}.",
                sectionClash.Id));

        // A teacher cannot be in two rooms at once.
        if (request.TeacherId is { } teacherId)
        {
            var teacherClash = candidates.FirstOrDefault(c => c.TeacherId == teacherId);
            if (teacherClash is not null)
                conflicts.Add(new TimetableConflict("teacher",
                    $"{teacherClash.TeacherName} is already teaching {teacherClash.SectionName} in this period on {day}.",
                    teacherClash.Id));
        }

        // A room cannot hold two lessons at once.
        if (request.ClassroomId is { } classroomId)
        {
            var roomClash = candidates.FirstOrDefault(c => c.ClassroomId == classroomId);
            if (roomClash is not null)
                conflicts.Add(new TimetableConflict("classroom",
                    $"{roomClash.ClassroomName} is already booked for {roomClash.SectionName} in this period on {day}.",
                    roomClash.Id));
        }

        return conflicts;
    }

    public async Task<int> SaveSlotAsync(TimetableSlotRequest request, CancellationToken ct = default)
    {
        if (request.DayOfWeek is < 1 or > 7)
            throw DomainException.Invalid("Day of week must be between 1 (Monday) and 7 (Sunday).");

        var period = await _db.TimetablePeriods.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == request.TimetablePeriodId, ct)
            ?? throw DomainException.Invalid("That period does not exist.");

        if (period.IsBreak)
            throw DomainException.Invalid("Lessons cannot be scheduled during a break.");

        var conflicts = await CheckConflictsAsync(request, ct);
        if (conflicts.Count > 0)
            throw DomainException.Conflict(string.Join(" ", conflicts.Select(c => c.Message)));

        // Warn rather than refuse: schools do sometimes have a teacher cover a subject they
        // are not formally assigned to, and blocking that would make the timetable unusable.
        if (request.TeacherId is { } teacher)
        {
            var isAssigned = await _db.TeachingAssignments.AnyAsync(
                a => a.TeacherId == teacher && a.SubjectId == request.SubjectId
                     && a.SectionId == request.SectionId && a.IsActive, ct);

            if (!isAssigned)
                _logger.LogWarning(
                    "Timetable slot assigns teacher {TeacherId} to subject {SubjectId} for section {SectionId} " +
                    "without a matching teaching assignment.",
                    teacher, request.SubjectId, request.SectionId);
        }

        var sessionId = await _db.AcademicSessions.Where(s => s.IsCurrent).Select(s => s.Id).FirstOrDefaultAsync(ct);
        if (sessionId == 0) throw DomainException.Invalid("No academic session is marked as current.");

        TimetableSlot slot;

        if (request.Id is { } id and > 0)
        {
            slot = await _db.TimetableSlots.FirstOrDefaultAsync(s => s.Id == id, ct)
                   ?? throw new KeyNotFoundException("That timetable slot does not exist.");
        }
        else
        {
            slot = new TimetableSlot { SchoolId = _currentUser.SchoolId, AcademicSessionId = sessionId };
            _db.TimetableSlots.Add(slot);
        }

        slot.SectionId = request.SectionId;
        slot.SubjectId = request.SubjectId;
        slot.TeacherId = request.TeacherId;
        slot.ClassroomId = request.ClassroomId;
        slot.TimetablePeriodId = request.TimetablePeriodId;
        slot.DayOfWeek = request.DayOfWeek;
        // Denormalised from the period so the RFID engine can match a movement to a lesson
        // without joining the period table on every event.
        slot.StartTime = period.StartTime;
        slot.EndTime = period.EndTime;
        slot.EffectiveFrom = request.EffectiveFrom;
        slot.EffectiveTo = request.EffectiveTo;
        slot.Notes = request.Notes;
        slot.IsActive = true;

        await _db.SaveChangesAsync(ct);
        return slot.Id;
    }

    public async Task DeleteSlotAsync(int id, CancellationToken ct = default)
    {
        var slot = await _db.TimetableSlots.FirstOrDefaultAsync(s => s.Id == id, ct)
            ?? throw new KeyNotFoundException("That timetable slot does not exist.");

        // Deactivated rather than removed: attendance records point at this slot, and deleting
        // it would leave a term's registers with no lesson to explain them.
        slot.IsActive = false;
        await _db.SaveChangesAsync(ct);
    }

    private async Task<IReadOnlyList<TimetableEntry>> QueryAsync(
        System.Linq.Expressions.Expression<Func<TimetableSlot, bool>> predicate, CancellationToken ct)
    {
        var entries = await _db.TimetableSlots.AsNoTracking()
            .Where(s => s.IsActive)
            .Where(predicate)
            .OrderBy(s => s.DayOfWeek).ThenBy(s => s.StartTime)
            .Select(s => new TimetableEntry
            {
                Id = s.Id,
                DayOfWeek = s.DayOfWeek,
                PeriodId = s.TimetablePeriodId,
                PeriodName = s.TimetablePeriod!.Name,
                PeriodSequence = s.TimetablePeriod.Sequence,
                StartTime = s.StartTime,
                EndTime = s.EndTime,
                IsBreak = s.TimetablePeriod.IsBreak,
                SectionId = s.SectionId,
                SectionName = s.Section!.DisplayName,
                SubjectId = s.SubjectId,
                SubjectName = s.Subject!.Name,
                SubjectColour = s.Subject.ColourHex,
                TeacherId = s.TeacherId,
                TeacherName = s.Teacher == null ? null : s.Teacher.User!.FirstName + " " + s.Teacher.User.LastName,
                ClassroomId = s.ClassroomId,
                ClassroomName = s.Classroom == null ? null : s.Classroom.Name,
                // Lets the UI mark which lessons will be attendance-tracked automatically.
                IsMonitored = s.ClassroomId != null &&
                              _db.RfidLocations.Any(l => l.ClassroomId == s.ClassroomId && l.IsActive),
                Notes = s.Notes
            })
            .ToListAsync(ct);

        return entries.Select(e => e with { DayName = DayNames[Math.Clamp(e.DayOfWeek, 1, 7)] }).ToList();
    }

    /// <summary>.NET's Sunday-first DayOfWeek to ISO-8601's Monday-first numbering.</summary>
    private static int ToIsoDay(DateOnly date) => (int)date.DayOfWeek == 0 ? 7 : (int)date.DayOfWeek;
}
