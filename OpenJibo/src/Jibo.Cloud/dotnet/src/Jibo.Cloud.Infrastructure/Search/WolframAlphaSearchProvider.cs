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
        string query,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(options.ApiKey) || string.IsNullOrWhiteSpace(query))
            return null;

        var normalizedQuery = query.Trim();
        if (TryGetCachedValue(normalizedQuery, out var cachedResult))
            return cachedResult;

        try
        {
            var requestUri = BuildRequestUri(normalizedQuery);
            using var response = await httpClient.GetAsync(requestUri, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                SetCachedValue(normalizedQuery, null, options.FailureCacheTtlSeconds);
                return null;
            }

            var answerText = (await response.Content.ReadAsStringAsync(cancellationToken)).Trim();
            if (!IsUsableAnswer(answerText))
            {
                SetCachedValue(normalizedQuery, null, options.FailureCacheTtlSeconds);
                return null;
            }

            var result = new KnowledgeSearchResult(answerText, SearchBackendKind.Wolfram);
            SetCachedValue(normalizedQuery, result, options.CacheTtlSeconds);
            return result;
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(exception, "Wolfram Alpha lookup failed.");
            SetCachedValue(normalizedQuery, null, options.FailureCacheTtlSeconds);
            return null;
        }
    }

    private Uri BuildRequestUri(string query)
    {
        var endpoint = string.IsNullOrWhiteSpace(options.ApiEndpoint)
            ? "http://api.wolframalpha.com/v1/spoken"
            : options.ApiEndpoint.Trim();

        var builder = new UriBuilder(endpoint);
        var queryString = $"appid={Uri.EscapeDataString(options.ApiKey!)}&i={Uri.EscapeDataString(query)}";
        builder.Query = queryString;
        return builder.Uri;
    }

    private static bool IsUsableAnswer(string answerText)
    {
        if (string.IsNullOrWhiteSpace(answerText)) return false;

        var lowered = answerText.ToLowerInvariant();
        return !FailurePhrases.Any(phrase => lowered.Contains(phrase, StringComparison.Ordinal));
    }

    private bool TryGetCachedValue(string query, out KnowledgeSearchResult? result)
    {
        result = null;
        if (!_cache.TryGetValue(query, out var entry) || entry.ExpiresAtUtc <= DateTimeOffset.UtcNow)
            return false;

        result = entry.Result;
        return true;
    }

    private void SetCachedValue(string query, KnowledgeSearchResult? result, int ttlSeconds)
    {
        _cache[query] = new CacheEntry(
            result,
            DateTimeOffset.UtcNow.AddSeconds(Math.Max(1, ttlSeconds)));
    }

    private sealed record CacheEntry(KnowledgeSearchResult? Result, DateTimeOffset ExpiresAtUtc);
}
