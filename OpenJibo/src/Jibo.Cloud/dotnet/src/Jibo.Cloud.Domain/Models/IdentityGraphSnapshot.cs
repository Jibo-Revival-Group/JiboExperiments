namespace Jibo.Cloud.Domain.Models;

public sealed class IdentityGraphSnapshot
{
    public string AccountId { get; init; } = string.Empty;
    public string LoopId { get; init; } = string.Empty;
    public string RobotId { get; init; } = string.Empty;
    public string DeviceId { get; init; } = string.Empty;
    public int SnapshotVersion { get; init; } = 1;
    public string ContentHash { get; init; } = string.Empty;
    public string SignatureAlgorithm { get; init; } = string.Empty;
    public string SignatureKeyId { get; init; } = string.Empty;
    public string SignaturePayload { get; init; } = string.Empty;
    public string Signature { get; init; } = string.Empty;
    public IdentityGraphAdmissionAssessment AdmissionAssessment { get; init; } = new();
    public IReadOnlyList<PersonRecord> People { get; init; } = [];
    public IReadOnlyList<LoopMemberRecord> Members { get; init; } = [];
    public IReadOnlyList<IdentityGraphRelationship> Relationships { get; init; } = [];
    public IReadOnlyList<IdentityGraphEvidenceSignal> EvidenceSignals { get; init; } = [];
}
