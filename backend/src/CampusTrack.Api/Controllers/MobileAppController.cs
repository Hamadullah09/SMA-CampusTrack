using System.Security.Cryptography;
using CampusTrack.Application.Authorization;
using CampusTrack.Application.Common.Interfaces;
using CampusTrack.Domain.Common;
using CampusTrack.Domain.Communication;
using CampusTrack.Domain.Enums;
using CampusTrack.Infrastructure.Identity;
using CampusTrack.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CampusTrack.Api.Controllers;

/// <summary>
/// Distribution of the parent/student mobile app.
///
/// The app is handed out as an .apk rather than through the Play Store, so the school hosts
/// the binary itself. The metadata and download endpoints are deliberately anonymous: a
/// parent who has not signed in yet is exactly the person who needs the app, and requiring a
/// login to fetch a public client binary would be a circular requirement rather than
/// security. Publishing a build stays behind an administrator permission.
/// </summary>
[Route("api/v1/app")]
public class MobileAppController : ApiControllerBase
{
    /// <summary>
    /// Android refuses to install anything larger than this from most file managers anyway,
    /// and the cap stops an accidental upload of the wrong artefact filling the disk.
    /// </summary>
    private const long MaxUploadBytes = 200L * 1024 * 1024;

    private const string ApkContentType = "application/vnd.android.package-archive";

    private readonly CampusTrackDbContext _db;
    private readonly IFileStorage _storage;
    private readonly IDateTimeProvider _clock;
    private readonly ILogger<MobileAppController> _logger;

