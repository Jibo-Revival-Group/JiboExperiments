namespace Jibo.Cloud.Application.Services;

public sealed class LaunchRuleMatchResult
{
    public required ParsedLaunchRule Rule { get; init; }
    public required string SkillId { get; init; }
    public required string Intent { get; init; }
    public required IReadOnlyDictionary<string, string> Entities { get; init; }
}

public static class RobotLaunchRuleMatcher
{
    public static LaunchRuleMatchResult? TryMatch(string transcript, IReadOnlyList<ParsedLaunchRule> rules)
    {
        if (string.IsNullOrWhiteSpace(transcript) || rules.Count == 0) return null;

        var tokens = TokenizeTranscript(transcript);
        if (tokens.Count == 0) return null;

        LaunchRuleMatchResult? best = null;
        var bestScore = -1;

        foreach (var rule in rules)
        {
            if (!rule.Entities.TryGetValue("skill", out var skillId) || string.IsNullOrWhiteSpace(skillId))
                continue;

            if (rule.LiteralTokens.Count == 0) continue;

            if (!ContainsSubsequence(tokens, rule.LiteralTokens)) continue;

            var score = rule.LiteralTokens.Count;
            if (score <= bestScore) continue;

            bestScore = score;
            var entities = new Dictionary<string, string>(rule.Entities, StringComparer.OrdinalIgnoreCase);
            var intent = entities.TryGetValue("intent", out var intentValue) && !string.IsNullOrWhiteSpace(intentValue)
                ? intentValue
                : "menu";

            best = new LaunchRuleMatchResult
            {
                Rule = rule,
                SkillId = skillId,
                Intent = intent,
                Entities = entities
            };
        }

        return best;
    }

    private static IReadOnlyList<string> TokenizeTranscript(string transcript)
    {
        return transcript
            .ToLowerInvariant()
            .Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(token => token.Trim().TrimEnd('.', ',', '!', '?', ';'))
            .Where(token => token.Length > 0)
            .ToArray();
    }

    private static bool ContainsSubsequence(IReadOnlyList<string> haystack, IReadOnlyList<string> needle)
    {
        if (needle.Count == 0) return false;

        var start = 0;
        foreach (var token in needle)
        {
            var found = false;
            for (var i = start; i < haystack.Count; i++)
            {
                if (!string.Equals(haystack[i], token, StringComparison.Ordinal)) continue;
                start = i + 1;
                found = true;
                break;
            }

            if (!found) return false;
        }

        return true;
    }
}
