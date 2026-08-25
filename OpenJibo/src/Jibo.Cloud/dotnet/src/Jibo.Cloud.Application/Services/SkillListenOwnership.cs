using System.Text.Json;
using Jibo.Runtime.Abstractions;

namespace Jibo.Cloud.Application.Services;

internal static class SkillListenOwnership
{
    private static readonly HashSet<string> NonQuestionKnowledgeTokens = new(StringComparer.Ordinal)
    {
        "no",
        "nope",
        "nah",
        "yes",
        "yeah",
        "yep",
        "yup",
        "ready",
        "ok",
        "okay",
        "sure"
    };

    public static bool IsGlobalOrLaunchRule(string? rule)
    {
        if (string.IsNullOrWhiteSpace(rule)) return true;

        return string.Equals(rule, "launch", StringComparison.OrdinalIgnoreCase) ||
               rule.StartsWith("globals/", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsSkillRule(string? rule)
    {
        if (IsGlobalOrLaunchRule(rule) || string.IsNullOrWhiteSpace(rule)) return false;

        // Stock MIM rules are "skill/name" (shared/yes_no, exercise/want_to). Bare tokens such as
        // launch or wake-word are not in-skill listens.
        return rule.Contains('/', StringComparison.Ordinal);
    }

    public static bool ReadListenHotphrase(TurnContext turn)
    {
        if (!turn.Attributes.TryGetValue("listenHotphrase", out var value) || value is null) return false;

        return value switch
        {
            bool flag => flag,
            JsonElement { ValueKind: JsonValueKind.True } => true,
            JsonElement { ValueKind: JsonValueKind.False } => false,
            _ when bool.TryParse(value.ToString(), out var parsed) => parsed,
            _ => false
        };
    }

    public static bool IsSkillOwnedListen(
        bool listenHotphrase,
        IEnumerable<string> listenRules,
        IEnumerable<string> clientRules)
    {
        if (listenHotphrase) return false;

        return listenRules.Concat(clientRules).Any(IsSkillRule);
    }

    public static bool IsSkillOwnedListen(TurnContext turn)
    {
        return IsSkillOwnedListen(
            ReadListenHotphrase(turn),
            ReadRuleList(turn, "listenRules"),
            ReadRuleList(turn, "clientRules"));
    }

    public static string? ReadPrimarySkillRule(IEnumerable<string> listenRules, IEnumerable<string> clientRules)
    {
        return listenRules.Concat(clientRules).FirstOrDefault(IsSkillRule);
    }

    public static string? ReadPrimarySkillRule(TurnContext turn)
    {
        return ReadPrimarySkillRule(ReadRuleList(turn, "listenRules"), ReadRuleList(turn, "clientRules"));
    }

    public static bool IsCloudOwnedFollowUp(TurnContext turn)
    {
        if (!turn.Attributes.TryGetValue(PersonalReportOrchestrator.StateMetadataKey, out var value) ||
            value is null)
            return false;

        var state = value.ToString();
        return !string.IsNullOrWhiteSpace(state) &&
               !string.Equals(state, PersonalReportOrchestrator.IdleState, StringComparison.OrdinalIgnoreCase);
    }

    public static bool ShouldSuppressCompetingSpeech(TurnContext turn, string? intentName)
    {
        if (IsCloudOwnedFollowUp(turn)) return false;

        if (string.Equals(intentName, "skill_listen", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(intentName, "prompt_echo", StringComparison.OrdinalIgnoreCase))
            return true;

        if (!string.Equals(intentName, "yes", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(intentName, "no", StringComparison.OrdinalIgnoreCase))
            return false;

        return IsSkillOwnedListen(turn);
    }

    public static bool IsNonQuestionKnowledgeQuery(string? transcript)
    {
        if (TranscriptHeuristics.IsLikelyPromptEchoTranscript(transcript)) return true;

        var normalized = NormalizeLoose(transcript);
        if (string.IsNullOrWhiteSpace(normalized)) return false;

        var tokens = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return tokens.Length == 1 && NonQuestionKnowledgeTokens.Contains(tokens[0]);
    }

    private static IEnumerable<string> ReadRuleList(TurnContext turn, string key)
    {
        if (!turn.Attributes.TryGetValue(key, out var value) || value is null) return [];

        return value switch
        {
            IReadOnlyList<string> typed => typed,
            IEnumerable<string> strings => strings,
            JsonElement { ValueKind: JsonValueKind.Array } json => json.EnumerateArray()
                .Where(static item => item.ValueKind == JsonValueKind.String)
                .Select(static item => item.GetString() ?? string.Empty)
                .Where(static item => !string.IsNullOrWhiteSpace(item)),
            _ => []
        };
    }

    private static string NormalizeLoose(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;

        var builder = new char[value.Length];
        var length = 0;
        foreach (var character in value.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(character) || character is '\'' or ' ')
                builder[length++] = character;
            else
                builder[length++] = ' ';
        }

        return string.Join(
            ' ',
            new string(builder, 0, length).Split(' ',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }
}
