namespace Jibo.Cloud.Domain.Models;

public sealed class LoopMemberRecord
{
    public string Id { get; init; } = $"mbr-{Guid.NewGuid():N}";
    public string LoopId { get; init; } = string.Empty;
    public string? AccountId { get; init; }
    public string? Email { get; init; }
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
    public string? Gender { get; init; }
    public long? Birthday { get; init; }
    public bool IsChild { get; init; }
    public string? PhoneNumber { get; init; }
    public string Status { get; init; } = "active";
    public string Type { get; init; } = "owner";
    public string? Nickname { get; init; }
    public string? PhoneticName { get; init; }
    public bool FaceEnrolled { get; init; }
    public bool VoiceEnrolled { get; init; }
    public string? LegalGuardianId { get; init; }
    public string? AgreementId { get; init; }
    public DateTimeOffset CreatedUtc { get; init; } = DateTimeOffset.UtcNow;
    /// <summary>
    /// When set, a Portal edit owns name/gender until the robot's roster catches up.
    /// </summary>
    public DateTimeOffset? PortalEditedUtc { get; init; }
}