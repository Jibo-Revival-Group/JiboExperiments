using Jibo.Cloud.Application.Abstractions;
using Jibo.Runtime.Abstractions;

namespace Jibo.Cloud.Application.Services;

public sealed class RobotLaunchRuleOrchestrator(IRobotLaunchRuleStore launchRuleStore)
{
    private static readonly string[] WakeLeadPhrases =
    [
        "hey jibo",
        "hello jibo",
        "hi jibo",
        "ok jibo",
        "okay jibo",
        "jibo"
    ];

    public Task<JiboInteractionDecision?> TryBuildDecisionAsync(
        TurnContext turn,
        string transcript,
        ICloudStateStore? cloudStateStore,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!ShouldTryLaunchRules(turn)) return Task.FromResult<JiboInteractionDecision?>(null);
        if (string.IsNullOrWhiteSpace(transcript)) return Task.FromResult<JiboInteractionDecision?>(null);

        var files = launchRuleStore.List();
        if (files.Count == 0) return Task.FromResult<JiboInteractionDecision?>(null);

        var parsedRules = new List<ParsedLaunchRule>();
        foreach (var file in files)
        {
            if (string.IsNullOrWhiteSpace(file.Content)) continue;
            parsedRules.AddRange(RobotLaunchRuleParser.Parse(file.FileName, file.Content));
        }

        if (parsedRules.Count == 0) return Task.FromResult<JiboInteractionDecision?>(null);

        var match = TryMatchTranscript(transcript, parsedRules);
        if (match is null) return Task.FromResult<JiboInteractionDecision?>(null);

        var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["launchRuleMatch"] = "true",
            ["launchRuleIntent"] = match.Intent,
            ["launchRuleFile"] = match.Rule.SourceFile,
            ["launchRuleName"] = match.Rule.RuleName,
            ["skillId"] = match.SkillId,
            ["localIntent"] = match.Intent
        };

        foreach (var (key, value) in match.Entities)
            payload[key] = value;

        return Task.FromResult<JiboInteractionDecision?>(new JiboInteractionDecision(
            match.Intent,
            string.Empty,
            match.SkillId,
            payload));
    }

    private static LaunchRuleMatchResult? TryMatchTranscript(string transcript, IReadOnlyList<ParsedLaunchRule> rules)
    {
        foreach (var candidate in BuildTranscriptCandidates(transcript))
        {
            var match = RobotLaunchRuleMatcher.TryMatch(candidate, rules);
            if (match is not null) return match;
        }

        return null;
    }

    private static IEnumerable<string> BuildTranscriptCandidates(string transcript)
    {
        var trimmed = transcript.Trim();
        if (trimmed.Length == 0) yield break;

        yield return trimmed;

        var stripped = StripWakePhrase(trimmed);
        if (!string.Equals(stripped, trimmed, StringComparison.OrdinalIgnoreCase))
            yield return stripped;
    }

    private static bool ShouldTryLaunchRules(TurnContext turn)
    {
        var messageType = turn.Attributes.TryGetValue("messageType", out var rawMessageType)
            ? rawMessageType?.ToString()
            : null;

        if (string.Equals(messageType, "TRIGGER", StringComparison.OrdinalIgnoreCase)) return false;
        if (string.Equals(messageType, "CLIENT_NLU", StringComparison.OrdinalIgnoreCase)) return false;

        if (TurnAttributeReader.ReadBool(turn, "listenHotphrase")) return true;
        if (string.Equals(messageType, "LISTEN", StringComparison.OrdinalIgnoreCase)) return true;

        return TurnAttributeReader.ReadRules(turn, "listenRules")
            .Concat(TurnAttributeReader.ReadRules(turn, "clientRules"))
            .Any(rule => string.Equals(rule, "launch", StringComparison.OrdinalIgnoreCase)
                         || rule.Contains("global_commands_launch", StringComparison.OrdinalIgnoreCase));
    }

    private static string StripWakePhrase(string transcript)
    {
        var normalized = transcript.Trim();
        if (normalized.Length == 0) return normalized;

        var lowered = normalized.ToLowerInvariant();
        foreach (var phrase in WakeLeadPhrases.OrderByDescending(phrase => phrase.Length))
        {
            if (!lowered.StartsWith(phrase, StringComparison.Ordinal)) continue;

            var remainder = normalized[phrase.Length..].TrimStart(',', ' ', '.', '!', '?');
            if (remainder.Length > 0)
                return remainder;
        }

        return normalized;
    }
}
