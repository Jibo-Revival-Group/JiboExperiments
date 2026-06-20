using Jibo.Runtime.Abstractions;

namespace Jibo.Cloud.Application.Services;

public sealed partial class JiboInteractionService
{
    private JiboInteractionDecision BuildHaLightsOffDecision(TurnContext turn)
    {
        if (userIntegrationStore is null)
        {
            return new JiboInteractionDecision(
                "ha_lights_off",
                "Home Assistant control is not available on this server right now.");
        }

        var deviceId = turn.DeviceId;
        var friendlyId = turn.DeviceId;

        if (cloudStateStore is not null)
        {
            var robot = cloudStateStore.GetRobot();
            if (string.IsNullOrWhiteSpace(friendlyId))
                friendlyId = robot.RobotId;
            if (string.IsNullOrWhiteSpace(deviceId))
                deviceId = robot.DeviceId;
        }

        var link = userIntegrationStore.FindLinkForJibo(deviceId, friendlyId);
        if (link is null)
        {
            return new JiboInteractionDecision(
                "ha_lights_off",
                "I don't have Home Assistant set up for my room yet.");
        }

        return new JiboInteractionDecision(
            "ha_lights_off",
            "Okay, turning off the lights.");
    }
}
