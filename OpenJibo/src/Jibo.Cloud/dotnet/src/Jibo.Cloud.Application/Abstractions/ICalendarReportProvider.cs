using Jibo.Runtime.Abstractions;

namespace Jibo.Cloud.Application.Abstractions;

public interface ICalendarReportProvider
{
    Task<CalendarReportSnapshot?> GetReportAsync(TurnContext turn, CancellationToken cancellationToken = default);
}

public sealed record CalendarReportSnapshot(
    IReadOnlyList<string> EventSummaries,
    IReadOnlyList<string> EventTimesOnAt,
    IReadOnlyList<string> TomorrowEventSummaries,
    bool HasServiceError = false);
