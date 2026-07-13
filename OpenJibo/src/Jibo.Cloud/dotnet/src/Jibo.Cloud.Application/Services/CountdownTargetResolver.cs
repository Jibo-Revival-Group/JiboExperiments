using Jibo.Cloud.Application.Abstractions;

namespace Jibo.Cloud.Application.Services;

public sealed class CountdownTargetResolver(IHolidayCountdownCatalog holidayCatalog)
{
    public bool TryResolve(string? targetPhrase, out CountdownTarget target)
    {
        target = null!;
        if (string.IsNullOrWhiteSpace(targetPhrase)) return false;

        var normalized = TranscriptTextNormalizer.NormalizeLooseText(targetPhrase);
        if (normalized.StartsWith("the ", StringComparison.Ordinal))
            normalized = normalized[4..];

        if (holidayCatalog.TryResolve(normalized, out var holiday))
        {
            target = new CountdownTarget(holiday.CanonicalName, holiday.Rule);
            return true;
        }

        if (TryParseWeekday(normalized, out var weekday, out var weekdayLabel))
        {
            target = new CountdownTarget(weekdayLabel, weekday);
            return true;
        }

        if (SpokenDateParser.TryParse(normalized, out var dateLabel, out var month, out var day))
        {
            target = new CountdownTarget(dateLabel, month, day);
            return true;
        }

        return false;
    }

    private static bool TryParseWeekday(string normalized, out DayOfWeek weekday, out string label)
    {
        weekday = DayOfWeek.Sunday;
        label = string.Empty;
        if (!TryParseDayOfWeek(normalized, out weekday)) return false;

        label = weekday.ToString();
        return true;
    }

    private static bool TryParseDayOfWeek(string dayToken, out DayOfWeek dayOfWeek)
    {
        dayOfWeek = DayOfWeek.Sunday;
        return dayToken switch
        {
            "monday" => AssignDayOfWeek(DayOfWeek.Monday, out dayOfWeek),
            "tuesday" => AssignDayOfWeek(DayOfWeek.Tuesday, out dayOfWeek),
            "wednesday" => AssignDayOfWeek(DayOfWeek.Wednesday, out dayOfWeek),
            "thursday" => AssignDayOfWeek(DayOfWeek.Thursday, out dayOfWeek),
            "friday" => AssignDayOfWeek(DayOfWeek.Friday, out dayOfWeek),
            "saturday" => AssignDayOfWeek(DayOfWeek.Saturday, out dayOfWeek),
            "sunday" => AssignDayOfWeek(DayOfWeek.Sunday, out dayOfWeek),
            _ => false
        };
    }

    private static bool AssignDayOfWeek(DayOfWeek value, out DayOfWeek target)
    {
        target = value;
        return true;
    }
}

public sealed class CountdownTarget
{
    public CountdownTarget(string label, HolidayDateRule rule)
    {
        Label = label;
        Rule = rule;
    }

    public CountdownTarget(string label, DayOfWeek weekday)
    {
        Label = label;
        Weekday = weekday;
    }

    public CountdownTarget(string label, int month, int day)
    {
        Label = label;
        Month = month;
        Day = day;
    }

    public string Label { get; }
    public HolidayDateRule? Rule { get; }
    public DayOfWeek? Weekday { get; }
    public int? Month { get; }
    public int? Day { get; }

    public DateOnly ResolveNextOccurrence(DateOnly referenceDate)
    {
        if (Rule is not null) return HolidayDateCalculator.GetNextOccurrence(referenceDate, Rule);
        if (Weekday is not null) return HolidayDateCalculator.GetNextWeekdayOccurrence(referenceDate, Weekday.Value);
        if (Month is not null && Day is not null)
            return HolidayDateCalculator.GetNextMonthDayOccurrence(referenceDate, Month.Value, Day.Value);

        throw new InvalidOperationException("Countdown target is missing date resolution data.");
    }
}
