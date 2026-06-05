using System.Net.WebSockets;
using System.Text;
using Jibo.Cloud.Application.Abstractions;
using Jibo.Cloud.Application.Services;
using Jibo.Cloud.Domain.Models;
using Microsoft.AspNetCore.Http;

namespace Jibo.Cloud.Api.Hosting;

internal sealed class WebSocketRequestCoordinator(
    JiboWebSocketService webSocketService,
    IWebSocketTelemetrySink telemetrySink)
{
    internal async Task HandleAsync(HttpContext context)
    {
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

        using var socket = await context.WebSockets.AcceptWebSocketAsync();

        var openEnvelope = CreateEnvelope(context, kind, token);
        var openSession = webSocketService.GetOrCreateSession(openEnvelope);
        await telemetrySink.RecordConnectionOpenedAsync(openEnvelope, openSession, context.RequestAborted);

        var isPrematureClose = false;

        while (socket.State == WebSocketState.Open)
        {
            ReceivedSocketMessage received;
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

                throw;
            }

            var envelope = CreateEnvelope(
                context,
                kind,
                token,
                received.MessageType == WebSocketMessageType.Text ? Encoding.UTF8.GetString(received.Buffer) : null,
                received.MessageType == WebSocketMessageType.Binary ? received.Buffer : null);

            var replies = await webSocketService.HandleMessageAsync(envelope, context.RequestAborted);
            var session = webSocketService.GetOrCreateSession(envelope);
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
        await telemetrySink.RecordConnectionClosedAsync(closeEnvelope, closeSession,
            $"socket-loop-ended{(isPrematureClose ? "-prematurely" : string.Empty)}", context.RequestAborted);
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
