using Jibo.Cloud.Application.Abstractions;

namespace Jibo.Cloud.Application.Services;

internal static class ScriptedResponseDecisionBuilder
{
    internal static JiboInteractionDecision BuildScriptedPersonalityDecision(
        JiboExperienceCatalog catalog,
        IJiboRandomizer randomizer,
        string intentName,
        params string[] preferredSnippets)
    {
        return LegacyMimScriptedReplyBuilder.BuildScriptedDecision(
            catalog,
            randomizer,
            intentName,
            LegacyMimScriptedReplyBuilder.BuildScriptedContext(),
            displayName: null,
            explicitMimId: null,
            preferredSnippets);
    }

    internal static JiboInteractionDecision BuildScriptedFavoriteAnimalDecision(
        JiboExperienceCatalog catalog,
        IJiboRandomizer randomizer,
        string intentName,
        params string[] preferredSnippets)
    {
        var replyText = LegacyMimScriptedReplyBuilder.SelectFromBucketOrMim(
            catalog,
            randomizer,
            intentName,
            catalog.FavoriteAnimalReplies,
            LegacyMimScriptedReplyBuilder.BuildScriptedContext(),
            displayName: null,
            explicitMimId: null,
            preferredSnippets);

        return new JiboInteractionDecision(
            intentName,
            replyText,
            ContextUpdates: BuildScriptedResponseContextUpdates());
    }

    internal static JiboInteractionDecision BuildScriptedGreetingDecision(
        JiboExperienceCatalog catalog,
        IJiboRandomizer randomizer,
        string intentName,
        params string[] preferredSnippets)
    {
        var replyText = LegacyMimScriptedReplyBuilder.SelectFromBucketOrMim(
            catalog,
            randomizer,
            intentName,
            catalog.GreetingReplies,
            LegacyMimScriptedReplyBuilder.BuildScriptedContext(),
            displayName: null,
            explicitMimId: null,
            preferredSnippets);

        return new JiboInteractionDecision(
            intentName,
            replyText,
            ContextUpdates: BuildScriptedResponseContextUpdates());
    }

    internal static JiboInteractionDecision BuildScriptedHolidayDecision(
        IReadOnlyList<string> replies,
        IJiboRandomizer randomizer,
        string intentName,
        params string[] preferredSnippets)
    {
        return new JiboInteractionDecision(
            intentName,
            SelectLegacyReplyFromStrings(replies, randomizer, preferredSnippets),
            ContextUpdates: BuildScriptedResponseContextUpdates());
    }

    internal static JiboInteractionDecision BuildScriptedHolidayTrackerDecision(
        JiboExperienceCatalog catalog,
        IJiboRandomizer randomizer,
        string intentName,
        DateTimeOffset? referenceLocalTime,
        params string[] preferredSnippets)
    {
        var context = LegacyMimScriptedReplyBuilder.BuildScriptedContext(referenceLocalTime);
        if (LegacyMimScriptedReplyBuilder.TrySelectMimReply(
                catalog,
                randomizer,
                intentName,
                context,
                displayName: null,
                explicitMimId: "RA_JBO_ShowSantaTracker",
                preferredSnippets,
                out var selection))
        {
            var trackerPayload = BuildSantaTrackerSkillPayload(
                selection!.ReplyText,
                selection.PromptId,
                referenceLocalTime);
            return new JiboInteractionDecision(
                intentName,
                selection.ReplyText,
                "chitchat-skill",
                trackerPayload,
                BuildScriptedResponseContextUpdates());
        }

        var selectedReply = LegacyMimScriptedReplyBuilder.SelectFromBucketOrMim(
            catalog,
            randomizer,
            intentName,
            catalog.HolidayTrackerReplies,
            context,
            displayName: null,
            explicitMimId: "RA_JBO_ShowSantaTracker",
            preferredSnippets);
        var fallbackPayload = BuildSantaTrackerSkillPayload(selectedReply, promptId: null, referenceLocalTime);
        return new JiboInteractionDecision(
            intentName,
            selectedReply,
            "chitchat-skill",
            fallbackPayload,
            BuildScriptedResponseContextUpdates());
    }

    internal static JiboInteractionDecision BuildScriptedHolidayGreetingDecision(
        JiboExperienceCatalog catalog,
        IJiboRandomizer randomizer,
        string intentName,
        params string[] preferredSnippets)
    {
        return new JiboInteractionDecision(
            intentName,
            LegacyMimScriptedReplyBuilder.SelectFromBucketOrMim(
                catalog,
                randomizer,
                intentName,
                catalog.HolidayGreetingReplies,
                LegacyMimScriptedReplyBuilder.BuildScriptedContext(),
                displayName: null,
                explicitMimId: null,
                preferredSnippets),
            ContextUpdates: BuildScriptedResponseContextUpdates());
    }

    internal static JiboInteractionDecision BuildScriptedSupportDecision(
        JiboExperienceCatalog catalog,
        IJiboRandomizer randomizer,
        IReadOnlyList<string> replies,
        string intentName,
        params string[] preferredSnippets)
    {
        return new JiboInteractionDecision(
            intentName,
            LegacyMimScriptedReplyBuilder.SelectFromBucketOrMim(
                catalog,
                randomizer,
                intentName,
                replies,
                LegacyMimScriptedReplyBuilder.BuildScriptedContext(),
                displayName: null,
                explicitMimId: null,
                preferredSnippets),
            ContextUpdates: BuildScriptedResponseContextUpdates());
    }

