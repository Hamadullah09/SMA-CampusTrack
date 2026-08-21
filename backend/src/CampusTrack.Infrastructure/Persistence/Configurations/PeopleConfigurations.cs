using CampusTrack.Domain.Identity;
using CampusTrack.Domain.People;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CampusTrack.Infrastructure.Persistence.Configurations;

public class SchoolConfiguration : IEntityTypeConfiguration<School>
{
    public void Configure(EntityTypeBuilder<School> b)
    {
        b.ToTable("schools");
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.Code).HasMaxLength(32).IsRequired();
        b.Property(x => x.TimeZoneId).HasMaxLength(64);
        b.Property(x => x.Address).HasMaxLength(400);
        b.Property(x => x.Website).HasMaxLength(300);
        b.HasIndex(x => x.Code).IsUnique();
    }
}

public class ApplicationUserConfiguration : IEntityTypeConfiguration<ApplicationUser>
{
    public void Configure(EntityTypeBuilder<ApplicationUser> b)
    {
        b.Property(x => x.FirstName).HasMaxLength(80).IsRequired();
        b.Property(x => x.LastName).HasMaxLength(80).IsRequired();
        b.Property(x => x.NationalId).HasMaxLength(64);
        b.Property(x => x.Address).HasMaxLength(400);
        b.Property(x => x.City).HasMaxLength(120);
        b.Property(x => x.ProfileImagePath).HasMaxLength(400);
        b.Property(x => x.TimeZoneId).HasMaxLength(64);
        b.Property(x => x.PreferredLanguage).HasMaxLength(8);
        b.Property(x => x.LastLoginIp).HasMaxLength(64);

        // FullName is composed in C#; there is no column behind it.
        b.Ignore(x => x.FullName);

        b.HasIndex(x => x.SchoolId);
        b.HasIndex(x => x.IsActive);
        // Supports the "search users by name" box without a full scan.
        b.HasIndex(x => new { x.LastName, x.FirstName });
    }
}

public class ApplicationRoleConfiguration : IEntityTypeConfiguration<ApplicationRole>
{
    public void Configure(EntityTypeBuilder<ApplicationRole> b)
    {
        b.Property(x => x.Description).HasMaxLength(300);
    }
}

public class PermissionConfiguration : IEntityTypeConfiguration<Permission>
{
    public void Configure(EntityTypeBuilder<Permission> b)
    {
        b.ToTable("permissions");
        b.Property(x => x.Name).HasMaxLength(120).IsRequired();
        b.Property(x => x.Group).HasMaxLength(60).IsRequired();
        b.Property(x => x.DisplayName).HasMaxLength(160).IsRequired();
        b.Property(x => x.Description).HasMaxLength(400);
        b.HasIndex(x => x.Name).IsUnique();
        b.HasIndex(x => x.Group);
    }
}

public class RolePermissionConfiguration : IEntityTypeConfiguration<RolePermission>
{
    public void Configure(EntityTypeBuilder<RolePermission> b)
    {
        b.ToTable("role_permissions");
        b.HasKey(x => new { x.RoleId, x.PermissionId });

        b.HasOne(x => x.Role).WithMany(r => r!.RolePermissions)
            .HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Permission).WithMany(p => p!.RolePermissions)
            .HasForeignKey(x => x.PermissionId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class UserPermissionConfiguration : IEntityTypeConfiguration<UserPermission>
{
    public void Configure(EntityTypeBuilder<UserPermission> b)
    {
        b.ToTable("user_permissions");
        b.HasKey(x => new { x.UserId, x.PermissionId });

        b.HasOne(x => x.User).WithMany()
            .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Permission).WithMany()
            .HasForeignKey(x => x.PermissionId).OnDelete(DeleteBehavior.Cascade);
        b.HasQueryFilter(x => !x.User!.IsDeleted);
    }
}

public class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> b)
    {
        b.ToTable("refresh_tokens");
        b.Property(x => x.TokenHash).HasMaxLength(128).IsRequired();
        b.Property(x => x.ReplacedByTokenHash).HasMaxLength(128);
        b.Property(x => x.CreatedByIp).HasMaxLength(64);
        b.Property(x => x.RevokedByIp).HasMaxLength(64);
        b.Property(x => x.RevokedReason).HasMaxLength(200);
        b.Property(x => x.DeviceName).HasMaxLength(160);

        // Refresh happens on every token rotation, so this lookup must be a unique index hit.
        b.HasIndex(x => x.TokenHash).IsUnique();
        b.HasIndex(x => new { x.UserId, x.ExpiresAtUtc });

        b.HasOne(x => x.User).WithMany(u => u!.RefreshTokens)
            .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        // A deleted account's tokens must not be refreshable.
        b.HasQueryFilter(x => !x.User!.IsDeleted);
    }
}

