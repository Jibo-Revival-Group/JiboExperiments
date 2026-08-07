using System.Net.WebSockets;
using System.Security.Cryptography;
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
    RobotPresenceRegistry robotPresenceRegistry,
    ICloudStateStore cloudStateStore,
    ILogger<WebSocketRequestCoordinator> logger)
{
    private static readonly TimeSpan TurnWatchdogInterval = TimeSpan.FromMilliseconds(250);

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
            new RobotPresenceRegistry(),
            cloudStateStore,
            NullLogger<WebSocketRequestCoordinator>.Instance)
    {
    }

    internal WebSocketRequestCoordinator(
        JiboWebSocketService webSocketService,
        HomeAssistantWebSocketHandler homeAssistantWebSocketHandler,
        IWebSocketTelemetrySink telemetrySink,
        RobotNotificationRegistry robotNotificationRegistry,
        ICloudStateStore cloudStateStore,
        ILogger<WebSocketRequestCoordinator> logger)
        : this(
            webSocketService,
            homeAssistantWebSocketHandler,
            telemetrySink,
            robotNotificationRegistry,
            new RobotPresenceRegistry(),
            cloudStateStore,
            logger)
    {
    }

    internal async Task HandleAsync(HttpContext context)
    {
        var kind = SocketKindResolver.Resolve(context.Request.Host.Host, context.Request.Path);
        if (string.Equals(kind, "home-assistant", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogInformation(
                "WebSocket request routed to Home Assistant handler traceId={TraceId} host={Host} path={Path} remoteIp={RemoteIp}",
                context.TraceIdentifier,
                context.Request.Host.Host,
                context.Request.Path,
                context.Connection.RemoteIpAddress?.ToString());
            await homeAssistantWebSocketHandler.HandleAsync(context);
            return;
        }

        var token = TokenResolver.Resolve(context.Request);
        var tokenFingerprint = Fingerprint(token);
        logger.LogInformation(
            "WebSocket request received traceId={TraceId} kind={Kind} tokenFingerprint={TokenFingerprint} " +
            "host={Host} path={Path} remoteIp={RemoteIp} userAgent={UserAgent}",
            context.TraceIdentifier,
            kind,
            tokenFingerprint,
            context.Request.Host.Host,
            context.Request.Path,
            context.Connection.RemoteIpAddress?.ToString(),
            context.Request.Headers.UserAgent.ToString());
        switch (kind)
        {
            case "unknown":
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                logger.LogWarning("WebSocket request rejected as unknown kind={Kind} host={Host} path={Path}",
                    kind, context.Request.Host.Host, context.Request.Path);
                return;
            case "api-socket" when string.IsNullOrWhiteSpace(token):
            case "neo-hub-listen" or "neo-hub-proactive" when string.IsNullOrWhiteSpace(token):
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                logger.LogWarning(
                    "WebSocket request rejected due to missing token kind={Kind} host={Host} path={Path} remoteIp={RemoteIp}",
                    kind, context.Request.Host.Host, context.Request.Path,
                    context.Connection.RemoteIpAddress?.ToString());
                return;
        }

        // Stock OS 1.9's Neo Hub client closes when ASP.NET sends a WebSocket
        // control-frame keepalive: first at the default two-minute interval,
        // then at 30 seconds when that interval was trialled. Limit the
        // compatibility behavior to the legacy Hub routes; other sockets keep
        // the framework default.
        var acceptContext = kind is "neo-hub-listen" or "neo-hub-proactive"
            ? new WebSocketAcceptContext { KeepAliveInterval = Timeout.InfiniteTimeSpan }
            : null;
        using var socket = acceptContext is null
            ? await context.WebSockets.AcceptWebSocketAsync()
            : await context.WebSockets.AcceptWebSocketAsync(acceptContext);
        var connectionId = Guid.NewGuid().ToString("N");
        var openEnvelope = CreateEnvelope(context, kind, token, connectionId);
        var session = webSocketService.GetOrCreateSession(openEnvelope);
        var initialRobotKeys = ResolveRobotKeys(token, session);
        logger.LogInformation(
            "WebSocket connection accepted connectionId={ConnectionId} traceId={TraceId} kind={Kind} " +
            "tokenFingerprint={TokenFingerprint} sessionId={SessionId} deviceId={DeviceId} " +
            "robotId={RobotId} friendlyName={FriendlyName} robotKeyCount={RobotKeyCount}",
            connectionId,
            context.TraceIdentifier,
            kind,
            tokenFingerprint,
            session.SessionId,
            session.DeviceId,
            ReadSessionMetadata(session, "registeredRobotId") ?? ReadSessionMetadata(session, "robotId"),
            ReadSessionMetadata(session, "robotFriendlyId") ?? ReadSessionMetadata(session, "friendlyId"),
            initialRobotKeys.Count);
        await telemetrySink.RecordConnectionOpenedAsync(openEnvelope, session, context.RequestAborted);

        var registeredApiSocket = false;
        var presenceConnectionId = robotPresenceRegistry.Register(kind, socket, initialRobotKeys);
        if (string.Equals(kind, "api-socket", StringComparison.OrdinalIgnoreCase))
        {
            var robotKeys = ResolveRobotKeys(token, session);
            robotNotificationRegistry.Register(robotKeys, socket);
            registeredApiSocket = true;
            var drained = await robotNotificationRegistry.DrainPendingAsync(
                robotKeys,
                socket,
                context.RequestAborted);
            logger.LogInformation(
                "api-socket registered for LoopUpdated push keyCount={KeyCount} pendingDrained={PendingDrained}",
                robotKeys.Count,
                drained);
        }

        var isPrematureClose = false;
        var loopTransId = session.TurnState.TransId;

        try
        {
            Task<ReceivedSocketMessage>? pendingReceive = null;
            var watchdogDelay = Task.Delay(TurnWatchdogInterval, context.RequestAborted);
            while (socket.State == WebSocketState.Open)
            {
                ReceivedSocketMessage received;
                try
                {
                    pendingReceive ??= ReceiveAsync(socket, context.RequestAborted);
                    var completedTask = await Task.WhenAny(pendingReceive, watchdogDelay);
                    if (completedTask != pendingReceive)
                    {
                        watchdogDelay = Task.Delay(TurnWatchdogInterval, context.RequestAborted);
                        var idleEnvelope = CreateEnvelope(context, kind, token, connectionId);
                        var idleReplies = await webSocketService.HandleIdleAsync(
                            session,
                            idleEnvelope,
                            context.RequestAborted);
                        if (idleReplies.Count > 0)
                        {
                            logger.LogInformation(
                                "WebSocket turn watchdog reply batch ready connectionId={ConnectionId} kind={Kind} replyCount={ReplyCount}",
                                connectionId,
                                kind,
                                idleReplies.Count);
                            await SendRepliesAsync(socket, idleReplies, context.RequestAborted);
                            await telemetrySink.RecordOutboundAsync(
                                idleEnvelope,
                                session,
                                idleReplies,
                                context.RequestAborted);
                        }
                        continue;
                    }

                    received = await pendingReceive;
                    pendingReceive = null;
                    logger.LogDebug(
                        "WebSocket frame received connectionId={ConnectionId} kind={Kind} messageType={MessageType} bytes={Bytes}",
                        connectionId,
                        kind,
                        received.MessageType,
                        received.Buffer.Length);
                    if (received.MessageType == WebSocketMessageType.Close)
                    {
                        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", context.RequestAborted);
                        logger.LogInformation("WebSocket close frame received connectionId={ConnectionId} kind={Kind}",
                            connectionId, kind);
                        break;
                    }
                }
                catch (WebSocketException exception)
                {
                    if (exception.WebSocketErrorCode != WebSocketError.ConnectionClosedPrematurely) throw;
                    isPrematureClose = true;
                    logger.LogDebug(exception,
                        "WebSocket connection closed prematurely connectionId={ConnectionId} kind={Kind}",
                        connectionId, kind);
                    break;
                }

                var envelope = CreateEnvelope(
                    context,
                    kind,
                    token,
                    connectionId,
                    received.MessageType == WebSocketMessageType.Text ? Encoding.UTF8.GetString(received.Buffer) : null,
                    received.MessageType == WebSocketMessageType.Binary ? received.Buffer : null);

                IReadOnlyList<WebSocketReply> replies;
                using (AmbientTurnProgressPublisher.Begin(
                           (reply, cancellationToken) => SendRepliesAsync(socket, [reply], cancellationToken)))
                {
                    replies = await webSocketService.HandleMessageAsync(envelope, context.RequestAborted);
                }

                var refreshedKeys = ResolveRobotKeys(token, session);
                robotPresenceRegistry.UpdateRobotKeys(presenceConnectionId, refreshedKeys);
                if (registeredApiSocket)
                {
                    var drainedAfterKeyRefresh = await robotNotificationRegistry.UpdateKeysAsync(
                        socket,
                        refreshedKeys,
                        context.RequestAborted);
                    if (drainedAfterKeyRefresh > 0)
                    {
                        logger.LogInformation(
                            "api-socket keys refreshed; drained pending LoopUpdated count={PendingDrained} keyCount={KeyCount}",
                            drainedAfterKeyRefresh,
                            refreshedKeys.Count);
                    }
                }

                if (!string.IsNullOrWhiteSpace(session.TurnState.TransId))
                    loopTransId = session.TurnState.TransId;
                logger.LogDebug(
                    "WebSocket reply batch ready connectionId={ConnectionId} kind={Kind} sessionId={SessionId} " +
                    "deviceId={DeviceId} messageType={MessageType} replyCount={ReplyCount}",
                    connectionId,
                    kind,
                    session.SessionId,
                    session.DeviceId,
                    SocketMessageTypeReader.Read(envelope.Text),
                    replies.Count);
                await telemetrySink.RecordInboundAsync(envelope, session, SocketMessageTypeReader.Read(envelope.Text),
                    context.RequestAborted);
                await SendRepliesAsync(socket, replies, context.RequestAborted);

                await telemetrySink.RecordOutboundAsync(envelope, session, replies, context.RequestAborted);
            }
        }
        finally
        {
            if (registeredApiSocket)
                robotNotificationRegistry.Remove(socket);
            robotPresenceRegistry.Remove(presenceConnectionId);
        }

        var closeEnvelope = CreateEnvelope(context, kind, token, connectionId);
        if (isPrematureClose)
            webSocketService.MarkPrematureSocketLoopEnded(session, loopTransId);

        await telemetrySink.RecordConnectionClosedAsync(closeEnvelope, session,
            $"socket-loop-ended{(isPrematureClose ? "-prematurely" : string.Empty)}", context.RequestAborted);
        logger.LogInformation(
            "WebSocket connection closed connectionId={ConnectionId} kind={Kind} sessionId={SessionId} " +
            "deviceId={DeviceId} tokenFingerprint={TokenFingerprint} prematureClose={PrematureClose}",
            connectionId,
            kind,
            session.SessionId,
            session.DeviceId,
            tokenFingerprint,
            isPrematureClose);
    }

    private HashSet<string> ResolveRobotKeys(string? token, CloudSession session)
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

        foreach (var identityValue in GetSessionIdentityValues(session))
        {
            keys.Add(identityValue);
            var registeredDevice = cloudStateStore.FindDeviceByFriendlyId(identityValue);
            if (registeredDevice is null)
                continue;

            if (!string.IsNullOrWhiteSpace(registeredDevice.DeviceId))
                keys.Add(registeredDevice.DeviceId.Trim());
            if (!string.IsNullOrWhiteSpace(registeredDevice.RobotId))
                keys.Add(registeredDevice.RobotId.Trim());
            if (!string.IsNullOrWhiteSpace(registeredDevice.FriendlyName))
                keys.Add(registeredDevice.FriendlyName.Trim());
        }

        return keys;
    }

    private static IEnumerable<string> GetSessionIdentityValues(CloudSession session)
    {
        var values = new[]
        {
            session.DeviceId,
            ReadSessionMetadata(session, "registeredDeviceId"),
            ReadSessionMetadata(session, "registeredRobotId"),
            ReadSessionMetadata(session, "robotID"),
            ReadSessionMetadata(session, "robotId"),
            ReadSessionMetadata(session, "robotFriendlyId"),
            ReadSessionMetadata(session, "friendlyId"),
            ReadSessionMetadata(session, "deviceId")
        };

        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
                yield return value.Trim();
        }
    }

    private static string? ReadSessionMetadata(CloudSession session, string key)
    {
        return session.Metadata.TryGetValue(key, out var value) ? value?.ToString() : null;
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

    private static string Fingerprint(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "none";

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value.Trim()));
        return Convert.ToHexString(hash)[..12].ToLowerInvariant();
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

    private static async Task SendRepliesAsync(
        WebSocket socket,
        IReadOnlyList<WebSocketReply> replies,
        CancellationToken cancellationToken)
    {
        foreach (var reply in replies)
        {
            if (string.IsNullOrWhiteSpace(reply.Text)) continue;
            if (reply.DelayMs > 0) await Task.Delay(reply.DelayMs, cancellationToken);

            var payload = Encoding.UTF8.GetBytes(reply.Text);
            await socket.SendAsync(payload, WebSocketMessageType.Text, true, cancellationToken);
        }
    }

    private sealed record ReceivedSocketMessage(WebSocketMessageType MessageType, byte[] Buffer);
}
