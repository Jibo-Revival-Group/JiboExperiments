using System.Globalization;
using System.Text.RegularExpressions;

namespace Jibo.Cloud.Application.Services;

public static class HomeAssistantClimateCommandParser
{
    public enum ClimateAction
    {
        SetTemperature,
        CoolDown,
        WarmUp
    }

    public enum ClimateScope
    {
        Room,
        Named
    }

    private const decimal MinTemperature = 45m;
    private const decimal MaxTemperature = 90m;

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

    private static readonly HashSet<string> CoolDownPhrases = new(StringComparer.Ordinal)
    {
        "it's hot in here",
        "its hot in here",
        "it is hot in here",
        "i'm hot",
        "im hot",
        "i am hot",
        "too hot",
        "too hot in here",
        "it's too hot",
        "its too hot",
        "it is too hot",
        "way too hot",
        "make it cooler",
        "cool it down",
        "cool down",
        "can you cool it down",
        "turn down the heat",
        "turn the heat down",
        "lower the heat",
        "lower the temperature"
    };

    private static readonly HashSet<string> WarmUpPhrases = new(StringComparer.Ordinal)
    {
        "it's cold in here",
        "its cold in here",
        "it is cold in here",
        "i'm cold",
        "im cold",
        "i am cold",
        "too cold",
        "too cold in here",
        "it's too cold",
        "its too cold",
        "it is too cold",
        "way too cold",
        "make it warmer",
        "warm it up",
        "heat it up",
        "can you warm it up",
        "turn up the heat",
        "turn the heat up",
        "raise the heat",
        "raise the temperature"
    };

    private static readonly Regex SetTemperaturePattern = new(
        @"^(?:set|change|adjust)\s+(?:the\s+)?(?:temperature|temp(?:erature)?|thermostat)(?:\s+(?:in|for)\s+(?<target>.+?))?\s+to\s+(?<temp>\d+(?:\.\d+)?)\s*(?:degrees?|fahrenheit|celsius)?\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex NamedSetTemperaturePattern = new(
        @"^(?:set|change|adjust)\s+(?:the\s+)?(?<target>.+?)\s+(?:temperature|temp(?:erature)?|thermostat)\s+to\s+(?<temp>\d+(?:\.\d+)?)\s*(?:degrees?|fahrenheit|celsius)?\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    // make it [to] N [degrees]
    private static readonly Regex MakeItTemperaturePattern = new(
        @"^make\s+it\s+(?:to\s+)?(?<temp>\d+(?:\.\d+)?)\s*(?:degrees?|fahrenheit|celsius)?\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    // make [the] temperature|temp|thermostat [to] N [degrees]
    private static readonly Regex MakeTemperaturePattern = new(
        @"^make\s+(?:the\s+)?(?:temperature|temp(?:erature)?|thermostat)\s+(?:to\s+)?(?<temp>\d+(?:\.\d+)?)\s*(?:degrees?|fahrenheit|celsius)?\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    // temperature|temp|thermostat to N [degrees] (no set/change/adjust)
    private static readonly Regex BareTemperatureToPattern = new(
        @"^(?:temperature|temp(?:erature)?|thermostat)\s+to\s+(?<temp>\d+(?:\.\d+)?)\s*(?:degrees?|fahrenheit|celsius)?\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static bool TryParse(string? transcript, out ClimateCommand command)
    {
        command = default;
        var normalized = NormalizeCommandPhrase(transcript);
        if (string.IsNullOrWhiteSpace(normalized)) return false;

        if (CoolDownPhrases.Contains(normalized))
        {
            command = new ClimateCommand(ClimateAction.CoolDown, ClimateScope.Room, null, null);
            return true;
        }

        if (WarmUpPhrases.Contains(normalized))
        {
            command = new ClimateCommand(ClimateAction.WarmUp, ClimateScope.Room, null, null);
            return true;
        }

        var namedMatch = NamedSetTemperaturePattern.Match(normalized);
        if (namedMatch.Success &&
            TryParseTemperature(namedMatch.Groups["temp"].Value, out var namedTemp))
        {
            var target = namedMatch.Groups["target"].Value.Trim();
            if (!string.IsNullOrWhiteSpace(target) && !IsGenericClimateTarget(target))
            {
                command = new ClimateCommand(
                    ClimateAction.SetTemperature,
                    ClimateScope.Named,
                    target,
                    namedTemp);
                return true;
            }
        }

        var match = SetTemperaturePattern.Match(normalized);
        if (match.Success && TryParseTemperature(match.Groups["temp"].Value, out var temperature))
        {
            var targetName = match.Groups["target"].Success
                ? match.Groups["target"].Value.Trim()
                : null;

            if (!string.IsNullOrWhiteSpace(targetName) && !IsGenericClimateTarget(targetName))
            {
                command = new ClimateCommand(
                    ClimateAction.SetTemperature,
                    ClimateScope.Named,
                    targetName,
                    temperature);
                return true;
            }

            command = new ClimateCommand(
                ClimateAction.SetTemperature,
                ClimateScope.Room,
                null,
                temperature);
            return true;
        }

        foreach (var informalPattern in new[]
                 {
                     MakeItTemperaturePattern,
                     MakeTemperaturePattern,
                     BareTemperatureToPattern
                 })
        {
            var informalMatch = informalPattern.Match(normalized);
            if (!informalMatch.Success ||
                !TryParseTemperature(informalMatch.Groups["temp"].Value, out var informalTemp))
                continue;

            command = new ClimateCommand(
                ClimateAction.SetTemperature,
                ClimateScope.Room,
                null,
                informalTemp);
            return true;
        }

        return false;
    }

    public static string FormatTargetForSpeech(string? targetName)
    {
        if (string.IsNullOrWhiteSpace(targetName)) return "the thermostat";

        var trimmed = targetName.Trim();
        if (trimmed.Contains("thermostat", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Contains("hvac", StringComparison.OrdinalIgnoreCase))
            return trimmed;

        return $"{trimmed} thermostat";
    }

    public static string FormatTemperatureForSpeech(decimal temperature)
    {
        if (temperature == decimal.Truncate(temperature))
            return $"{decimal.ToInt32(temperature)} degrees";

        return $"{temperature.ToString(CultureInfo.InvariantCulture)} degrees";
    }

    private static bool TryParseTemperature(string token, out decimal temperature)
    {
        temperature = default;
        if (!decimal.TryParse(token, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
            return false;

        if (parsed < MinTemperature || parsed > MaxTemperature)
            return false;

        temperature = parsed;
        return true;
    }

    private static bool IsGenericClimateTarget(string target)
    {
        if (target.Equals("the", StringComparison.OrdinalIgnoreCase) ||
            target.Equals("a", StringComparison.OrdinalIgnoreCase) ||
            target.Equals("an", StringComparison.OrdinalIgnoreCase))
            return true;

        return target.Equals("thermostat", StringComparison.OrdinalIgnoreCase) ||
               target.Equals("the thermostat", StringComparison.OrdinalIgnoreCase) ||
               target.Equals("temperature", StringComparison.OrdinalIgnoreCase) ||
               target.Equals("the temperature", StringComparison.OrdinalIgnoreCase) ||
               target.Equals("heat", StringComparison.OrdinalIgnoreCase) ||
               target.Equals("the heat", StringComparison.OrdinalIgnoreCase);
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

    public readonly record struct ClimateCommand(
        ClimateAction Action,
        ClimateScope Scope,
        string? TargetName,
        decimal? Temperature);
}
