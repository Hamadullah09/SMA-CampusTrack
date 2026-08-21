using System.Text;
using CampusTrack.Api.Data;
using CampusTrack.Api.HostedServices;
using CampusTrack.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

// MS SQL Server is the production database. The Sqlite option exists only
// so the system can be demoed on a machine without SQL Server installed
// (set Database:Provider=Sqlite, e.g. in appsettings.Development.json).
builder.Services.AddDbContext<AppDbContext>(o =>
{
    if (string.Equals(builder.Configuration["Database:Provider"], "Sqlite",
            StringComparison.OrdinalIgnoreCase))
        o.UseSqlite(builder.Configuration.GetConnectionString("Sqlite")
                    ?? "Data Source=campustrack.dev.db");
    else
        o.UseSqlServer(builder.Configuration.GetConnectionString("Default"));
});

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o => o.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = builder.Configuration["Jwt:Issuer"],
        ValidAudience = builder.Configuration["Jwt:Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"]!))
    });
builder.Services.AddAuthorization();

builder.Services.AddHttpClient();
builder.Services.AddSingleton<RfidSequenceEngine>();
builder.Services.AddScoped<AttendanceService>();
builder.Services.AddScoped<NotificationService>();
builder.Services.AddScoped<SummaryService>();
builder.Services.AddScoped<JwtService>();
builder.Services.AddSingleton<FileStorageService>();
builder.Services.AddHostedService<RfidSweepService>();
builder.Services.AddHostedService<SummaryScheduler>();

builder.Services.AddControllers().AddJsonOptions(o =>
    // accept "08:00" as well as "08:00:00" (HTML <input type="time"> sends HH:mm)
    o.JsonSerializerOptions.Converters.Add(new FlexibleTimeOnlyConverter()));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(o =>
{
    o.SwaggerDoc("v1", new OpenApiInfo { Title = "CampusTrack API", Version = "v1" });
    o.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        In = ParameterLocation.Header, Name = "Authorization",
        Type = SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = "JWT"
    });
    o.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        { new OpenApiSecurityScheme { Reference = new OpenApiReference
            { Type = ReferenceType.SecurityScheme, Id = "Bearer" } }, Array.Empty<string>() }
    });
});

builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    DbSeeder.Seed(db);
}

app.UseSwagger();
app.UseSwaggerUI();
app.UseCors();
app.UseDefaultFiles();      // serves wwwroot/index.html -> teacher/admin portal
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();
