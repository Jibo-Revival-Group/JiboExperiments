using Jibo.Cloud.Application.Abstractions;

namespace Jibo.Cloud.Application.Services;

public static class HolidayDateCalculator
{
    public static DateOnly GetNextOccurrence(DateOnly referenceDate, HolidayDateRule rule)
    {
        return rule.Type switch
        {
            "fixed" => GetNextFixedOccurrence(referenceDate, rule.Month!.Value, rule.Day!.Value),
            "nthWeekday" => GetNextVariableOccurrence(
                referenceDate,
                year => NthWeekdayOfMonth(
                    year,
                    rule.Month!.Value,
                    ParseDayOfWeek(rule.DayOfWeek!),
                    rule.Occurrence!.Value)),
            "lastWeekday" => GetNextVariableOccurrence(
                referenceDate,
                year => LastWeekdayOfMonth(year, rule.Month!.Value, ParseDayOfWeek(rule.DayOfWeek!))),
            "mondayBefore" => GetNextVariableOccurrence(
                referenceDate,
                year => MondayOnOrBefore(new DateOnly(year, rule.Month!.Value, rule.Day!.Value))),
            "easterOffset" => GetNextVariableOccurrence(
                referenceDate,
                year => CalculateWesternEasterSunday(year).AddDays(rule.Days!.Value)),
            "orthodoxEasterOffset" => GetNextVariableOccurrence(
                referenceDate,
                year => CalculateOrthodoxEasterSunday(year).AddDays(rule.Days!.Value)),
            "yearLookup" => GetNextYearLookupOccurrence(referenceDate, rule.Dates!),
            _ => throw new ArgumentOutOfRangeException(nameof(rule), rule.Type, "Unknown holiday date rule type.")
        };
    }

    public static int CountDaysUntil(DateOnly referenceDate, DateOnly targetDate)
    {
        return targetDate.DayNumber - referenceDate.DayNumber;
    }

    public static DateOnly GetNextWeekdayOccurrence(DateOnly referenceDate, DayOfWeek dayOfWeek)
    {
        var offset = ((int)dayOfWeek - (int)referenceDate.DayOfWeek + 7) % 7;
        return referenceDate.AddDays(offset);
    }

    public static DateOnly GetNextMonthDayOccurrence(DateOnly referenceDate, int month, int day)
    {
        return GetNextFixedOccurrence(referenceDate, month, day);
    }

    private static DateOnly GetNextFixedOccurrence(DateOnly referenceDate, int month, int day)
    {
        var year = referenceDate.Year;
        if (day > DateTime.DaysInMonth(year, month))
            year++;

        DateOnly candidate;
        try
        {
            candidate = new DateOnly(year, month, day);
        }
        catch
        {
            candidate = new DateOnly(year + 1, month, day);
        }

        if (candidate < referenceDate)
        {
            year = candidate.Year + 1;
            candidate = new DateOnly(year, month, day);
        }

        return candidate;
    }

    private static DateOnly GetNextVariableOccurrence(DateOnly referenceDate, Func<int, DateOnly> computeForYear)
    {
        var year = referenceDate.Year;
        var candidate = computeForYear(year);
        if (candidate < referenceDate) candidate = computeForYear(year + 1);
        return candidate;
    }

    private static DateOnly GetNextYearLookupOccurrence(
        DateOnly referenceDate,
        IReadOnlyDictionary<string, string> datesByYear)
    {
        foreach (var year in datesByYear.Keys
                     .Select(static key => int.Parse(key, System.Globalization.CultureInfo.InvariantCulture))
                     .OrderBy(static year => year))
        {
            if (!TryParseMonthDay(datesByYear[year.ToString(System.Globalization.CultureInfo.InvariantCulture)],
                    out var month, out var day))
                continue;

            var candidate = new DateOnly(year, month, day);
            if (candidate >= referenceDate) return candidate;
        }

        var lastYear = datesByYear.Keys
            .Select(static key => int.Parse(key, System.Globalization.CultureInfo.InvariantCulture))
            .Max();
        TryParseMonthDay(datesByYear[lastYear.ToString(System.Globalization.CultureInfo.InvariantCulture)],
            out var fallbackMonth, out var fallbackDay);
        return new DateOnly(lastYear + 1, fallbackMonth, fallbackDay);
    }

    private static bool TryParseMonthDay(string value, out int month, out int day)
    {
        month = 0;
        day = 0;
        var parts = value.Split('-', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2) return false;
        return int.TryParse(parts[0], out month)
               && int.TryParse(parts[1], out day)
               && month is >= 1 and <= 12
               && day is >= 1 and <= 31;
    }

    public static DateOnly NthWeekdayOfMonth(int year, int month, DayOfWeek dayOfWeek, int occurrence)
    {
        var date = new DateOnly(year, month, 1);
        var offset = ((int)dayOfWeek - (int)date.DayOfWeek + 7) % 7;
        return date.AddDays(offset + 7 * (occurrence - 1));
    }

    public static DateOnly LastWeekdayOfMonth(int year, int month, DayOfWeek dayOfWeek)
    {
        var date = new DateOnly(year, month, DateTime.DaysInMonth(year, month));
        var offset = ((int)date.DayOfWeek - (int)dayOfWeek + 7) % 7;
        return date.AddDays(-offset);
    }

    private static DateOnly MondayOnOrBefore(DateOnly anchor)
    {
        if (anchor.DayOfWeek == DayOfWeek.Monday) return anchor;

        var offset = ((int)anchor.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
        return anchor.AddDays(-offset);
    }

    public static DateOnly CalculateWesternEasterSunday(int year)
    {
        var a = year % 19;
        var b = year / 100;
        var c = year % 100;
        var d = b / 4;
        var e = b % 4;
        var f = (b + 8) / 25;
        var g = (b - f + 1) / 3;
        var h = (19 * a + b - d - g + 15) % 30;
        var i = c / 4;
        var k = c % 4;
        var l = (32 + 2 * e + 2 * i - h - k) % 7;
        var m = (a + 11 * h + 22 * l) / 451;
        var month = (h + l - 7 * m + 114) / 31;
        var day = (h + l - 7 * m + 114) % 31 + 1;
        return new DateOnly(year, month, day);
    }

    public static DateOnly CalculateOrthodoxEasterSunday(int year)
    {
        var a = year % 4;
        var b = year % 7;
        var c = year % 19;
        var d = (19 * c + 15) % 30;
        var e = (2 * a + 4 * b - d + 34) % 7;
        var month = (d + e + 114) / 31;
        var day = (d + e + 114) % 31 + 1;

        var julianEaster = new DateTime(year, month, day, 0, 0, 0, DateTimeKind.Utc);
        var gregorianEaster = julianEaster.AddDays(13);
        if (year >= 2100) gregorianEaster = julianEaster.AddDays(14);

        return DateOnly.FromDateTime(gregorianEaster);
    }

    private static DayOfWeek ParseDayOfWeek(string value) =>
        value.ToLowerInvariant() switch
        {
            "monday" => DayOfWeek.Monday,
            "tuesday" => DayOfWeek.Tuesday,
            "wednesday" => DayOfWeek.Wednesday,
            "thursday" => DayOfWeek.Thursday,
            "friday" => DayOfWeek.Friday,
            "saturday" => DayOfWeek.Saturday,
            "sunday" => DayOfWeek.Sunday,
            _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown day of week.")
        };
}
