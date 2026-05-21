namespace Jibo.Cloud.Domain.Models;

public sealed class HolidayRecord
{
    public string Id { get; init; } = $"holiday-{Guid.NewGuid():N}";
    public string EventId { get; init; } = string.Empty;
    public string Name { get; init; } = "Holiday";
    public string Category { get; init; } = "holiday";
    public string? Subcategory { get; init; }
    public string LoopId { get; init; } = "openjibo-default-loop";
    public string? MemberId { get; init; }
    public bool IsEnabled { get; init; } = true;
    public DateOnly Date { get; init; } = DateOnly.FromDateTime(DateTime.UtcNow);
    public DateOnly? EndDate { get; init; }
    public string Source { get; init; } = "nager-date";
    public string CountryCode { get; init; } = "US";
    public DateTimeOffset Created { get; init; } = DateTimeOffset.UtcNow;
}