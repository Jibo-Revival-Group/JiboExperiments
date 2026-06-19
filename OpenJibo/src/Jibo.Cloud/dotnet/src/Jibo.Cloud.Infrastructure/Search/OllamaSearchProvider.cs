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
        if (request.Backend != SearchBackendKind.Ollama || string.IsNullOrWhiteSpace(request.Query))
            return null;

        var endpoint = SearchBackendSettingsResolver.ResolveEndpoint(
            options,
            SearchBackendKind.Ollama,
            request.UseFallbackSettings);
        if (string.IsNullOrWhiteSpace(endpoint)) return null;

        var primaryModel = SearchBackendSettingsResolver.ResolveModel(
            options,
            SearchBackendKind.Ollama,
            request.UseFallbackSettings);
        var cacheKey = BuildCacheKey(request, primaryModel);
        if (TryGetCachedValue(cacheKey, out var cachedResult))
            return cachedResult;

        var alternateModel = SearchBackendSettingsResolver.ResolveAlternateModel(
            options,
            SearchBackendKind.Ollama,
            primaryModel);
        var modelsToTry = alternateModel is null
            ? [primaryModel]
            : new[] { primaryModel, alternateModel };

        foreach (var model in modelsToTry)
        {
            var result = await TryGenerateAsync(
                endpoint,
                model,
                request.Query.Trim(),
                cancellationToken);
            if (result is not null)
            {
                SetCachedValue(cacheKey, result, options.CacheTtlSeconds);
                return result;
            }
        }

        SetCachedValue(cacheKey, null, options.FailureCacheTtlSeconds);
        return null;
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
                if (IsModelUnavailable(response.StatusCode, body))
                    logger.LogDebug("Ollama model {Model} is unavailable.", model);
                else
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

    private static bool IsModelUnavailable(HttpStatusCode statusCode, string body)
    {
        if (statusCode is HttpStatusCode.NotFound or HttpStatusCode.BadRequest)
            return body.Contains("model", StringComparison.OrdinalIgnoreCase) &&
                   body.Contains("not found", StringComparison.OrdinalIgnoreCase);

        return false;
    }

    private static string BuildCacheKey(KnowledgeSearchRequest request, string model)
    {
        return $"ollama|{request.UseFallbackSettings}|{model}|{request.Query.Trim()}";
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
