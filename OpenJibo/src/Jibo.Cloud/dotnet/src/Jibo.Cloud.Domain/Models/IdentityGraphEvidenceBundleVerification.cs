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
    public string AdmissionRecommendation { get; init; } = "quarantine";
    public string AdmissionDecisionHash { get; init; } = string.Empty;
    public string SnapshotContentHash { get; init; } = string.Empty;
    public IReadOnlyList<string> Errors { get; init; } = [];
}
