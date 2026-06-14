using System.Text.RegularExpressions;

namespace Jibo.Cloud.Application.Services;

internal static class TranscriptTextNormalizer
{
    private static readonly Regex PunctuationToSpaceRegex = new(
        @"[^\p{L}\p{N}\s']+",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex WhitespaceRegex = new(
        @"\s+",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    internal static string NormalizeLooseText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;

        return WhitespaceRegex.Replace(
                PunctuationToSpaceRegex.Replace(value.Trim().ToLowerInvariant(), " "),
                " ")
            .Trim();
    }

    internal static string StripLeadingPhrases(string value, params string[] phrases)
    {
        if (string.IsNullOrWhiteSpace(value) || phrases.Length == 0) return value;

        var normalized = value;
        while (TryStripLeadingPhrase(normalized, phrases, out var trimmed))
            normalized = trimmed;

        return normalized;
    }

    private static bool TryStripLeadingPhrase(string normalizedValue, IReadOnlyList<string> phrases, out string trimmed)
    {
        foreach (var phrase in phrases)
        {
            if (string.IsNullOrWhiteSpace(phrase)) continue;

            if (string.Equals(normalizedValue, phrase, StringComparison.Ordinal))
            {
                trimmed = string.Empty;
                return true;
            }

            if (!normalizedValue.StartsWith($"{phrase} ", StringComparison.Ordinal)) continue;
            trimmed = normalizedValue[(phrase.Length + 1)..].TrimStart();
            return true;
        }

        trimmed = normalizedValue;
        return false;
    }
}