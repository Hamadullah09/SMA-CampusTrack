using CampusTrack.Application.Common.Interfaces;
using CampusTrack.Application.Rfid;
using CampusTrack.Domain.Identity;
using CampusTrack.Infrastructure.Attendance;
using CampusTrack.Infrastructure.Dashboards;
using CampusTrack.Infrastructure.Scheduling;
using CampusTrack.Infrastructure.Identity;
using CampusTrack.Infrastructure.Notifications;
using CampusTrack.Infrastructure.People;
using CampusTrack.Infrastructure.Persistence;
using CampusTrack.Infrastructure.Persistence.Interceptors;
using CampusTrack.Infrastructure.Realtime;
using CampusTrack.Infrastructure.Reporting;
using CampusTrack.Infrastructure.Rfid;
using CampusTrack.Infrastructure.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CampusTrack.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        services.AddOptionsAndValidation(configuration, environment);
        services.AddPersistence(configuration);
        services.AddIdentityAndAuth(configuration);
        services.AddApplicationServices();
        services.AddRfidPipeline();
        services.AddBackgroundWorkers();

        return services;
    }

    private static IServiceCollection AddOptionsAndValidation(
        this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        services.Configure<SchoolTimeOptions>(configuration.GetSection(SchoolTimeOptions.SectionName));
        services.Configure<FirebaseOptions>(configuration.GetSection(FirebaseOptions.SectionName));
        services.Configure<SmtpOptions>(configuration.GetSection(SmtpOptions.SectionName));
        services.Configure<RfidDeviceOptions>(configuration.GetSection(RfidDeviceOptions.SectionName));
        services.Configure<FileStorageOptions>(configuration.GetSection(FileStorageOptions.SectionName));

        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .Validate(o => !string.IsNullOrWhiteSpace(o.Key), "Jwt:Key must be configured.")
            .Validate(o => o.Key.Length >= 32, "Jwt:Key must be at least 32 characters.")
            // Refusing to start beats running production on a key that is in the repository.
            .Validate(o => environment.IsDevelopment() || o.Key != JwtOptions.PlaceholderKey,
                "Jwt:Key is still the placeholder value. Set a real secret before running outside development.")
            .ValidateOnStart();

        return services;
    }

    private static IServiceCollection AddPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured.");

        services.AddScoped<AuditingInterceptor>();

        services.AddDbContext<CampusTrackDbContext>((provider, options) =>
        {
            options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString), mySql =>
            {
                mySql.MigrationsAssembly(typeof(CampusTrackDbContext).Assembly.FullName);
                mySql.SchemaBehavior(Pomelo.EntityFrameworkCore.MySql.Infrastructure.MySqlSchemaBehavior.Ignore);
                // Survives the brief connection loss of a database failover or restart without
                // failing the RFID pipeline outright.
                mySql.EnableRetryOnFailure(
                    maxRetryCount: 5,
                    maxRetryDelay: TimeSpan.FromSeconds(10),
                    errorNumbersToAdd: null);
                mySql.CommandTimeout(60);
            });

            options.AddInterceptors(provider.GetRequiredService<AuditingInterceptor>());
        });

        services.AddScoped<IApplicationDbContext>(p => p.GetRequiredService<CampusTrackDbContext>());
        services.AddScoped<DatabaseSeeder>();

        return services;
    }

    private static IServiceCollection AddIdentityAndAuth(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddIdentityCore<ApplicationUser>(options =>
            {
                options.User.RequireUniqueEmail = false;   // students often have no email address

                options.Password.RequiredLength = 8;
                options.Password.RequireDigit = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireNonAlphanumeric = false;

                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                options.Lockout.AllowedForNewUsers = true;

                options.SignIn.RequireConfirmedEmail = false;
            })
            .AddRoles<ApplicationRole>()
            .AddEntityFrameworkStores<CampusTrackDbContext>()
            .AddDefaultTokenProviders();

        services.AddScoped<IUserProfileBuilder, UserProfileBuilder>();
        services.AddScoped<ITokenService, TokenService>();
        services.AddScoped<IAuthService, AuthService>();

        // Permission policies are created on demand from the policy name.
        services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
        services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();

        return services;
    }

    private static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddMemoryCache();

        services.AddScoped<ICurrentUser, CurrentUser>();
        services.AddSingleton<IDateTimeProvider, DateTimeProvider>();
        services.AddSingleton<ITokenHasher, TokenHasher>();
        services.AddSingleton<ISettingsProvider, SettingsProvider>();
        services.AddSingleton<IFileStorage, LocalFileStorage>();

        services.AddScoped<INotificationService, NotificationService>();
        services.AddSingleton<IPushSender, FirebasePushSender>();
        services.AddSingleton<IEmailSender, SmtpEmailSender>();
        services.AddScoped<IRealtimePublisher, SignalRPublisher>();

        services.AddScoped<IAttendanceEngine, AttendanceEngine>();
        services.AddScoped<IDailyReportService, DailyReportService>();
        services.AddSingleton<IExportService, ExportService>();

        services.AddScoped<IPersonAccountFactory, PersonAccountFactory>();
        services.AddScoped<IStudentService, StudentService>();
        services.AddScoped<ITeacherService, TeacherService>();
        services.AddScoped<IGuardianService, GuardianService>();
        services.AddScoped<ITimetableService, TimetableService>();
        services.AddScoped<IAttendanceQueryService, AttendanceQueryService>();
        services.AddScoped<IDashboardService, DashboardService>();

        // Resilient outbound HTTP: Firebase is a third party on the notification path, and a
        // transient failure there must not turn into a failed movement event.
        services.AddHttpClient("fcm").AddStandardResilienceHandler();
        services.AddHttpClient("fcm-auth").AddStandardResilienceHandler();

        return services;
    }

    private static IServiceCollection AddRfidPipeline(this IServiceCollection services)
    {
        // Singletons: the queue and the sequence buffer hold cross-request state by design.
        services.AddSingleton<IRfidIngestQueue, RfidIngestQueue>();
        services.AddSingleton<TagSequenceBuffer>();

        services.AddScoped<IRfidIngestionService, RfidIngestionService>();
        services.AddScoped<IRfidMovementService, RfidMovementService>();
        services.AddScoped<IRfidNotificationDispatcher, RfidNotificationDispatcher>();
        services.AddScoped<IRfidSimulator, RfidSimulator>();
        services.AddScoped<IRfidQueryService, RfidQueryService>();

        services.AddAuthentication()
            .AddScheme<DeviceAuthenticationOptions, DeviceAuthenticationHandler>(
                DeviceAuthenticationOptions.SchemeName, _ => { });

        return services;
    }

    private static IServiceCollection AddBackgroundWorkers(this IServiceCollection services)
    {
        services.AddHostedService<RfidEventProcessor>();
        services.AddHostedService<ReaderHealthMonitor>();
        services.AddHostedService<ScheduledJobRunner>();

        return services;
    }
}
