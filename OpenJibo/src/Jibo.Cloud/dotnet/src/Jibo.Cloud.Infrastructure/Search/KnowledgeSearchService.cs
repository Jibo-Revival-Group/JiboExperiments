using Jibo.Cloud.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace Jibo.Cloud.Infrastructure.Search;

public sealed class KnowledgeSearchService(
    SearchBackendOptions options,
    IEnumerable<IKnowledgeSearchProvider> providers,
    ILogger<KnowledgeSearchService> logger)
    : IKnowledgeSearchService
{
    private readonly IReadOnlyDictionary<SearchBackendKind, IKnowledgeSearchProvider> _providers =
        providers.ToDictionary(provider => provider.Kind);

    public bool IsConfigured =>
        options.Primary.IsUsable || options.Fallback?.IsUsable == true;

    public async Task<KnowledgeSearchResult?> SearchAsync(
        string query,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query)) return null;

        KnowledgeSearchResult? primaryResult = null;
        if (options.Primary.IsUsable)
        {
            primaryResult = await TrySearchBackendAsync(options.Primary, query, cancellationToken);
            if (primaryResult?.Outcome == KnowledgeSearchOutcome.Found)
                return primaryResult;
        }

        if (options.Fallback is null || !options.Fallback.IsUsable)
            return primaryResult ?? KnowledgeSearchResult.Unavailable(options.Primary.Kind);

        var fallbackResult = await TrySearchBackendAsync(options.Fallback, query, cancellationToken);
        if (fallbackResult?.Outcome == KnowledgeSearchOutcome.Found)
            return fallbackResult;

        return CombineFailedAttempts(primaryResult, fallbackResult, options.Primary.Kind, options.Fallback.Kind);
    }

    private async Task<KnowledgeSearchResult?> TrySearchBackendAsync(
        SearchBackendSpec backendSpec,
        string query,
        CancellationToken cancellationToken)
    {
        if (!_providers.TryGetValue(backendSpec.Kind, out var provider))
        {
            logger.LogDebug(
                "Search backend {BackendKind} is configured but no provider is registered.",
                backendSpec.Kind);
            return KnowledgeSearchResult.Unavailable(backendSpec.Kind);
        }

        var result = await provider.SearchAsync(
            new KnowledgeSearchRequest(query, backendSpec),
            cancellationToken);
        return result ?? KnowledgeSearchResult.Unavailable(backendSpec.Kind);
    }

    private static KnowledgeSearchResult CombineFailedAttempts(
        KnowledgeSearchResult? primary,
        KnowledgeSearchResult? fallback,
        SearchBackendKind primaryKind,
        SearchBackendKind fallbackKind)
    {
        var primaryOutcome = primary?.Outcome ?? KnowledgeSearchOutcome.Unavailable;
        var fallbackOutcome = fallback?.Outcome ?? KnowledgeSearchOutcome.Unavailable;

        if (primaryOutcome == KnowledgeSearchOutcome.Unavailable &&
            fallbackOutcome == KnowledgeSearchOutcome.Unavailable)
            return KnowledgeSearchResult.Unavailable(primary?.BackendKind ?? primaryKind);

        return KnowledgeSearchResult.NotFound(fallback?.BackendKind ?? primary?.BackendKind ?? fallbackKind);
    }
}