    public MobileAppController(
        CampusTrackDbContext db,
        IFileStorage storage,
        IDateTimeProvider clock,
        ILogger<MobileAppController> logger)
    {
        _db = db;
        _storage = storage;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>What the download page shows: version, size and checksum of the current build.</summary>
    [HttpGet("latest")]
    [AllowAnonymous]
    public async Task<ActionResult<object>> Latest(
        [FromQuery] MobilePlatform platform = MobilePlatform.Android, CancellationToken ct = default)
    {
        var release = await _db.MobileAppReleases.AsNoTracking()
            .Where(r => r.Platform == platform && r.IsCurrent)
            .Select(r => new
            {
                r.Id, r.Version, r.BuildNumber, r.FileName, r.SizeBytes,
                r.Sha256, r.ReleaseNotes, r.DownloadCount,
                publishedAtUtc = r.CreatedAtUtc,
                platform = r.Platform.ToString(),
            })
            .FirstOrDefaultAsync(ct);

        // A school that has not published a build yet is a normal state, not an error: the
        // page renders "not available yet" rather than a failure.
        return release is null
            ? Ok(new { available = false })
            : Ok(new { available = true, release });
    }

    /// <summary>Streams the current build. This is the link that goes on a letter home.</summary>
    [HttpGet("download/{platform}")]
    [AllowAnonymous]
    public async Task<IActionResult> Download(MobilePlatform platform, CancellationToken ct)
    {
        var release = await _db.MobileAppReleases
            .FirstOrDefaultAsync(r => r.Platform == platform && r.IsCurrent, ct);

        if (release is null)
            throw new KeyNotFoundException("No build of the app has been published yet.");

        var stream = await _storage.OpenAsync(release.StoredPath, ct);
        if (stream is null)
        {
            // The row survived but the file did not. Say so plainly rather than serving a
            // zero-byte apk that fails to install with no explanation.
            _logger.LogError(
                "Release {ReleaseId} ({Version}) is missing its file at {StoredPath}",
                release.Id, release.Version, release.StoredPath);

            throw DomainException.Invalid(
                "The published build is missing its file. Upload it again from the admin portal.");
        }

        // Counted without tracking the person: how many downloads happened is useful to the
        // office, who downloaded what is not worth recording.
        await _db.MobileAppReleases
            .Where(r => r.Id == release.Id)
            .ExecuteUpdateAsync(u => u.SetProperty(r => r.DownloadCount, r => r.DownloadCount + 1), ct);

        return File(stream, ApkContentType, release.FileName, enableRangeProcessing: true);
    }

    // ------------------------------------------------------------ administration ----

    [HttpGet("releases")]
    [HasPermission(Permissions.MobileApp.Manage)]
    public async Task<ActionResult<IReadOnlyList<object>>> Releases(CancellationToken ct) =>
        Ok(await _db.MobileAppReleases.AsNoTracking()
            .OrderByDescending(r => r.BuildNumber)
            .Select(r => (object)new
            {
                r.Id, r.Version, r.BuildNumber, r.FileName, r.SizeBytes, r.Sha256,
                r.ReleaseNotes, r.IsCurrent, r.DownloadCount,
                platform = r.Platform.ToString(),
                publishedAtUtc = r.CreatedAtUtc,
            })
            .ToListAsync(ct));

    /// <summary>Publishes a build and makes it the one the download link serves.</summary>
    [HttpPost("releases")]
    [HasPermission(Permissions.MobileApp.Manage)]
    [RequestSizeLimit(MaxUploadBytes)]
    public async Task<ActionResult<object>> Publish(
        [FromForm] PublishReleaseRequest request, CancellationToken ct)
    {
        var file = request.File;

        if (file is null || file.Length == 0)
            throw DomainException.Invalid("Choose the .apk file to publish.");

        if (file.Length > MaxUploadBytes)
            throw DomainException.Invalid($"That file is larger than the {MaxUploadBytes / 1024 / 1024} MB limit.");

        var platform = request.Platform ?? MobilePlatform.Android;

        if (platform == MobilePlatform.Android
            && !file.FileName.EndsWith(".apk", StringComparison.OrdinalIgnoreCase))
        {
            throw DomainException.Invalid("An Android build must be an .apk file.");
        }

        if (await _db.MobileAppReleases.AnyAsync(
                r => r.Platform == platform && r.BuildNumber == request.BuildNumber, ct))
        {
            throw DomainException.Conflict(
                $"Build {request.BuildNumber} has already been published. Increment the build number.");
        }

        string sha256;
        await using (var hashing = file.OpenReadStream())
        {
            sha256 = Convert.ToHexString(await SHA256.HashDataAsync(hashing, ct)).ToLowerInvariant();
        }

        await using var content = file.OpenReadStream();
        var stored = await _storage.SaveAsync(
            content, file.FileName, "mobile-releases", FileStoragePolicy.MobileRelease, ct);

        var release = new MobileAppRelease
        {
            SchoolId = CurrentUser.SchoolId,
            Platform = platform,
            Version = request.Version.Trim(),
            BuildNumber = request.BuildNumber,
            FileName = file.FileName,
            StoredPath = stored.StoredPath,
            SizeBytes = stored.SizeBytes,
            Sha256 = sha256,
            ReleaseNotes = request.ReleaseNotes,
            IsCurrent = true,
            CreatedAtUtc = _clock.UtcNow,
        };

        await _db.InTransactionAsync(async token =>
        {
            // Exactly one build per platform is current, so publishing demotes the rest in
            // the same unit of work as the insert.
            await _db.MobileAppReleases
                .Where(r => r.Platform == platform && r.IsCurrent)
                .ExecuteUpdateAsync(u => u.SetProperty(r => r.IsCurrent, false), token);

            _db.MobileAppReleases.Add(release);
            await _db.SaveChangesAsync(token);
        }, ct);

        _logger.LogInformation(
            "Published {Platform} build {BuildNumber} ({Version}), {SizeBytes} bytes",
            platform, release.BuildNumber, release.Version, release.SizeBytes);

        return Created($"/api/v1/app/releases/{release.Id}", new
        {
            release.Id, release.Version, release.BuildNumber, release.SizeBytes, release.Sha256,
        });
    }

    /// <summary>Rolls the download link back to an earlier build.</summary>
    [HttpPost("releases/{id:int}/promote")]
    [HasPermission(Permissions.MobileApp.Manage)]
    public async Task<IActionResult> Promote(int id, CancellationToken ct)
    {
        var release = Found(await _db.MobileAppReleases.FirstOrDefaultAsync(r => r.Id == id, ct), "release");

        await _db.InTransactionAsync(async token =>
        {
            await _db.MobileAppReleases
                .Where(r => r.Platform == release.Platform && r.IsCurrent)
                .ExecuteUpdateAsync(u => u.SetProperty(r => r.IsCurrent, false), token);

            release.IsCurrent = true;
            await _db.SaveChangesAsync(token);
        }, ct);

        return NoContent();
    }

    [HttpDelete("releases/{id:int}")]
    [HasPermission(Permissions.MobileApp.Manage)]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var release = Found(await _db.MobileAppReleases.FirstOrDefaultAsync(r => r.Id == id, ct), "release");

        if (release.IsCurrent)
            throw DomainException.Conflict(
                "That is the build families are downloading. Promote another one first.");

        await _storage.DeleteAsync(release.StoredPath, ct);
        _db.MobileAppReleases.Remove(release);
        await _db.SaveChangesAsync(ct);

        return NoContent();
    }
}

public class PublishReleaseRequest
{
    public IFormFile? File { get; set; }
    public string Version { get; set; } = string.Empty;
    public int BuildNumber { get; set; }
    public string? ReleaseNotes { get; set; }
    public MobilePlatform? Platform { get; set; }
}
