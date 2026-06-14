using Jibo.Cloud.Api.Hosting;
using Jibo.Cloud.Application.Abstractions;
using Jibo.Cloud.Application.Services;
using Jibo.Cloud.Infrastructure.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenJiboCloud(builder.Configuration);
builder.Services.AddSingleton<WebSocketRequestCoordinator>();
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