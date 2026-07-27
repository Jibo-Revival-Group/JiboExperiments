using Jibo.Cloud.Application.Abstractions;

namespace Jibo.Cloud.Application.Services;

internal static class PartOfDayCorrectionReplyBuilder
{
    internal static LegacyMimSelection BuildSelection(
        JiboExperienceCatalog catalog,
        IJiboRandomizer randomizer,
        JiboPartOfDay claimed,
        string? displayName,
        DateTimeOffset? referenceLocalTime = null)
    {
        var localTime = referenceLocalTime ?? DateTimeOffset.UtcNow;
        var context = new LegacyMimConditionEvaluator.Context(
            HolidayClaim: null,
            Holiday: null,
            CurrentDate: DateOnly.FromDateTime(localTime.LocalDateTime),
            PodClaim: claimed.ToClaimToken(),
            Pod: JiboPartOfDayExtensions.GetPartOfDay(localTime).ToPodToken(),
            HasSpeaker: !string.IsNullOrWhiteSpace(displayName));

        return LegacyMimReplySelector.Select(
            catalog.PartOfDayCorrectionReplies,
            randomizer,
            context,
            displayName,
            GetFallbackReply(claimed),
            "PartOfDayCorrection");
    }

    internal static string BuildReply(
        JiboExperienceCatalog catalog,
        IJiboRandomizer randomizer,
        JiboPartOfDay claimed,
        string? displayName,
        DateTimeOffset? referenceLocalTime = null)
    {
        return BuildSelection(catalog, randomizer, claimed, displayName, referenceLocalTime).ReplyText;
    }

    private static string GetFallbackReply(JiboPartOfDay claimed) =>
        claimed switch
        {
            JiboPartOfDay.Morning => "And a good morning to you, any time of day.",
            JiboPartOfDay.Afternoon => "Sure. I guess it is afternoon somewhere.",
            JiboPartOfDay.Evening => "I may be wrong, but I don't think it's evening.",
            _ => "Hello."
        };
}
