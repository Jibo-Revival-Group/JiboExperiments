using System.Net.WebSockets;
using System.Text;
using Jibo.Cloud.Application.Abstractions;
using Jibo.Cloud.Application.Services;
using Jibo.Cloud.Domain.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jibo.Cloud.Api.Hosting;

internal sealed class WebSocketRequestCoordinator(
    JiboWebSocketService webSocketService,
    HomeAssistantWebSocketHandler homeAssistantWebSocketHandler,
    IWebSocketTelemetrySink telemetrySink,
    ILogger<WebSocketRequestCoordinator> logger)
{
    internal WebSocketRequestCoordinator(
        JiboWebSocketService webSocketService,
        HomeAssistantWebSocketHandler homeAssistantWebSocketHandler,
        IWebSocketTelemetrySink telemetrySink)
        : this(webSocketService, homeAssistantWebSocketHandler, telemetrySink,
            NullLogger<WebSocketRequestCoordinator>.Instance)
    {
    }

    internal async Task HandleAsync(HttpContext context)
    {
        var kind = SocketKindResolver.Resolve(context.Request.Host.Host, context.Request.Path);
        if (string.Equals(kind, "home-assistant", StringComparison.OrdinalIgnoreCase))
        {
            await homeAssistantWebSocketHandler.HandleAsync(context);
            return;
        }

        var token = TokenResolver.Resolve(context.Request);
        logger.LogDebug("WebSocket request start kind={Kind} token={Token} host={Host} path={Path}", kind, token,
            context.Request.Host.Host, context.Request.Path);
        switch (kind)
        {
            case "unknown":
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                logger.LogDebug("WebSocket request rejected as unknown kind host={Host} path={Path}",
                    context.Request.Host.Host, context.Request.Path);
                return;
            case "api-socket" when string.IsNullOrWhiteSpace(token):
            case "neo-hub-listen" or "neo-hub-proactive" when string.IsNullOrWhiteSpace(token):
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                logger.LogDebug("WebSocket request rejected due to missing token kind={Kind} host={Host} path={Path}",
                    kind, context.Request.Host.Host, context.Request.Path);
                return;
        }

        using var socket = await context.WebSockets.AcceptWebSocketAsync();
        logger.LogDebug("WebSocket accepted kind={Kind} token={Token}", kind, token);

        var openEnvelope = CreateEnvelope(context, kind, token);
        var openSession = webSocketService.GetOrCreateSession(openEnvelope);
        await telemetrySink.RecordConnectionOpenedAsync(openEnvelope, openSession, context.RequestAborted);

        var isPrematureClose = false;
        var loopTransId = openSession.TurnState.TransId;

        while (socket.State == WebSocketState.Open)
        {
            ReceivedSocketMessage received;
            try
            {
                received = await ReceiveAsync(socket, context.RequestAborted);
                logger.LogDebug(
                    "WebSocket frame received kind={Kind} token={Token} messageType={MessageType} bytes={Bytes}",
                    kind,
                    token,
                    received.MessageType,
                    received.Buffer.Length);
                if (received.MessageType == WebSocketMessageType.Close)
                {
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", context.RequestAborted);
                    logger.LogDebug("WebSocket close frame received kind={Kind} token={Token}", kind, token);
                    break;
                }
            }
            catch (WebSocketException exception)
            {
                if (exception.WebSocketErrorCode != WebSocketError.ConnectionClosedPrematurely) throw;
                isPrematureClose = true;
                logger.LogDebug(exception,
                    "WebSocket connection closed prematurely kind={Kind} token={Token}", kind, token);
                break;
            }

            var envelope = CreateEnvelope(
                context,
                kind,
                token,
                received.MessageType == WebSocketMessageType.Text ? Encoding.UTF8.GetString(received.Buffer) : null,
                received.MessageType == WebSocketMessageType.Binary ? received.Buffer : null);

            var replies = await webSocketService.HandleMessageAsync(envelope, context.RequestAborted);
            var session = webSocketService.GetOrCreateSession(envelope);
            if (!string.IsNullOrWhiteSpace(session.TurnState.TransId))
                loopTransId = session.TurnState.TransId;
            logger.LogDebug(
                "WebSocket reply batch ready kind={Kind} token={Token} messageType={MessageType} replyCount={ReplyCount}",
                kind,
                token,
                SocketMessageTypeReader.Read(envelope.Text),
                replies.Count);
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

        var closeEnvelope = CreateEnvelope(context, kind, token);
        var closeSession = webSocketService.GetOrCreateSession(closeEnvelope);
        if (isPrematureClose)
            webSocketService.MarkPrematureSocketLoopEnded(closeSession, loopTransId);

        await telemetrySink.RecordConnectionClosedAsync(closeEnvelope, closeSession,
            $"socket-loop-ended{(isPrematureClose ? "-prematurely" : string.Empty)}", context.RequestAborted);
        logger.LogDebug("WebSocket request end kind={Kind} token={Token} prematureClose={PrematureClose}", kind, token,
            isPrematureClose);
    }

    private static WebSocketMessageEnvelope CreateEnvelope(
        HttpContext context,
        string kind,
        string? token,
        string? text = null,
        byte[]? binary = null)
    {
        return new WebSocketMessageEnvelope
        {
            ConnectionId = Guid.NewGuid().ToString("N"),
            HostName = context.Request.Host.Host,
            Path = context.Request.Path.Value ?? "/",
            Kind = kind,
            Token = token,
            Text = text,
            Binary = binary
        };
    }

    private static async Task<ReceivedSocketMessage> ReceiveAsync(WebSocket socket, CancellationToken cancellationToken)
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

    private sealed record ReceivedSocketMessage(WebSocketMessageType MessageType, byte[] Buffer);
}