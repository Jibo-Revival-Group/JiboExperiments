using Jibo.Cloud.Application.Abstractions;
using Jibo.Runtime.Abstractions;

namespace Jibo.Cloud.Application.Services;

public sealed partial class JiboInteractionService
{
    private static JiboInteractionDecision BuildModularSkillDecision(SkillRouteDecision route)
    {
        return new JiboInteractionDecision(
            route.MatchedIntent,
            string.Empty,
            route.SkillId,
            new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["skillId"] = route.SkillId,
                ["skillRoute"] = true,
                ["routeKind"] = route.RouteKind,
                ["matchedIntent"] = route.MatchedIntent,
                ["executionTarget"] = route.ExecutionTarget,
                ["runtime"] = route.Runtime,
                ["version"] = route.Version,
                ["routeScore"] = route.Score,
                ["routeReason"] = route.Reason,
                ["matchedEntities"] = route.MatchedEntities
            },
            SkillRoute: route);
    }

    private static string? ReadSkillAttribute(TurnContext turn, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (turn.Attributes.TryGetValue(key, out var value) &&
                !string.IsNullOrWhiteSpace(value?.ToString()))
                return value!.ToString();
        }

        return null;
    }

    private static IReadOnlySet<string> ReadSkillContexts(TurnContext turn)
    {
        var contexts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in new[] { "skillContexts", "contextBindings" })
        {
            if (!turn.Attributes.TryGetValue(key, out var value) || value is null) continue;

            if (value is IEnumerable<string> strings)
            {
                contexts.UnionWith(strings.Where(context => !string.IsNullOrWhiteSpace(context)));
                continue;
            }

            var text = value.ToString();
            if (!string.IsNullOrWhiteSpace(text)) contexts.Add(text);
        }

        return contexts;
    }
}
