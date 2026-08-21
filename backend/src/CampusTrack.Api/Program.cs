using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using CampusTrack.Api.Middleware;
using CampusTrack.Application.Common.Interfaces;
using CampusTrack.Infrastructure;
using CampusTrack.Infrastructure.Identity;
using CampusTrack.Infrastructure.Persistence;
using CampusTrack.Infrastructure.Realtime;
using FluentValidation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using Serilog;
using Serilog.Events;

// Bootstrap logger: captures failures that happen before configuration is fully read,
// which is exactly when the most confusing startup errors occur.
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog((context, services, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .Enrich.WithProperty("Application", "CampusTrack.Api")
        .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
        .MinimumLevel.Override("Microsoft.EntityFrameworkCore.Database.Command", LogEventLevel.Warning));

    // ---------------------------------------------------------------- services ----

    builder.Services.AddInfrastructure(builder.Configuration, builder.Environment);

    builder.Services.AddControllers()
        .AddJsonOptions(options =>
        {
            // Enums travel as names, not numbers: a client reading "SchoolEntry" needs no
            // shared lookup table, and inserting an enum member cannot silently shift meaning.
            options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
            options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
            options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
        });

    builder.Services.AddValidatorsFromAssemblyContaining<CampusTrack.Application.Common.Models.PagedQuery>();

    builder.Services.AddSignalR(options =>
    {
        options.EnableDetailedErrors = builder.Environment.IsDevelopment();
        options.KeepAliveInterval = TimeSpan.FromSeconds(15);
        options.ClientTimeoutInterval = TimeSpan.FromSeconds(60);
    });

    ConfigureAuthentication(builder);
    ConfigureCors(builder);
    ConfigureRateLimiting(builder);
    ConfigureSwagger(builder);

    builder.Services.AddHealthChecks()
        .AddMySql(
            builder.Configuration.GetConnectionString("Default")!,
            name: "mysql",
            tags: ["ready"]);

    // Behind a reverse proxy the client address arrives in a header; without this every
    // audit entry and rate-limit bucket would record the proxy instead of the caller.
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.KnownNetworks.Clear();
        options.KnownProxies.Clear();
    });

    var app = builder.Build();

    // ------------------------------------------------------------- middleware ----

    app.UseForwardedHeaders();
    app.UseMiddleware<ExceptionHandlingMiddleware>();
    app.UseSerilogRequestLogging(options =>
    {
        options.MessageTemplate =
            "{RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms";
        options.GetLevel = (httpContext, elapsed, ex) =>
            ex is not null || httpContext.Response.StatusCode >= 500 ? LogEventLevel.Error
            : httpContext.Response.StatusCode >= 400 ? LogEventLevel.Warning
            // Health probes fire constantly; logging each one buries everything else.
            : httpContext.Request.Path.StartsWithSegments("/health") ? LogEventLevel.Verbose
            : LogEventLevel.Information;
    });

    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI(options =>
        {
            options.SwaggerEndpoint("/swagger/v1/swagger.json", "CampusTrack API v1");
            options.DocumentTitle = "SMA Campus Track API";
            options.DisplayRequestDuration();
        });
    }
    else
    {
        app.UseHsts();
        app.UseHttpsRedirection();
    }

    app.UseStaticFiles();
    app.UseRouting();
    app.UseCors("CampusTrackClients");
    app.UseRateLimiter();
    app.UseAuthentication();
    app.UseAuthorization();

    app.MapControllers();
    app.MapHub<CampusHub>("/hubs/campus");

    app.MapHealthChecks("/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
    {
        Predicate = _ => false   // liveness only asks "is the process up"
    });
    app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
    {
        Predicate = check => check.Tags.Contains("ready")
    });

    // The SPA is served from wwwroot in production; unknown paths fall through to it so
    // client-side routes survive a refresh.
    app.MapFallbackToFile("index.html");

    await InitialiseDatabaseAsync(app);

    Log.Information("CampusTrack API starting in {Environment}", app.Environment.EnvironmentName);
    await app.RunAsync();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "CampusTrack API failed to start");
    throw;
}
finally
{
    await Log.CloseAndFlushAsync();
}

// ------------------------------------------------------------------ helpers ----

static void ConfigureAuthentication(WebApplicationBuilder builder)
{
    var jwtSection = builder.Configuration.GetSection(JwtOptions.SectionName);
    var key = jwtSection["Key"] ?? JwtOptions.PlaceholderKey;

    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
            options.SaveToken = false;
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = jwtSection["Issuer"],
                ValidAudience = jwtSection["Audience"],
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
                // Default is five minutes, which would keep a revoked token alive well past
                // its stated expiry. Thirty seconds covers ordinary clock drift.
                ClockSkew = TimeSpan.FromSeconds(30)
            };

            options.Events = new JwtBearerEvents
            {
                // Browsers cannot set headers on a WebSocket handshake, so SignalR passes the
                // token as a query parameter. Accept it only for hub paths.
                OnMessageReceived = context =>
                {
                    var accessToken = context.Request.Query["access_token"];
                    if (!string.IsNullOrEmpty(accessToken) &&
                        context.HttpContext.Request.Path.StartsWithSegments("/hubs"))
                    {
                        context.Token = accessToken;
                    }
                    return Task.CompletedTask;
                }
            };
        });
}

