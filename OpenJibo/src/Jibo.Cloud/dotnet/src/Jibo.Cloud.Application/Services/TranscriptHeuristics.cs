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
        "i hear you",
        "i didn't catch that",
        "say that again",
        "please say that again",
        "thanks for watching",
        "thank you for watching",
        "i hope you try again",
        "i hope you try again in a little while"
    };

    private static readonly string[] PromptEchoPrefixes =
    [
        "do you want to ",
        "do you want ",
        "would you like to ",
        "would you like ",
        "do you feel like ",
        "shall we ",
        "can we ",
        "could we ",
        "should we ",
        "may we ",
        "what do you want to ",
        "what would you like to ",
        "want to take a ",
        "want to take one",
        "want to do ",
        "you want to do "
    ];

    private static readonly string[] PromptEchoQuestionMarkers =
    [
        " do you want to ",
        " do you want ",
        " would you like to ",
        " would you like ",
        " should we ",
        " shall we "
    ];

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

    public static bool IsLikelyPromptEchoTranscript(string? value)
    {
        var normalized = NormalizeLooseTranscript(value);
        if (string.IsNullOrWhiteSpace(normalized)) return false;

        if (IsLikelyRobotSelfAudioTranscript(normalized)) return true;

        if (PromptEchoPrefixes.Any(prefix => normalized.StartsWith(prefix, StringComparison.Ordinal)))
            return true;

        // Full-prompt captures often keep a leading clause before the question.
        return PromptEchoQuestionMarkers.Any(marker =>
            normalized.Contains(marker, StringComparison.Ordinal));
    }

    public static bool IsLikelySkillOfferPromptEcho(string? value)
    {
        var normalized = NormalizeLooseTranscript(value);
        if (string.IsNullOrWhiteSpace(normalized)) return false;

        if (normalized.Contains("take a picture", StringComparison.Ordinal) ||
            normalized.Contains("take a photo", StringComparison.Ordinal) ||
            normalized.Contains("take one", StringComparison.Ordinal))
        {
            return normalized.Contains("do you want", StringComparison.Ordinal) ||
                   normalized.Contains("want to take", StringComparison.Ordinal) ||
                   normalized.StartsWith("want to take", StringComparison.Ordinal);
        }

        if (normalized.Contains("yoga", StringComparison.Ordinal))
        {
            return normalized.Contains("do you want", StringComparison.Ordinal) ||
                   normalized.Contains("should we", StringComparison.Ordinal) ||
                   normalized.StartsWith("want to do", StringComparison.Ordinal) ||
                   normalized.StartsWith("you want to do", StringComparison.Ordinal);
        }

        return false;
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