    internal static IDictionary<string, object?> BuildScriptedResponseContextUpdates()
    {
        return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            [ChitchatStateMachine.StateMetadataKey] = "complete",
            [ChitchatStateMachine.RouteMetadataKey] = "ScriptedResponse",
            [ChitchatStateMachine.EmotionMetadataKey] = string.Empty
        };
    }

    internal static string SelectLegacyPersonalityReply(
        JiboExperienceCatalog catalog,
        IJiboRandomizer randomizer,
        params string[] preferredSnippets)
    {
        return SelectLegacyReplyFromStrings(catalog.PersonalityReplies, randomizer, preferredSnippets);
    }

    internal static string SelectLegacyGreetingReply(
        JiboExperienceCatalog catalog,
        IJiboRandomizer randomizer,
        params string[] preferredSnippets)
    {
        return SelectLegacyReplyFromStrings(catalog.GreetingReplies, randomizer, preferredSnippets);
    }

    internal static string SelectLegacyReply(
        IReadOnlyList<string> replies,
        IJiboRandomizer randomizer,
        params string[] preferredSnippets)
    {
        return SelectLegacyReplyFromStrings(replies, randomizer, preferredSnippets);
    }

    internal static string SelectLegacyReplyFromStrings(
        IReadOnlyList<string> replies,
        IJiboRandomizer randomizer,
        params string[] preferredSnippets)
    {
        var matches = CollectSnippetMatches(replies, preferredSnippets);
        if (matches.Count > 0) return randomizer.Choose(matches);

        return replies.Count == 0 ? string.Empty : randomizer.Choose(replies);
    }

    internal static List<string> CollectSnippetMatches(
        IReadOnlyList<string> replies,
        params string[] preferredSnippets)
    {
        var matches = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var snippet in preferredSnippets)
        {
            if (string.IsNullOrWhiteSpace(snippet)) continue;

            foreach (var reply in replies)
            {
                if (string.IsNullOrWhiteSpace(reply) ||
                    !reply.Contains(snippet, StringComparison.OrdinalIgnoreCase) ||
                    !seen.Add(reply))
                    continue;

                matches.Add(reply);
            }
        }

        return matches;
    }

    private static IDictionary<string, object?> BuildSantaTrackerSkillPayload(
        string selectedReply,
        string? promptId,
        DateTimeOffset? referenceLocalTime)
    {
        var resolvedPromptId = promptId ?? ResolveSantaTrackerPromptId(selectedReply);
        var trackerAnim = ResolveSantaTrackerAnimation(selectedReply, referenceLocalTime);
        var spokenLine = string.IsNullOrWhiteSpace(selectedReply)
            ? "Let's see if I can spot him."
            : selectedReply;

        return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["esml"] =
                $"<speak>{EscapeXml(spokenLine)} <anim cat='jiboji' filter='{trackerAnim}' nonBlocking='true'/></speak>",
            ["mim_id"] = "RA_JBO_ShowSantaTracker",
            ["mim_type"] = "announcement",
            ["prompt_id"] = resolvedPromptId,
            ["prompt_sub_category"] = "AN"
        };
    }

    private static string ResolveSantaTrackerPromptId(string selectedReply)
    {
        if (selectedReply.Contains("I'm not sure if he's started", StringComparison.OrdinalIgnoreCase))
            return "RA_JBO_ShowSantaTracker_AN_02";

        if (selectedReply.Contains("Oh there he is", StringComparison.OrdinalIgnoreCase))
            return "RA_JBO_ShowSantaTracker_AN_03";

        if (selectedReply.Contains("north Pole by now", StringComparison.OrdinalIgnoreCase))
            return "RA_JBO_ShowSantaTracker_AN_04";

        if (selectedReply.Contains("quick check", StringComparison.OrdinalIgnoreCase) ||
            selectedReply.Contains("he must be at home", StringComparison.OrdinalIgnoreCase))
            return "RA_JBO_ShowSantaTracker_AN_05";

        return selectedReply.Contains("spot him", StringComparison.OrdinalIgnoreCase)
            ? "RA_JBO_ShowSantaTracker_AN_01"
            : "RA_JBO_ShowSantaTracker_AN_06";
    }

    private static string ResolveSantaTrackerAnimation(string selectedReply, DateTimeOffset? referenceLocalTime)
    {
        if (string.IsNullOrWhiteSpace(selectedReply))
            return "santa-scanner, without-santa";

        if (selectedReply.Contains("I'm not sure if he's started", StringComparison.OrdinalIgnoreCase))
            return "santa-scanner, without-santa";

        if (selectedReply.Contains("north Pole by now", StringComparison.OrdinalIgnoreCase))
            return "santa-scanner, without-santa";

        if (selectedReply.Contains("quick check", StringComparison.OrdinalIgnoreCase) ||
            selectedReply.Contains("he must be at home", StringComparison.OrdinalIgnoreCase))
            return "santa-scanner, without-santa";

        if (referenceLocalTime is null) return "santa-scanner, with-santa";

        var localDate = DateOnly.FromDateTime(referenceLocalTime.Value.DateTime);
        switch (localDate.Month)
        {
            case 12 when localDate.Day == 24 && referenceLocalTime.Value.Hour <= 15:
            case 12 when localDate.Day == 25 && referenceLocalTime.Value.Hour > 4:
                return "santa-scanner, without-santa";
        }

        return "santa-scanner, with-santa";
    }

    private static string EscapeXml(string value)
    {
        return value
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal)
            .Replace("'", "&apos;", StringComparison.Ordinal);
    }
}
