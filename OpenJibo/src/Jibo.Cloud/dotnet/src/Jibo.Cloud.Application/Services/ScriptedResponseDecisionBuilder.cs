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
        params string[] preferredSnippets)
    {
        return new JiboInteractionDecision(
            intentName,
            SelectLegacyReply(catalog.HolidayTrackerReplies, randomizer, preferredSnippets),
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
}