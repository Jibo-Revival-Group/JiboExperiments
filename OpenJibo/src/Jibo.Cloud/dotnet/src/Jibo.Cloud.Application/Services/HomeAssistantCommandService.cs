using Jibo.Cloud.Application.Abstractions;
using Jibo.Runtime.Abstractions;

namespace Jibo.Cloud.Application.Services;

public sealed class HomeAssistantCommandService(
    IUserIntegrationStore integrationStore,
    HomeAssistantConnectionRegistry registry,
    ICloudStateStore cloudStateStore)
{
    public async Task<bool> TryDispatchLightsOffAsync(TurnContext turn, CancellationToken cancellationToken = default)
    {
        var (deviceId, friendlyId) = ResolveJiboIdentity(turn);
        var link = integrationStore.FindLinkForJibo(deviceId, friendlyId);
        if (link is null) return false;

        return await registry.SendCommandAsync(link.HaInstanceId, "lights_off_current_room", cancellationToken);
    }

    private (string? DeviceId, string? FriendlyId) ResolveJiboIdentity(TurnContext turn)
    {
        var deviceId = turn.DeviceId;
        var friendlyId = turn.DeviceId;

        var robot = cloudStateStore.GetRobot();
        if (string.IsNullOrWhiteSpace(friendlyId))
            friendlyId = robot.RobotId;
        if (string.IsNullOrWhiteSpace(deviceId))
            deviceId = robot.DeviceId;

        return (deviceId, friendlyId);
    }
}
