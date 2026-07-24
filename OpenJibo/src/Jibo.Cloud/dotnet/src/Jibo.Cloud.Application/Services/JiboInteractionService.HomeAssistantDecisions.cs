using Jibo.Runtime.Abstractions;

namespace Jibo.Cloud.Application.Services;

public sealed partial class JiboInteractionService
{
    private async Task<JiboInteractionDecision> BuildHaLightsOffDecisionAsync(
        TurnContext turn,
        CancellationToken cancellationToken)
    {
        return await BuildHaLightsDecisionAsync(
            turn,
            HomeAssistantLightCommandParser.LightAction.Off,
            "ha_lights_off",
            cancellationToken);
    }

    private async Task<JiboInteractionDecision> BuildHaLightsOnDecisionAsync(
        TurnContext turn,
        CancellationToken cancellationToken)
    {
        return await BuildHaLightsDecisionAsync(
            turn,
            HomeAssistantLightCommandParser.LightAction.On,
            "ha_lights_on",
            cancellationToken);
    }

    private async Task<JiboInteractionDecision> BuildHaLightsDecisionAsync(
        TurnContext turn,
        HomeAssistantLightCommandParser.LightAction expectedAction,
        string intentName,
        CancellationToken cancellationToken)
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

        if (lightCommand.Scope != HomeAssistantLightCommandParser.LightScope.Named ||
            string.IsNullOrWhiteSpace(lightCommand.TargetName))
        {
            var roomReply = expectedAction == HomeAssistantLightCommandParser.LightAction.On
                ? "Okay, turning on the lights."
                : "Okay, turning off the lights.";
            return new JiboInteractionDecision(intentName, roomReply);
        }

        if (homeAssistantCommandService is null)
        {
            var targetLabel = HomeAssistantLightCommandParser.FormatTargetForSpeech(lightCommand.TargetName);
            var optimistic = expectedAction == HomeAssistantLightCommandParser.LightAction.On
                ? $"Okay, turning on {targetLabel}."
                : $"Okay, turning off {targetLabel}.";
            return new JiboInteractionDecision(intentName, optimistic);
        }

        homeAssistantPendingClimateStore?.Clear(deviceId, friendlyId);

        var result = await homeAssistantCommandService.DispatchLightCommandAsync(
            turn,
            intentName,
            waitForResult: true,
            cancellationToken);

        if (result is null)
            return new JiboInteractionDecision(
                intentName,
                "I couldn't reach Home Assistant just now.");

        if (result.IsNotFound)
        {
            var heard = string.IsNullOrWhiteSpace(result.HeardName)
                ? lightCommand.TargetName
                : result.HeardName;
            return new JiboInteractionDecision(
                intentName,
                $"There is no light named {heard}.");
        }

        if (!result.IsOk)
            return new JiboInteractionDecision(
                intentName,
                "I couldn't control that light right now.");

