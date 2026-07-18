namespace Jibo.Cloud.Infrastructure.Calendar;

public sealed class IcalCalendarFeedInspector(IIcalFeedFetcher feedFetcher)
{
    public async Task<IcalCalendarFeedProbeResult> ProbeAsync(
        string icalUrl,
        CancellationToken cancellationToken = default)
    {
        if (!IcalUrlValidator.TryValidateHttpsPublicUrl(icalUrl, out _, out var validationError))
            return new IcalCalendarFeedProbeResult(false, validationError, 0, 0, []);

        var fetch = await feedFetcher.FetchAsync(icalUrl, cancellationToken);
        if (!fetch.Ok || string.IsNullOrWhiteSpace(fetch.Body))
            return new IcalCalendarFeedProbeResult(false, fetch.Error ?? "iCal feed could not be loaded.", 0, 0, []);

        var localZone = TimeZoneInfo.Local;
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, localZone).DateTime);
        var tomorrow = today.AddDays(1);
        var events = IcalCalendarParser.ParseEventsForWindow(fetch.Body, today, tomorrow, localZone);
        var todayCount = events.Count(item => item.Date == today);
        var tomorrowCount = events.Count(item => item.Date == tomorrow);
        var samples = events
            .Take(3)
            .Select(item => item.Summary)
            .ToArray();

        return new IcalCalendarFeedProbeResult(true, null, todayCount, tomorrowCount, samples);
    }
}

public sealed record IcalCalendarFeedProbeResult(
    bool Ok,
    string? Error,
    int TodayEventCount,
    int TomorrowEventCount,
    IReadOnlyList<string> SampleSummaries);
