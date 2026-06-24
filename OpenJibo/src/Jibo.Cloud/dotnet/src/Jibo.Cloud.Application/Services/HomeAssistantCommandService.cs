using System.Globalization;
using Jibo.Cloud.Application.Abstractions;
using Jibo.Cloud.Domain.Models;
using Jibo.Runtime.Abstractions;

namespace Jibo.Cloud.Application.Services;

public sealed class HomeAssistantCommandService(
    IUserIntegrationStore integrationStore,
    HomeAssistantConnectionRegistry registry,
    ICloudStateStore cloudStateStore)
{
    public bool CanReachHomeAssistant(TurnContext turn)
    {
        var link = FindLink(turn);
        return link is not null && registry.IsInstanceConnected(link.HaInstanceId);
    }

    public async Task<bool> TryDispatchLightCommandAsync(
        TurnContext turn,
        string intentName,
        CancellationToken cancellationToken = default)
    {
        var lightCommand = ResolveLightCommand(turn, intentName);
        if (lightCommand is null) return false;

        var link = FindLink(turn);
        if (link is null || !registry.IsInstanceConnected(link.HaInstanceId)) return false;

        var command = BuildHaCommand(lightCommand.Value);
        IReadOnlyDictionary<string, string>? parameters = null;
        if (lightCommand.Value.Scope == HomeAssistantLightCommandParser.LightScope.Named &&
            !string.IsNullOrWhiteSpace(lightCommand.Value.TargetName))
            parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["targetName"] = lightCommand.Value.TargetName
            };

        return await registry.SendCommandAsync(link.HaInstanceId, command, parameters, cancellationToken);
    }

    public async Task<bool> TryDispatchClimateCommandAsync(
        TurnContext turn,
        string intentName,
        CancellationToken cancellationToken = default)
    {
        var climateCommand = ResolveClimateCommand(turn, intentName);
        if (climateCommand is null) return false;

        var link = FindLink(turn);
        if (link is null || !registry.IsInstanceConnected(link.HaInstanceId)) return false;

        var command = BuildHaClimateCommand(climateCommand.Value);
        var parameters = BuildHaClimateParameters(climateCommand.Value);
        return await registry.SendCommandAsync(link.HaInstanceId, command, parameters, cancellationToken);
    }

    private HomeAssistantLinkRecord? FindLink(TurnContext turn)
    {
        var (deviceId, friendlyId) = JiboIdentityResolver.Resolve(turn, cloudStateStore);
        return integrationStore.FindLinkForJibo(deviceId, friendlyId);
    }

    private static HomeAssistantLightCommandParser.LightCommand? ResolveLightCommand(
        TurnContext turn,
        string intentName)
    {
        var transcript = turn.NormalizedTranscript ?? turn.RawTranscript;
        if (HomeAssistantLightCommandParser.TryParse(transcript, out var parsed))
            return parsed;

        return intentName.ToLowerInvariant() switch
        {
            "ha_lights_off" => new HomeAssistantLightCommandParser.LightCommand(
                HomeAssistantLightCommandParser.LightAction.Off,
                HomeAssistantLightCommandParser.LightScope.Room,
                null),
            "ha_lights_on" => new HomeAssistantLightCommandParser.LightCommand(
                HomeAssistantLightCommandParser.LightAction.On,
                HomeAssistantLightCommandParser.LightScope.Room,
                null),
            _ => null
        };
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

    private static HomeAssistantClimateCommandParser.ClimateCommand? ResolveClimateCommand(
        TurnContext turn,
        string intentName)
    {
        var transcript = turn.NormalizedTranscript ?? turn.RawTranscript;
        if (HomeAssistantClimateCommandParser.TryParse(transcript, out var parsed))
            return parsed;

        return intentName.ToLowerInvariant() switch
        {
            "ha_climate_cool_down" => new HomeAssistantClimateCommandParser.ClimateCommand(
                HomeAssistantClimateCommandParser.ClimateAction.CoolDown,
                HomeAssistantClimateCommandParser.ClimateScope.Room,
                null,
                null),
            "ha_climate_warm_up" => new HomeAssistantClimateCommandParser.ClimateCommand(
                HomeAssistantClimateCommandParser.ClimateAction.WarmUp,
                HomeAssistantClimateCommandParser.ClimateScope.Room,
                null,
                null),
            _ => null
        };
    }

    private static string BuildHaClimateCommand(HomeAssistantClimateCommandParser.ClimateCommand climateCommand)
    {
        return (climateCommand.Action, climateCommand.Scope) switch
        {
            (HomeAssistantClimateCommandParser.ClimateAction.SetTemperature,
                HomeAssistantClimateCommandParser.ClimateScope.Room) =>
                "climate_set_temperature_current_room",
            (HomeAssistantClimateCommandParser.ClimateAction.SetTemperature,
                HomeAssistantClimateCommandParser.ClimateScope.Named) =>
                "climate_set_temperature_named",
            (HomeAssistantClimateCommandParser.ClimateAction.CoolDown,
                HomeAssistantClimateCommandParser.ClimateScope.Room) =>
                "climate_cool_down_current_room",
            (HomeAssistantClimateCommandParser.ClimateAction.WarmUp,
                HomeAssistantClimateCommandParser.ClimateScope.Room) =>
                "climate_warm_up_current_room",
            _ => throw new InvalidOperationException("Unsupported climate command.")
        };
    }

    private static IReadOnlyDictionary<string, string>? BuildHaClimateParameters(
        HomeAssistantClimateCommandParser.ClimateCommand climateCommand)
    {
        Dictionary<string, string>? parameters = null;

        if (climateCommand.Scope == HomeAssistantClimateCommandParser.ClimateScope.Named &&
            !string.IsNullOrWhiteSpace(climateCommand.TargetName))
        {
            parameters ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            parameters["targetName"] = climateCommand.TargetName;
        }

        if (climateCommand.Temperature is not null)
        {
            parameters ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            parameters["temperature"] = climateCommand.Temperature.Value.ToString(CultureInfo.InvariantCulture);
        }

        if (climateCommand.Action is HomeAssistantClimateCommandParser.ClimateAction.CoolDown
            or HomeAssistantClimateCommandParser.ClimateAction.WarmUp)
        {
            parameters ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            parameters["delta"] = "2";
        }

        return parameters;
    }
}