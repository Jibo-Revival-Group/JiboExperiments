using System.Text.RegularExpressions;

namespace Jibo.Cloud.Infrastructure.Audio;

internal static class AudioTranscriptNormalizer
{
    private static readonly Regex PunctuationToSpaceRegex = new(
        @"[^\p{L}\p{N}\s']+",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex WhitespaceRegex = new(
        @"\s+",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static string NormalizeLooseTranscript(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;

        var normalized = WhitespaceRegex.Replace(
                PunctuationToSpaceRegex.Replace(value.Trim().ToLowerInvariant(), " "),
                " ")
            .Trim();

        return normalized is "blank audio" or "blank_audio"
            ? string.Empty
            : normalized;
    }
}