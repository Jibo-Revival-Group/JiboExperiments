using System.Text.RegularExpressions;

namespace Jibo.Cloud.Application.Services;

public static class DefineCommandParser
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
        "what is the definition of",
        "what s the definition of",
        "what's the definition of",
        "define the word",
        "define"
    ];

    private static readonly Regex WhatDoesMeanPattern = new(
        @"^what does (?<word>.+) mean$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static bool TryParse(string? transcript, out string? word)
    {
        word = null;
        var normalized = NormalizeCommandPhrase(transcript);
        if (string.IsNullOrWhiteSpace(normalized)) return false;

        foreach (var prefix in PrefixPatterns)
        {
            if (normalized.Equals(prefix, StringComparison.Ordinal))
                return true;

            if (!normalized.StartsWith($"{prefix} ", StringComparison.Ordinal))
                continue;

            word = CleanWord(normalized[(prefix.Length + 1)..]);
            return true;
        }

        var match = WhatDoesMeanPattern.Match(normalized);
        if (!match.Success) return false;

        word = CleanWord(match.Groups["word"].Value);
        return true;
    }

    private static string? CleanWord(string? value)
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
