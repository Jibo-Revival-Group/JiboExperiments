using System.Collections.Concurrent;
using System.Net.WebSockets;
using Jibo.Cloud.Domain.Models;

namespace Jibo.Cloud.Application.Services;

/// <summary>
/// Tracks process-local robot WebSocket lifetimes. Unlike a last-message timestamp, this
/// remains accurate while a robot is connected but quiet or asleep.
/// </summary>
public sealed class RobotPresenceRegistry
{
    private readonly ConcurrentDictionary<Guid, PresenceConnection> _connections = new();

    public Guid Register(string kind, WebSocket socket, IReadOnlyCollection<string>? robotKeys = null,
        string? sessionId = null)
    {
        ArgumentNullException.ThrowIfNull(socket);

        var id = Guid.NewGuid();
        _connections[id] = new PresenceConnection(socket, kind, NormalizeKeys(robotKeys), DateTimeOffset.UtcNow,
            sessionId);
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

            connections.Add(new RobotPresenceConnection(connection.RobotKeys, connection.Kind, connection.ConnectedUtc,
                connection.SessionId));
        }

        return connections;
    }

    public IReadOnlyList<RobotPresenceConnection> GetLiveConnections(
        IReadOnlyList<CloudSession> sessions, IReadOnlyList<DeviceRegistration> devices)
    {
        var connections = GetLiveConnections();
        if (connections.Count == 0) return connections;

        var visibleDeviceIds = devices.Where(device => !device.IsHidden && device.ArchivedUtc is null)
            .Select(device => device.DeviceId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var linkedDevices = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var session in sessions)
            if (session.Metadata.TryGetValue("registeredDeviceId", out var value) &&
                value is string deviceId && visibleDeviceIds.Contains(deviceId))
                linkedDevices[session.SessionId] = deviceId;

        // An administrator can link a quiet socket after its runtime keys were captured.
        return connections.Select(connection =>
                connection.SessionId is { } sessionId && linkedDevices.TryGetValue(sessionId, out var deviceId)
                    ? connection with { RegisteredDeviceId = deviceId }
                    : connection)
            .ToArray();
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
        DateTimeOffset ConnectedUtc,
        string? SessionId);
}

public sealed record RobotPresenceConnection(
    IReadOnlySet<string> RobotKeys,
    string Kind,
    DateTimeOffset ConnectedUtc,
    string? SessionId = null)
{
    public string? RegisteredDeviceId { get; init; }

    public bool MatchesDevice(DeviceRegistration device) => RegisteredDeviceId is { } deviceId
        ? deviceId.Equals(device.DeviceId, StringComparison.OrdinalIgnoreCase)
        : RobotKeys.Contains(device.DeviceId) || RobotKeys.Contains(device.RobotId);
}
