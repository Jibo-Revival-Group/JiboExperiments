namespace Jibo.Cloud.Application.Services;

/// <summary>
/// Maps OpenJibo holiday countdown labels to on-robot <c>@be/clock</c> holiday entity ids
/// (see <c>@be/clock/resources/holidays/holidays.json</c>).
/// </summary>
public static class ClockHolidayIdMapper
{
    private static readonly Dictionary<string, string> ByCanonicalName =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Christmas Day"] = "christmas",
            ["Christmas Eve"] = "christmasEve",
            ["Easter Sunday"] = "easter",
            ["Good Friday"] = "goodFriday",
            ["Hanukkah"] = "hanukkah",
            ["United States Independence Day"] = "julyFourth",
            ["Labor Day"] = "labor",
            ["Memorial Day"] = "memorial",
            ["Thanksgiving Day"] = "thanksgiving",
            ["Halloween"] = "halloween",
            ["Valentine's Day"] = "valintines",
            ["St. Patrick's Day"] = "saintPatricks",
            ["Presidents' Day"] = "presidents",
            ["Martin Luther King Jr. Day"] = "mlk",
            ["New Year's Eve"] = "newYearsEve",
            ["New Year's Day"] = "newYearsDay",
            ["Cinco de Mayo"] = "cincoDeMayo",
            ["Mother's Day"] = "mothers",
            ["Father's Day"] = "fathers",
            ["April Fools' Day"] = "aprilFools"
        };

    public static bool TryMap(string? canonicalName, out string holidayId)
    {
        holidayId = string.Empty;
        if (string.IsNullOrWhiteSpace(canonicalName)) return false;
        if (!ByCanonicalName.TryGetValue(canonicalName.Trim(), out var mapped) ||
            string.IsNullOrWhiteSpace(mapped))
            return false;

        holidayId = mapped;
        return true;
    }
}
