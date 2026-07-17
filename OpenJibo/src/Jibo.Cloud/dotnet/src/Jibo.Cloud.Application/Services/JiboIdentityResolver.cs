using Jibo.Cloud.Application.Abstractions;
using Jibo.Runtime.Abstractions;

namespace Jibo.Cloud.Application.Services;

public static class JiboIdentityResolver
{
    public static (string? DeviceId, string? FriendlyId) Resolve(TurnContext turn, ICloudStateStore cloudStateStore)
    {
        var sessionDeviceId = turn.DeviceId?.Trim();

        if (!string.IsNullOrWhiteSpace(sessionDeviceId))
        {
            var registered = cloudStateStore.FindDeviceByFriendlyId(sessionDeviceId);
            if (registered is not null)
            {
                var friendly = string.IsNullOrWhiteSpace(registered.RobotId)
                    ? registered.DeviceId
                    : registered.RobotId;
                return (registered.DeviceId, friendly);
            }

            // Unregistered turn identity is the robot key. Never inherit GetRobot().DeviceId —
            // that singleton collapses every unregistered hyphenated id onto one shared device.
            return (sessionDeviceId, sessionDeviceId);
        }

        var robot = cloudStateStore.GetRobot();
        var deviceId = robot.DeviceId;
        var friendlyId = string.IsNullOrWhiteSpace(robot.RobotId) ? deviceId : robot.RobotId;
        return (deviceId, friendlyId);
    }
}
