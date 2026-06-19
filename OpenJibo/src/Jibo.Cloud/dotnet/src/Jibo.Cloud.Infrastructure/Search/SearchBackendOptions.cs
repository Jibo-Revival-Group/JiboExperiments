using Jibo.Cloud.Application.Abstractions;

namespace Jibo.Cloud.Infrastructure.Search;

public sealed class SearchBackendOptions
{
    public SearchBackendSpec Primary { get; init; } = SearchBackendSpec.None;

    public SearchBackendSpec? Fallback { get; init; }

    public int CacheTtlSeconds { get; init; } = 300;

    public int FailureCacheTtlSeconds { get; init; } = 45;

    public string? LlmInstructions { get; init; }

    public static SearchBackendOptions Create(
        string? primary,
        string? fallback,
        int cacheTtlSeconds,
        int failureCacheTtlSeconds,
        string? llmInstructions = null)
    {
        var primarySpec = SearchBackendSpecParser.Parse(primary);
        SearchBackendSpec? fallbackSpec = null;
        var parsedFallback = SearchBackendSpecParser.Parse(fallback);
        if (parsedFallback.IsUsable && !SpecsEquivalent(primarySpec, parsedFallback))
            fallbackSpec = parsedFallback;

        return new SearchBackendOptions
        {
            Primary = primarySpec,
            Fallback = fallbackSpec,
            CacheTtlSeconds = cacheTtlSeconds,
            FailureCacheTtlSeconds = failureCacheTtlSeconds,
            LlmInstructions = SearchInstructionsResolver.Normalize(llmInstructions)
        };
    }

    private static bool SpecsEquivalent(SearchBackendSpec left, SearchBackendSpec right)
    {
        return left.Kind == right.Kind &&
               string.Equals(left.Credential, right.Credential, StringComparison.Ordinal) &&
               string.Equals(left.Model, right.Model, StringComparison.OrdinalIgnoreCase);
    }
}
