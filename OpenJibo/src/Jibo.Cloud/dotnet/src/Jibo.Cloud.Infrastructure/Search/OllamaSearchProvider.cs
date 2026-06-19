using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Jibo.Cloud.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace Jibo.Cloud.Infrastructure.Search;

public sealed class OllamaSearchProvider(
    HttpClient httpClient,
    SearchBackendOptions options,
    ILogger<OllamaSearchProvider> logger)
    : IKnowledgeSearchProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly ConcurrentDictionary<string, CacheEntry> _cache =
        new(StringComparer.OrdinalIgnoreCase);

    public SearchBackendKind Kind => SearchBackendKind.Ollama;

    public async Task<KnowledgeSearchResult?> SearchAsync(
        KnowledgeSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.BackendSpec.Kind != SearchBackendKind.Ollama || string.IsNullOrWhiteSpace(request.Query))
            return null;

        var endpoint = SearchBackendSettingsResolver.ResolveEndpoint(request.BackendSpec);
        if (string.IsNullOrWhiteSpace(endpoint)) return null;

        var model = SearchBackendSettingsResolver.ResolveModel(request.BackendSpec);
        var cacheKey = BuildCacheKey(request.BackendSpec, model, request.Query.Trim());
        if (TryGetCachedValue(cacheKey, out var cachedResult))
            return cachedResult;

        var result = await TryGenerateAsync(endpoint, model, request.Query.Trim(), cancellationToken);
        SetCachedValue(cacheKey, result, result is null ? options.FailureCacheTtlSeconds : options.CacheTtlSeconds);
        return result;
    }

    private async Task<KnowledgeSearchResult?> TryGenerateAsync(
        string endpoint,
        string model,
        string prompt,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClient.PostAsJsonAsync(
                endpoint,
                new OllamaGenerateRequest(model, prompt),
                cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Ollama lookup failed with status {StatusCode}.", response.StatusCode);
                return null;
            }

            var parsed = JsonSerializer.Deserialize<OllamaGenerateResponse>(body, JsonOptions);
            var answerText = KnowledgeSearchResponseFormatter.NormalizeForSpeech(parsed?.Response ?? string.Empty);
            return string.IsNullOrWhiteSpace(answerText)
                ? null
                : new KnowledgeSearchResult(answerText, SearchBackendKind.Ollama);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(exception, "Ollama lookup failed for model {Model}.", model);
            return null;
        }
    }

    private static string BuildCacheKey(SearchBackendSpec spec, string model, string query)
    {
        return $"ollama|{spec.Credential}|{model}|{query}";
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

    private sealed record OllamaGenerateRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("prompt")] string Prompt,
        [property: JsonPropertyName("stream")] bool Stream = false);

    private sealed record OllamaGenerateResponse(
        [property: JsonPropertyName("response")] string? Response);
}
