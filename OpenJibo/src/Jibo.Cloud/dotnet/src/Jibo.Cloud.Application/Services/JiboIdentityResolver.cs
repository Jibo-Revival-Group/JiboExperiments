using Jibo.Cloud.Application.Abstractions;
using Jibo.Runtime.Abstractions;

namespace Jibo.Cloud.Application.Services;

public static class JiboIdentityResolver
{
    public static (string? DeviceId, string? FriendlyId) Resolve(TurnContext turn, ICloudStateStore cloudStateStore)
    {
        var candidate = ReadRobotKeyFromTurn(turn);

        if (!string.IsNullOrWhiteSpace(candidate))
        {
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
