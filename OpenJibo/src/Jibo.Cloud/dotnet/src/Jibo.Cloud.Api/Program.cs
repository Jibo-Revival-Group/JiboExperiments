using System.Net.WebSockets;
using System.Text;
using Jibo.Cloud.Api.Hosting;
using Jibo.Cloud.Application.Abstractions;
using Jibo.Cloud.Application.Services;
using Jibo.Cloud.Domain.Models;
using Jibo.Cloud.Infrastructure.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenJiboCloud(builder.Configuration);

var app = builder.Build();

app.Logger.LogInformation("Starting Open Jibo Cloud Api version {Version}", OpenJiboCloudBuildInfo.Version);

app.UseWebSockets();

app.Use(async (context, next) =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        await next();
        return;
    }

    var kind = SocketKindResolver.Resolve(context.Request.Host.Host, context.Request.Path);
    var token = TokenResolver.Resolve(context.Request);
    switch (kind)
    {
        case "unknown":
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        case "api-socket" when string.IsNullOrWhiteSpace(token):
        case "neo-hub-listen" or "neo-hub-proactive" when string.IsNullOrWhiteSpace(token):
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
    }

    var webSocketService = context.RequestServices.GetRequiredService<JiboWebSocketService>();
    var telemetrySink = context.RequestServices.GetRequiredService<IWebSocketTelemetrySink>();

    using var socket = await context.WebSockets.AcceptWebSocketAsync();

    var openEnvelope = new WebSocketMessageEnvelope
    {
        ConnectionId = Guid.NewGuid().ToString("N"),
        HostName = context.Request.Host.Host,
        Path = context.Request.Path.Value ?? "/",
        Kind = kind,
        Token = token
    };
    var openSession = ResolveSession(webSocketService, openEnvelope);
    await telemetrySink.RecordConnectionOpenedAsync(openEnvelope, openSession, context.RequestAborted);

    var isPrematureClose = false;

    while (socket.State == WebSocketState.Open)
    {
        ReceivedSocketMessage received = null!;
        try
        {
            received = await ReceiveAsync(socket, context.RequestAborted);
            if (received.MessageType == WebSocketMessageType.Close)
            {
                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", context.RequestAborted);
                break;
            }
        }
        catch (WebSocketException exception)
        {
            if (exception.WebSocketErrorCode == WebSocketError.ConnectionClosedPrematurely)
            {
                isPrematureClose = true;
                break;
            }
        }

        var envelope = new WebSocketMessageEnvelope
        {
            ConnectionId = Guid.NewGuid().ToString("N"),
            HostName = context.Request.Host.Host,
            Path = context.Request.Path.Value ?? "/",
            Kind = kind,
            Token = token,
            Text = received.MessageType == WebSocketMessageType.Text ? Encoding.UTF8.GetString(received.Buffer) : null,
            Binary = received.MessageType == WebSocketMessageType.Binary ? received.Buffer : null
        };

        var replies = await webSocketService.HandleMessageAsync(envelope, context.RequestAborted);
        var session = ResolveSession(webSocketService, envelope);
        await telemetrySink.RecordInboundAsync(envelope, session, SocketMessageTypeReader.Read(envelope.Text),
            context.RequestAborted);
        foreach (var reply in replies)
        {
            if (string.IsNullOrWhiteSpace(reply.Text)) continue;

            if (reply.DelayMs > 0) await Task.Delay(reply.DelayMs, context.RequestAborted);

            var payload = Encoding.UTF8.GetBytes(reply.Text);
            await socket.SendAsync(payload, WebSocketMessageType.Text, true, context.RequestAborted);
        }

        await telemetrySink.RecordOutboundAsync(envelope, session, replies, context.RequestAborted);
    }

    var closeEnvelope = new WebSocketMessageEnvelope
    {
        ConnectionId = Guid.NewGuid().ToString("N"),
        HostName = context.Request.Host.Host,
        Path = context.Request.Path.Value ?? "/",
        Kind = kind,
        Token = token
    };
    var closeSession = ResolveSession(webSocketService, closeEnvelope);
    await telemetrySink.RecordConnectionClosedAsync(closeEnvelope, closeSession,
        $"socket-loop-ended{(isPrematureClose ? "-prematurely" : string.Empty)}", context.RequestAborted);
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
return;

static async Task<ReceivedSocketMessage> ReceiveAsync(WebSocket socket, CancellationToken cancellationToken)
{
    var buffer = new byte[8192];
    using var ms = new MemoryStream();

    WebSocketReceiveResult result;
    do
    {
        result = await socket.ReceiveAsync(buffer, cancellationToken);
        ms.Write(buffer, 0, result.Count);
    } while (!result.EndOfMessage);

    return new ReceivedSocketMessage(result.MessageType, ms.ToArray());
}

static CloudSession ResolveSession(JiboWebSocketService webSocketService, WebSocketMessageEnvelope envelope)
{
    return webSocketService.GetOrCreateSession(envelope);
}

internal sealed record ReceivedSocketMessage(WebSocketMessageType MessageType, byte[] Buffer);

public partial class Program;
