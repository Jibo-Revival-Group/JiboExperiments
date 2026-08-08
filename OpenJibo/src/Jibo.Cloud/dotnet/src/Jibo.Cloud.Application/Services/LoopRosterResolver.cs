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
        if (all.Count == 0) return all;
        if (all.Count == 1) return all;

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
            if (matched.Length > 0)
                return [SelectSingleLoop(matched, configuredRobotId, keys)];
        }

        if (!string.IsNullOrWhiteSpace(configuredRobotId))
        {
            var configured = all.Where(loop =>
                    loop.RobotId.Equals(configuredRobotId, StringComparison.OrdinalIgnoreCase) ||
                    loop.RobotFriendlyId.Equals(configuredRobotId, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (configured.Length > 0)
                return [SelectSingleLoop(configured, configuredRobotId, keys)];
        }

        // Unidentified caller (e.g. SSM's key-less Loop#list()): never hand back the synthetic
        // bootstrap loop when a real household loop exists, or SyncManager rejects it as
        // "robot <kb hex> not in loop" and KB is never written.
        var nonBootstrap = all.Where(loop => !IsBootstrapLoop(loop)).ToArray();
        if (nonBootstrap.Length > 0)
        {
            var withRobotMember = nonBootstrap
                .Where(loop => stateStore.GetLoopMembers(loop.LoopId)
                    .Any(member => string.Equals(member.Type, "robot", StringComparison.OrdinalIgnoreCase)))
                .ToArray();
            if (withRobotMember.Length == 1)
                return [withRobotMember[0]];
            if (withRobotMember.Length > 1)
                return [withRobotMember.OrderByDescending(loop => loop.UpdatedUtc).First()];

            return [nonBootstrap.OrderByDescending(loop => loop.UpdatedUtc).First()];
        }

        var defaultLoop = all.FirstOrDefault(loop =>
            loop.LoopId.Equals("openjibo-default-loop", StringComparison.OrdinalIgnoreCase));
        return defaultLoop is null ? [all[0]] : [defaultLoop];
    }

    /// <summary>
    /// Synthetic loops created for the in-process bootstrap device
    /// (<c>InMemoryCloudStateStore</c> constructor) — never a real robot's household.
    /// </summary>
    internal static bool IsBootstrapLoop(LoopRecord loop) =>
        loop.RobotId.StartsWith("openjibo-bootstrap-", StringComparison.OrdinalIgnoreCase) ||
        loop.RobotFriendlyId.StartsWith("openjibo-bootstrap-", StringComparison.OrdinalIgnoreCase);

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
        string? deviceId,
        string? configuredRobotId = null)
    {
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
                keys.Add(value.Trim());
        }

        Add(friendlyId);
        Add(deviceId);
        Add(configuredRobotId);

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

    /// <summary>
    /// SyncManager requires exactly one loop. Prefer configured KB hex RobotId,
    /// else a loop matching a Pegasus-style friendly key, else a non-default loop,
    /// else first match.
    /// </summary>
    internal static LoopRecord SelectSingleLoop(
        IReadOnlyList<LoopRecord> matched,
        string? configuredRobotId,
        IReadOnlySet<string>? callerKeys = null)
    {
        if (matched.Count == 0)
            throw new ArgumentException("matched must not be empty.", nameof(matched));
        if (matched.Count == 1)
            return matched[0];

        IReadOnlyList<LoopRecord> candidates = matched;

        if (!string.IsNullOrWhiteSpace(configuredRobotId))
        {
            var byConfigured = matched.Where(loop =>
                    loop.RobotId.Equals(configuredRobotId, StringComparison.OrdinalIgnoreCase) ||
                    loop.RobotFriendlyId.Equals(configuredRobotId, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (byConfigured.Length == 1)
                return byConfigured[0];
            if (byConfigured.Length > 1)
                candidates = byConfigured;
        }

        if (callerKeys is { Count: > 0 })
        {
            // Prefer Pegasus-style Word-Word-Word-Word friendly ids from the caller key set.
            foreach (var key in callerKeys)
            {
                if (!LooksLikePegasusFriendlyId(key)) continue;
                var byFriendly = candidates.Where(loop =>
                        loop.RobotFriendlyId.Equals(key, StringComparison.OrdinalIgnoreCase) ||
                        loop.RobotId.Equals(key, StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                if (byFriendly.Length == 1)
                    return byFriendly[0];
                if (byFriendly.Length > 1)
                    candidates = byFriendly;
            }

            foreach (var key in callerKeys)
            {
                if (LooksLikePegasusFriendlyId(key)) continue;
                var byKey = candidates.Where(loop =>
                        loop.RobotFriendlyId.Equals(key, StringComparison.OrdinalIgnoreCase) ||
                        loop.RobotId.Equals(key, StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                if (byKey.Length == 1)
                    return byKey[0];
                if (byKey.Length > 1)
                    candidates = byKey;
            }
        }

        // UpdateRobot can rewrite both the bootstrap loop and the household loop to the same
        // configured hex; never let the bootstrap/default loop win that tie.
        var nonDefault = candidates.FirstOrDefault(loop =>
            !IsBootstrapLoop(loop) &&
            !loop.LoopId.Equals("openjibo-default-loop", StringComparison.OrdinalIgnoreCase));
        return nonDefault ?? candidates[0];
    }

    private static bool LooksLikePegasusFriendlyId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        var parts = value.Trim().Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length == 4 && parts.All(part => part.Any(char.IsLetter));
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
