using System.Text.Json;
using Jibo.Runtime.Abstractions;

namespace Jibo.Cloud.Application.Services;

internal static class IntroductionsFollowUp
{
    internal const string Rule = "introductions/intro_looper";
    internal const string ReplyMetadataKey = "introductionsNameReply";
    private static readonly string[] NamePrefixes = ["my name is ", "my name's ", "my name s ", "i am ", "i'm ", "i m "];

    internal static bool IsNameRule(string rule) =>
        string.Equals(rule, Rule, StringComparison.OrdinalIgnoreCase);

    internal static ResponsePlan? BuildPlan(TurnContext turn, Func<string?, string> normalize)
    {
        if (!HasNameRule(turn)) return null;
        if (turn.Attributes.TryGetValue("clientIntent", out var clientIntent) &&
            !string.IsNullOrWhiteSpace(clientIntent?.ToString()))
        {
            if (!string.Equals(clientIntent.ToString(), "loopmember", StringComparison.OrdinalIgnoreCase))
                return null;

            if (turn.Attributes.TryGetValue("clientEntities", out var value) &&
                value is JsonElement { ValueKind: JsonValueKind.Object } entities &&
                entities.TryGetProperty("loopMemberReferent", out var referent) &&
                referent.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(referent.GetString()))
                return null;
        }

        var text = normalize(turn.NormalizedTranscript ?? turn.RawTranscript);
        if (string.IsNullOrWhiteSpace(text)) return null;

        var reply = text is "cancel" or "escape" or "exit" or "forget it" or "never mind" or "nevermind" or "later"
            or "no one" or "nobody"
            ? new Selection("cancel", new Dictionary<string, object?>())
            : ResolveName(turn, normalize, text);
        return new ResponsePlan
        {
            SessionId = turn.SessionId,
            IntentName = reply.Intent.Length == 0 ? "introductions_name_no_match" : reply.Intent,
            ProtocolMetadata = { [ReplyMetadataKey] = reply }
        };
    }

    internal static bool HasNameRule(TurnContext turn) =>
        SkillListenOwnership.ReadPrimarySkillRule(turn) is { } rule && IsNameRule(rule);

    private static Selection ResolveName(TurnContext turn, Func<string?, string> normalize, string text)
    {
        foreach (var prefix in NamePrefixes)
        {
            if (!text.StartsWith(prefix, StringComparison.Ordinal)) continue;
            text = text[prefix.Length..];
            break;
        }

        // Match against the same roster the robot's enrollment menu is displaying.
        var matches = JiboInteractionService.TryReadLoopUsersFromTurn(turn).Where(member =>
                !string.Equals(member.Type, "robot", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(member.FirstName) &&
                (text == normalize(member.FirstName) || text == normalize($"{member.FirstName} {member.LastName}")))
            .DistinctBy(member => member.Id, StringComparer.Ordinal)
            .Take(2)
            .ToArray();
        if (matches.Length != 1) return new Selection(string.Empty, new Dictionary<string, object?>());

        var match = matches[0];
        return new Selection("loopmember", new Dictionary<string, object?>
        {
            ["loopMemberReferent"] = match.Id,
            ["given-name"] = match.FirstName,
            ["last-name"] = match.LastName
        });
    }

    internal sealed record Selection(string Intent, IReadOnlyDictionary<string, object?> Entities);
}
