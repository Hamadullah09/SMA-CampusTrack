using CampusTrack.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CampusTrack.Infrastructure.Services;

public class FileStorageOptions
{
    public const string SectionName = "FileStorage";

    /// <summary>Root directory for uploads. Defaults to ./storage beside the application.</summary>
    public string RootPath { get; set; } = "storage";
    public long MaxFileSizeBytes { get; set; } = 25 * 1024 * 1024;

    /// <summary>
    /// Extensions accepted for upload. An allow-list rather than a deny-list: a deny-list is a
    /// promise to have thought of every dangerous extension, which nobody can keep.
    /// </summary>
    public string[] AllowedExtensions { get; set; } =
    [
        ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx",
        ".txt", ".csv", ".rtf", ".odt",
        ".png", ".jpg", ".jpeg", ".gif", ".webp", ".heic",
        ".zip", ".mp3", ".mp4", ".m4a"
    ];
}

/// <summary>
/// Stores uploads on local disk behind a storage-agnostic interface, so moving to object
/// storage later is one new implementation rather than a change at every call site.
///
/// Filenames from users are never trusted: each file is stored under a generated name inside
/// a date-partitioned folder, and the original name is kept only as metadata for display.
/// That defeats path traversal and stops a user-chosen extension from deciding how the file
/// is served.
/// </summary>
public class LocalFileStorage : IFileStorage
{
    private readonly FileStorageOptions _options;
    private readonly ILogger<LocalFileStorage> _logger;
    private readonly string _root;

    public LocalFileStorage(IOptions<FileStorageOptions> options, ILogger<LocalFileStorage> logger)
    {
        _options = options.Value;
        _logger = logger;

        _root = Path.IsPathRooted(_options.RootPath)
            ? _options.RootPath
            : Path.Combine(AppContext.BaseDirectory, _options.RootPath);

        Directory.CreateDirectory(_root);
    }

    public async Task<StoredFile> SaveAsync(
        Stream content, string originalFileName, string folder, CancellationToken ct = default)
    {
        var extension = Path.GetExtension(originalFileName).ToLowerInvariant();

        if (!_options.AllowedExtensions.Contains(extension))
            throw new InvalidOperationException($"Files of type '{extension}' are not accepted.");

        if (content.CanSeek && content.Length > _options.MaxFileSizeBytes)
            throw new InvalidOperationException(
                $"That file is larger than the {_options.MaxFileSizeBytes / (1024 * 1024)} MB limit.");

        // Date partitioning keeps directories to a manageable size; some file systems degrade
        // badly with tens of thousands of entries in one folder.
        var relativeFolder = Path.Combine(SanitiseFolder(folder), DateTime.UtcNow.ToString("yyyy/MM"));
        var absoluteFolder = Path.Combine(_root, relativeFolder);
        Directory.CreateDirectory(absoluteFolder);

        var storedName = $"{Guid.NewGuid():N}{extension}";
        var absolutePath = Path.Combine(absoluteFolder, storedName);

        await using (var target = File.Create(absolutePath))
        {
            await content.CopyToAsync(target, ct);
        }

        var info = new FileInfo(absolutePath);

        if (info.Length > _options.MaxFileSizeBytes)
        {
            // Non-seekable streams only reveal their size after the copy.
            File.Delete(absolutePath);
            throw new InvalidOperationException(
                $"That file is larger than the {_options.MaxFileSizeBytes / (1024 * 1024)} MB limit.");
        }

        var storedPath = Path.Combine(relativeFolder, storedName).Replace('\\', '/');

        return new StoredFile(storedPath, Path.GetFileName(originalFileName), ContentTypeFor(extension), info.Length);
    }

    public Task<Stream?> OpenAsync(string storedPath, CancellationToken ct = default)
    {
        var absolute = ResolveWithinRoot(storedPath);
        if (absolute is null || !File.Exists(absolute)) return Task.FromResult<Stream?>(null);

        Stream stream = File.OpenRead(absolute);
        return Task.FromResult<Stream?>(stream);
    }

    public Task<bool> DeleteAsync(string storedPath, CancellationToken ct = default)
    {
        var absolute = ResolveWithinRoot(storedPath);
        if (absolute is null || !File.Exists(absolute)) return Task.FromResult(false);

        try
        {
            File.Delete(absolute);
            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not delete stored file {Path}", storedPath);
            return Task.FromResult(false);
        }
    }

    public bool Exists(string storedPath) => ResolveWithinRoot(storedPath) is { } p && File.Exists(p);

    /// <summary>
    /// Resolves a stored path and refuses anything that escapes the storage root. This is the
    /// single choke point for path traversal - "../../appsettings.json" resolves outside the
    /// root and is rejected here rather than in each caller.
    /// </summary>
    private string? ResolveWithinRoot(string storedPath)
    {
        if (string.IsNullOrWhiteSpace(storedPath)) return null;

        var combined = Path.GetFullPath(Path.Combine(_root, storedPath));
        var rootFull = Path.GetFullPath(_root);

        if (!combined.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Rejected a file path that resolved outside the storage root: {Path}", storedPath);
            return null;
        }

        return combined;
    }

    private static string SanitiseFolder(string folder)
    {
        var cleaned = new string(folder.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '/').ToArray());
        return string.IsNullOrWhiteSpace(cleaned) ? "misc" : cleaned.Trim('/');
    }

    private static string ContentTypeFor(string extension) => extension switch
    {
        ".pdf" => "application/pdf",
        ".doc" => "application/msword",
        ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        ".xls" => "application/vnd.ms-excel",
        ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        ".ppt" => "application/vnd.ms-powerpoint",
        ".pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        ".csv" => "text/csv",
        ".txt" => "text/plain",
        ".zip" => "application/zip",
        ".mp3" or ".m4a" => "audio/mpeg",
        ".mp4" => "video/mp4",
        _ => "application/octet-stream"
    };
}
