using Jibo.Cloud.Application.Abstractions;

namespace Jibo.Cloud.Application.Services;

internal sealed class LegacyMimSelection
{
    public required string ReplyText { get; init; }
    public string? MimId { get; init; }
    public string? PromptId { get; init; }
    public string? Emotion { get; init; }
}

internal static class LegacyMimReplySelector
{
    internal static LegacyMimSelection Select(
        IReadOnlyList<JiboConditionedReply> replies,
        IJiboRandomizer randomizer,
        LegacyMimConditionEvaluator.Context context,
        string? displayName,
        string fallbackReply,
        string? fallbackMimId = null,
        params string[] legacySnippets)
    {
        var matchingReplies = replies
            .Where(reply => LegacyMimConditionEvaluator.Matches(reply.Condition, context))
            .Where(reply => MatchesSpeakerRequirement(reply.Condition, context.HasSpeaker))
            .ToArray();

        var pool = legacySnippets.Length > 0
            ? FilterByLegacySnippets(matchingReplies, legacySnippets)
            : matchingReplies;
        if (pool.Length == 0 && matchingReplies.Length > 0) pool = matchingReplies;

        if (pool.Length == 0)
        {
            return new LegacyMimSelection
            {
                ReplyText = LegacyMimTemplateRenderer.Render(fallbackReply, displayName),
                MimId = fallbackMimId
            };
        }

        var chosen = ChooseWeighted(randomizer, pool);
        return new LegacyMimSelection
        {
            ReplyText = LegacyMimTemplateRenderer.Render(chosen.Reply, displayName),
            MimId = chosen.MimId,
            PromptId = chosen.PromptId,
            Emotion = chosen.Emotion
        };
    }

    internal static string SelectReplyText(
        IReadOnlyList<JiboConditionedReply> replies,
        IJiboRandomizer randomizer,
        LegacyMimConditionEvaluator.Context context,
        string? displayName,
        string fallbackReply,
        params string[] legacySnippets)
    {
        return Select(replies, randomizer, context, displayName, fallbackReply, null, legacySnippets).ReplyText;
    }

    private static JiboConditionedReply[] FilterByLegacySnippets(
        IReadOnlyList<JiboConditionedReply> replies,
        params string[] legacySnippets)
    {
        if (legacySnippets.Length == 0) return replies.ToArray();

        foreach (var snippet in legacySnippets)
        {
            if (string.IsNullOrWhiteSpace(snippet)) continue;

            var matches = replies
                .Where(reply => reply.Reply.Contains(snippet, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (matches.Length > 0) return matches;
        }

        return [];
    }

    internal static JiboConditionedReply[] FilterMatchingReplies(
        IReadOnlyList<JiboConditionedReply> replies,
        LegacyMimConditionEvaluator.Context context,
        params string[] legacySnippets)
    {
        var matchingReplies = replies
            .Where(reply => LegacyMimConditionEvaluator.Matches(reply.Condition, context))
            .Where(reply => MatchesSpeakerRequirement(reply.Condition, context.HasSpeaker))
            .ToArray();

        if (matchingReplies.Length == 0) return [];

        var snippetMatches = FilterByLegacySnippets(matchingReplies, legacySnippets);
        if (legacySnippets.Length == 0) return matchingReplies;

        return snippetMatches;
    }

    private static bool MatchesSpeakerRequirement(string? condition, bool hasSpeaker)
    {
        var normalized = condition?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalized)) return true;

        if (string.Equals(normalized, "loopMember", StringComparison.OrdinalIgnoreCase)) return hasSpeaker;

        if (normalized.Contains("!loopMember", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("!speaker", StringComparison.OrdinalIgnoreCase))
            return !hasSpeaker;

        if (normalized.Contains("!!speaker", StringComparison.OrdinalIgnoreCase)) return hasSpeaker;

        return true;
    }

    private static JiboConditionedReply ChooseWeighted(
        IJiboRandomizer randomizer,
        IReadOnlyList<JiboConditionedReply> replies)
    {
        if (replies.Count == 1) return replies[0];

        var maxWeight = replies.Max(reply => EffectiveWeight(reply.Weight));
        var minWeight = replies.Min(reply => EffectiveWeight(reply.Weight));
        var topTier = Math.Abs(maxWeight - minWeight) < 0.001
            ? replies
            : replies
                .Where(reply => Math.Abs(EffectiveWeight(reply.Weight) - maxWeight) < 0.001)
                .ToArray();
        var pool = topTier.Count > 0 ? topTier : replies.ToArray();

        if (pool.Count == 1) return pool[0];

        var totalWeight = pool.Sum(reply => EffectiveWeight(reply.Weight));
        var roll = randomizer.NextUnitInterval() * totalWeight;
        var cumulative = 0.0;

        foreach (var reply in pool)
        {
            cumulative += EffectiveWeight(reply.Weight);
            if (roll < cumulative) return reply;
        }

        return pool[^1];
    }

    private static double EffectiveWeight(double weight) => weight <= 0 ? 0.1 : weight;
}
