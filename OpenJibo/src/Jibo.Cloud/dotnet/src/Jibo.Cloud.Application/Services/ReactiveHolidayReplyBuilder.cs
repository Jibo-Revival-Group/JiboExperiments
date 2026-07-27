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

        if (isHolidayToday)
        {
            var reply = SelectReply(
                catalog.HolidayResponseReplies,
                randomizer,
                new LegacyMimConditionEvaluator.Context(holidayClaim, holidayClaim, currentDate),
                GetHolidayResponseFallback(holidayClaim));

            return new JiboInteractionDecision(
                intentName,
                reply,
                ContextUpdates: BuildContextUpdates("HolidayResponse"));
        }

        var notHolidayReply = SelectReply(
            catalog.NotHolidayReplies,
            randomizer,
            new LegacyMimConditionEvaluator.Context(holidayClaim, holidayClaim, currentDate),
            GetNotHolidayFallback(holidayClaim));

        return new JiboInteractionDecision(
            intentName,
            notHolidayReply,
            ContextUpdates: BuildContextUpdates("NotHoliday"));
    }

    private static string SelectReply(
        IReadOnlyList<JiboConditionedReply> replies,
        IJiboRandomizer randomizer,
        LegacyMimConditionEvaluator.Context context,
        string fallback)
    {
        var matchingReplies = replies
            .Where(reply => LegacyMimConditionEvaluator.Matches(reply.Condition, context))
            .ToArray();

        if (matchingReplies.Length == 0) return fallback;

        return ChooseWeighted(randomizer, matchingReplies);
    }

    private static string ChooseWeighted(IJiboRandomizer randomizer, IReadOnlyList<JiboConditionedReply> replies)
    {
        var maxWeight = replies.Max(reply => reply.Weight <= 0 ? 0.1 : reply.Weight);
        var topTier = replies
            .Where(reply => Math.Abs((reply.Weight <= 0 ? 0.1 : reply.Weight) - maxWeight) < 0.001)
            .Select(reply => reply.Reply)
            .Where(reply => !string.IsNullOrWhiteSpace(reply))
            .ToArray();

        return topTier.Length == 0
            ? string.Empty
            : randomizer.Choose(topTier);
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
