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
        "hey geebo",
        "hi gebo",
        "hi geebo",
        "hello gebo",
        "hello geebo",
        "hey jeebo",
        "hey jebo",
        "hey jibbo",
        "hey jimbo",
        "hey chibo",
        "hey jupo",
        "hey j bowl",
        "hey g bo",
        "hey gee bow",
        "jibo",
        "gibo",
        "gebo",
        "geebo",
        "jeebo",
        "jebo",
        "jibbo",
        "jimbo",
        "chibo",
        "jupo",
        "j bowl",
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

    internal static string ExtractWakePhraseCommand(string? value)
    {
        var normalized = NormalizeLooseText(value);
        if (string.IsNullOrWhiteSpace(normalized)) return string.Empty;

        var withoutLeadingWakePhrase = StripLeadingPhrases(normalized, WakePhraseLeadPhrases);
        if (!string.Equals(withoutLeadingWakePhrase, normalized, StringComparison.Ordinal))
            return withoutLeadingWakePhrase;

        return TryExtractEmbeddedWakePhraseCommand(normalized, out var command) ? command : normalized;
    }

    internal static bool IsWakePhraseOnly(string? value)
    {
        var normalized = NormalizeLooseText(value);
        return !string.IsNullOrWhiteSpace(normalized) &&
               string.IsNullOrWhiteSpace(StripLeadingWakePhrase(normalized));
    }

    internal static bool HasTerminalWakePhraseWithoutCommand(string? value)
    {
        var normalized = NormalizeLooseText(value);
        if (string.IsNullOrWhiteSpace(normalized) || IsWakePhraseOnly(normalized)) return false;

        return TryStripTerminalWakePhrase(normalized, out var beforeWakePhrase) &&
               !string.IsNullOrWhiteSpace(beforeWakePhrase);
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

    private static bool TryExtractEmbeddedWakePhraseCommand(string normalizedValue, out string command)
    {
        var tokens = normalizedValue.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var bestCommandStart = -1;
        var bestPhraseLength = 0;

        for (var index = 1; index < tokens.Length; index += 1)
        {
            foreach (var phrase in WakePhraseLeadPhrases)
            {
                var phraseTokens = phrase.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (phraseTokens.Length == 0 ||
                    index + phraseTokens.Length >= tokens.Length ||
                    !TokensMatch(tokens, index, phraseTokens))
                    continue;

                var commandStart = index + phraseTokens.Length;
                if (commandStart < bestCommandStart ||
                    commandStart == bestCommandStart && phraseTokens.Length <= bestPhraseLength)
                    continue;

                bestCommandStart = commandStart;
                bestPhraseLength = phraseTokens.Length;
            }
        }

        command = bestCommandStart < 0 ? string.Empty : string.Join(' ', tokens.Skip(bestCommandStart));
        return !string.IsNullOrWhiteSpace(command);
    }

    private static bool TryStripTerminalWakePhrase(string normalizedValue, out string beforeWakePhrase)
    {
        foreach (var phrase in WakePhraseLeadPhrases)
        {
            if (string.IsNullOrWhiteSpace(phrase)) continue;

            if (!normalizedValue.EndsWith($" {phrase}", StringComparison.Ordinal)) continue;

            beforeWakePhrase = normalizedValue[..^(phrase.Length + 1)].TrimEnd();
            return true;
        }

        beforeWakePhrase = normalizedValue;
        return false;
    }

    private static bool TokensMatch(IReadOnlyList<string> tokens, int startIndex, IReadOnlyList<string> phraseTokens)
    {
        if (startIndex + phraseTokens.Count > tokens.Count) return false;

        for (var offset = 0; offset < phraseTokens.Count; offset += 1)
        {
            if (!string.Equals(tokens[startIndex + offset], phraseTokens[offset], StringComparison.Ordinal))
                return false;
        }

        return true;
    }
}
