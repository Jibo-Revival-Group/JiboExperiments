using Jibo.Cloud.Application.Abstractions;
using Jibo.Runtime.Abstractions;

namespace Jibo.Cloud.Application.Services;

public static class JiboIdentityResolver
{
    public static (string? DeviceId, string? FriendlyId) Resolve(TurnContext turn, ICloudStateStore cloudStateStore)
    {
        var robot = cloudStateStore.GetRobot();
        var sessionDeviceId = turn.DeviceId?.Trim();

        var deviceId = robot.DeviceId;
        var friendlyId = robot.RobotId;

        if (!string.IsNullOrWhiteSpace(sessionDeviceId))
        {
            var registered = cloudStateStore.FindDeviceByFriendlyId(sessionDeviceId);
            if (registered is not null)
            {
                deviceId = registered.DeviceId;
                friendlyId = registered.RobotId;
            }
            else if (sessionDeviceId.Contains('-', StringComparison.Ordinal))
            {
                friendlyId = sessionDeviceId;
            }
            else
            {
                deviceId = sessionDeviceId;
            }
        }

        if (string.IsNullOrWhiteSpace(friendlyId))
            friendlyId = deviceId;

        return (deviceId, friendlyId);
    }
}