namespace Jibo.Cloud.Application.Abstractions;

public interface IWikipediaSummaryProvider
{
    Task<string?> GetSummaryAsync(string subject, CancellationToken cancellationToken = default);
}
