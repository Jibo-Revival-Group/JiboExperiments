using Jibo.Cloud.Application.Abstractions;
using Jibo.Runtime.Abstractions;
using Microsoft.Extensions.Logging;

namespace Jibo.Cloud.Application.Services;

public sealed class RobotLaunchRuleOrchestrator(
    IRobotLaunchRuleStore launchRuleStore,
    ILogger<RobotLaunchRuleOrchestrator> logger)
{
    public Task<JiboInteractionDecision?> TryBuildDecisionAsync(
        TurnContext turn,
        string transcript,
        ICloudStateStore? cloudStateStore,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(transcript)) return Task.FromResult<JiboInteractionDecision?>(null);

        var files = launchRuleStore.List();
        if (files.Count == 0) return Task.FromResult<JiboInteractionDecision?>(null);

        var parsedRules = new List<ParsedLaunchRule>();
        foreach (var file in files)
        {
            if (string.IsNullOrWhiteSpace(file.Content)) continue;
            parsedRules.AddRange(RobotLaunchRuleParser.Parse(file.FileName, file.Content));
        }

        if (parsedRules.Count == 0)
        {
            logger.LogWarning(
                "Launch rule files are present but none parsed successfully. fileCount={FileCount}",
                files.Count);
            return Task.FromResult<JiboInteractionDecision?>(null);
        }

        if (!ShouldTryLaunchRules(turn))
        {
            logger.LogDebug(
                "Skipping launch rule matching for turn messageType={MessageType} listenHotphrase={ListenHotphrase}",
                ReadMessageType(turn),
                TurnAttributeReader.ReadBool(turn, "listenHotphrase"));
            return Task.FromResult<JiboInteractionDecision?>(null);
        }

        var match = TryMatchTranscript(transcript, parsedRules);
        if (match is null)
        {
            logger.LogInformation(
                "Launch rule miss transcript={Transcript} messageType={MessageType} ruleCount={RuleCount}",
                transcript,
                ReadMessageType(turn),
                parsedRules.Count);
            return Task.FromResult<JiboInteractionDecision?>(null);
        }

        logger.LogInformation(
            "Launch rule matched rule={RuleName} file={RuleFile} skill={SkillId} transcript={Transcript}",
            match.Rule.RuleName,
            match.Rule.SourceFile,
            match.SkillId,
            transcript);

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
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in new[]
                 {
                     transcript,
                     TranscriptTextNormalizer.NormalizeLooseText(transcript),
                     TranscriptTextNormalizer.ExtractWakePhraseCommand(transcript),
                     StripLegacyWakePhrase(transcript)
                 })
        {
            if (string.IsNullOrWhiteSpace(candidate)) continue;

            var trimmed = candidate.Trim();
            if (trimmed.Length == 0 || !seen.Add(trimmed)) continue;

            yield return trimmed;
        }
    }

    private static bool ShouldTryLaunchRules(TurnContext turn)
    {
        var messageType = ReadMessageType(turn);

        if (string.Equals(messageType, "TRIGGER", StringComparison.OrdinalIgnoreCase)) return false;
        if (string.Equals(messageType, "CLIENT_NLU", StringComparison.OrdinalIgnoreCase)) return false;

        if (TurnAttributeReader.ReadBool(turn, "listenHotphrase")) return true;
        if (string.Equals(messageType, "LISTEN", StringComparison.OrdinalIgnoreCase)) return true;
        if (string.Equals(messageType, "AUTO_FINALIZE", StringComparison.OrdinalIgnoreCase)) return true;
        if (string.Equals(messageType, "CLIENT_ASR", StringComparison.OrdinalIgnoreCase)) return true;

        return TurnAttributeReader.ReadRules(turn, "listenRules")
            .Concat(TurnAttributeReader.ReadRules(turn, "clientRules"))
            .Any(IsLaunchRule);
    }

    private static bool IsLaunchRule(string rule)
    {
        return string.Equals(rule, "launch", StringComparison.OrdinalIgnoreCase) ||
               rule.Contains("global_commands_launch", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ReadMessageType(TurnContext turn)
    {
        return turn.Attributes.TryGetValue("messageType", out var rawMessageType)
            ? rawMessageType?.ToString()
            : null;
    }

    private static string StripLegacyWakePhrase(string transcript)
    {
        var normalized = transcript.Trim();
        if (normalized.Length == 0) return normalized;

        var lowered = normalized.ToLowerInvariant();
        foreach (var phrase in LegacyWakeLeadPhrases.OrderByDescending(phrase => phrase.Length))
        {
            if (!lowered.StartsWith(phrase, StringComparison.Ordinal)) continue;

            var remainder = normalized[phrase.Length..].TrimStart(',', ' ', '.', '!', '?');
            if (remainder.Length > 0)
                return remainder;
        }

        return normalized;
    }

    private static readonly string[] LegacyWakeLeadPhrases =
    [
        "hey jibo",
        "hello jibo",
        "hi jibo",
        "ok jibo",
        "okay jibo",
        "jibo"
    ];
}
