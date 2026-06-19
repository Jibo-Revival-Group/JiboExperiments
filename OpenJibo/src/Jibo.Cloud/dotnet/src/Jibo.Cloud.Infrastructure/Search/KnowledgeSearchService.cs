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
        IsBackendConfigured(options.Backend) ||
        (options.FallbackBackend is not null && IsBackendConfigured(options.FallbackBackend.Value));

    public async Task<KnowledgeSearchResult?> SearchAsync(
        string query,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query)) return null;

        var primaryResult = await TrySearchBackendAsync(
            options.Backend,
            query,
            useFallbackSettings: false,
            cancellationToken);
        if (primaryResult is not null) return primaryResult;

        if (options.FallbackBackend is null ||
            options.FallbackBackend.Value == SearchBackendKind.None ||
            options.FallbackBackend.Value == options.Backend)
            return null;

        return await TrySearchBackendAsync(
            options.FallbackBackend.Value,
            query,
            useFallbackSettings: true,
            cancellationToken);
    }

    private async Task<KnowledgeSearchResult?> TrySearchBackendAsync(
        SearchBackendKind backendKind,
        string query,
        bool useFallbackSettings,
        CancellationToken cancellationToken)
    {
        if (!IsBackendConfigured(backendKind)) return null;

        if (!_providers.TryGetValue(backendKind, out var provider))
        {
            logger.LogDebug("Search backend {BackendKind} is configured but no provider is registered.", backendKind);
            return null;
        }

        return await provider.SearchAsync(
            new KnowledgeSearchRequest(query, backendKind, useFallbackSettings),
            cancellationToken);
    }

    private bool IsBackendConfigured(SearchBackendKind backendKind)
    {
        if (backendKind == SearchBackendKind.None) return false;

        return backendKind switch
        {
            SearchBackendKind.Wolfram or SearchBackendKind.ChatGPT =>
                !string.IsNullOrWhiteSpace(options.ApiKey),
            SearchBackendKind.Ollama => true,
            _ => false
        };
    }
}
