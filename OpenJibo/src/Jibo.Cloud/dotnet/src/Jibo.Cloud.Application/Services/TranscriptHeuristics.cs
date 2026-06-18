using System.Text.RegularExpressions;

namespace Jibo.Cloud.Application.Services;

public static class TranscriptHeuristics
{
    private static readonly HashSet<string> RobotSelfAudioPhrases = new(StringComparer.Ordinal)
    {
        "i heard you",
        "i heard you say",
        "i heard your",
        "i heard that",
        "okay you said",
        "ok you said",
        "okay, you said",
        "ok, you said",
        "you said",
        "i heard",
        "i can hear you",
        "i hear you"
    };

    private static readonly Regex PunctuationToSpaceRegex = new(
        @"[^\p{L}\p{N}\s']+",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex WhitespaceRegex = new(
        @"\s+",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static bool IsLikelyRobotSelfAudioTranscript(string? value)
    {
        var normalized = NormalizeLooseTranscript(value);
        if (string.IsNullOrWhiteSpace(normalized)) return false;

        return RobotSelfAudioPhrases.Contains(normalized) ||
               RobotSelfAudioPhrases.Any(phrase =>
                   normalized.StartsWith($"{phrase} ", StringComparison.Ordinal) ||
                   normalized.StartsWith($"{phrase},", StringComparison.Ordinal) ||
                   normalized.StartsWith($"{phrase}.", StringComparison.Ordinal));
    }

    public static string ExtractWakePhraseCommand(string? value)
    {
        return TranscriptTextNormalizer.ExtractWakePhraseCommand(value);
    }

    private static string NormalizeLooseTranscript(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;

        return WhitespaceRegex.Replace(
                PunctuationToSpaceRegex.Replace(value.Trim().ToLowerInvariant(), " "),
                " ")
            .Trim();
    }

}
