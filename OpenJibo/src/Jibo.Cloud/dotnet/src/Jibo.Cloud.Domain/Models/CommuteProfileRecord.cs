namespace Jibo.Cloud.Domain.Models;

public sealed class CommuteProfileRecord
{
    public string Id { get; init; } = $"commute-{Guid.NewGuid():N}";
    public string LoopId { get; init; } = "openjibo-default-loop";
    public string? MemberId { get; init; }
    public bool IsEnabled { get; init; } = true;
    public bool IsComplete { get; init; } = true;
    public string Mode { get; init; } = "driving";
    public int WorkHour { get; init; } = 8;
    public int WorkMinute { get; init; } = 30;
    public string? OriginName { get; init; } = "home";
    public string? DestinationName { get; init; } = "work";
    public int TypicalDurationMinutes { get; init; } = 25;
    public DateTimeOffset Created { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset Updated { get; init; } = DateTimeOffset.UtcNow;
}
