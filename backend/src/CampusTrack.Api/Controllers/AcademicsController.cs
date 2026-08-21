using CampusTrack.Application.Authorization;
using CampusTrack.Application.Common.Models;
using CampusTrack.Domain.Academics;
using CampusTrack.Domain.Common;
using CampusTrack.Domain.Enums;
using CampusTrack.Domain.Facilities;
using CampusTrack.Infrastructure.Identity;
using CampusTrack.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CampusTrack.Api.Controllers;

/// <summary>
/// The school's academic skeleton: sessions, classes, sections, subjects, courses, rooms and
/// who teaches what. These are reference data with straightforward rules, so the controller
/// works directly against the context; anything with real behaviour (timetabling, attendance,
/// grading) lives in a dedicated service.
/// </summary>
[Route("api/v1/academics")]
public class AcademicsController : ApiControllerBase
{
    private readonly CampusTrackDbContext _db;

    public AcademicsController(CampusTrackDbContext db) => _db = db;

    // ------------------------------------------------------- academic sessions ----

    [HttpGet("sessions")]
    [HasPermission(Permissions.Academics.ViewSessions)]
    public async Task<ActionResult<IReadOnlyList<object>>> GetSessions(CancellationToken ct) =>
        Ok(await _db.AcademicSessions.AsNoTracking()
            .OrderByDescending(s => s.StartDate)
            .Select(s => (object)new
            {
                s.Id, s.Name, s.Code, s.TermType, s.StartDate, s.EndDate, s.Status, s.IsCurrent,
                terms = s.Terms.OrderBy(t => t.Sequence)
                    .Select(t => new { t.Id, t.Name, t.Sequence, t.StartDate, t.EndDate, t.IsCurrent }),
                studentCount = _db.Enrollments.Count(e => e.AcademicSessionId == s.Id && e.Status == EnrollmentStatus.Active)
            })
            .ToListAsync(ct));

    [HttpPost("sessions")]
    [HasPermission(Permissions.Academics.ManageSessions)]
    public async Task<ActionResult<object>> CreateSession(SessionRequest request, CancellationToken ct)
    {
        if (request.EndDate <= request.StartDate)
            throw DomainException.Invalid("The session end date must fall after its start date.");

        if (await _db.AcademicSessions.AnyAsync(s => s.Code == request.Code, ct))
            throw DomainException.Conflict($"A session with code '{request.Code}' already exists.");

        var session = new AcademicSession
        {
            Name = request.Name.Trim(),
            Code = request.Code.Trim().ToUpperInvariant(),
            TermType = request.TermType,
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            Status = request.IsCurrent ? AcademicSessionStatus.Active : AcademicSessionStatus.Planned,
            IsCurrent = request.IsCurrent
        };

        // Exactly one session may be current: enrolments, timetables and attendance all
        // resolve "the current session" and two would make that ambiguous.
        if (request.IsCurrent) await ClearCurrentSessionsAsync(ct);

        _db.AcademicSessions.Add(session);
        await _db.SaveChangesAsync(ct);

        return Created($"/api/v1/academics/sessions/{session.Id}", new { session.Id, session.Name, session.Code });
    }

    [HttpPost("sessions/{id:int}/set-current")]
    [HasPermission(Permissions.Academics.ManageSessions)]
    public async Task<IActionResult> SetCurrentSession(int id, CancellationToken ct)
    {
        var session = Found(await _db.AcademicSessions.FirstOrDefaultAsync(s => s.Id == id, ct), "session");

        await ClearCurrentSessionsAsync(ct);
        session.IsCurrent = true;
        session.Status = AcademicSessionStatus.Active;
        await _db.SaveChangesAsync(ct);

        return NoContent();
    }

    private async Task ClearCurrentSessionsAsync(CancellationToken ct)
    {
        var current = await _db.AcademicSessions.Where(s => s.IsCurrent).ToListAsync(ct);
        foreach (var session in current) session.IsCurrent = false;
    }

    // ------------------------------------------------------------------ classes ----

