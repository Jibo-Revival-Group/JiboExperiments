using Jibo.Cloud.Application.Abstractions;

namespace Jibo.Cloud.Application.Services;

internal static class LegacyMimScriptedReplyBuilder
{
    internal static JiboInteractionDecision BuildScriptedDecision(
        JiboExperienceCatalog catalog,
        IJiboRandomizer randomizer,
        string intentName,
        LegacyMimConditionEvaluator.Context context,
        string? displayName,
        string? explicitMimId = null,
        params string[] legacySnippets)
    {
        if (TrySelectMimReply(
                catalog,
                randomizer,
                intentName,
                context,
                displayName,
                explicitMimId,
                legacySnippets,
                out var selection))
        {
            return new JiboInteractionDecision(
                intentName,
                selection!.ReplyText,
                SkillPayload: LegacyMimDecisionMetadata.BuildSkillPayload(selection, explicitMimId ?? intentName),
                ContextUpdates: LegacyMimDecisionMetadata.ApplyEmotion(
                    ScriptedResponseDecisionBuilder.BuildScriptedResponseContextUpdates(),
                    selection.Emotion));
        }

        return new JiboInteractionDecision(
            intentName,
            ScriptedResponseDecisionBuilder.SelectLegacyReplyFromStrings(
                catalog.PersonalityReplies,
                randomizer,
                legacySnippets),
            ContextUpdates: ScriptedResponseDecisionBuilder.BuildScriptedResponseContextUpdates());
    }

    internal static string SelectFromBucketOrMim(
        JiboExperienceCatalog catalog,
        IJiboRandomizer randomizer,
        string intentName,
        IReadOnlyList<string> legacyReplies,
        LegacyMimConditionEvaluator.Context context,
        string? displayName,
        string? explicitMimId = null,
        params string[] legacySnippets)
    {
        if (TrySelectMimReply(
                catalog,
                randomizer,
                intentName,
                context,
                displayName,
                explicitMimId,
                legacySnippets,
                out var selection))
            return selection!.ReplyText;

        return ScriptedResponseDecisionBuilder.SelectLegacyReplyFromStrings(legacyReplies, randomizer, legacySnippets);
    }

    internal static bool TrySelectMimReply(
        JiboExperienceCatalog catalog,
        IJiboRandomizer randomizer,
        string intentName,
        LegacyMimConditionEvaluator.Context context,
        string? displayName,
        string? explicitMimId,
        string[] legacySnippets,
        out LegacyMimSelection? selection)
    {
        selection = null;
        var mimReplies = LegacyMimIntentResolver.TryResolveReplies(catalog, intentName, explicitMimId);
        if (mimReplies is not { Count: > 0 }) return false;

        var conditionedMatches = LegacyMimReplySelector.FilterMatchingReplies(mimReplies, context);
        if (conditionedMatches.Length == 0) return false;

        if (legacySnippets.Length > 0)
        {
            var primarySnippet = legacySnippets.FirstOrDefault(snippet => !string.IsNullOrWhiteSpace(snippet));
            if (!string.IsNullOrWhiteSpace(primarySnippet))
            {
                var primaryMatches = LegacyMimReplySelector.FilterMatchingReplies(
                    mimReplies,
                    context,
                    primarySnippet);
                if (primaryMatches.Length == 0) return false;
            }
        }

        var fallback = legacySnippets.FirstOrDefault(snippet => !string.IsNullOrWhiteSpace(snippet)) ??
                       conditionedMatches[0].Reply ??
                       mimReplies[0].Reply;

        selection = LegacyMimReplySelector.Select(
            conditionedMatches,
            randomizer,
            context,
            displayName,
            fallback,
            explicitMimId ?? mimReplies[0].MimId ?? intentName);

        return true;
    }

    internal static LegacyMimConditionEvaluator.Context BuildScriptedContext(DateTimeOffset? referenceLocalTime = null)
    {
        var localTime = referenceLocalTime ?? DateTimeOffset.UtcNow;
        return new LegacyMimConditionEvaluator.Context(
            HolidayClaim: null,
            Holiday: null,
            CurrentDate: DateOnly.FromDateTime(localTime.LocalDateTime));
    }
}
