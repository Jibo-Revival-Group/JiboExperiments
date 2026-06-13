namespace Jibo.Cloud.Domain.Models;

public sealed class UserRecord
{
    public string Id { get; init; } = $"usr-{Guid.NewGuid():N}";
    public string Email { get; init; } = string.Empty;
    public string PasswordHash { get; init; } = string.Empty;
    public string Salt { get; init; } = string.Empty;
    public string FirstName { get; init; } = string.Empty;
    public string LastName { get; init; } = string.Empty;
    public string? Gender { get; init; }
    public long? Birthday { get; init; }
    public string AccessKeyId { get; init; } = $"ak-{Guid.NewGuid():N}";
    public string SecretAccessKey { get; init; } = $"sk-{Guid.NewGuid():N}";
    public bool IsActive { get; init; } = true;
    public DateTimeOffset CreatedUtc { get; init; } = DateTimeOffset.UtcNow;
}
