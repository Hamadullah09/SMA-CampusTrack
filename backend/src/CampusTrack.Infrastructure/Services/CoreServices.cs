using System.Security.Cryptography;
using System.Text;
using CampusTrack.Application.Common.Interfaces;
using Microsoft.Extensions.Options;

namespace CampusTrack.Infrastructure.Services;

public class SchoolTimeOptions
{
    public const string SectionName = "SchoolTime";
    /// <summary>IANA or Windows zone id. Bell times and "today" are interpreted here.</summary>
    public string TimeZoneId { get; set; } = "UTC";
}

/// <summary>
/// The system clock, plus the school's local calendar.
///
/// Everything is stored in UTC, but a school day is a local concept: a gate event at
/// 23:30 UTC may belong to the next school day in Riyadh. Centralising the conversion here
/// keeps that subtlety out of every query and makes it testable.
/// </summary>
public class DateTimeProvider : IDateTimeProvider
{
    public DateTimeProvider(IOptions<SchoolTimeOptions> options)
    {
        SchoolTimeZone = ResolveTimeZone(options.Value.TimeZoneId);
    }

    public TimeZoneInfo SchoolTimeZone { get; }

    public DateTime UtcNow => DateTime.UtcNow;
    public DateTimeOffset SchoolNow => TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, SchoolTimeZone);
    public DateOnly SchoolToday => DateOnly.FromDateTime(SchoolNow.DateTime);

    public DateTime ToUtc(DateTime schoolLocal)
    {
        var unspecified = DateTime.SpecifyKind(schoolLocal, DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(unspecified, SchoolTimeZone);
    }

    public DateTimeOffset ToSchoolTime(DateTime utc)
    {
        var asUtc = utc.Kind == DateTimeKind.Utc ? utc : DateTime.SpecifyKind(utc, DateTimeKind.Utc);
        return TimeZoneInfo.ConvertTime(new DateTimeOffset(asUtc), SchoolTimeZone);
    }

    public DateOnly ToSchoolDate(DateTime utc) => DateOnly.FromDateTime(ToSchoolTime(utc).DateTime);

    /// <summary>Accepts IANA or Windows ids and falls back to UTC rather than crashing at startup.</summary>
    private static TimeZoneInfo ResolveTimeZone(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return TimeZoneInfo.Utc;
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return TimeZoneInfo.Utc;
        }
    }
}

/// <summary>
/// Hashes device API keys and refresh tokens.
///
/// These are high-entropy random strings, not user-chosen passwords, so a single SHA-256 is
/// the right tool: brute force is infeasible against 384 bits of entropy, and the hash sits
/// on the hot path of every reader ingest call where bcrypt-style work factors would be a
/// self-inflicted denial of service. User passwords go through Identity's PBKDF2 instead.
/// </summary>
public class TokenHasher : ITokenHasher
{
    public string Hash(string plainText)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(plainText));
        return Convert.ToHexString(bytes);
    }

    public bool Verify(string plainText, string hash)
    {
        if (string.IsNullOrEmpty(plainText) || string.IsNullOrEmpty(hash)) return false;
        var computed = Hash(plainText);
        // Constant-time compare: a timing side channel here would leak the stored hash.
        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(computed), Encoding.ASCII.GetBytes(hash));
    }

    public string GenerateSecureToken(int byteLength = 48) =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(byteLength))
            .Replace("+", "-").Replace("/", "_").TrimEnd('=');
}
