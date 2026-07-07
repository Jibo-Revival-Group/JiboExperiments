namespace Jibo.Cloud.Domain.Models;

public sealed class TrustedServerAdmissionRecord
{
    public string AdmissionId { get; init; } = Guid.NewGuid().ToString("N");
    public string ServerId { get; init; } = string.Empty;
    public string CanonicalHost { get; init; } = string.Empty;
    public string ServerKind { get; init; } = "managed";
    public string Action { get; init; } = "admit";
    public string ActorDeviceId { get; init; } = string.Empty;
    public string ActorFriendlyId { get; init; } = string.Empty;
    public string? Reason { get; init; }
    public string SignatureAlgorithm { get; init; } = "HMAC-SHA256";
    public string SignatureKeyId { get; init; } = "open-jibo-local-trusted-server-admission-v1";
    public string Payload { get; init; } = string.Empty;
    public string Signature { get; init; } = string.Empty;
    public DateTimeOffset CreatedUtc { get; init; } = DateTimeOffset.UtcNow;
}
