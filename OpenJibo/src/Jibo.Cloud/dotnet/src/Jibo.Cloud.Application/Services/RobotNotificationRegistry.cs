using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jibo.Cloud.Application.Services;

/// <summary>
/// Live registry of robot <c>api-socket</c> WebSockets, used to push stock-style
/// <c>LoopUpdated</c> notifications so SSM can re-sync the on-device Loop immediately.
/// </summary>
public sealed class RobotNotificationRegistry(
    RobotPendingNotificationStore? pendingStore = null,
    ILogger<RobotNotificationRegistry>? logger = null)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ConcurrentDictionary<Guid, RobotConnection> _connections = new();
    private readonly RobotPendingNotificationStore _pendingStore = pendingStore ?? new RobotPendingNotificationStore();
    private readonly ILogger _logger = logger ?? NullLogger<RobotNotificationRegistry>.Instance;

    public void Register(IReadOnlyCollection<string> robotKeys, WebSocket socket)
    {
        ArgumentNullException.ThrowIfNull(socket);

        var keys = NormalizeKeys(robotKeys);
        if (keys.Count == 0) return;

        // Replace any prior registration for the same socket so keys can grow.
        Remove(socket);
        _connections[Guid.NewGuid()] = new RobotConnection(keys, socket);
    }

    /// <summary>
    /// Refresh notification keys when session identity expands after connect
    /// (friendly id / serial / KB hex), then drain pending against the new keys.
    /// </summary>
    public async Task<int> UpdateKeysAsync(
        WebSocket socket,
        IReadOnlyCollection<string> robotKeys,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(socket);

        var keys = NormalizeKeys(robotKeys);
        if (keys.Count == 0 || socket.State != WebSocketState.Open) return 0;

        var updated = false;
        foreach (var pair in _connections)
        {
            if (!ReferenceEquals(pair.Value.Socket, socket)) continue;
            pair.Value.RobotKeys.UnionWith(keys);
            updated = true;
            break;
        }

        if (!updated)
            Register(keys, socket);

        return await DrainPendingAsync(keys, socket, cancellationToken);
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
        CancellationToken cancellationToken = default)
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

        var pushed = await PushBytesToLiveSocketsAsync(targetKeys, bytes, cancellationToken);
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
                await socket.SendAsync(payload, WebSocketMessageType.Text, true, cancellationToken);
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

    public int OpenConnectionCount =>
        _connections.Count(pair => pair.Value.Socket.State == WebSocketState.Open);

    public IReadOnlyList<string[]> SnapshotOpenConnectionKeys()
    {
        return _connections
            .Where(pair => pair.Value.Socket.State == WebSocketState.Open)
            .Select(pair => pair.Value.RobotKeys.ToArray())
            .ToArray();
    }

    public int CountLiveOverlaps(IReadOnlyCollection<string> robotKeys)
    {
        var targetKeys = NormalizeKeys(robotKeys);
        if (targetKeys.Count == 0) return 0;

        return _connections.Count(pair =>
            pair.Value.Socket.State == WebSocketState.Open &&
            pair.Value.RobotKeys.Overlaps(targetKeys));
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
                await connection.Socket.SendAsync(
                    bytes,
                    WebSocketMessageType.Text,
                    true,
                    cancellationToken);
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

    private sealed class RobotConnection(HashSet<string> robotKeys, WebSocket socket)
    {
        public HashSet<string> RobotKeys { get; } = robotKeys;
        public WebSocket Socket { get; } = socket;
    }
}
