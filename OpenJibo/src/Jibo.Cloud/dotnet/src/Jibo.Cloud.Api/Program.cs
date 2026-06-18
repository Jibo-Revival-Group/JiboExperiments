using Jibo.Cloud.Api.Hosting;
using Jibo.Cloud.Application.Abstractions;
using Jibo.Cloud.Application.Services;
using Jibo.Cloud.Infrastructure.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.SystemConsole.Themes;

var builder = WebApplication.CreateBuilder(args);

if (ShouldResetDiagnosticsOnStartup(builder.Configuration))
    ResetDiagnosticsDirectories(builder.Configuration);

builder.Host.UseSerilog((context, services, loggerConfiguration) =>
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
            outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}");
});

builder.Services.AddOpenJiboCloud(builder.Configuration);
builder.Services.AddSingleton<WebSocketRequestCoordinator>();
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
    policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();

app.Logger.LogInformation("Starting Open Jibo Cloud Api version {Version}", OpenJiboCloudBuildInfo.Version);

app.UseCors();

var publicSitePath = ResolvePublicSitePath(builder.Configuration);
if (Directory.Exists(publicSitePath))
{
    app.Logger.LogInformation("Serving public site from {PublicSitePath}", publicSitePath);
    app.UseDefaultFiles(new DefaultFilesOptions
    {
        FileProvider = new PhysicalFileProvider(publicSitePath),
        RequestPath = string.Empty
    });
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new PhysicalFileProvider(publicSitePath),
        RequestPath = string.Empty
    });
}
else
{
    app.Logger.LogWarning(
        "Public site directory not found at {PublicSitePath}. Pages like /launch-rules.html will not be served.",
        publicSitePath);
}

app.UseWebSockets();

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

RobotLaunchRuleEndpoints.Map(app);

app.MapMethods("/{**path}", ["GET", "POST", "PUT"], async (HttpContext context, JiboCloudProtocolService service,
    IProtocolTelemetrySink telemetrySink, CancellationToken cancellationToken) =>
{
    if (ShouldBypassProtocolDispatch(context.Request.Path))
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return;
    }

    var envelope = await ApiRequestEnvelopeFactory.CreateAsync(context, cancellationToken);
    var result = await service.DispatchAsync(envelope, cancellationToken);
    await telemetrySink.RecordAsync(envelope, result, cancellationToken);

    context.Response.StatusCode = result.StatusCode;
    context.Response.ContentType = result.ContentType;

    foreach (var header in result.Headers) context.Response.Headers[header.Key] = header.Value;

    if (!string.IsNullOrEmpty(result.BodyText)) await context.Response.WriteAsync(result.BodyText, cancellationToken);
});

app.Run();

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
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, true);
        }
        catch
        {
            // Startup cleanup is best-effort so a stale file never blocks the app.
        }
    }
}

static string ResolvePublicSitePath(IConfiguration configuration)
{
    var candidates = new List<string>();
    var configuredPath = configuration["OpenJibo:PublicSite:DirectoryPath"];
    if (!string.IsNullOrWhiteSpace(configuredPath))
    {
        candidates.Add(Path.IsPathRooted(configuredPath)
            ? Path.GetFullPath(configuredPath)
            : Path.GetFullPath(configuredPath, ResolveRepoRoot()));
    }

    candidates.Add(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "wwwroot")));

    var repoRoot = FindOpenJiboRepoRoot(Directory.GetCurrentDirectory()) ??
                   FindOpenJiboRepoRoot(AppContext.BaseDirectory);
    if (repoRoot is not null)
        candidates.Add(Path.GetFullPath(Path.Combine(repoRoot, "src", "OpenJibo.Site")));

    foreach (var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
    {
        if (Directory.Exists(candidate))
            return candidate;
    }

    return candidates[0];
}

static string ResolveRepoRoot()
{
    return FindOpenJiboRepoRoot(Directory.GetCurrentDirectory()) ??
           FindOpenJiboRepoRoot(AppContext.BaseDirectory) ??
           Directory.GetCurrentDirectory();
}

static bool ShouldBypassProtocolDispatch(PathString path)
{
    var value = path.Value ?? string.Empty;
    if (value.StartsWith("/api/", StringComparison.OrdinalIgnoreCase)) return true;
    if (value.Equals("/health", StringComparison.OrdinalIgnoreCase)) return true;

    if (value.Equals("/", StringComparison.OrdinalIgnoreCase)) return true;

    return Path.GetExtension(value) switch
    {
        ".html" or ".htm" or ".css" or ".js" or ".ico" or ".svg" or ".png" or ".jpg" or ".jpeg" or ".webp" or
            ".woff" or ".woff2" or ".map" => true,
        _ => false
    };
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
