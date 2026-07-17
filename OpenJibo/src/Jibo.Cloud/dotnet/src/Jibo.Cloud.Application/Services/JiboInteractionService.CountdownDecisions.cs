namespace Jibo.Cloud.Application.Services;

public sealed partial class JiboInteractionService
{
    private JiboInteractionDecision BuildCountdownDecision(string transcript, DateTimeOffset? referenceLocalTime)
    {
        if (holidayCountdownCatalog is null || !HowLongUntilCommandParser.TryParse(transcript, out var targetPhrase))
            return new JiboInteractionDecision("countdown", CountdownSpokenReplyFormatter.FormatUnresolvedTarget());

        var referenceDate = DateOnly.FromDateTime(referenceLocalTime?.DateTime ?? DateTime.UtcNow);
        var resolver = new CountdownTargetResolver(holidayCountdownCatalog);
        if (!resolver.TryResolve(targetPhrase, out var target))
            return new JiboInteractionDecision("countdown", CountdownSpokenReplyFormatter.FormatUnresolvedTarget());

        // Holidays the on-robot clock skill knows: launch @be/clock (same path as time/day).
        // Cloud chitchat speak is unreliable when nimbus is wedged; clock is local and works.
        if (target.Rule is not null &&
            ClockHolidayIdMapper.TryMap(target.Label, out var holidayId))
        {
            return BuildClockLaunchDecision(
                "countdown",
                "clock",
                "whenIsHoliday",
                $"Checking how long until {target.Label}.",
                holidayId);
        }

        var nextOccurrence = target.ResolveNextOccurrence(referenceDate);
        var daysUntil = HolidayDateCalculator.CountDaysUntil(referenceDate, nextOccurrence);
        return new JiboInteractionDecision(
            "countdown",
            CountdownSpokenReplyFormatter.Format(target.Label, daysUntil));
    }
}
