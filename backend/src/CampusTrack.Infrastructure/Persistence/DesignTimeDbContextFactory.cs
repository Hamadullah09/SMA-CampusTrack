using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace CampusTrack.Infrastructure.Persistence;

/// <summary>
/// Lets <c>dotnet ef</c> build the model without starting the API. Migrations are therefore
/// generated from the Infrastructure project alone, and CI can produce a migration script
/// without any application configuration or a reachable database.
/// </summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<CampusTrackDbContext>
{
    public CampusTrackDbContext CreateDbContext(string[] args)
    {
        // Only used to shape the model. Override with CAMPUSTRACK_DESIGN_CONNECTION when
        // scaffolding against a real server.
        var connectionString =
            Environment.GetEnvironmentVariable("CAMPUSTRACK_DESIGN_CONNECTION")
            ?? "server=localhost;port=3306;database=campustrack;user=root;password=design-time-only";

        var options = new DbContextOptionsBuilder<CampusTrackDbContext>()
            .UseMySql(connectionString, ServerVersion.Parse("8.4.0-mysql"), mySql =>
            {
                mySql.MigrationsAssembly(typeof(DesignTimeDbContextFactory).Assembly.FullName);
                mySql.SchemaBehavior(Pomelo.EntityFrameworkCore.MySql.Infrastructure.MySqlSchemaBehavior.Ignore);
            })
            .Options;

        return new CampusTrackDbContext(options);
    }
}
