namespace CampusTrack.Api.Services;

/// <summary>Stores uploaded files under Storage:Root (default ./uploads).</summary>
public class FileStorageService
{
    private readonly string _root;

    public FileStorageService(IConfiguration cfg, IWebHostEnvironment env)
    {
        var configured = cfg["Storage:Root"];
        _root = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(env.ContentRootPath, "uploads")
            : configured;
        Directory.CreateDirectory(_root);
    }

    public async Task<string> SaveAsync(IFormFile file, string subfolder, CancellationToken ct = default)
    {
        var dir = Path.Combine(_root, subfolder);
        Directory.CreateDirectory(dir);
        var name = $"{Guid.NewGuid():N}{Path.GetExtension(file.FileName)}";
        var path = Path.Combine(dir, name);
        await using var stream = File.Create(path);
        await file.CopyToAsync(stream, ct);
        return Path.Combine(subfolder, name);   // relative path stored in DB
    }

    public string GetFullPath(string relativePath) => Path.Combine(_root, relativePath);
}
