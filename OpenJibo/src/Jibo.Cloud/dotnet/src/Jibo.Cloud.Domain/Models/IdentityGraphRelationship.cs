namespace Jibo.Cloud.Domain.Models;

public sealed class IdentityGraphRelationship
{
    public string SubjectId { get; init; } = string.Empty;
    public string SubjectKind { get; init; } = string.Empty;
    public string Relationship { get; init; } = string.Empty;
    public string ObjectId { get; init; } = string.Empty;
    public string ObjectKind { get; init; } = string.Empty;
    public string LoopId { get; init; } = string.Empty;
}