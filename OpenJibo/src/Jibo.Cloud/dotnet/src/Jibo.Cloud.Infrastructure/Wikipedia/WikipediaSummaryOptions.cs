namespace Jibo.Cloud.Infrastructure.Wikipedia;

public sealed class WikipediaSummaryOptions
{
    public string ApiBaseUrl { get; set; } = "https://en.wikipedia.org/w/api.php";

    public string RestBaseUrl { get; set; } = "https://en.wikipedia.org/api/rest_v1";

    /// <summary>
    /// Optional override. When empty, requests use OpenJibo/{cloud version} (jiborevived.com).
    /// </summary>
    public string? UserAgent { get; set; }

    public int OpenSearchLimit { get; set; } = 5;

    public int FailureCacheTtlSeconds { get; set; } = 45;

    public int SuccessCacheTtlSeconds { get; set; } = 300;
}
