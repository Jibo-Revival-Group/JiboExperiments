using Jibo.Cloud.Api.Hosting;
using Jibo.Cloud.Application.Abstractions;
using Jibo.Cloud.Application.Services;
using Jibo.Cloud.Infrastructure.DependencyInjection;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.SystemConsole.Themes;

OpenJiboEnvLoader.Load();

var builder = WebApplication.CreateBuilder(args);

if (ShouldResetDiagnosticsOnStartup(builder.Configuration))
    ResetDiagnosticsDirectories(builder.Configuration);

builder.Host.UseSerilog((context, _, loggerConfiguration) =>
{
    var minimumLevel = ParseLogEventLevel(context.Configuration["OpenJibo:Logging:MinimumLevel"]);
    var logDirectory = ResolveConfiguredPath(context.Configuration,
        "OpenJibo:Logging:DirectoryPath",
        "captures/logs");
    var logFileName = context.Configuration["OpenJibo:Logging:FileName"] ?? "openjibo-.log";
    Directory.CreateDirectory(logDirectory);

    loggerConfiguration
        .MinimumLevel.Is(minimumLevel)
        .Enrich.FromLogContext()
        .Enrich.WithProperty("Application", "OpenJibo.Cloud.Api")
        .WriteTo.Console(
            theme: AnsiConsoleTheme.Code,
            outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}")
        .WriteTo.File(
            Path.Combine(logDirectory, logFileName),
            rollingInterval: RollingInterval.Day,
            retainedFileCountLimit: 14,
            shared: true,
            restrictedToMinimumLevel: minimumLevel,
            outputTemplate:
            "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}");
});

builder.Services.AddOpenJiboCloud(builder.Configuration);
builder.Services.AddSingleton<HomeAssistantWebSocketHandler>();
builder.Services.AddSingleton<WebSocketRequestCoordinator>();
builder.Services.AddHttpClient("OpenJiboFleetPeerSync", client => client.Timeout = TimeSpan.FromSeconds(10));
builder.Services.AddHostedService<FleetPeerSyncService>();
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
    policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();

app.Logger.LogInformation("Starting Open Jibo Cloud Api version {Version}", OpenJiboCloudBuildInfo.Version);

app.UseCors();
app.UseDefaultFiles();
app.UseStaticFiles();
// Stock Jibo's Hub client drops an otherwise idle connection at the two-minute
// boundary. The middleware default heartbeat uses that same interval, so send
// keep-alive frames well before the Azure/robot idle cutoff.
app.UseWebSockets(new WebSocketOptions
{
    KeepAliveInterval = TimeSpan.FromSeconds(30)
});

app.Use(async (context, next) =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        await next();
        return;
    }

    var coordinator = context.RequestServices.GetRequiredService<WebSocketRequestCoordinator>();
    await coordinator.HandleAsync(context);
});

app.MapGet("/health", () => Results.Json(new
{
    ok = true,
    service = "OpenJibo Cloud Api",
    version = OpenJiboCloudBuildInfo.Version
}));

app.MapPortalStaticFiles();
app.MapPortalEndpoints();

app.MapMethods("/{**path}", ["GET", "POST", "PUT"], async (HttpContext context, JiboCloudProtocolService service,
    IProtocolTelemetrySink telemetrySink, CancellationToken cancellationToken) =>
{
    if (PortalStaticFileMapper.IsPortalPath(context.Request.Path))
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        await context.Response.WriteAsync("Not found", cancellationToken);
        return;
    }

    var envelope = await ApiRequestEnvelopeFactory.CreateAsync(context, cancellationToken);
    var result = await service.DispatchAsync(envelope);
    try
    {
        await telemetrySink.RecordAsync(envelope, result, cancellationToken);
    }
    catch (Exception ex)
    {
        app.Logger.LogWarning(ex,
            "HTTP telemetry recording failed for {Method} {Host}{Path}; returning response without telemetry.",
            envelope.Method,
            envelope.HostName,
            envelope.Path);
    }

    context.Response.StatusCode = result.StatusCode;
    context.Response.ContentType = result.ContentType;

    foreach (var header in result.Headers) context.Response.Headers[header.Key] = header.Value;

    if (!string.IsNullOrEmpty(result.BodyText)) await context.Response.WriteAsync(result.BodyText, cancellationToken);
});

app.Run();

return;

static bool ShouldResetDiagnosticsOnStartup(IConfiguration configuration)
{
    return bool.TryParse(configuration["OpenJibo:Logging:ResetOnStartup"], out var reset) && reset;
}

static void ResetDiagnosticsDirectories(IConfiguration configuration)
{
    var paths = new[]
    {
        ResolveConfiguredPath(configuration, "OpenJibo:Telemetry:DirectoryPath", "captures/websocket"),
        ResolveConfiguredPath(configuration, "OpenJibo:ProtocolTelemetry:DirectoryPath", "captures/http"),
        ResolveConfiguredPath(configuration, "OpenJibo:TurnTelemetry:DirectoryPath", "captures/turn"),
        ResolveConfiguredPath(configuration, "OpenJibo:Logging:DirectoryPath", "captures/logs")
    };

    foreach (var path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, true);
        }
        catch
        {
            // Startup cleanup is best-effort so a stale file never blocks the app.
        }
}

static string ResolveConfiguredPath(IConfiguration configuration, string key, string defaultPath)
{
    var configuredPath = configuration[key];
    if (string.IsNullOrWhiteSpace(configuredPath)) configuredPath = defaultPath;

    if (Path.IsPathRooted(configuredPath)) return Path.GetFullPath(configuredPath);

    var repoRoot = FindOpenJiboRepoRoot(Directory.GetCurrentDirectory()) ??
                   FindOpenJiboRepoRoot(AppContext.BaseDirectory) ??
                   Directory.GetCurrentDirectory();

    return Path.GetFullPath(configuredPath, repoRoot);
}

static string? FindOpenJiboRepoRoot(string? startPath)
{
    if (string.IsNullOrWhiteSpace(startPath)) return null;

    var directory = new DirectoryInfo(Path.GetFullPath(startPath));
    if (directory is { Exists: false, Parent: not null }) directory = directory.Parent;

    while (directory is not null)
    {
        if (File.Exists(Path.Combine(directory.FullName, "OpenJibo.slnx"))) return directory.FullName;

        directory = directory.Parent;
    }

    return null;
}

static LogEventLevel ParseLogEventLevel(string? value)
{
    return Enum.TryParse<LogEventLevel>(value, true, out var level)
        ? level
        : LogEventLevel.Debug;
}

public partial class Program;
