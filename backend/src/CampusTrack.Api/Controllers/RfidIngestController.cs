using CampusTrack.Application.Rfid;
using CampusTrack.Infrastructure.Identity;
using CampusTrack.Infrastructure.Rfid;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace CampusTrack.Api.Controllers;

/// <summary>
/// The endpoint RFID readers and local gateways post to.
///
/// Separated from the rest of the RFID API because it is a machine interface with different
/// rules: device-key authentication rather than user tokens, its own rate-limit budget sized
/// for burst traffic, and a contract that must stay stable for firmware that is awkward to
/// update once it is mounted above a doorway.
/// </summary>
[ApiController]
[Route("api/v1/rfid")]
[Authorize(AuthenticationSchemes = DeviceAuthenticationOptions.SchemeName)]
[EnableRateLimiting("rfid-ingest")]
public class RfidIngestController : ControllerBase
{
    private readonly IRfidIngestionService _ingestion;
    private readonly ILogger<RfidIngestController> _logger;

    public RfidIngestController(IRfidIngestionService ingestion, ILogger<RfidIngestController> logger)
    {
        _ingestion = ingestion;
        _logger = logger;
    }

    /// <summary>
    /// Accepts a batch of antenna reads.
    ///
    /// Returns as soon as the reads are queued; interpretation happens asynchronously. A
    /// gateway should treat a 200 as "safely received", retry on 5xx with the same BatchId,
    /// and back off when QueueDepth climbs.
    /// </summary>
    [HttpPost("reads")]
    [ProducesResponseType(typeof(RfidIngestResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<RfidIngestResponse>> PostReads(RfidReadBatch batch, CancellationToken ct)
    {
        var readerId = User.GetReaderId();
        if (readerId is null) return Unauthorized();

        // The body must agree with the authenticated identity: a reader may not post reads
        // claiming to be a different device.
        var authenticatedDevice = User.GetDeviceId();
        if (!string.Equals(batch.DeviceId, authenticatedDevice, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Device {Authenticated} tried to post reads as {Claimed}",
                authenticatedDevice, batch.DeviceId);
            return Forbid();
        }

        return Ok(await _ingestion.IngestAsync(batch, readerId.Value, ct));
    }

    /// <summary>
    /// Keep-alive from a reader. Silence is what marks a device offline, so this is how a
    /// quiet reader (an empty corridor at 3pm) proves it is still working.
    /// </summary>
    [HttpPost("heartbeat")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Heartbeat(RfidHeartbeat heartbeat, CancellationToken ct)
    {
        var readerId = User.GetReaderId();
        if (readerId is null) return Unauthorized();

        await _ingestion.RecordHeartbeatAsync(heartbeat, readerId.Value, ct);
        return NoContent();
    }

    /// <summary>Lets a newly provisioned device confirm its key and identity before going live.</summary>
    [HttpGet("whoami")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult WhoAmI() => Ok(new
    {
        deviceId = User.GetDeviceId(),
        readerId = User.GetReaderId(),
        authenticated = true,
        serverTimeUtc = DateTime.UtcNow
    });
}
