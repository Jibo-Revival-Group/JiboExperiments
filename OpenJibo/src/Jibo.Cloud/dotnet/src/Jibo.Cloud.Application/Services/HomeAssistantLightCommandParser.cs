using System.Text.RegularExpressions;

namespace Jibo.Cloud.Application.Services;

public static class HomeAssistantLightCommandParser
{
    public enum LightAction
    {
        Off,
        On
    }

    public enum LightScope
    {
        Room,
        Named
    }

    private static readonly string[] CommandLeadPhrases =
    [
        "hey jibo",
        "hello jibo",
        "hi jibo",
        "jibo",
        "o",
        "oh",
        "so",
        "well",
        "um",
        "uh",
        "hmm",
        "erm",
        "ah",
        "please",
        "can you",
        "ok jibo",
        "okay jibo"
    ];

    private static readonly HashSet<string> RoomLightPhrases = new(StringComparer.Ordinal)
    {
        "turn off the lights",
        "turn off the light",
        "turn the lights off",
        "turn the light off",
        "lights off",
        "light off",
        "switch off the lights",
        "switch off the light",
        "switch the lights off",
        "switch the light off",
        "kill the lights",
        "kill the light",
        "shut off the lights",
        "shut off the light",
        "shut the lights off",
        "shut the light off",
        "turn all the lights off",
        "turn all the light off",
        "all lights off",
        "all light off",
        "turn on the lights",
        "turn on the light",
        "turn the lights on",
        "turn the light on",
        "lights on",
        "light on",
        "switch on the lights",
        "switch on the light",
        "switch the lights on",
        "switch the light on",
        "turn all the lights on",
        "turn all the light on",
        "all lights on",
        "all light on"
    };

    // turn/switch on|off [the] <target> [light(s)|lamp(s)]
    private static readonly Regex NamedLightActionFirstPattern = new(
        @"^(?:turn|switch)\s+(?<action>on|off)\s+(?:the\s+)?(?<target>.+?)(?:\s+(?:light|lights|lamp|lamps))?\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    // turn/switch [the] <target> light(s)|lamp(s) on|off
    private static readonly Regex NamedLightActionLastPattern = new(
        @"^(?:turn|switch)\s+(?:the\s+)?(?<target>.+?)\s+(?:light|lights|lamp|lamps)\s+(?<action>on|off)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    // turn/switch [the] light(s)|lamp(s) on|off in <target>
    private static readonly Regex NamedLightInRoomWithVerbPattern = new(
        @"^(?:turn|switch)\s+(?:the\s+)?(?:light|lights|lamp|lamps)\s+(?<action>on|off)\s+in\s+(?<target>.+?)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    // light(s) on|off in <target>
    private static readonly Regex NamedLightInRoomPattern = new(
        @"^(?:light|lights)\s+(?<action>on|off)\s+in\s+(?<target>.+?)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static bool TryParse(string? transcript, out LightCommand command)
    {
        command = default;
        var normalized = NormalizeCommandPhrase(transcript);
        if (string.IsNullOrWhiteSpace(normalized)) return false;

        if (RoomLightPhrases.Contains(normalized))
        {
            command = new LightCommand(
                normalized.Contains(" on", StringComparison.Ordinal) ||
                normalized.EndsWith("on", StringComparison.Ordinal)
                    ? LightAction.On
                    : LightAction.Off,
                LightScope.Room,
                null);
            return true;
        }

        if (!TryMatchNamedLight(normalized, out var action, out var target))
            return false;

        command = new LightCommand(action, LightScope.Named, target);
        return true;
    }

    public static string FormatTargetForSpeech(string? targetName)
    {
        if (string.IsNullOrWhiteSpace(targetName)) return "the light";

        var trimmed = targetName.Trim();
        if (trimmed.Contains("light", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Contains("lamp", StringComparison.OrdinalIgnoreCase))
            return trimmed;

        return $"{trimmed} light";
    }

    private static bool TryMatchNamedLight(string normalized, out LightAction action, out string target)
    {
        action = default;
        target = string.Empty;

        foreach (var pattern in new[]
                 {
                     NamedLightActionLastPattern,
                     NamedLightInRoomWithVerbPattern,
                     NamedLightInRoomPattern,
                     NamedLightActionFirstPattern
                 })
        {
            var match = pattern.Match(normalized);
            if (!match.Success) continue;

            var targetName = TranscriptTextNormalizer.StripTrailingCourtesyWords(
                match.Groups["target"].Value.Trim());
            if (string.IsNullOrWhiteSpace(targetName) || IsGenericLightsTarget(targetName))
                continue;

            var actionToken = match.Groups["action"].Value;
            action = string.Equals(actionToken, "on", StringComparison.OrdinalIgnoreCase)
                ? LightAction.On
                : LightAction.Off;
            target = targetName;
            return true;
        }

        return false;
    }

    private static bool IsGenericLightsTarget(string target)
    {
        return target.Equals("lights", StringComparison.OrdinalIgnoreCase) ||
               target.Equals("the lights", StringComparison.OrdinalIgnoreCase) ||
               target.Equals("light", StringComparison.OrdinalIgnoreCase) ||
               target.Equals("the light", StringComparison.OrdinalIgnoreCase) ||
               target.Equals("lamps", StringComparison.OrdinalIgnoreCase) ||
               target.Equals("the lamps", StringComparison.OrdinalIgnoreCase) ||
               target.Equals("lamp", StringComparison.OrdinalIgnoreCase) ||
               target.Equals("the lamp", StringComparison.OrdinalIgnoreCase) ||
               target.Equals("all", StringComparison.OrdinalIgnoreCase) ||
               target.Equals("all the", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeCommandPhrase(string? value)
    {
        var normalized = TranscriptTextNormalizer.NormalizeLooseText(value);
        if (string.Equals(normalized, "uh huh", StringComparison.Ordinal) ||
            normalized.StartsWith("uh huh ", StringComparison.Ordinal))
            return normalized;

        normalized = TranscriptTextNormalizer.StripLeadingPhrases(normalized, CommandLeadPhrases);
        return TranscriptTextNormalizer.StripTrailingCourtesyWords(normalized);
    }

    public readonly record struct LightCommand(LightAction Action, LightScope Scope, string? TargetName);
}
