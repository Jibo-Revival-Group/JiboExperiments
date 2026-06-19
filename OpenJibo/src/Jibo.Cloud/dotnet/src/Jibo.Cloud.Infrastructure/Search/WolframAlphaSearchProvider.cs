using System.Collections.Concurrent;
using Jibo.Cloud.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace Jibo.Cloud.Infrastructure.Search;

public sealed class WolframAlphaSearchProvider(
    HttpClient httpClient,
    SearchBackendOptions options,
    ILogger<WolframAlphaSearchProvider> logger)
    : IKnowledgeSearchProvider
{
    private static readonly string[] FailurePhrases =
    [
        "did not understand",
        "cannot answer",
        "can't answer",
        "no spoken result available"
    ];

    private readonly ConcurrentDictionary<string, CacheEntry> _cache =
        new(StringComparer.OrdinalIgnoreCase);

    public SearchBackendKind Kind => SearchBackendKind.Wolfram;

    public async Task<KnowledgeSearchResult?> SearchAsync(
        KnowledgeSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.BackendSpec.Kind != SearchBackendKind.Wolfram ||
            string.IsNullOrWhiteSpace(request.BackendSpec.Credential) ||
            string.IsNullOrWhiteSpace(request.Query))
            return null;

        var normalizedQuery = request.Query.Trim();
        var cacheKey = BuildCacheKey(request.BackendSpec, normalizedQuery);
        if (TryGetCachedValue(cacheKey, out var cachedResult))
            return cachedResult;

        try
        {
            var requestUri = BuildRequestUri(request.BackendSpec.Credential, normalizedQuery);
            using var response = await httpClient.GetAsync(requestUri, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                SetCachedValue(cacheKey, null, options.FailureCacheTtlSeconds);
                return null;
            }

            var answerText = (await response.Content.ReadAsStringAsync(cancellationToken)).Trim();
            if (!IsUsableAnswer(answerText))
            {
                SetCachedValue(cacheKey, null, options.FailureCacheTtlSeconds);
                return null;
            }

            var result = new KnowledgeSearchResult(answerText, SearchBackendKind.Wolfram);
            SetCachedValue(cacheKey, result, options.CacheTtlSeconds);
            return result;
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(exception, "Wolfram Alpha lookup failed.");
            SetCachedValue(cacheKey, null, options.FailureCacheTtlSeconds);
            return null;
        }
    }

    private static Uri BuildRequestUri(string appId, string query)
    {
        var endpoint = SearchBackendSettingsResolver.DefaultWolframEndpoint;
        var builder = new UriBuilder(endpoint);
        var queryString = $"appid={Uri.EscapeDataString(appId)}&i={Uri.EscapeDataString(query)}";
        builder.Query = queryString;
        return builder.Uri;
    }

    private static bool IsUsableAnswer(string answerText)
    {
        if (string.IsNullOrWhiteSpace(answerText)) return false;

        var lowered = answerText.ToLowerInvariant();
        return !FailurePhrases.Any(phrase => lowered.Contains(phrase, StringComparison.Ordinal));
    }

    private static string BuildCacheKey(SearchBackendSpec spec, string query)
    {
        return $"wolfram|{spec.Credential}|{query}";
    }

    private bool TryGetCachedValue(string cacheKey, out KnowledgeSearchResult? result)
    {
        result = null;
        if (!_cache.TryGetValue(cacheKey, out var entry) || entry.ExpiresAtUtc <= DateTimeOffset.UtcNow)
            return false;

        result = entry.Result;
        return true;
    }

    private void SetCachedValue(string cacheKey, KnowledgeSearchResult? result, int ttlSeconds)
    {
        _cache[cacheKey] = new CacheEntry(
            result,
            DateTimeOffset.UtcNow.AddSeconds(Math.Max(1, ttlSeconds)));
    }

    private sealed record CacheEntry(KnowledgeSearchResult? Result, DateTimeOffset ExpiresAtUtc);
}
