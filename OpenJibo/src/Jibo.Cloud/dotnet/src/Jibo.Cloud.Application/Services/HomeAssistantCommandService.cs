using Jibo.Cloud.Application.Abstractions;
using Jibo.Runtime.Abstractions;

namespace Jibo.Cloud.Application.Services;

public sealed class HomeAssistantCommandService(
    IUserIntegrationStore integrationStore,
    HomeAssistantConnectionRegistry registry,
    ICloudStateStore cloudStateStore)
{
    public async Task<bool> TryDispatchLightCommandAsync(
        TurnContext turn,
        CancellationToken cancellationToken = default)
    {
        var transcript = turn.NormalizedTranscript ?? turn.RawTranscript;
        if (!HomeAssistantLightCommandParser.TryParse(transcript, out var lightCommand)) return false;

        var (deviceId, friendlyId) = ResolveJiboIdentity(turn);
        var link = integrationStore.FindLinkForJibo(deviceId, friendlyId);
        if (link is null) return false;

        var command = BuildHaCommand(lightCommand);
        IReadOnlyDictionary<string, string>? parameters = null;
        if (lightCommand.Scope == HomeAssistantLightCommandParser.LightScope.Named &&
            !string.IsNullOrWhiteSpace(lightCommand.TargetName))
            parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["targetName"] = lightCommand.TargetName
            };

        return await registry.SendCommandAsync(link.HaInstanceId, command, parameters, cancellationToken);
    }

    private static string BuildHaCommand(HomeAssistantLightCommandParser.LightCommand lightCommand)
    {
        return (lightCommand.Action, lightCommand.Scope) switch
        {
            (HomeAssistantLightCommandParser.LightAction.Off, HomeAssistantLightCommandParser.LightScope.Room) =>
                "lights_off_current_room",
            (HomeAssistantLightCommandParser.LightAction.On, HomeAssistantLightCommandParser.LightScope.Room) =>
                "lights_on_current_room",
            (HomeAssistantLightCommandParser.LightAction.Off, HomeAssistantLightCommandParser.LightScope.Named) =>
                "lights_off_named",
            (HomeAssistantLightCommandParser.LightAction.On, HomeAssistantLightCommandParser.LightScope.Named) =>
                "lights_on_named",
            _ => throw new InvalidOperationException("Unsupported light command.")
        };
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
