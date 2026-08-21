using CampusTrack.Application.Authorization;
using CampusTrack.Infrastructure.Rfid;
using Xunit;

namespace CampusTrack.UnitTests.Rfid;

/// <summary>
/// EPC normalisation sits in front of every tag lookup. If two readers format the same
/// physical card differently and normalisation lets that through, the same student resolves
/// to two identities — or to none.
/// </summary>
public class EpcNormalisationTests
{
    [Theory]
    [InlineData("e28011606000020c3f1a2b3c", "E28011606000020C3F1A2B3C")]
    [InlineData("E280 1160 6000 020C 3F1A 2B3C", "E28011606000020C3F1A2B3C")]
    [InlineData("E280-1160-6000-020C-3F1A-2B3C", "E28011606000020C3F1A2B3C")]
    [InlineData("E280:1160:6000:020C:3F1A:2B3C", "E28011606000020C3F1A2B3C")]
    [InlineData("  E28011606000020C3F1A2B3C  ", "E28011606000020C3F1A2B3C")]
    public void FormattingDifferencesResolveToTheSameValue(string raw, string expected)
    {
        Assert.Equal(expected, RfidIngestionService.NormaliseEpc(raw));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("NOT-HEX-AT-ALL")]
    [InlineData("E280ZZZZ")]          // Z is not a hex digit
    [InlineData("E280")]              // too short to be a real EPC
    public void InvalidValuesAreRejected(string? raw)
    {
        Assert.Null(RfidIngestionService.NormaliseEpc(raw));
    }

    [Fact]
    public void AbsurdlyLongValuesAreRejected()
    {
        // Guards the tag lookup against a malformed or hostile payload.
        var tooLong = new string('A', 200);
        Assert.Null(RfidIngestionService.NormaliseEpc(tooLong));
    }

    [Fact]
    public void MaskingLeavesOnlyTheTail()
    {
        // Logs, screenshots and support tickets must never carry a full card identifier.
        var masked = RfidMovementService.MaskEpc("E28011606000020C3F1A2B3C");

        Assert.EndsWith("1A2B3C", masked);
        Assert.DoesNotContain("E28011606000", masked);
        Assert.Equal("E28011606000020C3F1A2B3C".Length, masked.Length);
    }

    [Fact]
    public void MaskingHandlesShortValuesWithoutThrowing()
    {
        Assert.Equal("ABC", RfidMovementService.MaskEpc("ABC"));
    }
}

/// <summary>
/// The permission catalogue is discovered by reflection and seeded into the database. If
/// discovery breaks, authorisation silently loses its vocabulary.
/// </summary>
public class PermissionCatalogueTests
{
    [Fact]
    public void CatalogueIsDiscoveredAndNonTrivial()
    {
        Assert.True(Permissions.All.Count > 50,
            $"Expected the full catalogue; found {Permissions.All.Count}.");
    }

    [Fact]
    public void PermissionNamesAreUnique()
    {
        var duplicates = Permissions.All
            .GroupBy(p => p.Name)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        Assert.True(duplicates.Count == 0, $"Duplicated permissions: {string.Join(", ", duplicates)}");
    }

    [Fact]
    public void EveryPermissionIsGrouped()
    {
        Assert.All(Permissions.All, p => Assert.False(string.IsNullOrWhiteSpace(p.Group)));
    }

    [Fact]
    public void SuperAdminDefaultsToEveryPermission()
    {
        var granted = RolePermissionDefaults.Map[Permissions.RoleNames.SuperAdmin];
        Assert.Equal(Permissions.All.Count, granted.Length);
    }

    [Fact]
    public void EveryRoleDefaultReferencesRealPermissions()
    {
        var known = Permissions.All.Select(p => p.Name).ToHashSet();

        foreach (var (role, granted) in RolePermissionDefaults.Map)
        {
            var unknown = granted.Where(name => !known.Contains(name)).ToList();
            Assert.True(unknown.Count == 0,
                $"Role {role} references permissions that do not exist: {string.Join(", ", unknown)}");
        }
    }

    [Fact]
    public void StudentAndGuardianRolesCannotManageOtherPeople()
    {
        // A regression here would let a parent edit the school roll.
        foreach (var role in new[] { Permissions.RoleNames.Student, Permissions.RoleNames.Guardian })
        {
            var granted = RolePermissionDefaults.Map[role];

            Assert.DoesNotContain(Permissions.Students.Create, granted);
            Assert.DoesNotContain(Permissions.Students.Edit, granted);
            Assert.DoesNotContain(Permissions.Students.Delete, granted);
            Assert.DoesNotContain(Permissions.Students.View, granted);
            Assert.DoesNotContain(Permissions.Users.View, granted);
            Assert.DoesNotContain(Permissions.Attendance.Mark, granted);
            Assert.DoesNotContain(Permissions.Rfid.ManageTags, granted);
        }
    }

    [Fact]
    public void TeacherCannotAdministerUsersOrSettings()
    {
        var granted = RolePermissionDefaults.Map[Permissions.RoleNames.Teacher];

        Assert.DoesNotContain(Permissions.Users.Create, granted);
        Assert.DoesNotContain(Permissions.Users.ManageRoles, granted);
        Assert.DoesNotContain(Permissions.Settings.Manage, granted);
        Assert.DoesNotContain(Permissions.Rfid.ManageReaders, granted);
    }
}
