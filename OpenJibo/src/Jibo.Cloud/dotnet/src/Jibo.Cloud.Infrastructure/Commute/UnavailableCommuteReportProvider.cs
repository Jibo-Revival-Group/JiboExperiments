using Jibo.Cloud.Application.Abstractions;
using Jibo.Runtime.Abstractions;

namespace Jibo.Cloud.Infrastructure.Commute;

public sealed class UnavailableCommuteReportProvider : ICommuteReportProvider
{
    public Task<CommuteReportSnapshot?> GetReportAsync(
        TurnContext turn,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<CommuteReportSnapshot?>(null);
    }
}
