namespace Jibo.Cloud.Application.Abstractions;

public enum WikipediaSummaryOutcome
{
    Found,
    NotFound,
    Unavailable
}

public interface IWikipediaSummaryProvider
{
    Task<WikipediaSummaryResult> GetSummaryAsync(
        string subject,
        CancellationToken cancellationToken = default,
        bool bypassCache = false);
}

public sealed record WikipediaSummaryResult(
    string? Summary,
    WikipediaSummaryOutcome Outcome)
{
    public static WikipediaSummaryResult Found(string summary) =>
        new(summary, WikipediaSummaryOutcome.Found);

    public static WikipediaSummaryResult NotFound() =>
        new(null, WikipediaSummaryOutcome.NotFound);

    public static WikipediaSummaryResult Unavailable() =>
        new(null, WikipediaSummaryOutcome.Unavailable);
}
