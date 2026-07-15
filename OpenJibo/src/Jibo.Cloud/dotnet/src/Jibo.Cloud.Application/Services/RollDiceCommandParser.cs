using System.Text.RegularExpressions;

namespace Jibo.Cloud.Application.Services;

public readonly record struct RollDiceQuery(int Sides);

public static class RollDiceCommandParser
{
    public const int MinSides = 2;
    public const int MaxSides = 100;
    public const int DefaultSides = 6;

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

    private static readonly Regex DefaultDicePattern = new(
        @"^roll (?:a |the )?(?:dice|die)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex SidedDicePattern = new(
        @"^roll (?:a |the )?(?<sides>\d+|[a-z\-]+(?:\s+[a-z\-]+)?)[\s-]?sided (?:dice|die)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex DNotationPattern = new(
        @"^roll (?:a |the )?d(?<sides>\d+|[a-z\-]+(?:\s+[a-z\-]+)?)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static bool TryParse(string? transcript, out RollDiceQuery query)
    {
        query = default;
        var normalized = NormalizeCommandPhrase(transcript);
        if (string.IsNullOrWhiteSpace(normalized)) return false;

        if (DefaultDicePattern.IsMatch(normalized))
        {
            query = new RollDiceQuery(DefaultSides);
            return true;
        }

        var sidedMatch = SidedDicePattern.Match(normalized);
        if (sidedMatch.Success && TryParseSides(sidedMatch.Groups["sides"].Value, out var sidedCount))
        {
            query = new RollDiceQuery(sidedCount);
            return true;
        }

        var dMatch = DNotationPattern.Match(normalized);
        if (dMatch.Success && TryParseSides(dMatch.Groups["sides"].Value, out var dCount))
        {
            query = new RollDiceQuery(dCount);
            return true;
        }

        return false;
    }

    public static bool IsValidSideCount(int sides) => sides is >= MinSides and <= MaxSides;

    private static bool TryParseSides(string token, out int sides)
    {
        sides = 0;
        if (string.IsNullOrWhiteSpace(token)) return false;

        var normalized = token.Trim().ToLowerInvariant();
        if (int.TryParse(normalized, out sides))
            return IsValidSideCount(sides);

        var spoken = ParseSpokenInteger(normalized);
        if (spoken is null) return false;

        sides = spoken.Value;
        return IsValidSideCount(sides);
    }

    private static int? ParseSpokenInteger(string normalized)
    {
        if (int.TryParse(normalized, out var numeric)) return numeric;

        if (!normalized.Contains(' '))
            return normalized switch
            {
                "two" => 2,
                "three" => 3,
                "four" => 4,
                "five" => 5,
                "six" => 6,
                "seven" => 7,
                "eight" => 8,
                "nine" => 9,
                "ten" => 10,
                "eleven" => 11,
                "twelve" => 12,
                "thirteen" => 13,
                "fourteen" => 14,
                "fifteen" => 15,
                "sixteen" => 16,
                "seventeen" => 17,
                "eighteen" => 18,
                "nineteen" => 19,
                "twenty" => 20,
                "thirty" => 30,
                "forty" => 40,
                "fifty" => 50,
                "sixty" => 60,
                "seventy" => 70,
                "eighty" => 80,
                "ninety" => 90,
                "hundred" or "one hundred" => 100,
                _ => null
            };

        var parts = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 2 && parts[0] == "one" && parts[1] == "hundred") return 100;

        if (parts.Length != 2) return null;

        var first = ParseSpokenInteger(parts[0]);
        var second = ParseSpokenInteger(parts[1]);
        if (first is >= 20 && second is >= 0 and < 10) return first + second;

        return null;
    }

    private static string NormalizeCommandPhrase(string? value)
    {
        var normalized = TranscriptTextNormalizer.NormalizeLooseText(value);
        return TranscriptTextNormalizer.StripLeadingPhrases(normalized, CommandLeadPhrases);
    }
}
