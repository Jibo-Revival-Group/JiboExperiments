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
    RobotNotificationRegistry robotNotificationRegistry,
    ICloudStateStore cloudStateStore,
    ILogger<WebSocketRequestCoordinator> logger)
{
    /// <summary>
    /// Test helper constructor. Production DI injects the full primary constructor.
    /// </summary>
    internal WebSocketRequestCoordinator(
        JiboWebSocketService webSocketService,
        HomeAssistantWebSocketHandler homeAssistantWebSocketHandler,
        IWebSocketTelemetrySink telemetrySink,
        ICloudStateStore cloudStateStore)
        : this(
            webSocketService,
            homeAssistantWebSocketHandler,
            telemetrySink,
            new RobotNotificationRegistry(),
            cloudStateStore,
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

        var connectionId = Guid.NewGuid().ToString("N");
        var openEnvelope = CreateEnvelope(context, kind, token, connectionId);
        var session = webSocketService.GetOrCreateSession(openEnvelope);
        await telemetrySink.RecordConnectionOpenedAsync(openEnvelope, session, context.RequestAborted);

        var registeredApiSocket = false;
        if (string.Equals(kind, "api-socket", StringComparison.OrdinalIgnoreCase))
        {
            var robotKeys = ResolveApiSocketRobotKeys(token, session);
            robotNotificationRegistry.Register(robotKeys, socket);
            registeredApiSocket = true;
            var drained = await robotNotificationRegistry.DrainPendingAsync(
                robotKeys,
                socket,
                context.RequestAborted);
            logger.LogInformation(
                "api-socket registered for LoopUpdated push token={Token} keyCount={KeyCount} pendingDrained={PendingDrained} keys={Keys}",
                token,
                robotKeys.Count,
                drained,
                string.Join(',', robotKeys.Take(8)));
        }

        var isPrematureClose = false;
        var loopTransId = session.TurnState.TransId;

        try
        {
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
                    connectionId,
                    received.MessageType == WebSocketMessageType.Text ? Encoding.UTF8.GetString(received.Buffer) : null,
                    received.MessageType == WebSocketMessageType.Binary ? received.Buffer : null);

                var replies = await webSocketService.HandleMessageAsync(envelope, context.RequestAborted);
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
        }
        finally
        {
            if (registeredApiSocket)
                robotNotificationRegistry.Remove(socket);
        }

        var closeEnvelope = CreateEnvelope(context, kind, token, connectionId);
        if (isPrematureClose)
            webSocketService.MarkPrematureSocketLoopEnded(session, loopTransId);

        await telemetrySink.RecordConnectionClosedAsync(closeEnvelope, session,
            $"socket-loop-ended{(isPrematureClose ? "-prematurely" : string.Empty)}", context.RequestAborted);
        logger.LogDebug("WebSocket request end kind={Kind} token={Token} prematureClose={PrematureClose}", kind, token,
            isPrematureClose);
    }

    private HashSet<string> ResolveApiSocketRobotKeys(string? token, CloudSession session)
    {
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(token))
            keys.Add(token.Trim());

        // Stock/OpenJibo tokens look like token-{friendlyOrDeviceId}-{suffix}. Portal keys on
        // friendlyId, so extract that segment so LoopUpdated push can find this socket.
        if (TryParseRobotIdFromToken(token, out var tokenRobotId))
        {
            keys.Add(tokenRobotId);
            var tokenDevice = cloudStateStore.FindDeviceByFriendlyId(tokenRobotId);
            if (tokenDevice is not null)
            {
                if (!string.IsNullOrWhiteSpace(tokenDevice.DeviceId))
                    keys.Add(tokenDevice.DeviceId.Trim());
                if (!string.IsNullOrWhiteSpace(tokenDevice.RobotId))
                    keys.Add(tokenDevice.RobotId.Trim());
                if (!string.IsNullOrWhiteSpace(tokenDevice.FriendlyName))
                    keys.Add(tokenDevice.FriendlyName.Trim());
            }
        }

        var deviceId = session.DeviceId;
        if (string.IsNullOrWhiteSpace(deviceId) && !string.IsNullOrWhiteSpace(token))
            deviceId = cloudStateStore.FindSessionByToken(token)?.DeviceId;

        if (!string.IsNullOrWhiteSpace(deviceId))
        {
            keys.Add(deviceId.Trim());
            var device = cloudStateStore.FindDeviceByFriendlyId(deviceId);
            if (device is not null)
            {
                if (!string.IsNullOrWhiteSpace(device.DeviceId))
                    keys.Add(device.DeviceId.Trim());
                if (!string.IsNullOrWhiteSpace(device.RobotId))
                    keys.Add(device.RobotId.Trim());
                if (!string.IsNullOrWhiteSpace(device.FriendlyName))
                    keys.Add(device.FriendlyName.Trim());
            }
        }

        return keys;
    }

    /// <summary>
    /// Parses <c>token-{robotId}-{suffix}</c> (stock and OpenJibo IssueRobotToken shapes).
    /// Hyphenated friendlyIds are preserved; the final numeric/guid segment is the suffix.
    /// </summary>
    internal static bool TryParseRobotIdFromToken(string? token, out string robotId)
    {
        robotId = string.Empty;
        if (string.IsNullOrWhiteSpace(token)) return false;

        var trimmed = token.Trim();
        if (!trimmed.StartsWith("token-", StringComparison.OrdinalIgnoreCase))
            return false;

        var body = trimmed["token-".Length..];
        if (string.IsNullOrWhiteSpace(body)) return false;

        var lastDash = body.LastIndexOf('-');
        if (lastDash <= 0)
        {
            robotId = body;
            return !string.IsNullOrWhiteSpace(robotId);
        }

        var suffix = body[(lastDash + 1)..];
        // OpenJibo uses Guid:N; stock often uses a unix-ms timestamp. Either way the robot id
        // is everything before the final segment.
        if (suffix.Length >= 6 && suffix.All(static c => char.IsAsciiHexDigit(c) || char.IsDigit(c)))
        {
            robotId = body[..lastDash];
            return !string.IsNullOrWhiteSpace(robotId);
        }

        robotId = body;
        return true;
    }

    private static WebSocketMessageEnvelope CreateEnvelope(
        HttpContext context,
        string kind,
        string? token,
        string connectionId,
        string? text = null,
        byte[]? binary = null)
    {
        return new WebSocketMessageEnvelope
        {
            ConnectionId = connectionId,
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
