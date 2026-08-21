using CampusTrack.Application.Common.Interfaces;
using CampusTrack.Application.Common.Models;
using CampusTrack.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace CampusTrack.Api.Controllers;

/// <summary>
/// Shared plumbing for every controller: route shape, authentication default, and the small
/// helpers that keep responses consistent.
/// </summary>
[ApiController]
[Route("api/v1/[controller]")]
[Authorize]
[EnableRateLimiting("standard")]
[Produces("application/json")]
public abstract class ApiControllerBase : ControllerBase
{
    private ICurrentUser? _currentUser;

    /// <summary>The authenticated caller. Resolved lazily so anonymous endpoints pay nothing.</summary>
    protected ICurrentUser CurrentUser =>
        _currentUser ??= HttpContext.RequestServices.GetRequiredService<ICurrentUser>();

    protected int RequireUserId() =>
        CurrentUser.UserId ?? throw new UnauthorizedAccessException("You are not signed in.");

    /// <summary>
    /// Returns a page and puts the totals in headers as well as the body, so a client can
    /// render a pager from the response head without parsing the payload.
    /// </summary>
    protected ActionResult<PagedResult<T>> Paged<T>(PagedResult<T> result)
    {
        Response.Headers["X-Pagination-Total"] = result.TotalCount.ToString();
        Response.Headers["X-Pagination-Pages"] = result.TotalPages.ToString();
        return Ok(result);
    }

    /// <summary>Throws a 404-shaped exception when a lookup came back empty.</summary>
    protected static T Found<T>(T? entity, string what = "item") where T : class =>
        entity ?? throw new KeyNotFoundException($"That {what} does not exist.");

    /// <summary>
    /// Guards an operation that only the owner of a record may perform. Used where a
    /// permission alone is not enough - a teacher holds "grades.manage", but only for their
    /// own classes.
    /// </summary>
    protected static void EnsureOwnership(bool isOwner, string message = "You do not have access to this record.")
    {
        if (!isOwner) throw new DomainException("forbidden", message);
    }
}
