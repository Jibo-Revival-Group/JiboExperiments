using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text.Json;

namespace Jibo.Cloud.Application.Services;

/// <summary>
/// Live registry of robot <c>api-socket</c> WebSockets, used to push stock-style
/// <c>LoopUpdated</c> notifications so SSM can re-sync the on-device Loop immediately.
/// </summary>
public sealed class RobotNotificationRegistry
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ConcurrentDictionary<Guid, RobotConnection> _connections = new();

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
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(loopPayload);

        var targetKeys = NormalizeKeys(robotKeys);
        if (targetKeys.Count == 0) return 0;

        var envelope = new
        {
            payload = new
            {
                name = "LoopUpdated",
                payload = loopPayload
            }
        };

        var bytes = JsonSerializer.SerializeToUtf8Bytes(envelope, JsonOptions);
        var pushed = 0;

        foreach (var pair in _connections)
        {
            var connection = pair.Value;
            if (connection.Socket.State != WebSocketState.Open)
            {
                _connections.TryRemove(pair.Key, out _);
                continue;
            }

            if (!connection.RobotKeys.Overlaps(targetKeys)) continue;

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

        return pushed;
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
