using Jibo.Cloud.Application.Abstractions;
using Jibo.Runtime.Abstractions;

namespace Jibo.Cloud.Infrastructure.Calendar;

public sealed class UnavailableCalendarReportProvider : ICalendarReportProvider
{
    public Task<CalendarReportSnapshot?> GetReportAsync(
        TurnContext turn,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<CalendarReportSnapshot?>(null);
    }
}