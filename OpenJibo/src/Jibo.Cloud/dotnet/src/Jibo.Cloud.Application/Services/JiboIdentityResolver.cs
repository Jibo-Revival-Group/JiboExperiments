using Jibo.Cloud.Application.Abstractions;
using Jibo.Cloud.Domain.Models;
using Jibo.Runtime.Abstractions;

namespace Jibo.Cloud.Application.Services;

public static class JiboIdentityResolver
{
    public static (string? DeviceId, string? FriendlyId) Resolve(TurnContext turn, ICloudStateStore cloudStateStore)
    {
        var candidate = ReadRobotKeyFromTurn(turn);

        if (!string.IsNullOrWhiteSpace(candidate))
        {
            if (IsStockOwnerPlaceholder(candidate))
            {
                var canonicalRobot = FindRobotForStockOwnerPlaceholder(candidate, cloudStateStore);
                return (canonicalRobot?.DeviceId, canonicalRobot?.RobotId);
            }

            var registered = cloudStateStore.FindDeviceByFriendlyId(candidate);
            if (registered is not null)
            {
                var friendly = string.IsNullOrWhiteSpace(registered.RobotId)
                    ? registered.DeviceId
                    : registered.RobotId;
                return (registered.DeviceId, friendly);
            }

            // Unregistered turn identity is the robot key. Never inherit GetRobot().DeviceId —
            // that singleton collapses every unregistered hyphenated id onto one shared device.
            return (candidate, candidate);
        }

        var robot = cloudStateStore.GetRobot();
        var deviceId = robot.DeviceId;
        var friendlyId = string.IsNullOrWhiteSpace(robot.RobotId) ? deviceId : robot.RobotId;
        return (deviceId, friendlyId);
    }

    internal static bool IsStockOwnerPlaceholder(string? candidate) =>
        candidate?.StartsWith(JiboCloudProtocolService.StockOwnerPlaceholderPrefix,
            StringComparison.OrdinalIgnoreCase) is true;

    internal static LoopRecord? FindRegisteredRobotLoop(string? registeredRobotId, ICloudStateStore? store)
    {
        if (store is null || string.IsNullOrWhiteSpace(registeredRobotId)) return null;

        var matchingLoops = store.GetLoops().Where(loop => !loop.IsSuspended &&
            (loop.RobotId.Equals(registeredRobotId, StringComparison.OrdinalIgnoreCase) ||
             loop.RobotFriendlyId.Equals(registeredRobotId, StringComparison.OrdinalIgnoreCase))).Take(2).ToArray();
        return matchingLoops.Length == 1 ? matchingLoops[0] : null;
    }

    internal static DeviceRegistration? FindRobotForStockOwnerPlaceholder(string candidate, ICloudStateStore store)
    {
        // Resolve only an owner edge generated for an existing Loop with an active robot registration.
        var loop = store.GetLoops().SingleOrDefault(loop => string.Equals(
            JiboCloudProtocolService.StockOwnerPlaceholderId(loop.LoopId), candidate,
            StringComparison.OrdinalIgnoreCase));
        if (loop is null || loop.IsSuspended || string.IsNullOrWhiteSpace(loop.RobotId) ||
            IsStockOwnerPlaceholder(loop.RobotId)) return null;

        var robot = store.FindDeviceByFriendlyId(loop.RobotId);
        return robot is { IsHidden: false, IsActive: true, ArchivedUtc: null } &&
               string.Equals(robot.RobotId, loop.RobotId, StringComparison.OrdinalIgnoreCase)
            ? robot
            : null;
    }

    private static string? ReadRobotKeyFromTurn(TurnContext turn)
    {
        foreach (var key in new[] { "friendlyId", "robotFriendlyId", "robotID", "robotId" })
        {
            if (turn.Attributes.TryGetValue(key, out var value) &&
                value is not null &&
                !string.IsNullOrWhiteSpace(value.ToString()))
                return value.ToString()!.Trim();
        }

        if (SessionRobotIdentityBinder.TryReadGeneralRobotIdentity(
                turn.Attributes.TryGetValue("context", out var context) ? context?.ToString() : null,
                out var contextRobotId,
                out _))
            return contextRobotId;

        return string.IsNullOrWhiteSpace(turn.DeviceId) ? null : turn.DeviceId.Trim();
    }
}
