using Jibo.Cloud.Domain.Models;

namespace Jibo.Cloud.Application.Services;

internal static class JiboHolidayGreeting
{
    private static readonly string[] GreetingPrefixes =
    [
        "have a good ",
        "have a happy ",
        "have a merry ",
        "happy ",
        "merry "
    ];

    private static readonly (string Phrase, string? Claim)[] ExplicitGreetingPhrases =
    [
        ("happy holidays", null),
        ("season's greetings", null),
        ("seasons greetings", null),
        ("season s greetings", null)
    ];

    private static readonly (string Phrase, string Claim)[] HolidayNamePhrases =
    [
        ("christmas eve", "Christmas Eve"),
        ("christmas", "Christmas"),
        ("thanksgiving", "Thanksgiving"),
        ("good friday", "Good Friday"),
        ("ash wednesday", "Ash Wednesday"),
        ("palm sunday", "Palm Sunday"),
        ("easter", "Easter"),
        ("halloween", "Halloween"),
        ("hanukkah", "Hanukkah"),
        ("chanukah", "Hanukkah"),
        ("valentine's day", "Valentine's Day"),
        ("valentines day", "Valentine's Day"),
        ("st. patrick's day", "St. Patrick's Day"),
        ("st patricks day", "St. Patrick's Day"),
        ("independence day", "Independence Day"),
        ("fourth of july", "Independence Day"),
        ("july 4th", "Independence Day"),
        ("new year's day", "New Year's Day"),
        ("new years day", "New Year's Day"),
        ("new year", "New Year's Day"),
        ("kwanzaa", "Kwanzaa"),
        ("passover", "Passover"),
        ("memorial day", "Memorial Day"),
        ("labor day", "Labor Day"),
        ("veterans day", "Veterans Day"),
        ("presidents day", "Presidents Day"),
        ("president's day", "Presidents Day"),
        ("mlk day", "MLK Day"),
        ("martin luther king day", "MLK Day"),
        ("mother's day", "Mother's Day"),
        ("mothers day", "Mother's Day"),
        ("father's day", "Father's Day"),
        ("fathers day", "Father's Day"),
        ("flag day", "Flag Day"),
        ("groundhog day", "Groundhog Day"),
        ("april fool's day", "April Fool's Day"),
        ("april fools day", "April Fool's Day"),
        ("cinco de mayo", "Cinco de Mayo"),
        ("chinese new year", "Chinese New Year"),
        ("mardi gras", "Mardi Gras"),
        ("columbus day", "Columbus Day"),
        ("canadian thanksgiving", "Canadian Thanksgiving"),
        ("canada day", "Canada Day"),
        ("purim", "Purim"),
        ("diwali", "Diwali"),
        ("holi", "Holi")
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
            ["Christmas"] = ["Christmas Day"],
            ["Thanksgiving"] = ["Thanksgiving Day"],
            ["Hanukkah"] = ["Hannukah", "Chanukah"]
        };

    internal static bool TryExtractHolidayClaim(string loweredTranscript, out string? holidayClaim)
    {
        holidayClaim = null;
        if (string.IsNullOrWhiteSpace(loweredTranscript)) return false;

        var normalized = NormalizeTranscript(loweredTranscript);

        foreach (var (phrase, claim) in ExplicitGreetingPhrases)
        {
            if (!normalized.Contains(phrase, StringComparison.OrdinalIgnoreCase)) continue;

            holidayClaim = claim;
            return true;
        }

        foreach (var (holidayPhrase, claim) in HolidayNamePhrases)
        {
            foreach (var prefix in GreetingPrefixes)
            {
                if (!normalized.Contains(prefix + holidayPhrase, StringComparison.OrdinalIgnoreCase)) continue;

                holidayClaim = claim;
                return true;
            }
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

    private static string NormalizeTranscript(string loweredTranscript) =>
        loweredTranscript
            .Replace(",", " ", StringComparison.Ordinal)
            .Replace(".", " ", StringComparison.Ordinal)
            .Replace("!", " ", StringComparison.Ordinal)
            .Replace("?", " ", StringComparison.Ordinal)
            .Replace("  ", " ", StringComparison.Ordinal)
            .Trim();

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
