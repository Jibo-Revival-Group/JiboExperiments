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

        var nextOccurrence = target.ResolveNextOccurrence(referenceDate);
        var daysUntil = HolidayDateCalculator.CountDaysUntil(referenceDate, nextOccurrence);
        return new JiboInteractionDecision(
            "countdown",
            CountdownSpokenReplyFormatter.Format(target.Label, daysUntil));
    }
}
