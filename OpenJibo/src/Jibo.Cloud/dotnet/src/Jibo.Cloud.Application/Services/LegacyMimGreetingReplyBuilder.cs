using Jibo.Cloud.Application.Abstractions;

namespace Jibo.Cloud.Application.Services;

internal static class LegacyMimGreetingReplyBuilder
{
    internal static LegacyMimSelection BuildReactiveGreeting(
        JiboExperienceCatalog catalog,
        IJiboRandomizer randomizer,
        string greetingIntent,
        string? displayName,
        DateTimeOffset? referenceLocalTime)
    {
        var localTime = referenceLocalTime ?? DateTimeOffset.UtcNow;
        var pod = JiboPartOfDayExtensions.GetPartOfDay(localTime).ToPodToken();
        var podClaim = ResolvePodClaim(greetingIntent, pod);
        var context = new LegacyMimConditionEvaluator.Context(
            HolidayClaim: null,
            Holiday: null,
            CurrentDate: DateOnly.FromDateTime(localTime.LocalDateTime),
            PodClaim: podClaim,
            Pod: pod,
            HasSpeaker: !string.IsNullOrWhiteSpace(displayName));

        return LegacyMimReplySelector.Select(
            catalog.ReactiveGreetingReplies,
            randomizer,
            context,
            displayName,
            GetReactiveGreetingFallback(greetingIntent, displayName),
            "ReactiveGreeting");
    }

    internal static LegacyMimSelection BuildWhatsUp(
        JiboExperienceCatalog catalog,
        IJiboRandomizer randomizer,
        string? displayName,
        DateTimeOffset? referenceLocalTime)
    {
        var context = BuildBasicContext(referenceLocalTime, displayName);
        return LegacyMimReplySelector.Select(
            catalog.WhatsUpReplies,
            randomizer,
            context,
            displayName,
            "Not much. Just being Jibo.",
            "WhatsUpResp");
    }

    internal static LegacyMimSelection BuildGoodbye(
        JiboExperienceCatalog catalog,
        IJiboRandomizer randomizer,
        string? displayName,
        DateTimeOffset? referenceLocalTime)
    {
        var context = BuildBasicContext(referenceLocalTime, displayName);
        return LegacyMimReplySelector.Select(
            catalog.GoodbyeReplies,
            randomizer,
            context,
            displayName,
            "Goodbye. Break a leg.",
            "GoodbyeRespCM");
    }

    private static LegacyMimConditionEvaluator.Context BuildBasicContext(
        DateTimeOffset? referenceLocalTime,
        string? displayName)
    {
        var localTime = referenceLocalTime ?? DateTimeOffset.UtcNow;
        var pod = JiboPartOfDayExtensions.GetPartOfDay(localTime).ToPodToken();
        return new LegacyMimConditionEvaluator.Context(
            HolidayClaim: null,
            Holiday: null,
            CurrentDate: DateOnly.FromDateTime(localTime.LocalDateTime),
            PodClaim: pod,
            Pod: pod,
            HasSpeaker: !string.IsNullOrWhiteSpace(displayName));
    }

    private static string? ResolvePodClaim(string greetingIntent, string pod)
    {
        if (JiboPartOfDayExtensions.TryGetClaimedPartOfDay(greetingIntent, out var claimed))
            return claimed.ToClaimToken();

        return greetingIntent switch
        {
            "good_night" => "night",
            _ => pod
        };
    }

    private static string GetReactiveGreetingFallback(string greetingIntent, string? displayName)
    {
        var namePrefix = string.IsNullOrWhiteSpace(displayName) ? string.Empty : $", {displayName}";

        return greetingIntent switch
        {
            "good_morning" => $"Good morning{namePrefix}. It is great to see you.",
            "good_afternoon" => $"Good afternoon{namePrefix}. I am glad you are here.",
            "good_evening" => $"Good evening{namePrefix}. It is nice to have you back.",
            "good_night" => $"Good night{namePrefix}. Sleep well.",
            "welcome_back" => string.IsNullOrWhiteSpace(displayName)
                ? "Welcome back. It is good to see you."
                : $"Welcome back, {displayName}. It is good to see you.",
            _ => $"Hello{namePrefix}. It is nice to see you."
        };
    }
}
