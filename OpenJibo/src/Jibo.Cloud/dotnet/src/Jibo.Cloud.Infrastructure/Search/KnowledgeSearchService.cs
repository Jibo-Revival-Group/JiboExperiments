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

        if (options.Primary.IsUsable)
        {
            var primaryResult = await TrySearchBackendAsync(options.Primary, query, cancellationToken);
            if (primaryResult is not null) return primaryResult;
        }

        if (options.Fallback is null || !options.Fallback.IsUsable)
            return null;

        return await TrySearchBackendAsync(options.Fallback, query, cancellationToken);
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
            return null;
        }

        return await provider.SearchAsync(
            new KnowledgeSearchRequest(query, backendSpec),
            cancellationToken);
    }
}