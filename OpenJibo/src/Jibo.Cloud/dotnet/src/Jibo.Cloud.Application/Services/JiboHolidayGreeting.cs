using Jibo.Cloud.Domain.Models;

namespace Jibo.Cloud.Application.Services;

internal static class JiboHolidayGreeting
{
    private static readonly (string Phrase, string? Claim)[] HolidayPhrases =
    [
        ("merry christmas", "Christmas"),
        ("happy christmas", "Christmas"),
        ("happy new year", "New Year's Day"),
        ("happy halloween", "Halloween"),
        ("happy thanksgiving", "Thanksgiving"),
        ("happy easter", "Easter"),
        ("happy hanukkah", "Hanukkah"),
        ("happy holidays", null),
        ("season's greetings", null),
        ("seasons greetings", null),
        ("season s greetings", null)
    ];

    private static readonly Dictionary<string, string[]> HolidayAliases =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["New Year's Day"] = ["New Years Day", "New Year"],
            ["MLK Day"] = ["Martin Luther King Jr. Day", "Martin Luther King Day"],
            ["Presidents Day"] = ["President's Day", "Presidents' Day"],
            ["Valentine's Day"] = ["Valintines Day", "Valentines Day"],
            ["St. Patrick's Day"] = ["St Patricks Day", "Saint Patrick's Day"],
            ["Independence Day"] = ["July 4th", "Fourth of July"],
            ["Hanukkah"] = ["Hannukah", "Chanukah"]
        };

    internal static bool TryExtractHolidayClaim(string loweredTranscript, out string? holidayClaim)
    {
        holidayClaim = null;
        if (string.IsNullOrWhiteSpace(loweredTranscript)) return false;

        foreach (var (phrase, claim) in HolidayPhrases)
        {
            if (!loweredTranscript.Contains(phrase, StringComparison.OrdinalIgnoreCase)) continue;

            holidayClaim = claim;
            return true;
        }

        return false;
    }

    internal static IReadOnlyList<string> GetTodaysHolidayNames(
        IReadOnlyList<HolidayRecord> holidays,
        DateOnly today)
    {
        return holidays
            .Where(holiday => IsHolidayOnDate(holiday, today))
            .Select(holiday => holiday.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    internal static bool IsClaimedHolidayToday(string? holidayClaim, IReadOnlyList<string> todaysHolidayNames)
    {
        if (todaysHolidayNames.Count == 0) return false;

        if (string.IsNullOrWhiteSpace(holidayClaim))
            return true;

        return todaysHolidayNames.Any(name => NamesMatch(holidayClaim, name));
    }

    private static bool IsHolidayOnDate(HolidayRecord holiday, DateOnly date)
    {
        if (!holiday.IsEnabled || string.Equals(holiday.Category, "birthday", StringComparison.OrdinalIgnoreCase))
            return false;

        if (holiday.Date == date) return true;

        return holiday.EndDate is not null &&
               date >= holiday.Date &&
               date <= holiday.EndDate.Value;
    }

    private static bool NamesMatch(string claim, string holidayName)
    {
        if (string.Equals(claim, holidayName, StringComparison.OrdinalIgnoreCase)) return true;

        if (HolidayAliases.TryGetValue(claim, out var claimAliases) &&
            claimAliases.Any(alias => string.Equals(alias, holidayName, StringComparison.OrdinalIgnoreCase)))
            return true;

        if (HolidayAliases.TryGetValue(holidayName, out var holidayAliases) &&
            holidayAliases.Any(alias => string.Equals(alias, claim, StringComparison.OrdinalIgnoreCase)))
            return true;

        return false;
    }
}
