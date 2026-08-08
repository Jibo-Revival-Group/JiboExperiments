using Jibo.Cloud.Api.Hosting;
using Jibo.Cloud.Application.Abstractions;
using Jibo.Cloud.Application.Services;
using Jibo.Cloud.Infrastructure.DependencyInjection;
using Jibo.Cloud.Infrastructure.Persistence;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.SystemConsole.Themes;
using System.Diagnostics;

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
        .Filter.ByExcluding(e =>
            PortalLogPollingDiagnosticsState.DisableServerLogsEndpointLogging &&
            e.RenderMessage().Contains("/api/portal/server/logs"))
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

PortalLogPollingDiagnosticsState.DisableServerLogsEndpointLogging =
    bool.TryParse(builder.Configuration["OpenJibo:Logging:DisableServerLogsEndpointLogging"], out var disableLogs) && disableLogs;

app.Logger.LogInformation("Starting Open Jibo Cloud Api version {Version}", OpenJiboCloudBuildInfo.Version);
app.Logger.LogInformation(
    "Protocol auth diagnostics effectiveEnabled={Enabled} containerAppRevision={Revision}",
    bool.TryParse(builder.Configuration["OpenJibo:ProtocolAuthDiagnostics:Enabled"], out var protocolAuthDiagnosticsEnabled) &&
    protocolAuthDiagnosticsEnabled,
    Environment.GetEnvironmentVariable("CONTAINER_APP_REVISION") ?? "local");

var seededRobots = RobotCredentialSeedApplier.Apply(
    app.Services.GetRequiredService<ICloudStateStore>(),
    app.Configuration,
    app.Logger);
if (seededRobots > 0)
    app.Logger.LogInformation("Applied {Count} robot credential seed entries for LAN API identity", seededRobots);

app.UseCors();
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseWebSockets();

app.Use(async (context, next) =>
{
    var skipLogging = PortalLogPollingDiagnosticsState.DisableServerLogsEndpointLogging &&
        context.Request.Path.StartsWithSegments("/api/portal/server/logs");

    if (skipLogging)
    {
        await next();
        return;
    }

    var started = Stopwatch.GetTimestamp();
    var logger = context.RequestServices.GetRequiredService<ILoggerFactory>()
        .CreateLogger("Jibo.Cloud.Api.RequestDiagnostics");
    var remoteIp = context.Connection.RemoteIpAddress?.ToString();
    var userAgent = context.Request.Headers.UserAgent.ToString();

    logger.LogInformation(
        "HTTP request started traceId={TraceId} method={Method} host={Host} path={Path} " +
        "query={Query} remoteIp={RemoteIp} userAgent={UserAgent} webSocket={WebSocket}",
        context.TraceIdentifier,
        context.Request.Method,
        context.Request.Host.Host,
        context.Request.Path,
        context.Request.QueryString.HasValue ? context.Request.QueryString.Value : null,
        remoteIp,
        userAgent,
        context.WebSockets.IsWebSocketRequest);

    try
    {
        await next();
    }
    catch (Exception exception)
    {
        logger.LogError(
            exception,
            "HTTP request failed traceId={TraceId} method={Method} host={Host} path={Path} remoteIp={RemoteIp} webSocket={WebSocket}",
            context.TraceIdentifier,
            context.Request.Method,
            context.Request.Host.Host,
            context.Request.Path,
            remoteIp,
            context.WebSockets.IsWebSocketRequest);
        throw;
    }
    finally
    {
        logger.LogInformation(
            "HTTP request completed traceId={TraceId} method={Method} host={Host} path={Path} " +
            "statusCode={StatusCode} elapsedMs={ElapsedMs} remoteIp={RemoteIp} webSocket={WebSocket}",
            context.TraceIdentifier,
            context.Request.Method,
            context.Request.Host.Host,
            context.Request.Path,
            context.Response.StatusCode,
            Stopwatch.GetElapsedTime(started).TotalMilliseconds,
            remoteIp,
            context.WebSockets.IsWebSocketRequest);
    }
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

app.MapGet("/openjibo-ca.crt", (IConfiguration configuration) =>
{
    // Every robot gets the exact same CA cert copied onto it (see
    // BEam/install-openjibo-ca.sh) — this is not per-robot material, just a
    // convenient anonymous download so the robot-side install script does not
    // need a separate file transfer step.
    var caCertPath = ResolveConfiguredPath(
        configuration,
        "OpenJibo:Tls:CaCertPath",
        "src/Jibo.Cloud/node/tls/openjibo-ca.crt");

    return File.Exists(caCertPath)
        ? Results.File(caCertPath, "application/x-x509-ca-cert", "openjibo-ca.crt")
        : Results.NotFound(new
        {
            error = "CA certificate not found on this server.",
            expectedPath = caCertPath,
            fix = "Run scripts/cloud/generate-openjibo-ca.sh on the server."
        });
});

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
    app.Logger.LogInformation(
        "Protocol request received requestId={RequestId} traceId={TraceId} method={Method} host={Host} path={Path} " +
        "servicePrefix={ServicePrefix} operation={Operation} deviceId={DeviceId} firmwareVersion={FirmwareVersion} " +
        "applicationVersion={ApplicationVersion}",
        envelope.RequestId,
        context.TraceIdentifier,
        envelope.Method,
        envelope.HostName,
        envelope.Path,
        envelope.ServicePrefix,
        envelope.Operation,
        envelope.DeviceId,
        envelope.FirmwareVersion,
        envelope.ApplicationVersion);
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

    var responseBytes = result.BodyBytes?.Length ?? result.BodyText?.Length ?? 0;
    app.Logger.LogInformation(
        "Protocol request completed requestId={RequestId} traceId={TraceId} operation={Operation} " +
        "deviceId={DeviceId} statusCode={StatusCode} contentType={ContentType} responseBytes={ResponseBytes}",
        envelope.RequestId,
        context.TraceIdentifier,
        envelope.Operation,
        envelope.DeviceId,
        result.StatusCode,
        result.ContentType,
        responseBytes);

    foreach (var header in result.Headers) context.Response.Headers[header.Key] = header.Value;

    if (result.BodyBytes is { Length: > 0 } bodyBytes)
        await context.Response.Body.WriteAsync(bodyBytes, cancellationToken);
    else if (!string.IsNullOrEmpty(result.BodyText))
        await context.Response.WriteAsync(result.BodyText, cancellationToken);
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
