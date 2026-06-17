using System.Text.RegularExpressions;

namespace Jibo.Cloud.Application.Services;

internal static class TranscriptTextNormalizer
{
    private static readonly string[] WakePhraseLeadPhrases =
    [
        "hey jibo",
        "hi jibo",
        "hello jibo",
        "okay jibo",
        "ok jibo",
        "hey gibo",
        "hey gebo",
        "hi gebo",
        "hello gebo",
        "hey jeebo",
        "hey jebo",
        "hey jibbo",
        "hey jimbo",
        "hey chibo",
        "hey jupo",
        "hey g bo",
        "hey gee bow",
        "jibo",
        "gibo",
        "gebo",
        "jeebo",
        "jebo",
        "jibbo",
        "jimbo",
        "chibo",
        "jupo",
        "g bo",
        "gee bow"
    ];

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

    internal static string StripLeadingWakePhrase(string? value)
    {
        var normalized = NormalizeLooseText(value);
        if (string.IsNullOrWhiteSpace(normalized)) return string.Empty;

        return StripLeadingPhrases(normalized, WakePhraseLeadPhrases);
    }

    internal static bool IsWakePhraseOnly(string? value)
    {
        var normalized = NormalizeLooseText(value);
        return !string.IsNullOrWhiteSpace(normalized) &&
               string.IsNullOrWhiteSpace(StripLeadingWakePhrase(normalized));
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
