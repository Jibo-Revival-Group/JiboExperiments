using Jibo.Runtime.Abstractions;

namespace Jibo.Cloud.Application.Services;

public sealed partial class JiboInteractionService
{
    private JiboInteractionDecision BuildHaLightsOffDecision(TurnContext turn)
    {
        return BuildHaLightsDecision(turn, HomeAssistantLightCommandParser.LightAction.Off, "ha_lights_off");
    }

    private JiboInteractionDecision BuildHaLightsOnDecision(TurnContext turn)
    {
        return BuildHaLightsDecision(turn, HomeAssistantLightCommandParser.LightAction.On, "ha_lights_on");
    }

    private JiboInteractionDecision BuildHaLightsDecision(
        TurnContext turn,
        HomeAssistantLightCommandParser.LightAction expectedAction,
        string intentName)
    {
        if (userIntegrationStore is null || cloudStateStore is null)
        {
            return new JiboInteractionDecision(
                intentName,
                "Home Assistant control is not available on this server right now.");
        }

        var (deviceId, friendlyId) = JiboIdentityResolver.Resolve(turn, cloudStateStore);
        var link = userIntegrationStore.FindLinkForJibo(deviceId, friendlyId);
        if (link is null)
        {
            return new JiboInteractionDecision(
                intentName,
                "I don't have Home Assistant set up for my room yet.");
        }

        var transcript = turn.NormalizedTranscript ?? turn.RawTranscript;
        HomeAssistantLightCommandParser.TryParse(transcript, out var lightCommand);

        if (lightCommand.Scope == HomeAssistantLightCommandParser.LightScope.Named &&
            !string.IsNullOrWhiteSpace(lightCommand.TargetName))
        {
            var targetLabel = HomeAssistantLightCommandParser.FormatTargetForSpeech(lightCommand.TargetName);
            var reply = expectedAction == HomeAssistantLightCommandParser.LightAction.On
                ? $"Okay, turning on {targetLabel}."
                : $"Okay, turning off {targetLabel}.";
            return new JiboInteractionDecision(intentName, reply);
        }

        var roomReply = expectedAction == HomeAssistantLightCommandParser.LightAction.On
            ? "Okay, turning on the lights."
            : "Okay, turning off the lights.";
        return new JiboInteractionDecision(intentName, roomReply);
    }
}
