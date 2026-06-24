namespace Jibo.Cloud.Domain.Models;

public sealed class IdentityGraphEvidenceBundleVerification
{
    public bool IsValid { get; init; }
    public string EnvelopeVersion { get; init; } = string.Empty;
    public string BundleVersion { get; init; } = string.Empty;
    public string BundleHash { get; init; } = string.Empty;
    public string ComputedBundleHash { get; init; } = string.Empty;
    public string SignatureAlgorithm { get; init; } = string.Empty;
    public string SignatureKeyId { get; init; } = string.Empty;
    public string Signature { get; init; } = string.Empty;
    public string ComputedSignature { get; init; } = string.Empty;
    public string AdmissionPolicyVersion { get; init; } = string.Empty;
    public string AdmissionRecommendation { get; init; } = "quarantine";
    public IReadOnlyList<string> AdmissionReasons { get; init; } = [];
    public IReadOnlyList<string> SatisfiedEvidence { get; init; } = [];
    public IReadOnlyList<string> RecommendedActions { get; init; } = [];
    public string AdmissionDecisionHash { get; init; } = string.Empty;
    public string SnapshotContentHash { get; init; } = string.Empty;
    public string AccountId { get; init; } = string.Empty;
    public string LoopId { get; init; } = string.Empty;
    public string RobotId { get; init; } = string.Empty;
    public string DeviceId { get; init; } = string.Empty;
    public int PeopleCount { get; init; }
    public int MemberCount { get; init; }
    public int RelationshipCount { get; init; }
    public int EvidenceSignalCount { get; init; }
    public IReadOnlyList<string> BlockingEvidence { get; init; } = [];
    public IReadOnlyList<string> Errors { get; init; } = [];
}
