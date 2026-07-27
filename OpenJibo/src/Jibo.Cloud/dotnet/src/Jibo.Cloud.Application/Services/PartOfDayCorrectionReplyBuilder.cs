using Jibo.Cloud.Application.Abstractions;

namespace Jibo.Cloud.Application.Services;

internal static class PartOfDayCorrectionReplyBuilder
{
    internal static string BuildReply(
        JiboExperienceCatalog catalog,
        IJiboRandomizer randomizer,
        JiboPartOfDay claimed,
        string? displayName)
    {
        var claimToken = claimed.ToClaimToken();
        var matchingReplies = catalog.PartOfDayCorrectionReplies
            .Where(reply => MatchesPodClaim(reply.Condition, claimToken))
            .Select(reply => reply.Reply)
            .Where(reply => !string.IsNullOrWhiteSpace(reply))
            .ToArray();

        var selected = matchingReplies.Length > 0
            ? randomizer.Choose(matchingReplies)
            : GetFallbackReply(claimed);

        return RenderTemplate(selected, displayName);
    }

    private static string GetFallbackReply(JiboPartOfDay claimed) =>
        claimed switch
        {
            JiboPartOfDay.Morning => "And a good morning to you, any time of day.",
            JiboPartOfDay.Afternoon => "Sure. I guess it is afternoon somewhere.",
            JiboPartOfDay.Evening => "I may be wrong, but I don't think it's evening.",
            _ => "Hello."
        };

    private static bool MatchesPodClaim(string? condition, string claimToken)
    {
        if (string.IsNullOrWhiteSpace(condition) || string.IsNullOrWhiteSpace(claimToken)) return false;

        return condition.Contains($"PODclaim=='{claimToken}'", StringComparison.OrdinalIgnoreCase);
    }

    private static string RenderTemplate(string template, string? displayName)
    {
        var speakerName = string.IsNullOrWhiteSpace(displayName) ? string.Empty : displayName.Trim();
        return template
            .Replace("${loopMember}", speakerName, StringComparison.OrdinalIgnoreCase)
            .Replace(" ,", ",", StringComparison.Ordinal)
            .Replace(",,", ",", StringComparison.Ordinal)
            .Replace("  ", " ", StringComparison.Ordinal)
            .Trim();
    }
}
