using Jibo.Cloud.Application.Abstractions;
using Jibo.Runtime.Abstractions;

namespace Jibo.Cloud.Application.Services;

public sealed class RobotLaunchRuleOrchestrator(
    IRobotLaunchRuleStore launchRuleStore,
    RobotLaunchRuleHostSettings hostSettings)
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

        if (!IsLaunchTurn(turn)) return Task.FromResult<JiboInteractionDecision?>(null);

        var robotName = ResolveRobotName(turn, cloudStateStore);
        if (string.IsNullOrWhiteSpace(robotName)) return Task.FromResult<JiboInteractionDecision?>(null);

        var files = launchRuleStore.List(robotName);
        if (files.Count == 0) return Task.FromResult<JiboInteractionDecision?>(null);

        var parsedRules = new List<ParsedLaunchRule>();
        foreach (var file in files)
        {
            if (string.IsNullOrWhiteSpace(file.Content)) continue;
            parsedRules.AddRange(RobotLaunchRuleParser.Parse(file.FileName, file.Content));
        }

        var normalizedTranscript = StripWakePhrase(transcript);
        var match = RobotLaunchRuleMatcher.TryMatch(normalizedTranscript, parsedRules);
        if (match is null) return Task.FromResult<JiboInteractionDecision?>(null);

        var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["launchRuleMatch"] = "true",
            ["launchRuleIntent"] = match.Intent,
            ["launchRuleFile"] = match.Rule.SourceFile,
            ["launchRuleName"] = match.Rule.RuleName,
            ["skillId"] = match.SkillId,
            ["localIntent"] = match.Intent,
            ["robotFriendlyName"] = robotName
        };

        foreach (var (key, value) in match.Entities)
            payload[key] = value;

        return Task.FromResult<JiboInteractionDecision?>(new JiboInteractionDecision(
            match.Intent,
            string.Empty,
            match.SkillId,
            payload));
    }

    private string? ResolveRobotName(TurnContext turn, ICloudStateStore? cloudStateStore)
    {
        var resolved = RobotFriendlyNameResolver.Resolve(turn, cloudStateStore);
        if (!string.IsNullOrWhiteSpace(resolved)) return resolved;

        if (RobotFriendlyNameValidator.TryNormalize(hostSettings.DefaultRobotFriendlyName, out var configured, out _))
            return configured;

        var knownRobots = launchRuleStore.ListRobotFriendlyNames();
        if (knownRobots.Count == 1)
            return knownRobots[0];

        return knownRobots
            .Select(name => new { Name = name, RuleCount = launchRuleStore.List(name).Count })
            .Where(entry => entry.RuleCount > 0)
            .OrderByDescending(entry => entry.RuleCount)
            .ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .Select(entry => entry.Name)
            .FirstOrDefault();
    }

    private static bool IsLaunchTurn(TurnContext turn)
    {
        if (TurnAttributeReader.ReadBool(turn, "listenHotphrase")) return true;

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
