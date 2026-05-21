using System.Collections.Generic;
using System.Globalization;
using Jibo.Cloud.Application.Abstractions;
using Jibo.Cloud.Domain.Models;
using Jibo.Runtime.Abstractions;

namespace Jibo.Cloud.Application.Services;

public sealed partial class JiboInteractionService
{
    private static JiboInteractionDecision BuildRobotAgeDecision(DateTimeOffset? referenceLocalTime)
    {
        var referenceDate = DateOnly.FromDateTime((referenceLocalTime ?? DateTimeOffset.UtcNow).Date);
        var ageDescription = DescribePersonaAge(referenceDate, OpenJiboCloudBuildInfo.PersonaBirthday);
        return new JiboInteractionDecision(
            "robot_age",
            $"I count {OpenJiboCloudBuildInfo.PersonaBirthdayWords} as my birthday, so I am {ageDescription}.");
    }

    private static JiboInteractionDecision BuildRobotBirthdayDecision()
    {
        return new JiboInteractionDecision(
            "robot_birthday",
            $"My birthday is {OpenJiboCloudBuildInfo.PersonaBirthdayWords}.");
    }

    private static JiboInteractionDecision BuildTriggerIgnoredDecision()
    {
        return new JiboInteractionDecision(
            "trigger_ignored",
            string.Empty,
            "chitchat-skill",
            new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["skillId"] = "chitchat-skill",
                ["cloudResponseMode"] = "completion_only"
            });
    }

    private JiboInteractionDecision BuildReactiveGreetingDecision(
        TurnContext turn,
        string greetingIntent,
        DateTimeOffset? referenceLocalTime)
    {
        var presence = ResolveGreetingPresenceProfile(turn);
        var displayName = ResolvePreferredGreetingName(turn, presence);
        var replyText = BuildReactiveGreetingReply(greetingIntent, displayName, referenceLocalTime);
        RecordGreetingPresence(turn, presence, "ReactiveGreeting", greetingIntent, displayName, proactive: false);
        return new JiboInteractionDecision(
            greetingIntent,
            replyText,
            ContextUpdates: BuildGreetingContextUpdates("ReactiveGreeting", presence.PrimaryPersonId, false));
    }

    private JiboInteractionDecision BuildProactiveGreetingDecision(
        TurnContext turn,
        GreetingPresenceProfile presence,
        DateTimeOffset? referenceLocalTime)
    {
        var displayName = ResolvePreferredGreetingName(turn, presence);
        var greetingPrefix = ResolveTimeOfDayGreetingPrefix(referenceLocalTime);
        var replyText = string.IsNullOrWhiteSpace(displayName)
            ? $"{greetingPrefix}. I am glad to see you."
            : $"{greetingPrefix}, {displayName}. Welcome back.";
        RecordGreetingPresence(turn, presence, "ProactiveGreeting", "proactive_greeting", displayName, proactive: true);
        return new JiboInteractionDecision(
            "proactive_greeting",
            replyText,
            ContextUpdates: BuildGreetingContextUpdates("ProactiveGreeting", presence.PrimaryPersonId, true));
    }

    private static string BuildReactiveGreetingReply(
        string greetingIntent,
        string? displayName,
        DateTimeOffset? referenceLocalTime)
    {
        var namePrefix = string.IsNullOrWhiteSpace(displayName)
            ? string.Empty
            : $", {displayName}";

        return greetingIntent switch
        {
            "good_morning" => $"Good morning{namePrefix}. It is great to see you.",
            "good_afternoon" => $"Good afternoon{namePrefix}. I am glad you are here.",
            "good_evening" => $"Good evening{namePrefix}. It is nice to have you back.",
            "good_night" => $"Good night{namePrefix}. Sleep well.",
            "welcome_back" => string.IsNullOrWhiteSpace(displayName)
                ? $"Welcome back. {ResolveTimeOfDayGreetingPrefix(referenceLocalTime)}."
                : $"Welcome back, {displayName}. {ResolveTimeOfDayGreetingPrefix(referenceLocalTime)}.",
            _ => $"Hello{namePrefix}. It is nice to see you."
        };
    }

    private string? ResolvePreferredGreetingName(TurnContext turn, GreetingPresenceProfile presence)
    {
        var rememberedName = personalMemoryStore.GetName(ResolveTenantScope(turn, presence.PrimaryPersonId));
        if (!string.IsNullOrWhiteSpace(rememberedName)) return ToDisplayName(rememberedName);

        var tenantRememberedName = personalMemoryStore.GetName(ResolveTenantScope(turn));
        if (!string.IsNullOrWhiteSpace(tenantRememberedName)) return ToDisplayName(tenantRememberedName);

        if (!string.IsNullOrWhiteSpace(presence.PrimaryPersonId) &&
            presence.LoopUserFirstNames.TryGetValue(presence.PrimaryPersonId, out var firstName) &&
            !string.IsNullOrWhiteSpace(firstName))
            return ToDisplayName(firstName);

        return null;
    }

    private static string ToDisplayName(string value)
    {
        var trimmed = value.Trim();
        return string.IsNullOrWhiteSpace(trimmed)
            ? string.Empty
            : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(trimmed);
    }

    private bool ShouldHandleProactiveGreetingTrigger(
        TurnContext turn,
        string? triggerSource,
        GreetingPresenceProfile presence)
    {
        if (string.Equals(triggerSource, "SURPRISE", StringComparison.OrdinalIgnoreCase)) return false;

        if (!presence.HasKnownIdentity) return false;

        var lastGreetingUtc = ReadGreetingHistoryLastGreetedUtc(turn, presence);
        return !lastGreetingUtc.HasValue || DateTimeOffset.UtcNow - lastGreetingUtc.Value >= ProactiveGreetingCooldown;
    }

    private DateTimeOffset? ReadGreetingHistoryLastGreetedUtc(TurnContext turn, GreetingPresenceProfile presence)
    {
        var historyIdentity = ResolveGreetingHistoryIdentity(presence);
        if (!string.IsNullOrWhiteSpace(historyIdentity) && cloudStateStore is not null)
        {
            var loopId = ReadTenantAttribute(turn, "loopId") ?? "openjibo-default-loop";
            var greetingHistory = cloudStateStore.GetGreetingPresences(loopId)
                .FirstOrDefault(record =>
                    record.PersonId.Equals(historyIdentity, StringComparison.OrdinalIgnoreCase));
            if (greetingHistory is not null && greetingHistory.LastGreetedUtc.HasValue)
                return greetingHistory.LastGreetedUtc;
        }

        return ReadTimestampAttribute(turn, LastProactiveGreetingUtcMetadataKey);
    }

    private static string? ResolveGreetingHistoryIdentity(GreetingPresenceProfile presence)
    {
        if (!string.IsNullOrWhiteSpace(presence.PrimaryPersonId)) return presence.PrimaryPersonId;
        return !string.IsNullOrWhiteSpace(presence.SpeakerId) ? presence.SpeakerId : null;
    }

    private static DateTimeOffset? ReadTimestampAttribute(TurnContext turn, string key)
    {
        if (!turn.Attributes.TryGetValue(key, out var value) || value is null) return null;

        return DateTimeOffset.TryParse(
            value.ToString(),
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out var parsed)
            ? parsed
            : null;
    }

    private static IDictionary<string, object?> BuildGreetingContextUpdates(string route, string? speakerId,
        bool proactive)
    {
        var updates = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            [ChitchatStateMachine.StateMetadataKey] = "complete",
            [ChitchatStateMachine.RouteMetadataKey] = "ScriptedResponse",
            [ChitchatStateMachine.EmotionMetadataKey] = string.Empty,
            [GreetingRouteMetadataKey] = route,
            [GreetingSpeakerMetadataKey] = speakerId ?? string.Empty,
            [proactive ? LastProactiveGreetingUtcMetadataKey : LastReactiveGreetingUtcMetadataKey] = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture)
        };

        return updates;
    }

    private void RecordGreetingPresence(
        TurnContext turn,
        GreetingPresenceProfile presence,
        string route,
        string intentName,
        string? preferredName,
        bool proactive)
    {
        if (cloudStateStore is null) return;

        var identityId = ResolveGreetingHistoryIdentity(presence);
        if (string.IsNullOrWhiteSpace(identityId)) return;

        var now = DateTimeOffset.UtcNow;
        var tenantScope = ResolveTenantScope(turn, identityId);
        cloudStateStore.UpsertGreetingPresence(new GreetingPresenceRecord
        {
            AccountId = tenantScope.AccountId,
            LoopId = tenantScope.LoopId,
            PersonId = identityId,
            SpeakerId = presence.SpeakerId,
            PreferredName = preferredName,
            LastSeenUtc = now,
            LastGreetedUtc = now,
            LastGreetingRoute = route,
            LastGreetingIntent = intentName
        });
    }

    private static string ResolveTimeOfDayGreetingPrefix(DateTimeOffset? referenceLocalTime)
    {
        var hour = (referenceLocalTime ?? DateTimeOffset.UtcNow).Hour;
        return hour switch
        {
            >= 5 and < 12 => "Good morning",
            >= 12 and < 17 => "Good afternoon",
            _ => "Good evening"
        };
    }

    private JiboInteractionDecision BuildPizzaDecision()
    {
        return BuildPizzaAnimationDecision("pizza", "One pizza, coming right up.");
    }

    private JiboInteractionDecision BuildPizzaAnimationDecision(string intentName, string replyText)
    {
        var prompt = randomizer.Choose(PizzaMimPrompts);
        return new JiboInteractionDecision(
            intentName,
            replyText,
            "chitchat-skill",
            new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["esml"] = prompt.Esml,
                ["mim_id"] = "RA_JBO_MakePizza",
                ["mim_type"] = "announcement",
                ["prompt_id"] = prompt.PromptId,
                ["prompt_sub_category"] = "AN"
            });
    }

    private JiboInteractionDecision BuildProactivePizzaDayDecision(DateTimeOffset? referenceLocalTime)
    {
        var referenceDate = (referenceLocalTime ?? DateTimeOffset.UtcNow).Date;
        return BuildPizzaAnimationDecision(
            "proactive_pizza_day",
            $"Happy National Pizza Day for {referenceDate.ToString("MMMM d", CultureInfo.InvariantCulture)}. One pizza, coming right up.");
    }

    private JiboInteractionDecision BuildProactivePizzaPreferenceDecision()
    {
        return BuildPizzaAnimationDecision(
            "proactive_pizza_preference",
            "You mentioned pizza is a favorite, so I thought we should make one.");
    }

    private static JiboInteractionDecision BuildProactivePizzaFactOfferDecision()
    {
        var listenContexts = new[] { "shared/yes_no" };
        return new JiboInteractionDecision(
            "proactive_offer_pizza_fact",
            "Do you want to hear a fun pizza fact?",
            "chitchat-skill",
            new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["mim_id"] = "runtime-chat",
                ["mim_type"] = "question",
                ["prompt_id"] = "RUNTIME_PROMPT",
                ["prompt_sub_category"] = "Q",
                ["listen_contexts"] = listenContexts
            });
    }

    private static JiboInteractionDecision BuildProactivePizzaFactDecision()
    {
        return new JiboInteractionDecision(
            "proactive_pizza_fact",
            "Americans consume about 100 acres of pizza every day, roughly 350 slices per second. That's a lot of pizza.");
    }

    private JiboInteractionDecision BuildProactiveFunFactDecision(JiboExperienceCatalog catalog)
    {
        var categories = new List<ProactiveFactCategory>();
        AddProactiveFactCategory(categories, "fun_fact", catalog.FunFacts);
        AddProactiveFactCategory(categories, "robot_fact", catalog.RobotFacts);
        AddProactiveFactCategory(categories, "human_fact", catalog.HumanFacts);

        if (categories.Count == 0)
            return new JiboInteractionDecision("proactive_fun_fact", randomizer.Choose(catalog.SurpriseReplies));

        var selectedCategory = randomizer.Choose(categories);
        var fact = randomizer.Choose(selectedCategory.Replies);
        return new JiboInteractionDecision(
            "proactive_fun_fact",
            fact,
            "chitchat-skill",
            new Dictionary<string, object?>
            {
                ["mim_id"] = "runtime-fun-fact",
                ["mim_type"] = "announcement",
                ["prompt_id"] = "RUNTIME_FUN_FACT",
                ["replyType"] = "fun_fact",
                ["factCategory"] = selectedCategory.CategoryName
            });
    }

    private static void AddProactiveFactCategory(
        ICollection<ProactiveFactCategory> categories,
        string categoryName,
        IReadOnlyList<string> replies)
    {
        if (replies.Count == 0) return;

        categories.Add(new ProactiveFactCategory(categoryName, replies));
    }

    private JiboInteractionDecision BuildProactiveJokeDecision(JiboExperienceCatalog catalog)
    {
        return new JiboInteractionDecision(
            "proactive_joke",
            randomizer.Choose(catalog.Jokes),
            "@be/joke",
            new Dictionary<string, object?>
            {
                ["replyType"] = "joke"
            });
    }

    private static JiboInteractionDecision BuildProactiveOfferDeclinedDecision()
    {
        return new JiboInteractionDecision(
            "proactive_offer_declined",
            "No problem. We can save the pizza fact for another time.");
    }

    private string BuildGenericReply(JiboExperienceCatalog catalog, string transcript, string lowered)
    {
        if (string.IsNullOrWhiteSpace(transcript)) return "I am listening.";

        if (lowered.Contains("good morning", StringComparison.Ordinal))
            return "Good morning! It is nice to hear your voice.";

        if (lowered.Contains("good afternoon", StringComparison.Ordinal))
            return "Good afternoon. I am happy to be here.";

        return lowered.Contains("good night", StringComparison.Ordinal)
            ? "Good night. Sleep tight."
            : randomizer.Choose(catalog.GenericFallbackReplies)
                .Replace("{transcript}", transcript, StringComparison.Ordinal);
    }

    private JiboInteractionDecision BuildScriptedPersonalityDecision(
        JiboExperienceCatalog catalog,
        string intentName,
        params string[] preferredSnippets)
    {
        return ScriptedResponseDecisionBuilder.BuildScriptedPersonalityDecision(
            catalog,
            randomizer,
            intentName,
            preferredSnippets);
    }

    private JiboInteractionDecision BuildScriptedFavoriteAnimalDecision(
        JiboExperienceCatalog catalog,
        string intentName,
        params string[] preferredSnippets)
    {
        return ScriptedResponseDecisionBuilder.BuildScriptedFavoriteAnimalDecision(
            catalog,
            randomizer,
            intentName,
            preferredSnippets);
    }

    private JiboInteractionDecision BuildScriptedFriendDecision(
        JiboExperienceCatalog catalog,
        string intentName,
        params string[] preferredSnippets)
    {
        return new JiboInteractionDecision(
            intentName,
            SelectLegacyReply(catalog.FriendReplies, preferredSnippets),
            ContextUpdates: ScriptedResponseDecisionBuilder.BuildScriptedResponseContextUpdates());
    }

    private JiboInteractionDecision BuildScriptedBestFriendDecision(
        JiboExperienceCatalog catalog,
        string intentName,
        params string[] preferredSnippets)
    {
        return new JiboInteractionDecision(
            intentName,
            SelectLegacyReply(catalog.BestFriendReplies, preferredSnippets),
            ContextUpdates: ScriptedResponseDecisionBuilder.BuildScriptedResponseContextUpdates());
    }

    private JiboInteractionDecision BuildScriptedSingDecision(
        JiboExperienceCatalog catalog,
        string intentName,
        params string[] preferredSnippets)
    {
        return new JiboInteractionDecision(
            intentName,
            SelectLegacyReply(catalog.SingReplies, preferredSnippets),
            ContextUpdates: ScriptedResponseDecisionBuilder.BuildScriptedResponseContextUpdates());
    }

    private JiboInteractionDecision BuildScriptedHolidaySingDecision(
        JiboExperienceCatalog catalog,
        string intentName,
        params string[] preferredSnippets)
    {
        return new JiboInteractionDecision(
            intentName,
            SelectLegacyReply(catalog.HolidaySingReplies, preferredSnippets),
            ContextUpdates: ScriptedResponseDecisionBuilder.BuildScriptedResponseContextUpdates());
    }

    private JiboInteractionDecision BuildScriptedGreetingDecision(
        JiboExperienceCatalog catalog,
        string intentName,
        params string[] preferredSnippets)
    {
        return ScriptedResponseDecisionBuilder.BuildScriptedGreetingDecision(
            catalog,
            randomizer,
            intentName,
            preferredSnippets);
    }

    private JiboInteractionDecision BuildScriptedHolidayDecision(
        IReadOnlyList<string> replies,
        string intentName,
        params string[] preferredSnippets)
    {
        return ScriptedResponseDecisionBuilder.BuildScriptedHolidayDecision(
            replies,
            randomizer,
            intentName,
            preferredSnippets);
    }

    private JiboInteractionDecision BuildScriptedHolidayTrackerDecision(
        JiboExperienceCatalog catalog,
        string intentName,
        params string[] preferredSnippets)
    {
        return ScriptedResponseDecisionBuilder.BuildScriptedHolidayTrackerDecision(
            catalog,
            randomizer,
            intentName,
            preferredSnippets);
    }

    private JiboInteractionDecision BuildScriptedHolidayGreetingDecision(
        JiboExperienceCatalog catalog,
        string intentName,
        params string[] preferredSnippets)
    {
        return ScriptedResponseDecisionBuilder.BuildScriptedHolidayGreetingDecision(
            catalog,
            randomizer,
            intentName,
            preferredSnippets);
    }

    private JiboInteractionDecision BuildScriptedHolidayTemplateDecision(
        TurnContext turn,
        GreetingPresenceProfile presence,
        JiboExperienceCatalog catalog,
        string intentName,
        params string[] preferredSnippets)
    {
        var selected = ScriptedResponseDecisionBuilder.SelectLegacyReply(
            catalog.HolidayReplies,
            randomizer,
            preferredSnippets);
        return new JiboInteractionDecision(
            intentName,
            RenderHolidayTemplate(selected, turn, presence),
            ContextUpdates: ScriptedResponseDecisionBuilder.BuildScriptedResponseContextUpdates());
    }

    private string SelectLegacyPersonalityReply(JiboExperienceCatalog catalog, params string[] preferredSnippets)
    {
        return ScriptedResponseDecisionBuilder.SelectLegacyPersonalityReply(catalog, randomizer, preferredSnippets);
    }

    private string SelectLegacyGreetingReply(JiboExperienceCatalog catalog, params string[] preferredSnippets)
    {
        return ScriptedResponseDecisionBuilder.SelectLegacyGreetingReply(catalog, randomizer, preferredSnippets);
    }

    private string SelectLegacyReply(IReadOnlyList<string> replies, params string[] preferredSnippets)
    {
        return ScriptedResponseDecisionBuilder.SelectLegacyReply(replies, randomizer, preferredSnippets);
    }

    private string RenderHolidayTemplate(string template, TurnContext turn, GreetingPresenceProfile presence)
    {
        var ownerName = ResolvePreferredGreetingName(turn, presence);
        var speakerName = !string.IsNullOrWhiteSpace(ownerName) ? ownerName : "you";
        return template
            .Replace("${speaker}'s", $"{speakerName}'s", StringComparison.OrdinalIgnoreCase)
            .Replace("${speaker}", speakerName, StringComparison.OrdinalIgnoreCase)
            .Replace("${loop.owner}", string.IsNullOrWhiteSpace(ownerName) ? string.Empty : ownerName,
                StringComparison.OrdinalIgnoreCase)
            .Replace("  ", " ", StringComparison.Ordinal)
            .Trim();
    }
}
