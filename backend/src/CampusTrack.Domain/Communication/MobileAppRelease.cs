using CampusTrack.Domain.Common;
using CampusTrack.Domain.Enums;

namespace CampusTrack.Domain.Communication;

/// <summary>
/// A build of the parent/student mobile app that the school is handing out.
///
/// Android outside the Play Store means distributing the .apk directly, so the school needs
/// somewhere to publish it and families need a stable link that always points at the current
/// build. Releases are kept rather than overwritten: when a new build turns out to be broken,
/// the previous one is still on disk to promote back.
///
/// The checksum is stored because a sideloaded binary has none of the guarantees a store
/// listing provides -- it is what lets someone confirm the file they downloaded is the file
/// the school published.
/// </summary>
public class MobileAppRelease : TenantEntity<int>
{
    public MobilePlatform Platform { get; set; } = MobilePlatform.Android;

    /// <summary>Human-facing version, e.g. "1.4.0".</summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>Monotonic build number. Android uses this, not the display version, to
    /// decide whether one build supersedes another.</summary>
    public int BuildNumber { get; set; }

    public string FileName { get; set; } = string.Empty;
    public string StoredPath { get; set; } = string.Empty;
    public long SizeBytes { get; set; }

    /// <summary>SHA-256 of the uploaded file, lowercase hex.</summary>
    public string Sha256 { get; set; } = string.Empty;

    public string? ReleaseNotes { get; set; }

    /// <summary>The build the download link serves. Exactly one per platform.</summary>
    public bool IsCurrent { get; set; }

    /// <summary>Counts completed downloads, so the office can see whether a release landed.</summary>
    public int DownloadCount { get; set; }
}
