namespace Jibo.Cloud.Domain.Models;

public sealed class IdentityGraphSnapshot
{
    public string AccountId { get; init; } = string.Empty;
    public string LoopId { get; init; } = string.Empty;
    public string RobotId { get; init; } = string.Empty;
    public string DeviceId { get; init; } = string.Empty;
    public IReadOnlyList<PersonRecord> People { get; init; } = [];
    public IReadOnlyList<LoopMemberRecord> Members { get; init; } = [];
    public IReadOnlyList<IdentityGraphRelationship> Relationships { get; init; } = [];
}
