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
        return new JiboInteractionDecision(
            intentName,
            SelectLegacyPersonalityReply(catalog, randomizer, preferredSnippets),
            ContextUpdates: BuildScriptedResponseContextUpdates());
    }

    internal static JiboInteractionDecision BuildScriptedFavoriteAnimalDecision(
        JiboExperienceCatalog catalog,
        IJiboRandomizer randomizer,
        string intentName,
        params string[] preferredSnippets)
    {
        return new JiboInteractionDecision(
            intentName,
            SelectLegacyReply(catalog.FavoriteAnimalReplies, randomizer, preferredSnippets),
            ContextUpdates: BuildScriptedResponseContextUpdates());
    }

    internal static JiboInteractionDecision BuildScriptedGreetingDecision(
        JiboExperienceCatalog catalog,
        IJiboRandomizer randomizer,
        string intentName,
        params string[] preferredSnippets)
    {
        return new JiboInteractionDecision(
            intentName,
            SelectLegacyGreetingReply(catalog, randomizer, preferredSnippets),
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
            SelectLegacyReply(replies, randomizer, preferredSnippets),
            ContextUpdates: BuildScriptedResponseContextUpdates());
    }

    internal static JiboInteractionDecision BuildScriptedHolidayTrackerDecision(
        JiboExperienceCatalog catalog,
        IJiboRandomizer randomizer,
        string intentName,
        DateTimeOffset? referenceLocalTime,
        params string[] preferredSnippets)
    {
        var selectedReply = SelectLegacyReply(catalog.HolidayTrackerReplies, randomizer, preferredSnippets);
        var trackerPayload = BuildSantaTrackerSkillPayload(selectedReply, referenceLocalTime);
        return new JiboInteractionDecision(
            intentName,
            selectedReply,
            "chitchat-skill",
            trackerPayload,
            ContextUpdates: BuildScriptedResponseContextUpdates());
    }

    internal static JiboInteractionDecision BuildScriptedHolidayGreetingDecision(
        JiboExperienceCatalog catalog,
        IJiboRandomizer randomizer,
        string intentName,
        params string[] preferredSnippets)
    {
        return new JiboInteractionDecision(
            intentName,
            SelectLegacyReply(catalog.HolidayGreetingReplies, randomizer, preferredSnippets),
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
        foreach (var snippet in preferredSnippets)
        {
            if (string.IsNullOrWhiteSpace(snippet)) continue;

            var match = catalog.PersonalityReplies.FirstOrDefault(reply =>
                reply.Contains(snippet, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(match)) return match;
        }

        return catalog.PersonalityReplies.Count == 0 ? string.Empty : randomizer.Choose(catalog.PersonalityReplies);
    }

    internal static string SelectLegacyGreetingReply(
        JiboExperienceCatalog catalog,
        IJiboRandomizer randomizer,
        params string[] preferredSnippets)
    {
        foreach (var snippet in preferredSnippets)
        {
            if (string.IsNullOrWhiteSpace(snippet)) continue;

            var match = catalog.GreetingReplies.FirstOrDefault(reply =>
                reply.Contains(snippet, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(match)) return match;
        }

        return catalog.GreetingReplies.Count == 0 ? string.Empty : randomizer.Choose(catalog.GreetingReplies);
    }

    internal static string SelectLegacyReply(
        IReadOnlyList<string> replies,
        IJiboRandomizer randomizer,
        params string[] preferredSnippets)
    {
        foreach (var snippet in preferredSnippets)
        {
            if (string.IsNullOrWhiteSpace(snippet)) continue;

            var match = replies.FirstOrDefault(reply =>
                reply.Contains(snippet, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(match)) return match;
        }

        return replies.Count == 0 ? string.Empty : randomizer.Choose(replies);
    }

    private static IDictionary<string, object?> BuildSantaTrackerSkillPayload(
        string selectedReply,
        DateTimeOffset? referenceLocalTime)
    {
        var promptId = ResolveSantaTrackerPromptId(selectedReply);
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
            ["prompt_id"] = promptId,
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

        if (selectedReply.Contains("spot him", StringComparison.OrdinalIgnoreCase))
            return "RA_JBO_ShowSantaTracker_AN_01";

        return "RA_JBO_ShowSantaTracker_AN_06";
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

        if (referenceLocalTime is not null)
        {
            var localDate = DateOnly.FromDateTime(referenceLocalTime.Value.DateTime);
            if (localDate.Month == 12 && localDate.Day == 24 && referenceLocalTime.Value.Hour <= 15)
                return "santa-scanner, without-santa";

            if (localDate.Month == 12 && localDate.Day == 25 && referenceLocalTime.Value.Hour > 4)
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
