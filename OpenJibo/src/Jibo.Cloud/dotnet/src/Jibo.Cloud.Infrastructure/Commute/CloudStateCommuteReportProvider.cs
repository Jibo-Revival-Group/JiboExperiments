using System.Text.RegularExpressions;
using Jibo.Cloud.Application.Abstractions;
using Jibo.Cloud.Domain.Models;
using Jibo.Runtime.Abstractions;

namespace Jibo.Cloud.Infrastructure.Commute;

public sealed class CloudStateCommuteReportProvider(ICloudStateStore cloudStateStore) : ICommuteReportProvider
{
    private static readonly Regex TimeLabelRegex = new(
        @"(?<hour>\d{1,2})(?::(?<minute>\d{2}))?\s*(?<period>a\.?m\.?|p\.?m\.?)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public Task<CommuteReportSnapshot?> GetReportAsync(
        TurnContext turn,
        CancellationToken cancellationToken = default)
    {
        var loopId = ResolveLoopId(turn);
        var memberId = ResolveMemberId(turn);
        var commuteProfiles = cloudStateStore.GetCommuteProfiles(loopId);
        var commute = !string.IsNullOrWhiteSpace(memberId)
            ? commuteProfiles.FirstOrDefault(profile =>
                profile.IsEnabled &&
                !string.IsNullOrWhiteSpace(profile.MemberId) &&
                string.Equals(profile.MemberId, memberId, StringComparison.OrdinalIgnoreCase))
            : null;

        commute ??= commuteProfiles.FirstOrDefault(profile => profile.IsEnabled);

        if (commute is null || !commute.IsComplete)
            return Task.FromResult<CommuteReportSnapshot?>(
                new CommuteReportSnapshot(string.Empty, string.Empty, 0, RequiresSetup: true));

        var now = DateTimeOffset.Now;
        var workTarget = ResolveWorkTarget(now, commute);
        var earlyTarget = ResolveEarlyCalendarTarget(loopId, now, workTarget);
        var arrivalTarget = earlyTarget ?? workTarget;
        var minutesUntilWork = (int)Math.Round((arrivalTarget - now).TotalMinutes);
        var durationMinutes = commute.TypicalDurationMinutes > 0 ? commute.TypicalDurationMinutes : 25;
        var extraMinutes = Math.Max(0, durationMinutes - Math.Max(0, minutesUntilWork));

        var summary = commute.Mode.Trim().ToLowerInvariant() switch
        {
            "walking" => "your walk to work",
            "transit" => "your trip to work by public transportation",
            "bicycling" => "your bike ride to work",
            _ => "your drive to work"
        };

        return Task.FromResult<CommuteReportSnapshot?>(
            new CommuteReportSnapshot(
                string.IsNullOrWhiteSpace(commute.DestinationName) ? "work" : commute.DestinationName.Trim(),
                summary,
                durationMinutes,
                commute.Mode,
                earlyTarget is not null,
                minutesUntilWork,
                extraMinutes));
    }

    private static DateTimeOffset ResolveWorkTarget(DateTimeOffset now, CommuteProfileRecord commute)
    {
        var localDate = now.Date;
        var workTime = new DateTimeOffset(
            localDate.Year,
            localDate.Month,
            localDate.Day,
            Math.Clamp(commute.WorkHour, 0, 23),
            Math.Clamp(commute.WorkMinute, 0, 59),
            0,
            now.Offset);

        return workTime;
    }

    private DateTimeOffset? ResolveEarlyCalendarTarget(
        string loopId,
        DateTimeOffset now,
        DateTimeOffset workTarget)
    {
        var today = DateOnly.FromDateTime(now.DateTime);
        DateTimeOffset? earliest = null;

        foreach (var calendarEvent in cloudStateStore.GetCalendarEvents(loopId)
                     .Where(static calendarEvent => calendarEvent.IsEnabled)
                     .Where(calendarEvent => calendarEvent.Date == today))
        {
            if (!TryParseTimeLabel(calendarEvent.TimeLabel, now, out var eventTime)) continue;
            if (eventTime >= workTarget) continue;
            if (earliest is null || eventTime < earliest)
                earliest = eventTime;
        }

        return earliest;
    }

    private static bool TryParseTimeLabel(string? timeLabel, DateTimeOffset now, out DateTimeOffset parsed)
    {
        parsed = default;
        if (string.IsNullOrWhiteSpace(timeLabel)) return false;

        var match = TimeLabelRegex.Match(timeLabel);
        if (!match.Success) return false;

        if (!int.TryParse(match.Groups["hour"].Value, out var hour)) return false;
        var minute = match.Groups["minute"].Success && int.TryParse(match.Groups["minute"].Value, out var parsedMinute)
            ? parsedMinute
            : 0;
        var period = match.Groups["period"].Value.ToLowerInvariant();

        hour %= 12;
        if (period.StartsWith("p", StringComparison.Ordinal) && hour < 12) hour += 12;
        if (period.StartsWith("a", StringComparison.Ordinal) && hour == 12) hour = 0;

        parsed = new DateTimeOffset(
            now.Year,
            now.Month,
            now.Day,
            hour,
            minute,
            0,
            now.Offset);
        return true;
    }

    private static string ResolveLoopId(TurnContext turn)
    {
        if (turn.Attributes.TryGetValue("loopId", out var loopValue) &&
            loopValue is not null &&
            !string.IsNullOrWhiteSpace(loopValue.ToString()))
            return loopValue.ToString()!.Trim();

        return "openjibo-default-loop";
    }

    private static string? ResolveMemberId(TurnContext turn)
    {
        if (turn.Attributes.TryGetValue("personId", out var personValue) &&
            personValue is not null &&
            !string.IsNullOrWhiteSpace(personValue.ToString()))
            return personValue.ToString()!.Trim();

        if (turn.Attributes.TryGetValue("speakerId", out var speakerValue) &&
            speakerValue is not null &&
            !string.IsNullOrWhiteSpace(speakerValue.ToString()))
            return speakerValue.ToString()!.Trim();

        return null;
    }
}