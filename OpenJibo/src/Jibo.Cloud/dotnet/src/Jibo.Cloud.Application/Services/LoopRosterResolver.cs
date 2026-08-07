using System.Text.Json;
using Jibo.Cloud.Application.Abstractions;
using Jibo.Cloud.Domain.Models;

namespace Jibo.Cloud.Application.Services;

/// <summary>
/// Shared SyncManager-facing loop scoping: exactly one household loop per robot
/// for <c>Loop#list()</c> / portal People mutations.
/// </summary>
public static class LoopRosterResolver
{
    public static IReadOnlyList<LoopRecord> ResolveLoopsForKeys(
        ICloudStateStore stateStore,
        IEnumerable<string?> robotKeys,
        string? configuredRobotId = null)
    {
        var all = stateStore.GetLoops();
        if (all.Count <= 1) return all;

        var keys = NormalizeKeys(robotKeys);
        ExpandDeviceKeys(stateStore, keys);

        if (!string.IsNullOrWhiteSpace(configuredRobotId))
            keys.Add(configuredRobotId.Trim());

        if (keys.Count > 0)
        {
            var matched = all.Where(loop =>
                    keys.Contains(loop.RobotId) ||
                    keys.Contains(loop.RobotFriendlyId))
                .ToArray();
            if (matched.Length > 0) return matched;
        }

        if (!string.IsNullOrWhiteSpace(configuredRobotId))
        {
            var configured = all.Where(loop =>
                    loop.RobotId.Equals(configuredRobotId, StringComparison.OrdinalIgnoreCase) ||
                    loop.RobotFriendlyId.Equals(configuredRobotId, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (configured.Length > 0) return configured;
        }

        var defaultLoop = all.FirstOrDefault(loop =>
            loop.LoopId.Equals("openjibo-default-loop", StringComparison.OrdinalIgnoreCase));
        return defaultLoop is null ? [all[0]] : [defaultLoop];
    }

    public static LoopRecord? ResolvePrimaryLoopForKeys(
        ICloudStateStore stateStore,
        IEnumerable<string?> robotKeys,
        string? configuredRobotId = null)
    {
        return ResolveLoopsForKeys(stateStore, robotKeys, configuredRobotId).FirstOrDefault();
    }

    public static HashSet<string> CollectEnvelopeRobotKeys(
        ICloudStateStore stateStore,
        ProtocolEnvelope envelope,
        ProtocolRobotIdentity identity,
        string? configuredRobotId = null)
    {
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
                keys.Add(value.Trim());
        }

        Add(identity.DeviceId);
        Add(configuredRobotId);
        Add(envelope.DeviceId);

        JsonElement? body = null;
        try
        {
            body = envelope.TryParseBody();
        }
        catch (JsonException)
        {
            // Ignore malformed body when collecting identity keys.
        }

        if (body is not null)
        {
            Add(ReadString(body, "robotId"));
            Add(ReadString(body, "robotFriendlyId"));
            Add(ReadString(body, "friendlyId"));
            Add(ReadString(body, "deviceId"));
            Add(ReadString(body, "id"));
            Add(ReadString(body, "loopId"));
        }

        ExpandDeviceKeys(stateStore, keys);
        return keys;
    }

    public static HashSet<string> CollectPortalRobotKeys(
        ICloudStateStore stateStore,
        string? friendlyId,
        string? deviceId)
    {
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
                keys.Add(value.Trim());
        }

        Add(friendlyId);
        Add(deviceId);

        var device = stateStore.FindDeviceByFriendlyId(friendlyId ?? string.Empty) ??
                     stateStore.FindDeviceByFriendlyId(deviceId ?? string.Empty);
        if (device is not null)
        {
            Add(device.DeviceId);
            Add(device.RobotId);
            Add(device.FriendlyName);
        }

        ExpandDeviceKeys(stateStore, keys);
        return keys;
    }

    private static void ExpandDeviceKeys(ICloudStateStore stateStore, HashSet<string> keys)
    {
        foreach (var seed in keys.ToArray())
        {
            var device = stateStore.FindDeviceByFriendlyId(seed);
            if (device is null) continue;
            if (!string.IsNullOrWhiteSpace(device.DeviceId))
                keys.Add(device.DeviceId.Trim());
            if (!string.IsNullOrWhiteSpace(device.RobotId))
                keys.Add(device.RobotId.Trim());
            if (!string.IsNullOrWhiteSpace(device.FriendlyName))
                keys.Add(device.FriendlyName.Trim());
        }
    }

    private static HashSet<string> NormalizeKeys(IEnumerable<string?> robotKeys)
    {
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in robotKeys)
        {
            if (!string.IsNullOrWhiteSpace(key))
                keys.Add(key.Trim());
        }

        return keys;
    }

    private static string? ReadString(JsonElement? body, string propertyName)
    {
        if (body is null || body.Value.ValueKind != JsonValueKind.Object) return null;
        if (!body.Value.TryGetProperty(propertyName, out var property)) return null;
        return property.ValueKind == JsonValueKind.String ? property.GetString() : property.ToString();
    }
}
