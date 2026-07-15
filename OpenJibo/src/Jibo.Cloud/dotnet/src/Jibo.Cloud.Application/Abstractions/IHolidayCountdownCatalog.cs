namespace Jibo.Cloud.Application.Abstractions;

public sealed record HolidayCountdownEntry(string CanonicalName, HolidayDateRule Rule);

public sealed class HolidayDateRule
{
    public required string Type { get; init; }
    public int? Month { get; init; }
    public int? Day { get; init; }
    public string? DayOfWeek { get; init; }
    public int? Occurrence { get; init; }
    public int? Days { get; init; }
    public IReadOnlyDictionary<string, string>? Dates { get; init; }
}

public interface IHolidayCountdownCatalog
{
    bool TryResolve(string normalizedPhrase, out HolidayCountdownEntry entry);
}
