using Jibo.Runtime.Abstractions;

namespace Jibo.Cloud.Application.Abstractions;

public interface ICommuteReportProvider
{
    Task<CommuteReportSnapshot?> GetReportAsync(TurnContext turn, CancellationToken cancellationToken = default);
}

public sealed record CommuteReportSnapshot(
    string LocationName,
    string Summary,
    int DurationMinutes,
    string? Mode = null,
    bool EventIsEarly = false);
