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

    public readonly record struct LightCommand(LightAction Action, LightScope Scope, string? TargetName);

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
        "turn on the lights",
        "turn on the light",
        "turn the lights on",
        "turn the light on",
        "lights on",
        "light on",
        "switch on the lights",
        "switch on the light",
        "switch the lights on",
        "switch the light on"
    };

    private static readonly Regex NamedLightPattern = new(
        @"^(?:turn|switch)\s+(?<action>on|off)\s+(?:the\s+)?(?<target>.+?)(?:\s+(?:light|lights|lamp|lamps))?\s*$",
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

        var match = NamedLightPattern.Match(normalized);
        if (!match.Success) return false;

        var target = StripTrailingCourtesyWords(match.Groups["target"].Value.Trim());
        if (string.IsNullOrWhiteSpace(target) || IsGenericLightsTarget(target)) return false;

        var actionToken = match.Groups["action"].Value;
        command = new LightCommand(
            string.Equals(actionToken, "on", StringComparison.OrdinalIgnoreCase) ? LightAction.On : LightAction.Off,
            LightScope.Named,
            target);
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

    private static bool IsGenericLightsTarget(string target)
    {
        return target.Equals("lights", StringComparison.OrdinalIgnoreCase) ||
               target.Equals("the lights", StringComparison.OrdinalIgnoreCase) ||
               target.Equals("lamps", StringComparison.OrdinalIgnoreCase) ||
               target.Equals("the lamps", StringComparison.OrdinalIgnoreCase);
    }

    private static string StripTrailingCourtesyWords(string target)
    {
        var normalized = TranscriptTextNormalizer.NormalizeLooseText(target);
        if (string.IsNullOrWhiteSpace(normalized)) return string.Empty;

        foreach (var suffix in new[] { "please", "thanks", "thank you" })
        {
            if (normalized.Equals(suffix, StringComparison.Ordinal)) return string.Empty;
            if (normalized.EndsWith($" {suffix}", StringComparison.Ordinal))
                return normalized[..^(suffix.Length + 1)].Trim();
        }

        return normalized;
    }

    private static string NormalizeCommandPhrase(string? value)
    {
        var normalized = TranscriptTextNormalizer.NormalizeLooseText(value);
        if (string.Equals(normalized, "uh huh", StringComparison.Ordinal) ||
            normalized.StartsWith("uh huh ", StringComparison.Ordinal))
            return normalized;

        return TranscriptTextNormalizer.StripLeadingPhrases(normalized, CommandLeadPhrases);
    }
}
