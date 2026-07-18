using Jibo.Cloud.Infrastructure.Calendar;

namespace Jibo.Cloud.Tests.Infrastructure;

public sealed class IcalCalendarParserTests
{
    [Fact]
    public void ParseEventsForWindow_ReadsTimedAllDayAndWeeklyEvents()
    {
        var today = new DateOnly(2026, 7, 16);
        var tomorrow = today.AddDays(1);
        // Thursday 2026-07-16
        var ics = """
            BEGIN:VCALENDAR
            VERSION:2.0
            BEGIN:VEVENT
            DTSTART:20260716T150000Z
            DTEND:20260716T160000Z
            SUMMARY:Team sync
            END:VEVENT
            BEGIN:VEVENT
            DTSTART;VALUE=DATE:20260717
            DTEND;VALUE=DATE:20260718
            SUMMARY:Company holiday
            END:VEVENT
            BEGIN:VEVENT
            DTSTART:20260709T180000Z
            RRULE:FREQ=WEEKLY;BYDAY=TH
            SUMMARY:Weekly standup
            END:VEVENT
            END:VCALENDAR
            """;

        var events = IcalCalendarParser.ParseEventsForWindow(ics, today, tomorrow, TimeZoneInfo.Utc);

        Assert.Contains(events, item =>
            item.Date == today &&
            item.Summary == "Team sync" &&
            !item.IsAllDay);
        Assert.Contains(events, item =>
            item.Date == tomorrow &&
            item.Summary == "Company holiday" &&
            item.IsAllDay &&
            item.TimeLabel == "all day");
        Assert.Contains(events, item =>
            item.Date == today &&
            item.Summary == "Weekly standup");
    }
}
