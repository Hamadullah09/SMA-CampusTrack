using CampusTrack.RfidGateway;
using Serilog;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.Configure<GatewayOptions>(
    builder.Configuration.GetSection(GatewayOptions.SectionName));

builder.Services.AddHttpClient("api")
    .ConfigureHttpClient(client => client.Timeout = TimeSpan.FromSeconds(15));

builder.Services.AddHostedService<GatewayWorker>();

// Runs unattended in a cupboard on the school network, so the log is the only diagnostic
// anyone will have. Keep a rolling week of it on disk.
builder.Services.AddSerilog((services, configuration) => configuration
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File("logs/gateway-.log", rollingInterval: RollingInterval.Day, retainedFileCountLimit: 7));

var host = builder.Build();
await host.RunAsync();
