using Jibo.Cloud.Application.Abstractions;

namespace Jibo.Cloud.Application.Services;

public sealed partial class JiboInteractionService
{
    private async Task<JiboInteractionDecision> BuildFunFactDecisionAsync(
        JiboExperienceCatalog catalog,
        CancellationToken cancellationToken)
    {
        string? fact = null;
        if (funFactProvider is not null)
        {
            try
            {
                fact = await funFactProvider.GetRandomFactAsync(cancellationToken);
            }
            catch
            {
                // Fall back to local facts when the provider throws unexpectedly.
            }
        }

        fact ??= randomizer.Choose(catalog.FunFactFallbacks);

        return new JiboInteractionDecision(
            "fun_fact",
            fact,
            "chitchat-skill",
            new Dictionary<string, object?>
            {
                ["mim_id"] = "runtime-fun-fact",
                ["mim_type"] = "announcement",
                ["prompt_id"] = "RUNTIME_FUN_FACT",
                ["replyType"] = "fun_fact",
                ["factCategory"] = "fun_fact"
            });
    }
}