    [HttpGet("classes")]
    [HasPermission(Permissions.Academics.ViewClasses)]
    public async Task<ActionResult<IReadOnlyList<object>>> GetClasses(CancellationToken ct) =>
        Ok(await _db.SchoolClasses.AsNoTracking()
            .OrderBy(c => c.Level).ThenBy(c => c.Name)
            .Select(c => (object)new
            {
                c.Id, c.Name, c.Code, c.Level, c.CourseId, courseName = c.Course!.Name, c.IsActive,
                sectionCount = c.Sections.Count(s => s.IsActive),
                studentCount = _db.Students.Count(s => s.CurrentSection!.SchoolClassId == c.Id),
                sections = c.Sections.Where(s => s.IsActive).OrderBy(s => s.Name)
                    .Select(s => new
                    {
                        s.Id, s.Name, s.DisplayName, s.Capacity,
                        studentCount = _db.Students.Count(st => st.CurrentSectionId == s.Id),
                        homeroomTeacher = s.HomeroomTeacher == null
                            ? null
                            : s.HomeroomTeacher.User!.FirstName + " " + s.HomeroomTeacher.User.LastName
                    })
            })
            .ToListAsync(ct));

    [HttpPost("classes")]
    [HasPermission(Permissions.Academics.ManageClasses)]
    public async Task<ActionResult<object>> CreateClass(ClassRequest request, CancellationToken ct)
    {
        if (await _db.SchoolClasses.AnyAsync(c => c.Code == request.Code, ct))
            throw DomainException.Conflict($"A class with code '{request.Code}' already exists.");

        var schoolClass = new SchoolClass
        {
            Name = request.Name.Trim(),
            Code = request.Code.Trim().ToUpperInvariant(),
            Level = request.Level,
            CourseId = request.CourseId,
            IsActive = true
        };

        _db.SchoolClasses.Add(schoolClass);
        await _db.SaveChangesAsync(ct);

        return Created($"/api/v1/academics/classes/{schoolClass.Id}", new { schoolClass.Id, schoolClass.Name });
    }

    [HttpPut("classes/{id:int}")]
    [HasPermission(Permissions.Academics.ManageClasses)]
    public async Task<IActionResult> UpdateClass(int id, ClassRequest request, CancellationToken ct)
    {
        var schoolClass = Found(await _db.SchoolClasses.FirstOrDefaultAsync(c => c.Id == id, ct), "class");

        schoolClass.Name = request.Name.Trim();
        schoolClass.Level = request.Level;
        schoolClass.CourseId = request.CourseId;
        await _db.SaveChangesAsync(ct);

        return NoContent();
    }

