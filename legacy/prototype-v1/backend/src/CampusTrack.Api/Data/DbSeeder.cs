using CampusTrack.Api.Domain;
using Microsoft.AspNetCore.Identity;

namespace CampusTrack.Api.Data;

public static class DbSeeder
{
    /// <summary>Creates the schema (if missing) and a default admin login.</summary>
    public static void Seed(AppDbContext db)
    {
        db.Database.EnsureCreated();

        if (!db.Users.Any(u => u.Role == Roles.Admin))
        {
            var hasher = new PasswordHasher<User>();
            var admin = new User
            {
                Username = "admin",
                Role = Roles.Admin,
                FullName = "System Administrator"
            };
            admin.PasswordHash = hasher.HashPassword(admin, "Admin@123");
            db.Users.Add(admin);
            db.SaveChanges();
        }

        if (!db.Semesters.Any())
        {
            var today = DateOnly.FromDateTime(DateTime.Today);
            db.Semesters.Add(new Semester
            {
                Name = $"Semester {today.Year}",
                StartDate = today,
                EndDate = today.AddMonths(5),
                IsCurrent = true
            });
            db.SaveChanges();
        }
    }
}
