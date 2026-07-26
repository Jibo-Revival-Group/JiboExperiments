namespace Jibo.Cloud.Application.Services;

public static class WikipediaLookupParser
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
        "who was",
        "who is",
        "what was",
        "what is"
    ];

    public static bool TryParse(string? transcript, out string? subject)
    {
        subject = null;
        var normalized = NormalizeCommandPhrase(transcript);
        if (string.IsNullOrWhiteSpace(normalized)) return false;

        foreach (var prefix in PrefixPatterns)
        {
            if (normalized.Equals(prefix, StringComparison.Ordinal))
                return false;

            if (!normalized.StartsWith($"{prefix} ", StringComparison.Ordinal))
                continue;

            subject = CleanSubject(normalized[(prefix.Length + 1)..]);
            return !string.IsNullOrWhiteSpace(subject);
        }

        return false;
    }

    private static string? CleanSubject(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        var cleaned = value.Trim().TrimEnd('?', '.', '!', ',');
        if (string.IsNullOrWhiteSpace(cleaned)) return null;

        if (!cleaned.Any(char.IsLetter)) return null;

        return cleaned;
    }

    private static string NormalizeCommandPhrase(string? value)
    {
        var normalized = TranscriptTextNormalizer.NormalizeLooseText(value);
        return TranscriptTextNormalizer.StripLeadingPhrases(normalized, CommandLeadPhrases);
    }
}
