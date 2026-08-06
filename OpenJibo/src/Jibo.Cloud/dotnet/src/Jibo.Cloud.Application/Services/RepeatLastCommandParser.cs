using System.Text.RegularExpressions;

namespace Jibo.Cloud.Application.Services;

public static class RepeatLastCommandParser
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

    // Anchored to the whole utterance so "tell me a joke about school again" stays a joke request.
    private static readonly Regex RepeatVerbPattern = new(
        @"^(?:(?:can|could|would|will)\s+you\s+)?(?:do\s+(?:that|it|this|the\s+same\s+thing)\s+(?:again|one\s+more\s+time)|do\s+the\s+same\s+again|repeat\s+(?:that|it|this)|repeat\s+the\s+last\s+(?:thing|command|one))$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex RepeatShorthandPattern = new(
        @"^(?:(?:the\s+)?same\s+thing\s+again|one\s+more\s+time|again)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static bool IsRepeatRequest(string? transcript)
    {
        var normalized = NormalizeCommandPhrase(transcript);
        if (string.IsNullOrWhiteSpace(normalized)) return false;

        return RepeatVerbPattern.IsMatch(normalized) || RepeatShorthandPattern.IsMatch(normalized);
    }

    private static string NormalizeCommandPhrase(string? value)
    {
        var normalized = TranscriptTextNormalizer.StripTrailingCourtesyWords(value ?? string.Empty);
        return TranscriptTextNormalizer.StripLeadingPhrases(normalized, CommandLeadPhrases);
    }
}
