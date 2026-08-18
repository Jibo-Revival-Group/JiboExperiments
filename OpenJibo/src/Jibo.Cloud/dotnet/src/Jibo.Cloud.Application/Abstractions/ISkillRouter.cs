using Jibo.Cloud.Domain.Models;

namespace Jibo.Cloud.Application.Abstractions;

public interface ISkillRouter
{
    SkillRouteDecision? Route(SkillRoutingInput input);
}

public sealed class SkillRoutingInput
{
    public string? NluIntent { get; init; }
    public string? SemanticIntent { get; init; }
    public IReadOnlyDictionary<string, string> Entities { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    public string? Locale { get; init; }
    public IReadOnlySet<string> Contexts { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    public string? CurrentSkillId { get; init; }
    public string? PreferredExecutionTarget { get; init; }
}

public sealed class SkillRouteDecision
{
    public string SkillId { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public string ExecutionTarget { get; init; } = string.Empty;
    public string Runtime { get; init; } = string.Empty;
    public string MatchedIntent { get; init; } = string.Empty;
    public string RouteKind { get; init; } = string.Empty;
    public int Score { get; init; }
    public string Reason { get; init; } = string.Empty;
    public IReadOnlyList<string> MatchedEntities { get; init; } = [];
}
