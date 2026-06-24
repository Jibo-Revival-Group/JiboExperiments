namespace Jibo.Cloud.Domain.Models;

public sealed class IdentityGraphAdmissionAssessment
{
    public string PolicyVersion { get; init; } = "deny-by-evidence-v1";
    public string Recommendation { get; init; } = "quarantine";
    public IReadOnlyList<string> Reasons { get; init; } = [];
    public IReadOnlyList<string> RequiredEvidence { get; init; } = [];
    public IReadOnlyList<string> SatisfiedEvidence { get; init; } = [];
    public IReadOnlyList<string> BlockingEvidence { get; init; } = [];
    public IReadOnlyList<string> RecommendedActions { get; init; } = [];
}