public class LoginAttemptConfiguration : IEntityTypeConfiguration<LoginAttempt>
{
    public void Configure(EntityTypeBuilder<LoginAttempt> b)
    {
        b.ToTable("login_attempts");
        b.Property(x => x.UserNameOrEmail).HasMaxLength(256).IsRequired();
        b.Property(x => x.FailureReason).HasMaxLength(200);
        b.Property(x => x.IpAddress).HasMaxLength(64);
        b.Property(x => x.UserAgent).HasMaxLength(400);

        // Brute-force detection queries by identity or by source address over a time window.
        b.HasIndex(x => new { x.UserNameOrEmail, x.AttemptedAtUtc });
        b.HasIndex(x => new { x.IpAddress, x.AttemptedAtUtc });
    }
}

public class StudentConfiguration : IEntityTypeConfiguration<Student>
{
    public void Configure(EntityTypeBuilder<Student> b)
    {
        b.ToTable("students");
        b.Property(x => x.StudentCode).HasMaxLength(32).IsRequired();
        b.Property(x => x.AdmissionNumber).HasMaxLength(32);
        b.Property(x => x.BloodGroup).HasMaxLength(8);
        b.Property(x => x.MedicalNotes).HasMaxLength(1000);
        b.Property(x => x.EmergencyContactName).HasMaxLength(160);
        b.Property(x => x.EmergencyContactPhone).HasMaxLength(32);
        b.Property(x => x.TransportRoute).HasMaxLength(120);

        b.HasIndex(x => new { x.SchoolId, x.StudentCode }).IsUnique();
        b.HasIndex(x => x.UserId).IsUnique();
        b.HasIndex(x => x.CurrentSectionId);
        b.HasIndex(x => x.Status);

        b.HasOne(x => x.User).WithOne()
            .HasForeignKey<Student>(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.CurrentSection).WithMany()
            .HasForeignKey(x => x.CurrentSectionId).OnDelete(DeleteBehavior.SetNull);
    }
}

public class TeacherConfiguration : IEntityTypeConfiguration<Teacher>
{
    public void Configure(EntityTypeBuilder<Teacher> b)
    {
        b.ToTable("teachers");
        b.Property(x => x.TeacherCode).HasMaxLength(32).IsRequired();
        b.Property(x => x.Qualification).HasMaxLength(200);
        b.Property(x => x.Specialisation).HasMaxLength(200);
        b.Property(x => x.OfficeLocation).HasMaxLength(120);

        b.HasIndex(x => new { x.SchoolId, x.TeacherCode }).IsUnique();
        b.HasIndex(x => x.UserId).IsUnique();

        b.HasOne(x => x.User).WithOne()
            .HasForeignKey<Teacher>(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class StaffMemberConfiguration : IEntityTypeConfiguration<StaffMember>
{
    public void Configure(EntityTypeBuilder<StaffMember> b)
    {
        b.ToTable("staff_members");
        b.Property(x => x.StaffCode).HasMaxLength(32).IsRequired();
        b.Property(x => x.JobTitle).HasMaxLength(120).IsRequired();
        b.Property(x => x.Department).HasMaxLength(120);

        b.HasIndex(x => new { x.SchoolId, x.StaffCode }).IsUnique();
        b.HasIndex(x => x.UserId).IsUnique();

        b.HasOne(x => x.User).WithOne()
            .HasForeignKey<StaffMember>(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class GuardianConfiguration : IEntityTypeConfiguration<Guardian>
{
    public void Configure(EntityTypeBuilder<Guardian> b)
    {
        b.ToTable("guardians");
        b.Property(x => x.GuardianCode).HasMaxLength(32).IsRequired();
        b.Property(x => x.Occupation).HasMaxLength(120);
        b.Property(x => x.WorkplacePhone).HasMaxLength(32);
        b.Property(x => x.AlternatePhone).HasMaxLength(32);

        b.HasIndex(x => new { x.SchoolId, x.GuardianCode }).IsUnique();
        b.HasIndex(x => x.UserId).IsUnique();

        b.HasOne(x => x.User).WithOne()
            .HasForeignKey<Guardian>(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class GuardianStudentConfiguration : IEntityTypeConfiguration<GuardianStudent>
{
    public void Configure(EntityTypeBuilder<GuardianStudent> b)
    {
        b.ToTable("guardian_students");

        // One link per pair. The pair is also the lookup used on every guardian request to
        // prove the caller may see this child, so it must be a unique index.
        b.HasIndex(x => new { x.GuardianId, x.StudentId }).IsUnique();
        b.HasIndex(x => x.StudentId);

        b.HasOne(x => x.Guardian).WithMany(g => g!.Students)
            .HasForeignKey(x => x.GuardianId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Student).WithMany(s => s!.Guardians)
            .HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Cascade);

        b.HasQueryFilter(x => !x.IsDeleted);
    }
}
