namespace Jibo.Cloud.Domain.Models;

public sealed class IdentityGraphEvidenceBundle
{
    public string BundleVersion { get; init; } = "identity-graph-evidence-bundle-v1";
    public string AccountId { get; init; } = string.Empty;
    public string LoopId { get; init; } = string.Empty;
    public string RobotId { get; init; } = string.Empty;
    public string DeviceId { get; init; } = string.Empty;
    public string SnapshotContentHash { get; init; } = string.Empty;
    public string SnapshotSignature { get; init; } = string.Empty;
    public string AdmissionDecisionHash { get; init; } = string.Empty;
    public string AdmissionSignature { get; init; } = string.Empty;
    public string AdmissionRecommendation { get; init; } = "quarantine";
    public int PeopleCount { get; init; }
    public int MemberCount { get; init; }
    public int RelationshipCount { get; init; }
    public int EvidenceSignalCount { get; init; }
    public IReadOnlyList<string> BlockingEvidence { get; init; } = [];
    public string Payload { get; init; } = string.Empty;
    public string Envelope { get; init; } = string.Empty;
    public string BundleHash { get; init; } = string.Empty;
    public string SignatureAlgorithm { get; init; } = string.Empty;
    public string SignatureKeyId { get; init; } = string.Empty;
    public string Signature { get; init; } = string.Empty;
}
