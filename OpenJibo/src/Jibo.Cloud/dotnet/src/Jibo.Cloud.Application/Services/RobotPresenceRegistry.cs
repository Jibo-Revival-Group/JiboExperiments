using System.Collections.Concurrent;
using System.Net.WebSockets;

namespace Jibo.Cloud.Application.Services;

/// <summary>
/// Tracks process-local robot WebSocket lifetimes. Unlike a last-message timestamp, this
/// remains accurate while a robot is connected but quiet or asleep.
/// </summary>
public sealed class RobotPresenceRegistry
{
    private readonly ConcurrentDictionary<Guid, PresenceConnection> _connections = new();

    public Guid Register(string kind, WebSocket socket, IReadOnlyCollection<string>? robotKeys = null)
    {
        ArgumentNullException.ThrowIfNull(socket);

        var id = Guid.NewGuid();
        _connections[id] = new PresenceConnection(socket, kind, NormalizeKeys(robotKeys), DateTimeOffset.UtcNow);
        return id;
    }

    public void UpdateRobotKeys(Guid connectionId, IReadOnlyCollection<string>? robotKeys)
    {
        if (!_connections.TryGetValue(connectionId, out var connection)) return;
        _connections[connectionId] = connection with { RobotKeys = NormalizeKeys(robotKeys) };
    }

    public void Remove(Guid connectionId) => _connections.TryRemove(connectionId, out _);

    public IReadOnlyList<RobotPresenceConnection> GetLiveConnections()
    {
        var connections = new List<RobotPresenceConnection>();
        foreach (var pair in _connections)
        {
            var connection = pair.Value;
            if (connection.Socket.State != WebSocketState.Open)
            {
                _connections.TryRemove(pair.Key, out _);
                continue;
            }

            connections.Add(new RobotPresenceConnection(connection.RobotKeys, connection.Kind, connection.ConnectedUtc));
        }

        return connections;
    }

    private static IReadOnlySet<string> NormalizeKeys(IReadOnlyCollection<string>? robotKeys)
    {
        var normalized = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (robotKeys is null) return normalized;

        foreach (var key in robotKeys)
            if (!string.IsNullOrWhiteSpace(key)) normalized.Add(key.Trim());

        return normalized;
    }

    private sealed record PresenceConnection(
        WebSocket Socket,
        string Kind,
        IReadOnlySet<string> RobotKeys,
        DateTimeOffset ConnectedUtc);
}

public sealed record RobotPresenceConnection(
    IReadOnlySet<string> RobotKeys,
    string Kind,
    DateTimeOffset ConnectedUtc);
