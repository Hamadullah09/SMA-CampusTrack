using CampusTrack.Application.Common;
using CampusTrack.Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CampusTrack.Infrastructure.Services;

/// <summary>
/// Reads runtime settings with a short in-memory cache.
///
/// The RFID pipeline consults several of these per event, so hitting MySQL each time would
/// put the settings table on the hottest path in the product. A 60 second cache keeps that
/// cost near zero while still letting an administrator's change take effect promptly; edits
/// through the settings API invalidate immediately.
/// </summary>
public class SettingsProvider : ISettingsProvider
{
    private const string CacheKeyPrefix = "setting:";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(60);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IMemoryCache _cache;
    private readonly ILogger<SettingsProvider> _logger;

    public SettingsProvider(IServiceScopeFactory scopeFactory, IMemoryCache cache, ILogger<SettingsProvider> logger)
    {
        _scopeFactory = scopeFactory;
        _cache = cache;
        _logger = logger;
    }

    public async Task<string?> GetAsync(string key, CancellationToken ct = default)
    {
        if (_cache.TryGetValue(CacheKeyPrefix + key, out string? cached)) return cached;

        string? value;
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

            var row = await db.SystemSettings.AsNoTracking()
                .Where(s => s.Key == key)
                .Select(s => new { s.Value, s.DefaultValue })
                .FirstOrDefaultAsync(ct);

            value = row?.Value ?? row?.DefaultValue ?? FallbackFor(key);
        }
        catch (Exception ex)
        {
            // A settings read must never take the request down. Fall back to the compiled
            // default so the RFID pipeline keeps running through a database blip.
            _logger.LogWarning(ex, "Could not read setting {Key}; using compiled default", key);
            value = FallbackFor(key);
        }

        _cache.Set(CacheKeyPrefix + key, value, CacheDuration);
        return value;
    }

    public async Task<T> GetAsync<T>(string key, T defaultValue, CancellationToken ct = default)
    {
        var raw = await GetAsync(key, ct);
        if (string.IsNullOrWhiteSpace(raw)) return defaultValue;

        try
        {
            var target = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);

            if (target == typeof(bool))
                return (T)(object)(raw.Equals("true", StringComparison.OrdinalIgnoreCase) || raw == "1");
            if (target == typeof(TimeOnly))
                return (T)(object)TimeOnly.Parse(raw);
            if (target == typeof(TimeSpan))
                return (T)(object)TimeSpan.Parse(raw);

            return (T)Convert.ChangeType(raw, target, System.Globalization.CultureInfo.InvariantCulture);
        }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException)
        {
            _logger.LogWarning("Setting {Key} has value '{Value}' which is not a valid {Type}; using {Default}",
                key, raw, typeof(T).Name, defaultValue);
            return defaultValue;
        }
    }

    public async Task SetAsync(string key, string? value, CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

        var setting = await db.SystemSettings.FirstOrDefaultAsync(s => s.Key == key, ct)
            ?? throw new KeyNotFoundException($"Unknown setting '{key}'.");

        setting.Value = value;
        await db.SaveChangesAsync(ct);
        _cache.Remove(CacheKeyPrefix + key);
    }

    public void Invalidate()
    {
        foreach (var seed in SettingKeys.Defaults) _cache.Remove(CacheKeyPrefix + seed.Key);
    }

    private static string? FallbackFor(string key) =>
        SettingKeys.Defaults.FirstOrDefault(d => d.Key == key)?.DefaultValue;
}
