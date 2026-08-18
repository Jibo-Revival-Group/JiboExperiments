using Jibo.Cloud.Application.Abstractions;
using Jibo.Cloud.Domain.Models;
using Jibo.Runtime.Abstractions;

namespace Jibo.Cloud.Application.Services;

/// <summary>
/// Compatibility bridge for built-in packages whose behavior still lives in the
/// legacy interaction service. It deliberately delegates execution instead of
/// duplicating the existing decision builders.
/// </summary>
public sealed class LegacySkillAdapterRegistry(
    ISkillRegistry skillRegistry,
    IServiceProvider serviceProvider) : ILegacySkillAdapterRegistry
{
    public bool CanExecute(string skillId)
    {
        return skillRegistry.GetInstalledSkills().Any(skill =>
            skill.State == SkillLifecycleState.Enabled &&
            skill.Manifest is not null &&
            string.Equals(skill.Manifest.SkillId, skillId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(skill.Manifest.PackageType, "builtin", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(skill.Manifest.Adapter, "legacy", StringComparison.OrdinalIgnoreCase));
    }

    public async Task<JiboInteractionDecision?> ExecuteAsync(
        SkillRouteDecision route,
        TurnContext turn,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(route);
        ArgumentNullException.ThrowIfNull(turn);

        if (!CanExecute(route.SkillId)) return null;

        var interactionService = serviceProvider.GetService(typeof(JiboInteractionService)) as JiboInteractionService
                                  ?? throw new InvalidOperationException(
                                      "JiboInteractionService is not registered for the legacy skill adapter.");
        return await interactionService.BuildLegacySkillDecisionAsync(turn, cancellationToken);
    }
}
