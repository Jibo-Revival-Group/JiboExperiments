namespace Jibo.Cloud.Domain.Models;

public sealed class RecognitionObservationRecord
{
    public string ObservationId { get; init; } = $"rec-{Guid.NewGuid():N}";
    public string LoopId { get; init; } = string.Empty;
    public string MemberId { get; init; } = string.Empty;
    public string RobotId { get; init; } = string.Empty;
    public string Modality { get; init; } = string.Empty;
    public string Outcome { get; init; } = "recognized";
    public double? Confidence { get; init; }
    public string? Source { get; init; }
    public DateTimeOffset ObservedUtc { get; init; } = DateTimeOffset.UtcNow;
}