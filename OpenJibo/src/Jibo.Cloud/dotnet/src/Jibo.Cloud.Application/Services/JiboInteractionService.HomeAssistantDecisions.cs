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
            return new JiboInteractionDecision(
                intentName,
                "Home Assistant control is not available on this server right now.");

        var (deviceId, friendlyId) = JiboIdentityResolver.Resolve(turn, cloudStateStore);
        var link = userIntegrationStore.FindLinkForJibo(deviceId, friendlyId);
        if (link is null)
            return new JiboInteractionDecision(
                intentName,
                "I don't have Home Assistant set up for my room yet.");

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

    private JiboInteractionDecision BuildHaClimateSetTempDecision(TurnContext turn)
    {
        return BuildHaClimateDecision(turn, "ha_climate_set_temp");
    }

    private JiboInteractionDecision BuildHaClimateCoolDownDecision(TurnContext turn)
    {
        return BuildHaClimateDecision(turn, "ha_climate_cool_down");
    }

    private JiboInteractionDecision BuildHaClimateWarmUpDecision(TurnContext turn)
    {
        return BuildHaClimateDecision(turn, "ha_climate_warm_up");
    }

    private JiboInteractionDecision BuildHaClimateDecision(TurnContext turn, string intentName)
    {
        if (userIntegrationStore is null || cloudStateStore is null)
            return new JiboInteractionDecision(
                intentName,
                "Home Assistant control is not available on this server right now.");

        var (deviceId, friendlyId) = JiboIdentityResolver.Resolve(turn, cloudStateStore);
        var link = userIntegrationStore.FindLinkForJibo(deviceId, friendlyId);
        if (link is null)
            return new JiboInteractionDecision(
                intentName,
                "I don't have Home Assistant set up for my room yet.");

        var transcript = turn.NormalizedTranscript ?? turn.RawTranscript;
        HomeAssistantClimateCommandParser.TryParse(transcript, out var climateCommand);

        if (climateCommand.Action == HomeAssistantClimateCommandParser.ClimateAction.SetTemperature &&
            climateCommand.Temperature is not null)
        {
            var tempLabel =
                HomeAssistantClimateCommandParser.FormatTemperatureForSpeech(climateCommand.Temperature.Value);
            if (climateCommand.Scope == HomeAssistantClimateCommandParser.ClimateScope.Named &&
                !string.IsNullOrWhiteSpace(climateCommand.TargetName))
            {
                var targetLabel = HomeAssistantClimateCommandParser.FormatTargetForSpeech(climateCommand.TargetName);
                return new JiboInteractionDecision(
                    intentName,
                    $"Okay, setting the {targetLabel} to {tempLabel}.");
            }

            return new JiboInteractionDecision(
                intentName,
                $"Okay, setting the temperature to {tempLabel}.");
        }

        if (climateCommand.Action == HomeAssistantClimateCommandParser.ClimateAction.CoolDown)
            return new JiboInteractionDecision(intentName, "Okay, I'll cool things down a bit.");

        if (climateCommand.Action == HomeAssistantClimateCommandParser.ClimateAction.WarmUp)
            return new JiboInteractionDecision(intentName, "Okay, I'll warm things up a bit.");

        return intentName switch
        {
            "ha_climate_cool_down" => new JiboInteractionDecision(intentName, "Okay, I'll cool things down a bit."),
            "ha_climate_warm_up" => new JiboInteractionDecision(intentName, "Okay, I'll warm things up a bit."),
            _ => new JiboInteractionDecision(intentName, "Okay, I'll adjust the temperature.")
        };
    }
}