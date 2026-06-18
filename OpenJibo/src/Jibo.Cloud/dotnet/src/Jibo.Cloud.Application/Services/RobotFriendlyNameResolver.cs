using System.Text.Json;
using System.Text.RegularExpressions;
using Jibo.Cloud.Application.Abstractions;
using Jibo.Runtime.Abstractions;

namespace Jibo.Cloud.Application.Services;

public static partial class RobotFriendlyNameResolver
{
    private const string RobotFriendlyNameAttributeKey = "robotFriendlyName";

    [GeneratedRegex(@"^token-(.+)-(\d+)$", RegexOptions.CultureInvariant)]
    private static partial Regex RobotTokenPattern();

    public static string? Resolve(TurnContext turn, ICloudStateStore? cloudStateStore)
    {
        if (turn.Attributes.TryGetValue(RobotFriendlyNameAttributeKey, out var attributeName)
            && attributeName is string attributeText
            && IsValidFriendlyName(attributeText))
        {
            return attributeText;
        }

        if (turn.Attributes.TryGetValue("context", out var contextValue)
            && contextValue is string contextJson)
        {
            var contextName = ExtractFromContextJson(contextJson);
            if (IsValidFriendlyName(contextName)) return contextName;
        }

        if (IsValidFriendlyName(turn.DeviceId)) return turn.DeviceId;

        var registeredRobot = cloudStateStore?.GetRobot();
        if (registeredRobot is not null && IsValidFriendlyName(registeredRobot.DeviceId))
            return registeredRobot.DeviceId;

        return null;
    }

    public static string? ResolveFromSession(CloudSession session, ICloudStateStore? cloudStateStore)
    {
        if (session.Metadata.TryGetValue(RobotFriendlyNameAttributeKey, out var metadataName)
            && metadataName is string metadataText
            && IsValidFriendlyName(metadataText))
        {
            return metadataText;
        }

        var tokenName = ExtractFromToken(session.Token);
        if (IsValidFriendlyName(tokenName)) return tokenName;

        if (IsValidFriendlyName(session.DeviceId)) return session.DeviceId;

        var registeredRobot = cloudStateStore?.GetRobot();
        if (registeredRobot is not null && IsValidFriendlyName(registeredRobot.DeviceId))
            return registeredRobot.DeviceId;

        return null;
    }

    public static void CaptureFromContext(CloudSession session, string? contextJson)
    {
        var name = ExtractFromContextJson(contextJson);
        if (!IsValidFriendlyName(name)) return;
        session.Metadata[RobotFriendlyNameAttributeKey] = name!;
    }

    public static string? ExtractFromContextJson(string? contextJson)
    {
        if (string.IsNullOrWhiteSpace(contextJson)) return null;

        try
        {
            using var document = JsonDocument.Parse(contextJson);
            if (!document.RootElement.TryGetProperty("general", out var general)) return null;
            if (!general.TryGetProperty("robotID", out var robotId)) return null;

            var value = robotId.GetString();
            return IsValidFriendlyName(value) ? value : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static string? ExtractFromToken(string? token)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;

        var match = RobotTokenPattern().Match(token);
        if (!match.Success) return null;

        var candidate = match.Groups[1].Value;
        return IsValidFriendlyName(candidate) ? candidate : null;
    }

    private static bool IsValidFriendlyName(string? value)
    {
        return RobotFriendlyNameValidator.TryNormalize(value, out _, out _);
    }
}