        var matchedLabel = string.IsNullOrWhiteSpace(result.MatchedName)
            ? HomeAssistantLightCommandParser.FormatTargetForSpeech(lightCommand.TargetName)
            : result.MatchedName;
        var reply = expectedAction == HomeAssistantLightCommandParser.LightAction.On
            ? $"Okay, turning on {matchedLabel}."
            : $"Okay, turning off {matchedLabel}.";
        return new JiboInteractionDecision(intentName, reply);
    }

    private Task<JiboInteractionDecision> BuildHaClimateSetTempDecisionAsync(
        TurnContext turn,
        CancellationToken cancellationToken)
    {
        return BuildHaClimateDecisionAsync(turn, "ha_climate_set_temp", cancellationToken);
    }

    private Task<JiboInteractionDecision> BuildHaClimateCoolDownDecisionAsync(
        TurnContext turn,
        CancellationToken cancellationToken)
    {
        return BuildHaClimateDecisionAsync(turn, "ha_climate_cool_down", cancellationToken);
    }

    private Task<JiboInteractionDecision> BuildHaClimateWarmUpDecisionAsync(
        TurnContext turn,
        CancellationToken cancellationToken)
    {
        return BuildHaClimateDecisionAsync(turn, "ha_climate_warm_up", cancellationToken);
    }

    private async Task<JiboInteractionDecision> BuildHaClimateDecisionAsync(
        TurnContext turn,
        string intentName,
        CancellationToken cancellationToken)
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

        if (climateCommand.Scope == HomeAssistantClimateCommandParser.ClimateScope.Named &&
            climateCommand.Action == HomeAssistantClimateCommandParser.ClimateAction.SetTemperature &&
            climateCommand.Temperature is not null)
        {
            homeAssistantPendingClimateStore?.Clear(deviceId, friendlyId);
            var tempLabel =
                HomeAssistantClimateCommandParser.FormatTemperatureForSpeech(climateCommand.Temperature.Value);
            var targetLabel = HomeAssistantClimateCommandParser.FormatTargetForSpeech(climateCommand.TargetName);
            return new JiboInteractionDecision(
                intentName,
                $"Okay, setting the {targetLabel} to {tempLabel}.");
        }

        if (homeAssistantCommandService is null)
            return BuildOptimisticClimateDecision(intentName, climateCommand);

        homeAssistantPendingClimateStore?.Clear(deviceId, friendlyId);

        var result = await homeAssistantCommandService.DispatchClimateCommandAsync(
            turn,
            intentName,
            waitForResult: true,
            cancellationToken);

        if (result is null)
            return new JiboInteractionDecision(
                intentName,
                "I couldn't reach Home Assistant just now.");

        if (result.NeedsClarification &&
            result.Candidates is { Count: > 0 } &&
            homeAssistantPendingClimateStore is not null)
        {
            var action = climateCommand.Action switch
            {
                HomeAssistantClimateCommandParser.ClimateAction.CoolDown => "cool_down",
                HomeAssistantClimateCommandParser.ClimateAction.WarmUp => "warm_up",
                _ => "set_temperature"
            };
            homeAssistantPendingClimateStore.Set(
                friendlyId ?? deviceId ?? link.JiboFriendlyName,
                new HomeAssistantPendingClimateStore.PendingClimateAction(
                    action,
                    result.Candidates.ToArray(),
                    climateCommand.Temperature?.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    action is "cool_down" or "warm_up" ? "2" : null));

            var list = HomeAssistantEntityNameMatcher.FormatCandidateList(result.Candidates);
            return new JiboInteractionDecision(
                intentName,
                $"Which thermostat should I use, {list}?");
        }

        if (result.IsNotFound)
            return new JiboInteractionDecision(
                intentName,
                "I couldn't find a thermostat near me.");

        if (!result.IsOk)
            return new JiboInteractionDecision(
                intentName,
                "I couldn't adjust the temperature right now.");

        return BuildOptimisticClimateDecision(intentName, climateCommand);
    }

    private async Task<JiboInteractionDecision> BuildHaClimateClarifyDecisionAsync(
        TurnContext turn,
        CancellationToken cancellationToken)
    {
        const string intentName = "ha_climate_clarify";
        if (userIntegrationStore is null ||
            cloudStateStore is null ||
            homeAssistantCommandService is null ||
            homeAssistantPendingClimateStore is null)
            return new JiboInteractionDecision(
                intentName,
                "Home Assistant control is not available on this server right now.");

        var (deviceId, friendlyId) = JiboIdentityResolver.Resolve(turn, cloudStateStore);
        var pending = homeAssistantPendingClimateStore.TryGet(deviceId, friendlyId);
        if (pending is null)
            return new JiboInteractionDecision(
                intentName,
                "I don't know which thermostat you mean.");

        var transcript = turn.NormalizedTranscript ?? turn.RawTranscript ?? string.Empty;
        var match = HomeAssistantEntityNameMatcher.FindClosest(transcript, pending.Candidates);
        if (match is null)
        {
            var list = HomeAssistantEntityNameMatcher.FormatCandidateList(pending.Candidates);
            var heard = transcript.Trim();
            if (string.IsNullOrWhiteSpace(heard))
                heard = "that";
            return new JiboInteractionDecision(
                intentName,
                $"I don't see a thermostat called {heard}. Which one: {list}?");
        }

        var result = await homeAssistantCommandService.DispatchClimateApplyEntityAsync(
            turn,
            match.EntityId,
            pending.Action,
            pending.Temperature,
            pending.Delta,
            cancellationToken);

        homeAssistantPendingClimateStore.Clear(deviceId, friendlyId);

        if (result is null || !result.IsOk)
            return new JiboInteractionDecision(
                intentName,
                "I couldn't adjust that thermostat right now.");

        var name = string.IsNullOrWhiteSpace(result.MatchedName) ? match.Name : result.MatchedName;
        if (pending.Action == "set_temperature" && !string.IsNullOrWhiteSpace(pending.Temperature))
        {
            var tempLabel = HomeAssistantClimateCommandParser.FormatTemperatureForSpeech(
                decimal.Parse(pending.Temperature, System.Globalization.CultureInfo.InvariantCulture));
            return new JiboInteractionDecision(
                intentName,
                $"Okay, setting the {name} to {tempLabel}.");
        }

        if (pending.Action == "cool_down")
            return new JiboInteractionDecision(intentName, $"Okay, I'll cool things down with the {name}.");

        if (pending.Action == "warm_up")
            return new JiboInteractionDecision(intentName, $"Okay, I'll warm things up with the {name}.");

        return new JiboInteractionDecision(intentName, $"Okay, I'll use the {name}.");
    }

    private bool ShouldTreatAsHaClimateClarify(TurnContext turn, string loweredTranscript, string semanticIntent)
    {
        if (homeAssistantPendingClimateStore is null || cloudStateStore is null)
            return false;

        if (string.Equals(semanticIntent, "ha_lights_on", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(semanticIntent, "ha_lights_off", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(semanticIntent, "ha_climate_set_temp", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(semanticIntent, "ha_climate_cool_down", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(semanticIntent, "ha_climate_warm_up", StringComparison.OrdinalIgnoreCase))
            return false;

        var (deviceId, friendlyId) = JiboIdentityResolver.Resolve(turn, cloudStateStore);
        var pending = homeAssistantPendingClimateStore.TryGet(deviceId, friendlyId);
        if (pending is null) return false;

        if (string.IsNullOrWhiteSpace(loweredTranscript)) return false;
        if (TranscriptHeuristics.IsLikelyPromptEchoTranscript(loweredTranscript)) return false;

        return true;
    }

    private static JiboInteractionDecision BuildOptimisticClimateDecision(
        string intentName,
        HomeAssistantClimateCommandParser.ClimateCommand climateCommand)
    {
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
