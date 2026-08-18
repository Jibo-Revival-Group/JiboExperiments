using Jibo.Cloud.Application.Abstractions;
using Jibo.Cloud.Domain.Models;

namespace Jibo.Cloud.Application.Services;

/// <summary>
/// Selects enabled package metadata without loading or executing skill code.
/// Runtime adapters consume the resulting route in a later phase.
/// </summary>
public sealed class MetadataSkillRouter(
    ISkillRegistry skillRegistry,
    ILegacySkillAdapterRegistry legacySkillAdapters) : ISkillRouter
{
    public SkillRouteDecision? Route(SkillRoutingInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var intentCandidates = new[] { input.NluIntent, input.SemanticIntent }
            .Where(intent => !string.IsNullOrWhiteSpace(intent))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (intentCandidates.Length == 0) return null;

        var candidates = skillRegistry.GetInstalledSkills()
            .Where(skill => skill.State == SkillLifecycleState.Enabled && skill.Manifest is not null)
            // Built-in manifests are registered before their compatibility adapters are migrated.
            // They must remain visible to the registry without stealing live traffic from the
            // legacy implementation during that transition.
            .Where(skill => !string.Equals(skill.Manifest!.PackageType, "builtin",
                StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(skill.Manifest.Adapter, "legacy", StringComparison.OrdinalIgnoreCase) ||
                legacySkillAdapters.CanExecute(skill.Manifest.SkillId))
            .SelectMany(skill => skill.Manifest!.IntentBindings
                .Where(binding => intentCandidates.Contains(binding.Intent, StringComparer.OrdinalIgnoreCase))
                .Select(binding => BuildCandidate(skill, skill.Manifest!, binding, input)))
            .Where(candidate => candidate is not null)
            .Select(candidate => candidate!)
            .OrderByDescending(candidate => candidate.Score)
            .ThenByDescending(candidate => candidate.Binding.Priority)
            .ThenBy(candidate => candidate.Manifest.SkillId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(candidate => candidate.Manifest.Version, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var selected = candidates.FirstOrDefault();
        if (selected is null) return null;

        var routeKind = string.Equals(input.CurrentSkillId, selected.Manifest.SkillId,
                StringComparison.OrdinalIgnoreCase)
            ? "update"
            : string.IsNullOrWhiteSpace(input.CurrentSkillId) ? "launch" : "redirect";

        return new SkillRouteDecision
        {
            SkillId = selected.Manifest.SkillId,
            Version = selected.Manifest.Version,
            ExecutionTarget = ResolveExecutionTarget(selected.Manifest, input.PreferredExecutionTarget),
            Runtime = selected.Manifest.Runtime,
            MatchedIntent = selected.Binding.Intent,
            RouteKind = routeKind,
            Score = selected.Score,
            Reason = selected.Reason,
            MatchedEntities = selected.MatchedEntities
        };
    }

    private static Candidate? BuildCandidate(
        InstalledSkill installedSkill,
        SkillManifest manifest,
        SkillIntentBinding binding,
        SkillRoutingInput input)
    {
        var matchedEntities = binding.Match.Entities
            .Where(entity => input.Entities.TryGetValue(entity, out var value) && !string.IsNullOrWhiteSpace(value))
            .ToArray();
        var missingEntities = binding.Match.Entities
            .Where(entity => !matchedEntities.Contains(entity, StringComparer.OrdinalIgnoreCase))
            .ToArray();
        if (missingEntities.Length > 0) return null;

        var requiredContexts = binding.Match.Contexts
            .Where(context => !string.IsNullOrWhiteSpace(context))
            .ToArray();
        if (requiredContexts.Any(context => !input.Contexts.Contains(context))) return null;

        var supportedLanguages = binding.Match.Languages.Count > 0
            ? binding.Match.Languages
            : manifest.SupportedLanguages;
        var languageScore = 0;
        if (supportedLanguages.Count > 0)
        {
            var locale = input.Locale ?? string.Empty;
            var language = locale.Split('-', StringSplitOptions.RemoveEmptyEntries)[0];
            if (!supportedLanguages.Any(candidate =>
                    string.Equals(candidate, locale, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(candidate, language, StringComparison.OrdinalIgnoreCase)))
                return null;

            languageScore = 20;
        }

        var score = 1000 + binding.Priority + (matchedEntities.Length * 25) +
                    (requiredContexts.Length * 15) + languageScore;
        var reason = $"intent={binding.Intent}; priority={binding.Priority}; entities={matchedEntities.Length}";

        return new Candidate(manifest, binding, score, reason, matchedEntities);
    }

    private static string ResolveExecutionTarget(SkillManifest manifest, string? preferredTarget)
    {
        if (!string.Equals(manifest.ExecutionTarget, "both", StringComparison.OrdinalIgnoreCase))
            return manifest.ExecutionTarget;

        return string.Equals(preferredTarget, "robot", StringComparison.OrdinalIgnoreCase)
            ? "robot"
            : "server";
    }

    private sealed record Candidate(
        SkillManifest Manifest,
        SkillIntentBinding Binding,
        int Score,
        string Reason,
        IReadOnlyList<string> MatchedEntities);
}
