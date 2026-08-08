using Jibo.Cloud.Api.Hosting;
using Jibo.Cloud.Application.Abstractions;
using Jibo.Cloud.Application.Services;
using Jibo.Cloud.Infrastructure.DependencyInjection;
using Jibo.Cloud.Infrastructure.Persistence;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.SystemConsole.Themes;
using System.Diagnostics;
using Microsoft.AspNetCore.Server.Kestrel.Transport.Sockets;
using System.Net;
using System.Net.Sockets;

OpenJiboEnvLoader.Load();

var builder = WebApplication.CreateBuilder(args);

if (ShouldResetDiagnosticsOnStartup(builder.Configuration))
    ResetDiagnosticsDirectories(builder.Configuration);

ConfigureDefaultKestrelEndpoints(builder);
ConfigureReusableListenSockets(builder);

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

app.MapGet("/openjibo-ca.hash", (IConfiguration configuration) =>
{
    // Precomputed `openssl x509 -hash` output for the same CA above, so a
    // robot with no openssl binary can still create the OpenSSL CApath
    // symlink the native NotificationSubsystem's TLS verification uses.
    var caHashPath = ResolveConfiguredPath(
        configuration,
        "OpenJibo:Tls:CaHashPath",
        "src/Jibo.Cloud/node/tls/openjibo-ca.hash");

    return File.Exists(caHashPath)
        ? Results.Text(File.ReadAllText(caHashPath).Trim(), "text/plain")
        : Results.NotFound(new
        {
            error = "CA hash not found on this server.",
            expectedPath = caHashPath,
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

static void ConfigureDefaultKestrelEndpoints(WebApplicationBuilder builder)
{
    // Binds the same three ports every robot/Portal client expects (443 for the
    // native NotificationSubsystem's WSS hub, 24605/8765 for the credentials/JSC
    // HTTP API) directly from configuration, so this works identically whether
    // launched via `dotnet run`, a published binary under systemd, or Docker —
    // no ASPNETCORE_URLS or launch-specific wrapper script required.
    //
    // Any of these keys already present in config (appsettings.json,
    // ASPNETCORE_URLS, env vars, --Kestrel:... command-line args, ...) wins over
    // its specific default below — this only fills in whatever the caller
    // hasn't already configured themselves, key by key, rather than bailing out
    // entirely just because e.g. only ASPNETCORE_URLS was set.
    var defaults = new Dictionary<string, string?>();

    void DefaultIfUnset(string key, string value)
    {
        if (string.IsNullOrEmpty(builder.Configuration[key])) defaults[key] = value;
    }

    DefaultIfUnset("Kestrel:Endpoints:Http24605:Url", "http://0.0.0.0:24605");
    DefaultIfUnset("Kestrel:Endpoints:Http8765:Url", "http://0.0.0.0:8765");

    var certPath = ResolveConfiguredPath(builder.Configuration, "OpenJibo:Tls:CertPath", "src/Jibo.Cloud/node/cert.pem");
    var keyPath = ResolveConfiguredPath(builder.Configuration, "OpenJibo:Tls:KeyPath", "src/Jibo.Cloud/node/key.pem");
    var hasCert = File.Exists(certPath) && File.Exists(keyPath);

    // Only advertise :443 (and only require a cert) once generate-openjibo-ca.sh
    // has actually produced one — otherwise a fresh checkout with no cert yet
    // would fail to start instead of just running the two HTTP endpoints.
    //
    // :443 must ALSO be probed before Kestrel is ever told about it. If some
    // other process already owns :443 (EADDRINUSE) or this process lacks
    // CAP_NET_BIND_SERVICE (EACCES), Kestrel treats every configured endpoint
    // as mandatory — one failing to bind takes the ENTIRE host down, including
    // the 24605/8765 endpoints that have nothing to do with :443. So :443 is
    // opt-in only when a quick bind-and-release test proves it's actually
    // available; otherwise this logs a warning and just runs the two HTTP
    // endpoints instead of crashing the whole process.
    if (hasCert)
    {
        if (CanBindPort(443))
        {
            DefaultIfUnset("Kestrel:Endpoints:Https:Url", "https://0.0.0.0:443");
            DefaultIfUnset("Kestrel:Certificates:Default:Path", certPath);
            DefaultIfUnset("Kestrel:Certificates:Default:KeyPath", keyPath);
        }
        else
        {
            Console.Error.WriteLine(
                "openjibo: WARNING — cert.pem/key.pem found but :443 is not bindable " +
                "(already in use by another process, or this process lacks permission " +
                "to bind a privileged port — grant CAP_NET_BIND_SERVICE, e.g. via the " +
                "systemd unit's AmbientCapabilities=CAP_NET_BIND_SERVICE, or run as a " +
                "user that already has it). Skipping the :443 endpoint so the rest of " +
                "the server (24605/8765) still starts — LoopUpdated push over the " +
                "native NotificationSubsystem will NOT work until :443 is free.");
        }
    }

    builder.Configuration.AddInMemoryCollection(defaults);
}

static bool CanBindPort(int port)
{
    try
    {
        using var probe = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        // SO_REUSEADDR so this probe never trips over a socket Kestrel itself
        // left in TIME_WAIT from a previous run of this same process. Kestrel's
        // OWN listen sockets are made to set this too (ConfigureReusableListenSockets
        // below), so this probe's result now actually matches what Kestrel will do.
        probe.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        probe.Bind(new IPEndPoint(IPAddress.Any, port));
        return true;
    }
    catch (SocketException)
    {
        return false;
    }
}

static void ConfigureReusableListenSockets(WebApplicationBuilder builder)
{
    // Kestrel's default listen socket does NOT set SO_REUSEADDR. After a crash
    // (like tonight's), the old socket sits in TIME_WAIT for ~60s, and every
    // restart attempt within that window fails to re-bind the SAME port with
    // "Address already in use" — even with nothing else running — turning one
    // crash into a crash-loop. SO_REUSEADDR lets a fresh bind reuse a
    // TIME_WAIT'd port; it does NOT let two processes both actively listen on
    // the same port at once, so this is safe.
    builder.WebHost.UseSockets(socketOptions =>
    {
        socketOptions.CreateBoundListenSocket = endpoint =>
        {
            if (endpoint is not IPEndPoint ipEndPoint)
                return SocketTransportOptions.CreateDefaultBoundListenSocket(endpoint);

            var socket = new Socket(ipEndPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            socket.Bind(ipEndPoint);
            return socket;
        };
    });
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
