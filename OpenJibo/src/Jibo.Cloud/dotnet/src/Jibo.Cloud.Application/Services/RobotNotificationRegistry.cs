using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jibo.Cloud.Application.Services;

/// <summary>
/// One live <c>api-socket</c> registration, for the paste-back diagnostics report.
/// </summary>
public sealed record RobotNotificationConnectionInfo(
    DateTimeOffset ConnectedUtc,
    DateTimeOffset? LastInboundUtc,
    long InboundFrames,
    IReadOnlyList<string> RobotKeys);

/// <summary>
/// The most recent notification push, kept so a single diagnostics response can say
/// whether a frame was built, who it targeted, which sockets were live at the time,
/// and whether it actually went out.
/// </summary>
public sealed record RobotNotificationPushAttempt(
    DateTimeOffset AtUtc,
    string Name,
    IReadOnlyList<string> TargetKeys,
    IReadOnlyList<IReadOnlyList<string>> OpenConnectionKeys,
    int PushedCount,
    int FrameBytes,
    string FramePreview);

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
    private RobotNotificationPushAttempt? _lastPushAttempt;

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

        var pushed = await PushBytesToLiveSocketsAsync(targetKeys, bytes, cancellationToken, "LoopUpdated");
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
        var pushed = await PushBytesToLiveSocketsAsync(targetKeys, payload, cancellationToken, name);
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

    /// <summary>
    /// Counts a frame the robot sent us. A socket the cloud believes is open but that has
    /// gone quiet inbound is the signature of the robot having hung up on a silent cloud
    /// (<c>ServerPort::onTimer</c>, 120s) without the TCP teardown reaching us.
    /// </summary>
    public void RecordInboundFrame(WebSocket socket)
    {
        if (socket is null) return;

        foreach (var pair in _connections)
        {
            if (!ReferenceEquals(pair.Value.Socket, socket)) continue;
            pair.Value.NoteInboundFrame();
            return;
        }
    }

    public int PendingCount => _pendingStore.Count;

    public int OpenConnectionCount =>
        _connections.Count(pair => pair.Value.Socket.State == WebSocketState.Open);

    public RobotNotificationPushAttempt? LastPushAttempt => Volatile.Read(ref _lastPushAttempt);

    public IReadOnlyList<string[]> SnapshotOpenConnectionKeys()
    {
        return _connections
            .Where(pair => pair.Value.Socket.State == WebSocketState.Open)
            .Select(pair => pair.Value.RobotKeys.ToArray())
            .ToArray();
    }

    public IReadOnlyList<RobotNotificationConnectionInfo> SnapshotOpenConnections()
    {
        return _connections
            .Where(pair => pair.Value.Socket.State == WebSocketState.Open)
            .Select(pair => new RobotNotificationConnectionInfo(
                pair.Value.ConnectedUtc,
                pair.Value.LastInboundUtc,
                pair.Value.InboundFrames,
                pair.Value.RobotKeys.ToArray()))
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
        CancellationToken cancellationToken,
        string name)
    {
        var pushed = 0;
        var openConnections = 0;
        var openConnectionKeys = new List<IReadOnlyList<string>>();

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
            openConnectionKeys.Add(connection.RobotKeys.ToArray());
            if (!connection.RobotKeys.Overlaps(targetKeys))
            {
                // Information, not Debug: a key miss is the difference between "the robot
                // never got it" and "the robot ignored it", and it has to be answerable
                // from the log alone.
                _logger.LogInformation(
                    "{Name} push key miss connectionKeys={ConnectionKeys} targetKeys={TargetKeys}",
                    name,
                    string.Join(',', connection.RobotKeys),
                    string.Join(',', targetKeys));
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

        Volatile.Write(ref _lastPushAttempt, new RobotNotificationPushAttempt(
            DateTimeOffset.UtcNow,
            name,
            targetKeys.ToArray(),
            openConnectionKeys,
            pushed,
            bytes.Length,
            BuildFramePreview(bytes)));

        if (pushed == 0)
        {
            _logger.LogInformation(
                "{Name} push matched no sockets openConnections={OpenConnections} targetKeys={TargetKeys} " +
                "openConnectionKeys={OpenConnectionKeys}",
                name,
                openConnections,
                string.Join(',', targetKeys),
                string.Join(" | ", openConnectionKeys.Select(keys => string.Join(',', keys))));
        }

        return pushed;
    }

    private static string BuildFramePreview(byte[] bytes)
    {
        const int maxPreviewBytes = 2048;
        var text = System.Text.Encoding.UTF8.GetString(bytes, 0, Math.Min(bytes.Length, maxPreviewBytes));
        return bytes.Length > maxPreviewBytes ? text + "…" : text;
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
        private long _inboundFrames;
        private long _lastInboundTicks;

        public HashSet<string> RobotKeys { get; } = robotKeys;
        public WebSocket Socket { get; } = socket;
        public DateTimeOffset ConnectedUtc { get; } = DateTimeOffset.UtcNow;
        public long InboundFrames => Interlocked.Read(ref _inboundFrames);

        public DateTimeOffset? LastInboundUtc
        {
            get
            {
                var ticks = Interlocked.Read(ref _lastInboundTicks);
                return ticks == 0 ? null : new DateTimeOffset(ticks, TimeSpan.Zero);
            }
        }

        public void NoteInboundFrame()
        {
            Interlocked.Increment(ref _inboundFrames);
            Interlocked.Exchange(ref _lastInboundTicks, DateTimeOffset.UtcNow.UtcTicks);
        }
    }
}
