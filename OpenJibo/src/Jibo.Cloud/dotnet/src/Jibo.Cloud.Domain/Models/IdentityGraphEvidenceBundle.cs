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
    public string AdmissionPolicyVersion { get; init; } = "deny-by-evidence-v1";
    public string AdmissionRecommendation { get; init; } = "quarantine";
    public IReadOnlyList<string> AdmissionReasons { get; init; } = [];
    public IReadOnlyList<string> RequiredEvidence { get; init; } = [];
    public IReadOnlyList<string> SatisfiedEvidence { get; init; } = [];
    public IReadOnlyList<string> RecommendedActions { get; init; } = [];
    public IReadOnlyList<string> RevocationChecks { get; init; } = [];
    public IReadOnlyList<string> RevocationAnchors { get; init; } = [];
    public string RevocationListHash { get; init; } = string.Empty;
    public string TrustPurpose { get; init; } = "peer-admission-retention";
    public string PeerTransportStatus { get; init; } = "not-enabled";
    public string ReplicationReadiness { get; init; } = "blocked";
    public string SyncDirection { get; init; } = "snapshot-retention-only";
    public string PeerAdmissionMode { get; init; } = "offline-signed-evidence";
    public string RetentionPolicy { get; init; } = "owner-retained-until-peer-admission";
    public string AdmissionReviewStatus { get; init; } = "requires-local-revocation-check";
    public bool DirectPeerTransportAllowed { get; init; }
    public int PeopleCount { get; init; }
    public int MemberCount { get; init; }
    public int RelationshipCount { get; init; }
    public int EvidenceSignalCount { get; init; }
    public IReadOnlyList<string> RelationshipKinds { get; init; } = [];
    public IReadOnlyList<string> EvidenceSignalKinds { get; init; } = [];
    public IReadOnlyList<string> BlockingEvidence { get; init; } = [];
    public string Payload { get; init; } = string.Empty;
    public string Envelope { get; init; } = string.Empty;
    public string BundleHash { get; init; } = string.Empty;
    public string SignatureAlgorithm { get; init; } = string.Empty;
    public string SignatureKeyId { get; init; } = string.Empty;
    public string Signature { get; init; } = string.Empty;
}