static void ConfigureCors(WebApplicationBuilder builder)
{
    var origins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
                  ?? ["http://localhost:5173", "http://localhost:3000"];

    builder.Services.AddCors(options => options.AddPolicy("CampusTrackClients", policy =>
    {
        policy.WithOrigins(origins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            // Required for SignalR: the negotiate call sends credentials, and a wildcard
            // origin is rejected by browsers when credentials are allowed.
            .AllowCredentials()
            .WithExposedHeaders("X-Pagination-Total", "X-Pagination-Pages");
    }));
}

static void ConfigureRateLimiting(WebApplicationBuilder builder)
{
    builder.Services.AddRateLimiter(options =>
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

        // Sign-in is the endpoint worth throttling hardest: it is the one an attacker
        // hammers, and a real person signs in a handful of times a day.
        options.AddPolicy("auth", context => RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));

        // Readers legitimately post in bursts during arrival, so their limit is generous and
        // partitioned per device rather than per address - a whole site can share one NAT IP.
        options.AddPolicy("rfid-ingest", context => RateLimitPartition.GetTokenBucketLimiter(
            context.Request.Headers["X-Device-Id"].ToString() is { Length: > 0 } device
                ? device
                : context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new TokenBucketRateLimiterOptions
            {
                TokenLimit = 600,
                TokensPerPeriod = 300,
                ReplenishmentPeriod = TimeSpan.FromSeconds(10),
                QueueLimit = 0,
                AutoReplenishment = true
            }));

        options.AddPolicy("standard", context => RateLimitPartition.GetFixedWindowLimiter(
            context.User.Identity?.Name ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 300,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 10
            }));
    });
}

static void ConfigureSwagger(WebApplicationBuilder builder)
{
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(options =>
    {
        options.SwaggerDoc("v1", new OpenApiInfo
        {
            Title = "SMA Campus Track API",
            Version = "v1",
            Description =
                "RFID-first school management platform. Covers people and academics, UHF RFID " +
                "ingestion and movement tracking, attendance, assessment, notifications and reporting."
        });

        options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
        {
            Name = "Authorization",
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            In = ParameterLocation.Header,
            Description = "Paste the access token returned by /api/v1/auth/login."
        });

        options.AddSecurityDefinition("DeviceKey", new OpenApiSecurityScheme
        {
            Name = "X-Device-Key",
            Type = SecuritySchemeType.ApiKey,
            In = ParameterLocation.Header,
            Description = "Per-reader API key. Send with X-Device-Id on RFID ingestion endpoints."
        });

        // Microsoft.OpenApi 2.x references a declared scheme by name rather than by
        // constructing a scheme object with an embedded reference.
        options.AddSecurityRequirement(_ => new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference("Bearer")] = new List<string>()
        });

        var xmlPath = Path.Combine(AppContext.BaseDirectory, "CampusTrack.Api.xml");
        if (File.Exists(xmlPath)) options.IncludeXmlComments(xmlPath);
    });
}

/// <summary>
/// Applies migrations and seeds the baseline. Automatic migration is enabled by
/// configuration, so a production operator can keep schema changes as a deliberate,
/// reviewed step rather than something a container restart performs on its own.
/// </summary>
static async Task InitialiseDatabaseAsync(WebApplication app)
{
    using var scope = app.Services.CreateScope();
    var provider = scope.ServiceProvider;
    var logger = provider.GetRequiredService<ILogger<Program>>();

    var autoMigrate = app.Configuration.GetValue("Database:AutoMigrate", app.Environment.IsDevelopment());
    var autoSeed = app.Configuration.GetValue("Database:AutoSeed", true);

    try
    {
        var db = provider.GetRequiredService<CampusTrackDbContext>();

        if (autoMigrate)
        {
            logger.LogInformation("Applying database migrations");
            await db.Database.MigrateAsync();
        }
        else if (!await db.Database.CanConnectAsync())
        {
            logger.LogError("The database is not reachable. Check ConnectionStrings:Default.");
            return;
        }

        if (autoSeed)
        {
            var seeder = provider.GetRequiredService<DatabaseSeeder>();
            await seeder.SeedAsync();
        }
    }
    catch (Exception ex)
    {
        // Starting without a database is still useful: health endpoints report the failure
        // and an operator can fix connectivity without a crash loop obscuring the cause.
        logger.LogError(ex, "Database initialisation failed. The API will start, but /health/ready will report unhealthy.");
    }
}

/// <summary>Exposed so the integration test host can reference the entry point assembly.</summary>
public partial class Program;
