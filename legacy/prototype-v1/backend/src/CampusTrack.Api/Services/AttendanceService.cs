using CampusTrack.Api.Data;
using CampusTrack.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace CampusTrack.Api.Services;

/// <summary>
/// Turns a resolved RFID sequence into an AttendanceEvent and, for gate
/// events, immediately notifies the student's parent by push notification.
/// </summary>
public class AttendanceService
{
    private readonly AppDbContext _db;
    private readonly NotificationService _notifications;
    private readonly ILogger<AttendanceService> _log;

    public AttendanceService(AppDbContext db, NotificationService notifications,
                             ILogger<AttendanceService> log)
    {
        _db = db; _notifications = notifications; _log = log;
    }

    public async Task RecordEventAsync(int readerId, string epc, Direction direction,
                                       DateTime eventTime, CancellationToken ct = default)
    {
        var reader = await _db.RfidReaders.Include(r => r.Room)
                                          .FirstOrDefaultAsync(r => r.Id == readerId, ct);
        if (reader?.Room is null) return;

        var student = await _db.Students.Include(s => s.User)
                                        .Include(s => s.Parent)
                                        .FirstOrDefaultAsync(s => s.RfidEpc == epc, ct);
        if (student is null)
        {
            _log.LogWarning("RFID read for unknown EPC {Epc} at reader {Reader}", epc, reader.ReaderCode);
            return;
        }

        // suppress duplicates (same student/room/direction within the window)
        var since = eventTime - RfidSequenceEngine.DuplicateSuppression;
        bool duplicate = await _db.AttendanceEvents.AnyAsync(a =>
            a.StudentId == student.Id && a.RoomId == reader.RoomId &&
            a.Direction == direction && a.EventTime >= since, ct);
        if (duplicate) return;

        _db.AttendanceEvents.Add(new AttendanceEvent
        {
            StudentId = student.Id,
            RoomId = reader.RoomId,
            Direction = direction,
            EventTime = eventTime
        });
        await _db.SaveChangesAsync(ct);

        // gate movements are pushed to the parent instantly
        if (reader.Room.RoomType == RoomType.Gate && student.Parent is not null)
        {
            var verb = direction == Direction.Entry ? "entered" : "left";
            var local = eventTime.ToLocalTime();
            await _notifications.SendAsync(
                student.Parent.UserId,
                direction == Direction.Entry ? "GateEntry" : "GateExit",
                $"{student.User?.FullName} {verb} school",
                $"{student.User?.FullName} {verb} the school premises at {local:hh\\:mm tt} " +
                $"via {reader.Room.Name}.",
                new { studentId = student.Id, direction = direction.ToString(), time = eventTime },
                ct);
        }
    }
}
