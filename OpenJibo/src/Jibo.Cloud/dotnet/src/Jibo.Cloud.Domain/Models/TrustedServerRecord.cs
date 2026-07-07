namespace Jibo.Cloud.Domain.Models;

public sealed class TrustedServerRecord
{
    public string ServerId { get; init; } = Guid.NewGuid().ToString("N");
    public string CanonicalHost { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string ServerKind { get; init; } = "managed";
    public bool IsListed { get; init; } = true;
    public bool AcceptsPublicConnections { get; init; } = true;
    public bool ParticipatesInCloudSync { get; init; } = true;
    public bool RequiresHttps { get; init; } = true;
    public bool IsTrustRoot { get; init; }
    public bool IsActive { get; init; } = true;
    public string Description { get; init; } = string.Empty;
    public DateTimeOffset RegisteredAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastSeenAtUtc { get; init; }
}
