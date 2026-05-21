namespace Jibo.Cloud.Domain.Models;

public sealed class CalendarEventRecord
{
    public string Id { get; init; } = $"calendar-{Guid.NewGuid():N}";
    public string LoopId { get; init; } = "openjibo-default-loop";
    public string Summary { get; init; } = "Calendar event";
    public string? TimeLabel { get; init; }
    public DateOnly Date { get; init; } = DateOnly.FromDateTime(DateTime.UtcNow);
    public DateOnly? EndDate { get; init; }
    public bool IsAllDay { get; init; }
    public bool IsEnabled { get; init; } = true;
    public string Source { get; init; } = "manual";
    public string? MemberId { get; init; }
    public DateTimeOffset Created { get; init; } = DateTimeOffset.UtcNow;
}