    [HttpDelete("classes/{id:int}")]
    [HasPermission(Permissions.Academics.ManageClasses)]
    public async Task<IActionResult> DeleteClass(int id, CancellationToken ct)
    {
        var schoolClass = Found(await _db.SchoolClasses.FirstOrDefaultAsync(c => c.Id == id, ct), "class");

        var students = await _db.Students.CountAsync(s => s.CurrentSection!.SchoolClassId == id, ct);
        if (students > 0)
            throw DomainException.Conflict($"{students} student(s) are still in this class. Move them first.");

        _db.SchoolClasses.Remove(schoolClass);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    // ----------------------------------------------------------------- sections ----

    [HttpGet("sections")]
    [HasPermission(Permissions.Academics.ViewSections)]
    public async Task<ActionResult<IReadOnlyList<object>>> GetSections(
        [FromQuery] int? classId, CancellationToken ct)
    {
        var q = _db.Sections.AsNoTracking().AsQueryable();
        if (classId is { } id) q = q.Where(s => s.SchoolClassId == id);

        return Ok(await q.OrderBy(s => s.SchoolClass!.Level).ThenBy(s => s.Name)
            .Select(s => (object)new
            {
                s.Id, s.Name, s.DisplayName, s.SchoolClassId, className = s.SchoolClass!.Name,
                s.Capacity, s.HomeroomTeacherId,
                homeroomTeacher = s.HomeroomTeacher == null
                    ? null
                    : s.HomeroomTeacher.User!.FirstName + " " + s.HomeroomTeacher.User.LastName,
                s.DefaultClassroomId, classroomName = s.DefaultClassroom!.Name,
                studentCount = _db.Students.Count(st => st.CurrentSectionId == s.Id),
                s.IsActive
            })
            .ToListAsync(ct));
    }

    [HttpPost("sections")]
    [HasPermission(Permissions.Academics.ManageSections)]
    public async Task<ActionResult<object>> CreateSection(SectionRequest request, CancellationToken ct)
    {
        var schoolClass = Found(
            await _db.SchoolClasses.FirstOrDefaultAsync(c => c.Id == request.SchoolClassId, ct), "class");

        if (await _db.Sections.AnyAsync(s => s.SchoolClassId == request.SchoolClassId && s.Name == request.Name, ct))
            throw DomainException.Conflict($"Section '{request.Name}' already exists in {schoolClass.Name}.");

        var section = new Section
        {
            SchoolClassId = request.SchoolClassId,
            Name = request.Name.Trim(),
            // Composed once and stored, so every list and dropdown reads the same label
            // without joining back to the class.
            DisplayName = $"{schoolClass.Name} - {request.Name.Trim()}",
            Capacity = request.Capacity,
            HomeroomTeacherId = request.HomeroomTeacherId,
            DefaultClassroomId = request.DefaultClassroomId,
            IsActive = true
        };

        _db.Sections.Add(section);
        await _db.SaveChangesAsync(ct);

        return Created($"/api/v1/academics/sections/{section.Id}", new { section.Id, section.DisplayName });
    }

    [HttpPut("sections/{id:int}")]
    [HasPermission(Permissions.Academics.ManageSections)]
    public async Task<IActionResult> UpdateSection(int id, SectionRequest request, CancellationToken ct)
    {
        var section = Found(
            await _db.Sections.Include(s => s.SchoolClass).FirstOrDefaultAsync(s => s.Id == id, ct), "section");

        section.Name = request.Name.Trim();
        section.DisplayName = $"{section.SchoolClass!.Name} - {request.Name.Trim()}";
        section.Capacity = request.Capacity;
        section.HomeroomTeacherId = request.HomeroomTeacherId;
        section.DefaultClassroomId = request.DefaultClassroomId;

        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    // ----------------------------------------------------------------- subjects ----

    [HttpGet("subjects")]
    [HasPermission(Permissions.Academics.ViewSubjects)]
    public async Task<ActionResult<IReadOnlyList<object>>> GetSubjects(CancellationToken ct) =>
        Ok(await _db.Subjects.AsNoTracking()
            .OrderBy(s => s.Name)
            .Select(s => (object)new
            {
                s.Id, s.Name, s.Code, s.Description, s.Credits, s.TotalPlannedClasses,
                s.IsElective, s.ColourHex, s.IsActive,
                teacherCount = _db.TeachingAssignments.Where(a => a.SubjectId == s.Id && a.IsActive)
                    .Select(a => a.TeacherId).Distinct().Count()
            })
            .ToListAsync(ct));

    [HttpPost("subjects")]
    [HasPermission(Permissions.Academics.ManageSubjects)]
    public async Task<ActionResult<object>> CreateSubject(SubjectRequest request, CancellationToken ct)
    {
        if (await _db.Subjects.AnyAsync(s => s.Code == request.Code, ct))
            throw DomainException.Conflict($"A subject with code '{request.Code}' already exists.");

        var subject = new Subject
        {
            Name = request.Name.Trim(),
            Code = request.Code.Trim().ToUpperInvariant(),
            Description = request.Description,
            Credits = request.Credits,
            TotalPlannedClasses = request.TotalPlannedClasses,
            IsElective = request.IsElective,
            ColourHex = request.ColourHex,
            IsActive = true
        };

        _db.Subjects.Add(subject);
        await _db.SaveChangesAsync(ct);

        return Created($"/api/v1/academics/subjects/{subject.Id}", new { subject.Id, subject.Name });
    }

    [HttpPut("subjects/{id:int}")]
    [HasPermission(Permissions.Academics.ManageSubjects)]
    public async Task<IActionResult> UpdateSubject(int id, SubjectRequest request, CancellationToken ct)
    {
        var subject = Found(await _db.Subjects.FirstOrDefaultAsync(s => s.Id == id, ct), "subject");

        subject.Name = request.Name.Trim();
        subject.Description = request.Description;
        subject.Credits = request.Credits;
        subject.TotalPlannedClasses = request.TotalPlannedClasses;
        subject.IsElective = request.IsElective;
        subject.ColourHex = request.ColourHex;

        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpDelete("subjects/{id:int}")]
    [HasPermission(Permissions.Academics.ManageSubjects)]
    public async Task<IActionResult> DeleteSubject(int id, CancellationToken ct)
    {
        var subject = Found(await _db.Subjects.FirstOrDefaultAsync(s => s.Id == id, ct), "subject");

        var slots = await _db.TimetableSlots.CountAsync(s => s.SubjectId == id && s.IsActive, ct);
        if (slots > 0)
            throw DomainException.Conflict($"This subject appears in {slots} timetable slot(s). Remove them first.");

        _db.Subjects.Remove(subject);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    // ------------------------------------------------------------------ courses ----

    [HttpGet("courses")]
    [HasPermission(Permissions.Academics.ViewCourses)]
    public async Task<ActionResult<IReadOnlyList<object>>> GetCourses(CancellationToken ct) =>
        Ok(await _db.Courses.AsNoTracking()
            .OrderBy(c => c.Name)
            .Select(c => (object)new
            {
                c.Id, c.Name, c.Code, c.Description, c.DurationYears, c.IsActive,
                subjects = c.CourseSubjects.Select(cs => new
                {
                    cs.SubjectId, name = cs.Subject!.Name, cs.YearLevel, cs.IsMandatory
                }),
                classCount = c.Classes.Count
            })
            .ToListAsync(ct));

    [HttpPost("courses")]
    [HasPermission(Permissions.Academics.ManageCourses)]
    public async Task<ActionResult<object>> CreateCourse(CourseRequest request, CancellationToken ct)
    {
        if (await _db.Courses.AnyAsync(c => c.Code == request.Code, ct))
            throw DomainException.Conflict($"A course with code '{request.Code}' already exists.");

        var course = new Course
        {
            Name = request.Name.Trim(),
            Code = request.Code.Trim().ToUpperInvariant(),
            Description = request.Description,
            DurationYears = request.DurationYears,
            IsActive = true
        };

        foreach (var link in request.Subjects ?? [])
        {
            course.CourseSubjects.Add(new CourseSubject
            {
                SubjectId = link.SubjectId,
                YearLevel = link.YearLevel,
                IsMandatory = link.IsMandatory
            });
        }

        _db.Courses.Add(course);
        await _db.SaveChangesAsync(ct);

        return Created($"/api/v1/academics/courses/{course.Id}", new { course.Id, course.Name });
    }

    [HttpPut("courses/{id:int}")]
    [HasPermission(Permissions.Academics.ManageCourses)]
    public async Task<IActionResult> UpdateCourse(int id, CourseRequest request, CancellationToken ct)
    {
        var course = Found(await _db.Courses.Include(c => c.CourseSubjects)
            .FirstOrDefaultAsync(c => c.Id == id, ct), "course");

        course.Name = request.Name.Trim();
        course.Description = request.Description;
        course.DurationYears = request.DurationYears;

        // Subjects are replaced wholesale when supplied, which is what an editor expects
        // after ticking and unticking boxes.
        if (request.Subjects is not null)
        {
            _db.CourseSubjects.RemoveRange(course.CourseSubjects);
            foreach (var link in request.Subjects)
            {
                course.CourseSubjects.Add(new CourseSubject
                {
                    SubjectId = link.SubjectId,
                    YearLevel = link.YearLevel,
                    IsMandatory = link.IsMandatory
                });
            }
        }

        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpDelete("courses/{id:int}")]
    [HasPermission(Permissions.Academics.ManageCourses)]
    public async Task<IActionResult> DeleteCourse(int id, CancellationToken ct)
    {
        var course = Found(await _db.Courses.FirstOrDefaultAsync(c => c.Id == id, ct), "course");

        var classes = await _db.SchoolClasses.CountAsync(c => c.CourseId == id, ct);
        if (classes > 0)
            throw DomainException.Conflict($"{classes} class(es) still belong to this course.");

        _db.Courses.Remove(course);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    // --------------------------------------------------------------- classrooms ----

    [HttpGet("classrooms")]
    [HasPermission(Permissions.Classrooms.View)]
    public async Task<ActionResult<IReadOnlyList<object>>> GetClassrooms(CancellationToken ct) =>
        Ok(await _db.Classrooms.AsNoTracking()
            .OrderBy(c => c.Building).ThenBy(c => c.Name)
            .Select(c => (object)new
            {
                c.Id, c.Name, c.Code, c.Building, c.Floor, c.Capacity, c.RoomType,
                c.HasProjector, c.IsActive, c.MapX, c.MapY,
                // Tells the admin at a glance which rooms actually produce movement data.
                isMonitored = _db.RfidLocations.Any(l => l.ClassroomId == c.Id && l.IsActive)
            })
            .ToListAsync(ct));

    [HttpPost("classrooms")]
    [HasPermission(Permissions.Classrooms.Manage)]
    public async Task<ActionResult<object>> CreateClassroom(ClassroomRequest request, CancellationToken ct)
    {
        if (await _db.Classrooms.AnyAsync(c => c.Code == request.Code, ct))
            throw DomainException.Conflict($"A room with code '{request.Code}' already exists.");

        var classroom = new Classroom
        {
            Name = request.Name.Trim(),
            Code = request.Code.Trim().ToUpperInvariant(),
            Building = request.Building,
            Floor = request.Floor,
            Capacity = request.Capacity,
            RoomType = request.RoomType,
            HasProjector = request.HasProjector,
            MapX = request.MapX,
            MapY = request.MapY,
            IsActive = true
        };

        _db.Classrooms.Add(classroom);
        await _db.SaveChangesAsync(ct);

        return Created($"/api/v1/academics/classrooms/{classroom.Id}", new { classroom.Id, classroom.Name });
    }

    [HttpPut("classrooms/{id:int}")]
    [HasPermission(Permissions.Classrooms.Manage)]
    public async Task<IActionResult> UpdateClassroom(int id, ClassroomRequest request, CancellationToken ct)
    {
        var classroom = Found(await _db.Classrooms.FirstOrDefaultAsync(c => c.Id == id, ct), "room");

        classroom.Name = request.Name.Trim();
        classroom.Building = request.Building;
        classroom.Floor = request.Floor;
        classroom.Capacity = request.Capacity;
        classroom.RoomType = request.RoomType;
        classroom.HasProjector = request.HasProjector;
        classroom.MapX = request.MapX;
        classroom.MapY = request.MapY;

        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpDelete("classrooms/{id:int}")]
    [HasPermission(Permissions.Classrooms.Manage)]
    public async Task<IActionResult> DeleteClassroom(int id, CancellationToken ct)
    {
        var classroom = Found(await _db.Classrooms.FirstOrDefaultAsync(c => c.Id == id, ct), "room");

        var slots = await _db.TimetableSlots.CountAsync(s => s.ClassroomId == id && s.IsActive, ct);
        if (slots > 0)
            throw DomainException.Conflict($"This room is used by {slots} timetable slot(s).");

        _db.Classrooms.Remove(classroom);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPut("sessions/{id:int}")]
    [HasPermission(Permissions.Academics.ManageSessions)]
    public async Task<IActionResult> UpdateSession(int id, SessionRequest request, CancellationToken ct)
    {
        var session = Found(await _db.AcademicSessions.FirstOrDefaultAsync(s => s.Id == id, ct), "session");

        if (request.EndDate <= request.StartDate)
            throw DomainException.Invalid("The session end date must fall after its start date.");

        session.Name = request.Name.Trim();
        session.TermType = request.TermType;
        session.StartDate = request.StartDate;
        session.EndDate = request.EndDate;

        if (request.IsCurrent && !session.IsCurrent)
        {
            await ClearCurrentSessionsAsync(ct);
            session.IsCurrent = true;
            session.Status = AcademicSessionStatus.Active;
        }

        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>
    /// Removes an academic year. Refused while anything still hangs off it: a session
    /// carries the enrolments, teaching assignments and timetable for a whole year, and deleting it
    /// out from under them would orphan the school's entire academic record.
    /// </summary>
    [HttpDelete("sessions/{id:int}")]
    [HasPermission(Permissions.Academics.ManageSessions)]
    public async Task<IActionResult> DeleteSession(int id, CancellationToken ct)
    {
        var session = Found(await _db.AcademicSessions.FirstOrDefaultAsync(s => s.Id == id, ct), "session");

        if (session.IsCurrent)
            throw DomainException.Conflict(
                "The current academic session cannot be deleted. Make another session current first.");

        var enrollments = await _db.Enrollments.CountAsync(e => e.AcademicSessionId == id, ct);
        if (enrollments > 0)
            throw DomainException.Conflict(
                $"{enrollments} enrolment(s) belong to this session. Archive the year instead of deleting it.");

        var assignments = await _db.TeachingAssignments.CountAsync(a => a.AcademicSessionId == id, ct);
        if (assignments > 0)
            throw DomainException.Conflict($"{assignments} teaching assignment(s) belong to this session.");

        var periods = await _db.TimetablePeriods.CountAsync(x => x.AcademicSessionId == id, ct);
        if (periods > 0)
            throw DomainException.Conflict($"{periods} timetable period(s) belong to this session.");

        _db.AcademicSessions.Remove(session);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpDelete("sections/{id:int}")]
    [HasPermission(Permissions.Academics.ManageSections)]
    public async Task<IActionResult> DeleteSection(int id, CancellationToken ct)
    {
        var section = Found(await _db.Sections.FirstOrDefaultAsync(s => s.Id == id, ct), "section");

        var students = await _db.Students.CountAsync(s => s.CurrentSectionId == id, ct);
        if (students > 0)
            throw DomainException.Conflict($"{students} student(s) are still in this section. Move them first.");

        _db.Sections.Remove(section);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    // ------------------------------------------------------ teaching assignments ----

    [HttpGet("teaching-assignments")]
    [HasPermission(Permissions.Academics.ViewSections)]
    public async Task<ActionResult<IReadOnlyList<object>>> GetTeachingAssignments(
        [FromQuery] int? teacherId, [FromQuery] int? sectionId, CancellationToken ct)
    {
        var q = _db.TeachingAssignments.AsNoTracking().Where(a => a.IsActive);
        if (teacherId is { } tid) q = q.Where(a => a.TeacherId == tid);
        if (sectionId is { } sid) q = q.Where(a => a.SectionId == sid);

        return Ok(await q.Select(a => (object)new
        {
            a.Id, a.TeacherId,
            teacherName = a.Teacher!.User!.FirstName + " " + a.Teacher.User.LastName,
            a.SectionId, sectionName = a.Section!.DisplayName,
            a.SubjectId, subjectName = a.Subject!.Name,
            a.IsPrimary, a.AcademicSessionId
        }).ToListAsync(ct));
    }

    [HttpPost("teaching-assignments")]
    [HasPermission(Permissions.Academics.ManageTeachingAssignments)]
    public async Task<ActionResult<object>> AssignTeacher(TeachingAssignmentRequest request, CancellationToken ct)
    {
        var sessionId = request.AcademicSessionId
                        ?? await _db.AcademicSessions.Where(s => s.IsCurrent).Select(s => s.Id).FirstOrDefaultAsync(ct);

        if (sessionId == 0) throw DomainException.Invalid("No academic session is marked as current.");

        var existing = await _db.TeachingAssignments.FirstOrDefaultAsync(
            a => a.TeacherId == request.TeacherId
                 && a.SectionId == request.SectionId
                 && a.SubjectId == request.SubjectId
                 && a.AcademicSessionId == sessionId, ct);

        if (existing is not null)
        {
            // Reactivate rather than insert: the unique index would reject a duplicate, and
            // re-assigning a teacher who was removed mid-year is a normal thing to do.
            existing.IsActive = true;
            existing.IsPrimary = request.IsPrimary;
            await _db.SaveChangesAsync(ct);
            return Ok(new { existing.Id, reactivated = true });
        }

        var assignment = new TeachingAssignment
        {
            TeacherId = request.TeacherId,
            SectionId = request.SectionId,
            SubjectId = request.SubjectId,
            AcademicSessionId = sessionId,
            IsPrimary = request.IsPrimary,
            IsActive = true
        };

        _db.TeachingAssignments.Add(assignment);
        await _db.SaveChangesAsync(ct);

        return Created($"/api/v1/academics/teaching-assignments/{assignment.Id}", new { assignment.Id });
    }

    /// <summary>
    /// Moves an assignment onto a different teacher, subject or section -- what happens when
    /// cover changes mid-year. The unique index on the four keys is checked first so the
    /// caller gets a clear conflict rather than a database error.
    /// </summary>
    [HttpPut("teaching-assignments/{id:int}")]
    [HasPermission(Permissions.Academics.ManageTeachingAssignments)]
    public async Task<IActionResult> UpdateTeachingAssignment(
        int id, TeachingAssignmentRequest request, CancellationToken ct)
    {
        var assignment = Found(
            await _db.TeachingAssignments.FirstOrDefaultAsync(a => a.Id == id, ct), "teaching assignment");

        var sessionId = request.AcademicSessionId ?? assignment.AcademicSessionId;

        var clash = await _db.TeachingAssignments.AnyAsync(
            a => a.Id != id
                 && a.TeacherId == request.TeacherId
                 && a.SectionId == request.SectionId
                 && a.SubjectId == request.SubjectId
                 && a.AcademicSessionId == sessionId, ct);

        if (clash)
            throw DomainException.Conflict("That teacher already teaches this subject to this section.");

        assignment.TeacherId = request.TeacherId;
        assignment.SectionId = request.SectionId;
        assignment.SubjectId = request.SubjectId;
        assignment.AcademicSessionId = sessionId;
        assignment.IsPrimary = request.IsPrimary;

        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpDelete("teaching-assignments/{id:int}")]
    [HasPermission(Permissions.Academics.ManageTeachingAssignments)]
    public async Task<IActionResult> RemoveTeachingAssignment(int id, CancellationToken ct)
    {
        var assignment = Found(
            await _db.TeachingAssignments.FirstOrDefaultAsync(a => a.Id == id, ct), "assignment");

        assignment.IsActive = false;
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    // -------------------------------------------------------------- enrollments ----

    [HttpGet("enrollments")]
    [HasPermission(Permissions.Academics.ViewEnrollments)]
    public async Task<ActionResult<PagedResult<object>>> GetEnrollments(
        [FromQuery] PagedQuery paging, [FromQuery] int? sectionId, [FromQuery] int? sessionId, CancellationToken ct)
    {
        var q = _db.Enrollments.AsNoTracking().AsQueryable();
        if (sectionId is { } sid) q = q.Where(e => e.SectionId == sid);
        if (sessionId is { } aid) q = q.Where(e => e.AcademicSessionId == aid);

        var projected = q.OrderBy(e => e.Section!.DisplayName).ThenBy(e => e.RollNumber)
            .Select(e => (object)new
            {
                e.Id, e.StudentId,
                studentName = e.Student!.User!.FirstName + " " + e.Student.User.LastName,
                studentCode = e.Student.StudentCode,
                e.SectionId, sectionName = e.Section!.DisplayName,
                e.RollNumber, e.EnrolledOn, e.EndedOn, e.Status,
                sessionName = e.AcademicSession!.Name
            });

        return Paged(await projected.ToPagedResultAsync(paging.Page, paging.PageSize, ct));
    }

    /// <summary>Moves several students into a section at once - the start-of-year workflow.</summary>
    [HttpPost("enrollments/bulk")]
    [HasPermission(Permissions.Academics.ManageEnrollments)]
    public async Task<ActionResult<object>> BulkEnroll(BulkEnrollRequest request, CancellationToken ct)
    {
        var sessionId = request.AcademicSessionId
                        ?? await _db.AcademicSessions.Where(s => s.IsCurrent).Select(s => s.Id).FirstOrDefaultAsync(ct);

        if (sessionId == 0) throw DomainException.Invalid("No academic session is marked as current.");

        var section = Found(await _db.Sections.FirstOrDefaultAsync(s => s.Id == request.SectionId, ct), "section");

        var currentCount = await _db.Students.CountAsync(s => s.CurrentSectionId == request.SectionId, ct);
        if (currentCount + request.StudentIds.Count > section.Capacity)
            throw DomainException.Conflict(
                $"{section.DisplayName} holds {section.Capacity}; this would make {currentCount + request.StudentIds.Count}.");

        var students = await _db.Students.Where(s => request.StudentIds.Contains(s.Id)).ToListAsync(ct);
        var existing = await _db.Enrollments
            .Where(e => request.StudentIds.Contains(e.StudentId) && e.AcademicSessionId == sessionId)
            .ToListAsync(ct);

        var enrolled = 0;
        foreach (var student in students)
        {
            // Close any other enrolment for this session first; a student belongs to one
            // section at a time.
            foreach (var previous in existing.Where(e => e.StudentId == student.Id && e.SectionId != request.SectionId))
            {
                previous.Status = EnrollmentStatus.Transferred;
                previous.EndedOn = DateOnly.FromDateTime(DateTime.UtcNow);
            }

            var target = existing.FirstOrDefault(e => e.StudentId == student.Id && e.SectionId == request.SectionId);
            if (target is not null)
            {
                target.Status = EnrollmentStatus.Active;
                target.EndedOn = null;
            }
            else
            {
                _db.Enrollments.Add(new Enrollment
                {
                    StudentId = student.Id,
                    SectionId = request.SectionId,
                    AcademicSessionId = sessionId,
                    EnrolledOn = DateOnly.FromDateTime(DateTime.UtcNow),
                    Status = EnrollmentStatus.Active
                });
            }

            student.CurrentSectionId = request.SectionId;
            enrolled++;
        }

        await _db.SaveChangesAsync(ct);
        return Ok(new { enrolled, sectionName = section.DisplayName });
    }
}

// -------------------------------------------------------------------- requests ----

public record SessionRequest
{
    public required string Name { get; init; }
    public required string Code { get; init; }
    public TermType TermType { get; init; } = TermType.FullYear;
    public DateOnly StartDate { get; init; }
    public DateOnly EndDate { get; init; }
    public bool IsCurrent { get; init; }
}

public record ClassRequest
{
    public required string Name { get; init; }
    public required string Code { get; init; }
    public int Level { get; init; }
    public int? CourseId { get; init; }
}

public record SectionRequest
{
    public required int SchoolClassId { get; init; }
    public required string Name { get; init; }
    public int Capacity { get; init; } = 40;
    public int? HomeroomTeacherId { get; init; }
    public int? DefaultClassroomId { get; init; }
}

public record SubjectRequest
{
    public required string Name { get; init; }
    public required string Code { get; init; }
    public string? Description { get; init; }
    public int Credits { get; init; } = 1;
    public int TotalPlannedClasses { get; init; }
    public bool IsElective { get; init; }
    public string? ColourHex { get; init; }
}

public record CourseRequest
{
    public required string Name { get; init; }
    public required string Code { get; init; }
    public string? Description { get; init; }
    public int DurationYears { get; init; } = 1;
    public List<CourseSubjectLink>? Subjects { get; init; }
}

public record CourseSubjectLink
{
    public int SubjectId { get; init; }
    public int YearLevel { get; init; } = 1;
    public bool IsMandatory { get; init; } = true;
}

public record ClassroomRequest
{
    public required string Name { get; init; }
    public required string Code { get; init; }
    public string? Building { get; init; }
    public string? Floor { get; init; }
    public int Capacity { get; init; } = 40;
    public string? RoomType { get; init; }
    public bool HasProjector { get; init; }
    public double? MapX { get; init; }
    public double? MapY { get; init; }
}

public record TeachingAssignmentRequest
{
    public required int TeacherId { get; init; }
    public required int SectionId { get; init; }
    public required int SubjectId { get; init; }
    public int? AcademicSessionId { get; init; }
    public bool IsPrimary { get; init; } = true;
}

public record BulkEnrollRequest
{
    public required int SectionId { get; init; }
    public required List<int> StudentIds { get; init; }
    public int? AcademicSessionId { get; init; }
}
