using Jibo.Cloud.Api.Hosting;
using Jibo.Cloud.Application.Abstractions;
using Jibo.Cloud.Application.Services;
using Jibo.Cloud.Infrastructure.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenJiboCloud(builder.Configuration);
builder.Services.AddSingleton<WebSocketRequestCoordinator>();
builder.Services.AddSingleton<JiboCloudProtocolService>();
builder.Services.AddSingleton<OobePortalService>(sp =>
    new OobePortalService(
        sp.GetRequiredService<ICloudStateStore>(),
        sp.GetRequiredService<JiboCloudProtocolService>()
    ));
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
    policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();

app.Logger.LogInformation("Starting Open Jibo Cloud Api version {Version}", OpenJiboCloudBuildInfo.Version);

app.UseCors();
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

// OOBE Portal REST API endpoints
app.MapPost("/api/signup", async (HttpContext context, OobePortalService portalService) =>
{
    using var reader = new StreamReader(context.Request.Body);
    var body = await reader.ReadToEndAsync();
    var data = JsonSerializer.Deserialize<JsonElement>(body);

    var email = data.ValueKind != JsonValueKind.Undefined && data.TryGetProperty("email", out var emailElement) ? emailElement.GetString() ?? string.Empty : string.Empty;
    var password = data.ValueKind != JsonValueKind.Undefined && data.TryGetProperty("password", out var passwordElement) ? passwordElement.GetString() ?? string.Empty : string.Empty;
    var firstName = data.ValueKind != JsonValueKind.Undefined && data.TryGetProperty("firstName", out var fn) ? fn.GetString() : null;
    var lastName = data.ValueKind != JsonValueKind.Undefined && data.TryGetProperty("lastName", out var ln) ? ln.GetString() : null;

    var result = await portalService.SignupAsync(email, password, firstName, lastName);

    context.Response.StatusCode = result.Success ? 200 : 400;
    return Results.Json(result.Data ?? new { error = result.Error });
});

app.MapPost("/api/login", async (HttpContext context, OobePortalService portalService) =>
{
    using var reader = new StreamReader(context.Request.Body);
    var body = await reader.ReadToEndAsync();
    var data = JsonSerializer.Deserialize<JsonElement>(body);

    var email = data.ValueKind != JsonValueKind.Undefined && data.TryGetProperty("email", out var emailElement) ? emailElement.GetString() ?? string.Empty : string.Empty;
    var password = data.ValueKind != JsonValueKind.Undefined && data.TryGetProperty("password", out var passwordElement) ? passwordElement.GetString() ?? string.Empty : string.Empty;

    var result = await portalService.LoginAsync(email, password);

    context.Response.StatusCode = result.Success ? 200 : 401;
    return Results.Json(result.Data ?? new { error = result.Error });
});

app.MapGet("/api/robots", async (HttpContext context, OobePortalService portalService) =>
{
    var authHeader = context.Request.Headers.Authorization.FirstOrDefault();
    if (string.IsNullOrWhiteSpace(authHeader) || !authHeader.StartsWith("Bearer "))
    {
        context.Response.StatusCode = 401;
        return Results.Json(new { error = "Missing authorization header" });
    }

    var token = authHeader.Substring("Bearer ".Length);
    if (!portalService.ValidateSessionToken(token, out var userId))
    {
        context.Response.StatusCode = 401;
        return Results.Json(new { error = "Invalid token" });
    }

    var result = await portalService.GetRobotsAsync(userId!);
    return Results.Json(result.Data);
});

app.MapPost("/api/robots/setup", async (HttpContext context, OobePortalService portalService) =>
{
    var authHeader = context.Request.Headers.Authorization.FirstOrDefault();
    if (string.IsNullOrWhiteSpace(authHeader) || !authHeader.StartsWith("Bearer "))
    {
        context.Response.StatusCode = 401;
        return Results.Json(new { error = "Missing authorization header" });
    }

    var token = authHeader.Substring("Bearer ".Length);
    if (!portalService.ValidateSessionToken(token, out var userId))
    {
        context.Response.StatusCode = 401;
        return Results.Json(new { error = "Invalid token" });
    }

    using var reader = new StreamReader(context.Request.Body);
    var body = await reader.ReadToEndAsync();
    var data = JsonSerializer.Deserialize<JsonElement>(body);

    var ssid = data.ValueKind != JsonValueKind.Undefined && data.TryGetProperty("ssid", out var ssidElement) ? ssidElement.GetString() ?? string.Empty : string.Empty;
    var password = data.ValueKind != JsonValueKind.Undefined && data.TryGetProperty("password", out var passwordElement) ? passwordElement.GetString() ?? string.Empty : string.Empty;
    var staticIp = data.ValueKind != JsonValueKind.Undefined && data.TryGetProperty("staticIP", out var ip) ? ip.GetString() : null;
    var netmask = data.ValueKind != JsonValueKind.Undefined && data.TryGetProperty("netmask", out var nm) ? nm.GetString() : null;
    var gateway = data.ValueKind != JsonValueKind.Undefined && data.TryGetProperty("gateway", out var gw) ? gw.GetString() : null;
    var dns1 = data.ValueKind != JsonValueKind.Undefined && data.TryGetProperty("dns1", out var d1) ? d1.GetString() : null;
    var dns2 = data.ValueKind != JsonValueKind.Undefined && data.TryGetProperty("dns2", out var d2) ? d2.GetString() : null;

    var result = await portalService.PrepareRobotSetupAsync(userId!, ssid, password, staticIp, netmask, gateway, dns1, dns2);
    return Results.Json(result.Data);
});

app.MapGet("/api/robots/setup/{token}/status", async (string token, OobePortalService portalService) =>
{
    var result = await portalService.GetRobotSetupStatusAsync(token);
    return Results.Json(result.Data);
});

app.MapMethods("/{**path}", ["GET", "POST", "PUT"], async (HttpContext context, JiboCloudProtocolService service,
    IProtocolTelemetrySink telemetrySink, CancellationToken cancellationToken) =>
{
    var envelope = await ApiRequestEnvelopeFactory.CreateAsync(context, cancellationToken);
    var result = await service.DispatchAsync(envelope, cancellationToken);
    await telemetrySink.RecordAsync(envelope, result, cancellationToken);

    context.Response.StatusCode = result.StatusCode;
    context.Response.ContentType = result.ContentType;

    foreach (var header in result.Headers) context.Response.Headers[header.Key] = header.Value;

    if (!string.IsNullOrEmpty(result.BodyText)) await context.Response.WriteAsync(result.BodyText, cancellationToken);
});

app.Run();

public partial class Program;