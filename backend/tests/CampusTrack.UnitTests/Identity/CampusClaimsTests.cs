using System.Security.Claims;
using CampusTrack.Application.Common.Interfaces;
using CampusTrack.Infrastructure.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.IdentityModel.JsonWebTokens;
using Xunit;

namespace CampusTrack.UnitTests.Identity;

/// <summary>
/// Guards the claim names the system issues.
///
/// These tests exist because of a real defect. The teacher id was issued as "tid", which the
/// Microsoft identity stack already owns: the bearer handler rewrites it on the way in to
/// the Azure tenant-id URI. Nothing threw. The lookup by "tid" simply returned null, so every
/// teacher looked to the API like a person with no teacher profile, and the teacher dashboard
/// answered "you are not registered as a teacher" to actual teachers.
///
/// The sibling ids ("sid", "gid", "fid") happened not to collide, but they sit one framework
/// release away from doing so, so all four now carry a "ct_" prefix and are checked here.
///
/// The failure mode is silent, so the protection has to be a test rather than a comment.
/// </summary>
public class CampusClaimsTests
{
    /// <summary>
    /// The authoritative list of names ASP.NET Core will rewrite. Any claim we issue that
    /// appears here would be silently renamed before we ever read it back.
    /// </summary>
    /// <remarks>
    /// This is the map the JWT bearer handler actually consults. .NET 8 moved bearer
    /// validation onto <see cref="JsonWebTokenHandler"/>, which carries its own table --
    /// asserting against the older JwtSecurityTokenHandler map would pass while the real
    /// rewrite still happened.
    /// </remarks>
    private static readonly IDictionary<string, string> InboundMap =
        JsonWebTokenHandler.DefaultInboundClaimTypeMap;

    public static TheoryData<string, string> IssuedClaims() => new()
    {
        { nameof(CampusClaims.StudentId), CampusClaims.StudentId },
        { nameof(CampusClaims.TeacherId), CampusClaims.TeacherId },
        { nameof(CampusClaims.GuardianId), CampusClaims.GuardianId },
        { nameof(CampusClaims.StaffId), CampusClaims.StaffId },
        { nameof(CampusClaims.SchoolId), CampusClaims.SchoolId },
        { nameof(CampusClaims.Permission), CampusClaims.Permission },
        { nameof(CampusClaims.FullName), CampusClaims.FullName },
        { nameof(CampusClaims.MustChangePassword), CampusClaims.MustChangePassword },
        { nameof(CampusClaims.DeviceId), CampusClaims.DeviceId },
    };

    [Theory]
    [MemberData(nameof(IssuedClaims))]
    public void NoIssuedClaimCollidesWithTheInboundClaimTypeMap(string name, string claimType)
    {
        Assert.False(
            InboundMap.ContainsKey(claimType),
            $"CampusClaims.{name} is \"{claimType}\", which ASP.NET Core rewrites to " +
            $"\"{(InboundMap.TryGetValue(claimType, out var mapped) ? mapped : "?")}\". " +
            "Reading it back by its original name will silently return null. Rename it.");
    }

    [Fact]
    public void TheNameThatBrokeIsStillARewrittenClaim()
    {
        // Documents why the prefix exists. If this ever fails, the framework dropped the
        // mapping and the original bug would no longer reproduce -- worth knowing, but the
        // prefix stays regardless.
        Assert.True(InboundMap.ContainsKey("tid"));
        Assert.Equal("http://schemas.microsoft.com/identity/claims/tenantid", InboundMap["tid"]);
    }

    [Fact]
    public void ProfileIdsAreReadBackFromAPrincipalThatCarriesThem()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, "5"),
                new Claim(CampusClaims.SchoolId, "1"),
                new Claim(CampusClaims.StudentId, "11"),
                new Claim(CampusClaims.TeacherId, "22"),
                new Claim(CampusClaims.GuardianId, "33"),
                new Claim(CampusClaims.StaffId, "44"),
            ],
            authenticationType: "Test"));

        ICurrentUser current = new CurrentUser(new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext { User = principal },
        });

        Assert.True(current.IsAuthenticated);
        Assert.Equal(5, current.UserId);
        Assert.Equal(1, current.SchoolId);
        Assert.Equal(11, current.StudentId);
        Assert.Equal(22, current.TeacherId);
        Assert.Equal(33, current.GuardianId);
        Assert.Equal(44, current.StaffMemberId);
    }

    [Fact]
    public void AProfileIdIsNullWhenTheCallerHasNoSuchProfile()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "1")], authenticationType: "Test"));

        ICurrentUser current = new CurrentUser(new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext { User = principal },
        });

        // An administrator genuinely has no teacher profile, and that must stay
        // distinguishable from a teacher whose claim went missing in transit.
        Assert.Null(current.TeacherId);
        Assert.Null(current.StudentId);
    }
}
