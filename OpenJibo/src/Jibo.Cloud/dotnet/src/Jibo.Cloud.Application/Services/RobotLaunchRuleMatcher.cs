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

            if (!ContainsSubsequence(tokens, rule.LiteralTokens, out var matchedTokenCount)) continue;

            var requiredTokenCount = rule.LiteralTokens.Count(token => !token.IsOptional);
            var score = matchedTokenCount * 100 + requiredTokenCount;
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
        var normalized = TranscriptTextNormalizer.NormalizeLooseText(transcript);
        if (string.IsNullOrWhiteSpace(normalized)) return [];

        return normalized
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(token => token.Length > 0)
            .ToArray();
    }

    private static bool ContainsSubsequence(
        IReadOnlyList<string> haystack,
        IReadOnlyList<LaunchRuleToken> needle,
        out int matchedTokenCount)
    {
        matchedTokenCount = 0;
        if (needle.Count == 0) return false;

        var requiredTokens = needle.Where(token => !token.IsOptional).ToArray();
        if (requiredTokens.Length == 0) return false;

        var allowedRequiredMisses = requiredTokens.Length >= 5 ? 1 : 0;
        var requiredMisses = 0;
        var start = 0;

        foreach (var token in needle)
        {
            var found = false;
            for (var i = start; i < haystack.Count; i++)
            {
                if (!TokensEquivalent(haystack[i], token.Text)) continue;
                start = i + 1;
                matchedTokenCount += 1;
                found = true;
                break;
            }

            if (found) continue;
            if (token.IsOptional) continue;

            if (requiredMisses >= allowedRequiredMisses) return false;

            requiredMisses += 1;
        }

        return requiredMisses <= allowedRequiredMisses;
    }

    private static bool TokensEquivalent(string haystackToken, string needleToken)
    {
        if (string.Equals(haystackToken, needleToken, StringComparison.Ordinal)) return true;
        if (needleToken.Length < 4 || haystackToken.Length < 4) return false;

        return LevenshteinDistance(haystackToken, needleToken) <= 1;
    }

    private static int LevenshteinDistance(string left, string right)
    {
        if (left.Length == 0) return right.Length;
        if (right.Length == 0) return left.Length;

        var previous = new int[right.Length + 1];
        var current = new int[right.Length + 1];

        for (var j = 0; j <= right.Length; j += 1)
            previous[j] = j;

        for (var i = 1; i <= left.Length; i += 1)
        {
            current[0] = i;
            for (var j = 1; j <= right.Length; j += 1)
            {
                var cost = left[i - 1] == right[j - 1] ? 0 : 1;
                current[j] = Math.Min(
                    Math.Min(current[j - 1] + 1, previous[j] + 1),
                    previous[j - 1] + cost);
            }

            (previous, current) = (current, previous);
        }

        return previous[right.Length];
    }
}
