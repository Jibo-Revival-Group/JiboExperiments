using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Jibo.Cloud.Application.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jibo.Cloud.Application.Services;

/// <summary>
/// Live registry of robot <c>api-socket</c> WebSockets, used to push stock-style
/// <c>LoopUpdated</c> notifications so SSM can re-sync the on-device Loop immediately.
/// </summary>
public sealed class RobotNotificationRegistry(
    RobotPendingNotificationStore? pendingStore = null,
    ILogger<RobotNotificationRegistry>? logger = null,
    ITransportMetrics? transportMetrics = null)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ConcurrentDictionary<Guid, RobotConnection> _connections = new();
    private readonly ConditionalWeakTable<WebSocket, SemaphoreSlim> _sendGates = new();
    private readonly RobotPendingNotificationStore _pendingStore = pendingStore ?? new RobotPendingNotificationStore();
    private readonly ILogger _logger = logger ?? NullLogger<RobotNotificationRegistry>.Instance;
    private readonly ITransportMetrics _transportMetrics = transportMetrics ?? NullTransportMetrics.Instance;

    public void Register(IReadOnlyCollection<string> robotKeys, WebSocket socket)
    {
        ArgumentNullException.ThrowIfNull(socket);

        var keys = NormalizeKeys(robotKeys);
        if (keys.Count == 0) return;

        _connections[Guid.NewGuid()] = new RobotConnection(keys, socket);
    }

    public void Remove(WebSocket socket)
    {
        if (socket is null) return;

        foreach (var pair in _connections)
        {
            if (!ReferenceEquals(pair.Value.Socket, socket)) continue;
            _connections.TryRemove(pair.Key, out _);
        }
    }

    public async Task<int> PushLoopUpdatedAsync(
        IReadOnlyCollection<string> robotKeys,
        object loopPayload,
        CancellationToken cancellationToken = default,
        IReadOnlyCollection<string>? liveAliases = null)
    {
        ArgumentNullException.ThrowIfNull(loopPayload);

        var targetKeys = NormalizeKeys(robotKeys);
        if (targetKeys.Count == 0)
        {
            _logger.LogDebug("LoopUpdated push skipped: no target robot keys");
            return 0;
        }

        var envelope = CreateStockNotificationRecord("LoopUpdated", loopPayload);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(envelope, JsonOptions);
        _pendingStore.Enqueue("LoopUpdated", targetKeys, bytes);

        // Resolve observed IDs for this live send only. Queued notifications keep the
        // registered keys so an alias that is later revoked cannot drain old deliveries.
        var liveKeys = liveAliases is { Count: > 0 }
            ? NormalizeKeys(robotKeys.Concat(liveAliases).ToArray())
            : targetKeys;
        var pushed = await PushBytesToLiveSocketsAsync(liveKeys, bytes, cancellationToken);
        if (pushed > 0)
            _pendingStore.Drain(targetKeys);

        return pushed;
    }

    public async Task<int> PushRawNotificationAsync(
        IReadOnlyCollection<string> robotKeys,
        byte[] payload,
        string name = "LoopUpdated",
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(payload);

        var targetKeys = NormalizeKeys(robotKeys);
        if (targetKeys.Count == 0 || payload.Length == 0)
        {
            _logger.LogDebug("Notification push skipped: no target robot keys");
            return 0;
        }

        _pendingStore.Enqueue(name, targetKeys, payload);
        var pushed = await PushBytesToLiveSocketsAsync(targetKeys, payload, cancellationToken);
        if (pushed > 0)
            _pendingStore.Drain(targetKeys);
        return pushed;
    }

    public async Task<int> DrainPendingAsync(
        IReadOnlyCollection<string> robotKeys,
        WebSocket socket,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(socket);
        if (socket.State != WebSocketState.Open) return 0;

        var drained = _pendingStore.Drain(robotKeys);
        if (drained.Count == 0) return 0;

        var sent = 0;
        foreach (var payload in drained)
        {
            try
            {
                if (await SendAsync(socket, payload, cancellationToken))
                    sent++;
            }
            catch (WebSocketException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
        }

        return sent;
    }

    public int PendingCount => _pendingStore.Count;

    public async Task<bool> CloseForShutdownAsync(WebSocket socket, CancellationToken cancellationToken)
    {
        Remove(socket);
        var gate = GetSendGate(socket);
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (socket.State != WebSocketState.Open) return false;
            await socket.CloseOutputAsync(WebSocketCloseStatus.EndpointUnavailable, "server stopping", cancellationToken);
            return true;
        }
        finally
        {
            gate.Release();
        }
    }

    private SemaphoreSlim GetSendGate(WebSocket socket) =>
        _sendGates.GetValue(socket, _ => new SemaphoreSlim(1, 1));

    private async Task<bool> SendAsync(WebSocket socket, byte[] payload, CancellationToken cancellationToken)
    {
        var gate = GetSendGate(socket);
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (socket.State != WebSocketState.Open) return false;
            await socket.SendAsync(payload, WebSocketMessageType.Text, true, cancellationToken);
            _transportMetrics.WebSocketMessage("out", "api-socket", "text-json", "loop_updated", payload.Length);
            return true;
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<int> PushBytesToLiveSocketsAsync(
        IReadOnlySet<string> targetKeys,
        byte[] bytes,
        CancellationToken cancellationToken)
    {
        var pushed = 0;
        var openConnections = 0;

        foreach (var pair in _connections)
        {
            var connection = pair.Value;
            if (connection.Socket.State != WebSocketState.Open)
            {
                _logger.LogDebug("LoopUpdated push skipping closed api-socket connection");
                _connections.TryRemove(pair.Key, out _);
                continue;
            }

            openConnections++;
            if (!connection.RobotKeys.Overlaps(targetKeys))
            {
                _logger.LogDebug(
                    "LoopUpdated push key miss connectionKeys={ConnectionKeys} targetKeys={TargetKeys}",
                    string.Join(',', connection.RobotKeys.Take(8)),
                    string.Join(',', targetKeys.Take(8)));
                continue;
            }

            try
            {
                if (await SendAsync(connection.Socket, bytes, cancellationToken))
                    pushed++;
            }
            catch (WebSocketException)
            {
                _connections.TryRemove(pair.Key, out _);
            }
            catch (ObjectDisposedException)
            {
                _connections.TryRemove(pair.Key, out _);
            }
        }

        if (pushed == 0)
        {
            _logger.LogDebug(
                "LoopUpdated push matched no sockets openConnections={OpenConnections} targetKeys={TargetKeys}",
                openConnections,
                string.Join(',', targetKeys.Take(8)));
        }

        return pushed;
    }

    private static object CreateStockNotificationRecord(string name, object payload)
    {
        return new
        {
            _id = Guid.NewGuid().ToString("N"),
            created = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            skillId = "-1",
            payload = new
            {
                name,
                payload
            }
        };
    }

    private static HashSet<string> NormalizeKeys(IReadOnlyCollection<string>? robotKeys)
    {
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (robotKeys is null) return keys;

        foreach (var key in robotKeys)
        {
            if (string.IsNullOrWhiteSpace(key)) continue;
            keys.Add(key.Trim());
        }

        return keys;
    }

    private sealed record RobotConnection(HashSet<string> RobotKeys, WebSocket Socket);
}
