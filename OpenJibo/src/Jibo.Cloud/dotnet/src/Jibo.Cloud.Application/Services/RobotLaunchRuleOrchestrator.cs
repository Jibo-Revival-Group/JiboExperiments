using Jibo.Cloud.Application.Abstractions;
using Jibo.Runtime.Abstractions;

namespace Jibo.Cloud.Application.Services;

public sealed class RobotLaunchRuleOrchestrator(IRobotLaunchRuleStore launchRuleStore)
{
    public Task<JiboInteractionDecision?> TryBuildDecisionAsync(
        TurnContext turn,
        string transcript,
        ICloudStateStore? cloudStateStore,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!IsLaunchTurn(turn)) return Task.FromResult<JiboInteractionDecision?>(null);

        var robotName = RobotFriendlyNameResolver.Resolve(turn, cloudStateStore);
        if (string.IsNullOrWhiteSpace(robotName)) return Task.FromResult<JiboInteractionDecision?>(null);

        var files = launchRuleStore.List(robotName);
        if (files.Count == 0) return Task.FromResult<JiboInteractionDecision?>(null);

        var parsedRules = new List<ParsedLaunchRule>();
        foreach (var file in files)
        {
            if (string.IsNullOrWhiteSpace(file.Content)) continue;
            parsedRules.AddRange(RobotLaunchRuleParser.Parse(file.FileName, file.Content));
        }

        var match = RobotLaunchRuleMatcher.TryMatch(transcript, parsedRules);
        if (match is null) return Task.FromResult<JiboInteractionDecision?>(null);

        var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["launchRuleMatch"] = "true",
            ["launchRuleIntent"] = match.Intent,
            ["launchRuleFile"] = match.Rule.SourceFile,
            ["launchRuleName"] = match.Rule.RuleName,
            ["skillId"] = match.SkillId
        };

        foreach (var (key, value) in match.Entities)
            payload[key] = value;

        return Task.FromResult<JiboInteractionDecision?>(new JiboInteractionDecision(
            match.Intent,
            string.Empty,
            match.SkillId,
            payload));
    }

    private static bool IsLaunchTurn(TurnContext turn)
    {
        return ReadRules(turn, "listenRules")
            .Concat(ReadRules(turn, "clientRules"))
            .Any(rule => string.Equals(rule, "launch", StringComparison.OrdinalIgnoreCase)
                         || rule.Contains("global_commands_launch", StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<string> ReadRules(TurnContext turn, string key)
    {
        if (!turn.Attributes.TryGetValue(key, out var value) || value is null) yield break;

        switch (value)
        {
            case IReadOnlyList<string> typed:
                foreach (var rule in typed) yield return rule;
                break;
            case IEnumerable<string> strings:
                foreach (var rule in strings) yield return rule;
                break;
        }
    }
}
