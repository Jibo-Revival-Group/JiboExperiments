namespace Jibo.Cloud.Domain.Models;

public sealed class GreetingPresenceRecord
{
    public string Id { get; init; } = $"greeting-presence-{Guid.NewGuid():N}";
    public string AccountId { get; init; } = "usr_openjibo_owner";
    public string LoopId { get; init; } = "openjibo-default-loop";
    public string PersonId { get; init; } = string.Empty;
    public string? SpeakerId { get; init; }
    public string? PreferredName { get; init; }
    public DateTimeOffset LastSeenUtc { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastGreetedUtc { get; init; }
    public string? LastGreetingRoute { get; init; }
    public string? LastGreetingIntent { get; init; }
    public DateTimeOffset CreatedUtc { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedUtc { get; init; } = DateTimeOffset.UtcNow;
}
