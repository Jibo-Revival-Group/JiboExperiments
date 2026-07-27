using Jibo.Cloud.Application.Abstractions;

namespace Jibo.Cloud.Application.Services;

internal static class ReactiveHolidayReplyBuilder
{
    internal static JiboInteractionDecision BuildDecision(
        JiboExperienceCatalog catalog,
        IJiboRandomizer randomizer,
        string loweredTranscript,
        DateTimeOffset? referenceLocalTime,
        IReadOnlyList<string> todaysHolidayNames,
        string intentName)
    {
        if (!JiboHolidayGreeting.TryExtractHolidayClaim(loweredTranscript, out var holidayClaim))
            holidayClaim = null;

        var currentDate = DateOnly.FromDateTime((referenceLocalTime ?? DateTimeOffset.UtcNow).LocalDateTime);
        var isHolidayToday = JiboHolidayGreeting.IsClaimedHolidayToday(holidayClaim, todaysHolidayNames);
        var context = new LegacyMimConditionEvaluator.Context(
            holidayClaim,
            holidayClaim,
            currentDate);

        if (isHolidayToday)
        {
            var selection = LegacyMimReplySelector.Select(
                catalog.HolidayResponseReplies,
                randomizer,
                context,
                displayName: null,
                GetHolidayResponseFallback(holidayClaim),
                "HolidayResponse");

            return new JiboInteractionDecision(
                intentName,
                selection.ReplyText,
                SkillPayload: LegacyMimDecisionMetadata.BuildSkillPayload(selection, "HolidayResponse"),
                ContextUpdates: LegacyMimDecisionMetadata.ApplyEmotion(
                    BuildContextUpdates("HolidayResponse"),
                    selection.Emotion));
        }

        var notHolidaySelection = LegacyMimReplySelector.Select(
            catalog.NotHolidayReplies,
            randomizer,
            context,
            displayName: null,
            GetNotHolidayFallback(holidayClaim),
            "NotHoliday");

        return new JiboInteractionDecision(
            intentName,
            notHolidaySelection.ReplyText,
            SkillPayload: LegacyMimDecisionMetadata.BuildSkillPayload(notHolidaySelection, "NotHoliday"),
            ContextUpdates: LegacyMimDecisionMetadata.ApplyEmotion(
                BuildContextUpdates("NotHoliday"),
                notHolidaySelection.Emotion));
    }

    private static string GetHolidayResponseFallback(string? holidayClaim) =>
        string.Equals(holidayClaim, "Christmas", StringComparison.OrdinalIgnoreCase)
            ? "Thank you, Merry Christmas to you too."
            : "Thank you. Same to you.";

    private static string GetNotHolidayFallback(string? holidayClaim) =>
        string.Equals(holidayClaim, "Christmas", StringComparison.OrdinalIgnoreCase)
            ? "Um, unless my calendar is off, it isn't Christmastime."
            : "Sorry, I don't think that's today.";

    private static IDictionary<string, object?> BuildContextUpdates(string route)
    {
        return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            [ChitchatStateMachine.StateMetadataKey] = "complete",
            [ChitchatStateMachine.RouteMetadataKey] = route,
            [ChitchatStateMachine.EmotionMetadataKey] = string.Empty
        };
    }
}
