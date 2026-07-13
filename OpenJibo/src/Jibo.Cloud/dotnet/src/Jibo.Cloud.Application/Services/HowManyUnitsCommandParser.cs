using System.Text.RegularExpressions;

namespace Jibo.Cloud.Application.Services;

public readonly record struct HowManyUnitsQuery(string SmallUnitPhrase, string LargeUnitPhrase);

public static class HowManyUnitsCommandParser
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

    private static readonly Regex ConversionPattern = new(
        @"^how many (?!days (?:until|till) )(?!people )(?<small>.+?) (?:are )?in (?:a |an |one )?(?<large>.+?)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static bool TryParse(string? transcript, out HowManyUnitsQuery query)
    {
        query = default;
        var normalized = NormalizeCommandPhrase(transcript);
        if (string.IsNullOrWhiteSpace(normalized)) return false;

        var match = ConversionPattern.Match(normalized);
        if (!match.Success) return false;

        var small = CleanUnitPhrase(match.Groups["small"].Value);
        var large = CleanUnitPhrase(match.Groups["large"].Value);
        if (string.IsNullOrWhiteSpace(small) || string.IsNullOrWhiteSpace(large)) return false;

        query = new HowManyUnitsQuery(small, large);
        return true;
    }

    private static string? CleanUnitPhrase(string value)
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
