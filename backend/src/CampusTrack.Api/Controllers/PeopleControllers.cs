using CampusTrack.Application.Authorization;
using CampusTrack.Application.Common.Models;
using CampusTrack.Application.People;
using CampusTrack.Application.Rfid;
using CampusTrack.Infrastructure.Identity;
using CampusTrack.Infrastructure.People;
using CampusTrack.Infrastructure.Rfid;
using Microsoft.AspNetCore.Mvc;

namespace CampusTrack.Api.Controllers;

/// <summary>Student records, their enrolment, guardians and cards.</summary>
public class StudentsController : ApiControllerBase
{
    private readonly IStudentService _students;
    private readonly IRfidQueryService _rfid;

    public StudentsController(IStudentService students, IRfidQueryService rfid)
    {
        _students = students;
        _rfid = rfid;
    }

    [HttpGet]
    [HasPermission(Permissions.Students.View)]
    public async Task<ActionResult<PagedResult<StudentListItem>>> Search(
        [FromQuery] PersonQuery query, CancellationToken ct)
        => Paged(await _students.SearchAsync(query, ct));

    [HttpGet("{id:int}")]
    [HasPermission(Permissions.Students.View)]
    public async Task<ActionResult<StudentDetail>> Get(int id, CancellationToken ct)
        => Ok(await _students.GetAsync(id, ct));

    [HttpGet("by-section/{sectionId:int}")]
    [HasPermission(Permissions.Students.View)]
    public async Task<ActionResult<IReadOnlyList<StudentListItem>>> BySection(int sectionId, CancellationToken ct)
        => Ok(await _students.GetBySectionAsync(sectionId, ct));

    /// <summary>
    /// Creates a student, their sign-in account, their enrolment and optionally their card,
    /// in one transaction. Returns a temporary password when the system generated one.
    /// </summary>
    [HttpPost]
    [HasPermission(Permissions.Students.Create)]
    [ProducesResponseType(typeof(CreatedPersonResult), StatusCodes.Status201Created)]
    public async Task<ActionResult<CreatedPersonResult>> Create(CreateStudentRequest request, CancellationToken ct)
    {
        var result = await _students.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = result.Id }, result);
    }

    [HttpPut("{id:int}")]
    [HasPermission(Permissions.Students.Edit)]
    public async Task<IActionResult> Update(int id, UpdateStudentRequest request, CancellationToken ct)
    {
        await _students.UpdateAsync(id, request, ct);
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    [HasPermission(Permissions.Students.Delete)]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await _students.DeleteAsync(id, ct);
        return NoContent();
    }

    /// <summary>The student's movement timeline for a day.</summary>
    [HttpGet("{id:int}/activity")]
    [HasPermission(Permissions.Rfid.ViewEvents)]
    public async Task<ActionResult<IReadOnlyList<ActivityTimelineEntry>>> Activity(
        int id, [FromQuery] DateOnly? date, CancellationToken ct)
        => Ok(await _rfid.GetStudentTimelineAsync(id, date ?? DateOnly.FromDateTime(DateTime.UtcNow), ct));
}

public class TeachersController : ApiControllerBase
{
    private readonly ITeacherService _teachers;

    public TeachersController(ITeacherService teachers) => _teachers = teachers;

    [HttpGet]
    [HasPermission(Permissions.Teachers.View)]
    public async Task<ActionResult<PagedResult<TeacherListItem>>> Search(
        [FromQuery] PersonQuery query, CancellationToken ct)
        => Paged(await _teachers.SearchAsync(query, ct));

    [HttpGet("{id:int}")]
    [HasPermission(Permissions.Teachers.View)]
    public async Task<ActionResult<TeacherListItem>> Get(int id, CancellationToken ct)
        => Ok(await _teachers.GetAsync(id, ct));

    [HttpPost]
    [HasPermission(Permissions.Teachers.Create)]
    public async Task<ActionResult<CreatedPersonResult>> Create(CreateTeacherRequest request, CancellationToken ct)
    {
        var result = await _teachers.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = result.Id }, result);
    }

    [HttpPut("{id:int}")]
    [HasPermission(Permissions.Teachers.Edit)]
    public async Task<IActionResult> Update(int id, CreateTeacherRequest request, CancellationToken ct)
    {
        await _teachers.UpdateAsync(id, request, ct);
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    [HasPermission(Permissions.Teachers.Delete)]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await _teachers.DeleteAsync(id, ct);
        return NoContent();
    }
}

public class GuardiansController : ApiControllerBase
{
    private readonly IGuardianService _guardians;

    public GuardiansController(IGuardianService guardians) => _guardians = guardians;

    [HttpGet]
    [HasPermission(Permissions.Guardians.View)]
    public async Task<ActionResult<PagedResult<GuardianListItem>>> Search(
        [FromQuery] PersonQuery query, CancellationToken ct)
        => Paged(await _guardians.SearchAsync(query, ct));

    [HttpGet("{id:int}")]
    [HasPermission(Permissions.Guardians.View)]
    public async Task<ActionResult<GuardianListItem>> Get(int id, CancellationToken ct)
        => Ok(await _guardians.GetAsync(id, ct));

    [HttpPost]
    [HasPermission(Permissions.Guardians.Create)]
    public async Task<ActionResult<CreatedPersonResult>> Create(CreateGuardianRequest request, CancellationToken ct)
    {
        var result = await _guardians.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = result.Id }, result);
    }

    [HttpPut("{id:int}")]
    [HasPermission(Permissions.Guardians.Edit)]
    public async Task<IActionResult> Update(int id, CreateGuardianRequest request, CancellationToken ct)
    {
        await _guardians.UpdateAsync(id, request, ct);
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    [HasPermission(Permissions.Guardians.Delete)]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await _guardians.DeleteAsync(id, ct);
        return NoContent();
    }

    /// <summary>Links a child to this guardian. Staff-created links are approved immediately.</summary>
    [HttpPost("{id:int}/children")]
    [HasPermission(Permissions.Guardians.ManageLinks)]
    public async Task<IActionResult> LinkChild(int id, LinkChildRequest request, CancellationToken ct)
    {
        await _guardians.LinkChildAsync(id, request, ct);
        return NoContent();
    }

    [HttpDelete("{id:int}/children/{studentId:int}")]
    [HasPermission(Permissions.Guardians.ManageLinks)]
    public async Task<IActionResult> UnlinkChild(int id, int studentId, CancellationToken ct)
    {
        await _guardians.UnlinkChildAsync(id, studentId, ct);
        return NoContent();
    }

    /// <summary>Approves or rejects a pending guardian-child link.</summary>
    [HttpPost("links/{linkId:int}/approve")]
    [HasPermission(Permissions.Guardians.ManageLinks)]
    public async Task<IActionResult> ApproveLink(int linkId, [FromQuery] bool approved = true, CancellationToken ct = default)
    {
        await _guardians.ApproveLinkAsync(linkId, approved, ct);
        return NoContent();
    }
}
