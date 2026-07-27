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
        string? fallbackMimId = null)
    {
        var matchingReplies = replies
            .Where(reply => LegacyMimConditionEvaluator.Matches(reply.Condition, context))
            .Where(reply => MatchesSpeakerRequirement(reply.Condition, context.HasSpeaker))
            .ToArray();

        if (matchingReplies.Length == 0)
        {
            return new LegacyMimSelection
            {
                ReplyText = LegacyMimTemplateRenderer.Render(fallbackReply, displayName),
                MimId = fallbackMimId
            };
        }

        var chosen = ChooseWeighted(randomizer, matchingReplies);
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
        string fallbackReply)
    {
        return Select(replies, randomizer, context, displayName, fallbackReply).ReplyText;
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
        var maxWeight = replies.Max(reply => EffectiveWeight(reply.Weight));
        var topTier = replies
            .Where(reply => Math.Abs(EffectiveWeight(reply.Weight) - maxWeight) < 0.001)
            .ToArray();

        if (topTier.Length == 1) return topTier[0];

        return randomizer.Choose(topTier);
    }

    private static double EffectiveWeight(double weight) => weight <= 0 ? 0.1 : weight;
}
