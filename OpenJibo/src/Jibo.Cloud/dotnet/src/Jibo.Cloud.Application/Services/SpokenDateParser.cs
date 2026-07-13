using System.Globalization;
using System.Text.RegularExpressions;

namespace Jibo.Cloud.Application.Services;

public static class SpokenDateParser
{
    private static readonly Regex MonthDayNumericPattern = new(
        @"^(?<month>january|february|march|april|may|june|july|august|september|october|november|december)\s+(?<day>\d{1,2})(?:st|nd|rd|th)?$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex MonthDaySpokenPattern = new(
        @"^(?<month>january|february|march|april|may|june|july|august|september|october|november|december)\s+(?<day>[a-z\- ]+)$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Dictionary<string, int> OrdinalWords = new(StringComparer.OrdinalIgnoreCase)
    {
        ["first"] = 1,
        ["second"] = 2,
        ["third"] = 3,
        ["fourth"] = 4,
        ["fifth"] = 5,
        ["sixth"] = 6,
        ["seventh"] = 7,
        ["eighth"] = 8,
        ["ninth"] = 9,
        ["tenth"] = 10,
        ["eleventh"] = 11,
        ["twelfth"] = 12,
        ["thirteenth"] = 13,
        ["fourteenth"] = 14,
        ["fifteenth"] = 15,
        ["sixteenth"] = 16,
        ["seventeenth"] = 17,
        ["eighteenth"] = 18,
        ["nineteenth"] = 19,
        ["twentieth"] = 20,
        ["twenty first"] = 21,
        ["twenty-first"] = 21,
        ["twenty second"] = 22,
        ["twenty-second"] = 22,
        ["twenty third"] = 23,
        ["twenty-third"] = 23,
        ["twenty fourth"] = 24,
        ["twenty-fourth"] = 24,
        ["twenty fifth"] = 25,
        ["twenty-fifth"] = 25,
        ["twenty sixth"] = 26,
        ["twenty-sixth"] = 26,
        ["twenty seventh"] = 27,
        ["twenty-seventh"] = 27,
        ["twenty eighth"] = 28,
        ["twenty-eighth"] = 28,
        ["twenty ninth"] = 29,
        ["twenty-ninth"] = 29,
        ["thirtieth"] = 30,
        ["thirty first"] = 31,
        ["thirty-first"] = 31
    };

    public static bool TryParse(string? phrase, out string label, out int month, out int day)
    {
        label = string.Empty;
        month = 0;
        day = 0;
        if (string.IsNullOrWhiteSpace(phrase)) return false;

        var normalized = TranscriptTextNormalizer.NormalizeLooseText(phrase);
        if (normalized.StartsWith("the ", StringComparison.Ordinal))
            normalized = normalized[4..];

        var numericMatch = MonthDayNumericPattern.Match(normalized);
        if (numericMatch.Success)
        {
            if (!TryParseMonth(numericMatch.Groups["month"].Value, out month)) return false;
            if (!int.TryParse(numericMatch.Groups["day"].Value, out day)) return false;
            return TryBuildLabel(month, day, out label);
        }

        var spokenMatch = MonthDaySpokenPattern.Match(normalized);
        if (!spokenMatch.Success) return false;

        if (!TryParseMonth(spokenMatch.Groups["month"].Value, out month)) return false;
        var dayText = spokenMatch.Groups["day"].Value.Trim();
        if (!TryParseOrdinal(dayText, out day)) return false;

        return TryBuildLabel(month, day, out label);
    }

    private static bool TryBuildLabel(int month, int day, out string label)
    {
        label = string.Empty;
        if (day is < 1 or > 31) return false;
        if (day > DateTime.DaysInMonth(2024, month)) return false;

        var monthName = CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(month);
        label = $"{monthName} {day}";
        return true;
    }

    private static bool TryParseMonth(string value, out int month)
    {
        month = value.ToLowerInvariant() switch
        {
            "january" => 1,
            "february" => 2,
            "march" => 3,
            "april" => 4,
            "may" => 5,
            "june" => 6,
            "july" => 7,
            "august" => 8,
            "september" => 9,
            "october" => 10,
            "november" => 11,
            "december" => 12,
            _ => 0
        };
        return month != 0;
    }

    private static bool TryParseOrdinal(string value, out int day)
    {
        day = 0;
        if (OrdinalWords.TryGetValue(value, out day)) return true;

        if (int.TryParse(value, out day)) return day is >= 1 and <= 31;

        return false;
    }
}
