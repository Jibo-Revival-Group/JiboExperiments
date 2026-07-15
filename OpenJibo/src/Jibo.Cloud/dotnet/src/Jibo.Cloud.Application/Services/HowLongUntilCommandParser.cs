using System.Text.RegularExpressions;

namespace Jibo.Cloud.Application.Services;

public static class HowLongUntilCommandParser
{
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

    private static readonly string[] PrefixPatterns =
    [
        "how many days until",
        "how many days till",
        "how long until",
        "how long till",
        "days until",
        "days till",
        "time until",
        "time till"
    ];

    public static bool TryParse(string? transcript, out string? targetPhrase)
    {
        targetPhrase = null;
        var normalized = NormalizeCommandPhrase(transcript);
        if (string.IsNullOrWhiteSpace(normalized)) return false;

        foreach (var prefix in PrefixPatterns)
        {
            if (!normalized.StartsWith($"{prefix} ", StringComparison.Ordinal))
                continue;

            targetPhrase = CleanTargetPhrase(normalized[(prefix.Length + 1)..]);
            return !string.IsNullOrWhiteSpace(targetPhrase);
        }

        return false;
    }

    private static string? CleanTargetPhrase(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        var cleaned = value.Trim().TrimEnd('?', '.', '!', ',');
        return string.IsNullOrWhiteSpace(cleaned) ? null : cleaned;
    }

    private static string NormalizeCommandPhrase(string? value)
    {
        var normalized = TranscriptTextNormalizer.NormalizeLooseText(value);
        return TranscriptTextNormalizer.StripLeadingPhrases(normalized, CommandLeadPhrases);
    }
}
