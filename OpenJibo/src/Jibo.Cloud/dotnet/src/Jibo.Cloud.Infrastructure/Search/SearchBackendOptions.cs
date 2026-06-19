using Jibo.Cloud.Application.Abstractions;

namespace Jibo.Cloud.Infrastructure.Search;

public sealed class SearchBackendOptions
{
    public SearchBackendKind Backend { get; set; } = SearchBackendKind.None;

    public SearchBackendKind? FallbackBackend { get; set; }

    public string? ApiKey { get; set; }

    public string ApiEndpoint { get; set; } = "http://api.wolframalpha.com/v1/spoken";

    public int CacheTtlSeconds { get; set; } = 300;

    public int FailureCacheTtlSeconds { get; set; } = 45;
}
