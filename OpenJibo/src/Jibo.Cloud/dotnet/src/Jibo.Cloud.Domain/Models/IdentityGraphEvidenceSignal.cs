namespace Jibo.Cloud.Domain.Models;

public sealed class IdentityGraphEvidenceSignal
{
    public string SignalKind { get; init; } = string.Empty;
    public string SignalId { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
    public string Role { get; init; } = "corroborating";
    public string LoopId { get; init; } = string.Empty;
}