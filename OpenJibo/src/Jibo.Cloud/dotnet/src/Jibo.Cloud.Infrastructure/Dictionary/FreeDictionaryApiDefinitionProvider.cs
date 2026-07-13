using System.Collections.Concurrent;
using System.Text.Json;
using Jibo.Cloud.Application.Abstractions;
using Jibo.Cloud.Application.Services;
using Microsoft.Extensions.Logging;

namespace Jibo.Cloud.Infrastructure.Dictionary;

public sealed class FreeDictionaryApiDefinitionProvider(
    HttpClient httpClient,
    FreeDictionaryApiOptions options,
    ILogger<FreeDictionaryApiDefinitionProvider> logger)
    : IWordDefinitionProvider
{
    private readonly ConcurrentDictionary<string, CacheEntry> _definitionCache =
        new(StringComparer.OrdinalIgnoreCase);

    public async Task<string?> GetDefinitionAsync(string word, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(word)) return null;

        var cacheKey = NormalizeWordForCache(word);
        if (TryGetCachedValue(cacheKey, out var cachedDefinition))
            return cachedDefinition;

        try
        {
            var uri = BuildDefinitionUri(word);
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.TryAddWithoutValidation("User-Agent", ResolveUserAgent());

            using var response = await httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Free Dictionary API request failed for word {Word}. StatusCode={StatusCode} Reason={ReasonPhrase}",
                    word,
                    (int)response.StatusCode,
                    response.ReasonPhrase);
                SetCachedValue(cacheKey, null, options.FailureCacheTtlSeconds);
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var definition = TrySelectDefinition(document.RootElement);
            if (string.IsNullOrWhiteSpace(definition))
            {
                logger.LogWarning("Free Dictionary API returned no usable definition for word {Word}.", word);
                SetCachedValue(cacheKey, null, options.FailureCacheTtlSeconds);
                return null;
            }

            SetCachedValue(cacheKey, definition, options.SuccessCacheTtlSeconds);
            return definition;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Free Dictionary API lookup failed for word {Word}.", word);
            SetCachedValue(cacheKey, null, options.FailureCacheTtlSeconds);
            return null;
        }
    }

    private Uri BuildDefinitionUri(string word)
    {
        var baseUrl = string.IsNullOrWhiteSpace(options.BaseUrl)
            ? "https://freedictionaryapi.com"
            : options.BaseUrl.Trim().TrimEnd('/');
        var encodedWord = Uri.EscapeDataString(word.Trim());
        return new Uri($"{baseUrl}/api/v1/entries/en/{encodedWord}");
    }

    private string ResolveUserAgent()
    {
        return string.IsNullOrWhiteSpace(options.UserAgent) ? "OpenJiboCloud/1.0" : options.UserAgent.Trim();
    }

    private static string? TrySelectDefinition(JsonElement root)
    {
        if (!root.TryGetProperty("entries", out var entries) ||
            entries.ValueKind != JsonValueKind.Array)
            return null;

        foreach (var entry in entries.EnumerateArray())
        {
            if (!entry.TryGetProperty("senses", out var senses) ||
                senses.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var sense in FlattenSenses(senses))
            {
                if (IsVulgarSense(sense)) continue;

                var definition = ReadString(sense, "definition");
                var sanitized = DefinitionTextSanitizer.Sanitize(definition);
                if (!string.IsNullOrWhiteSpace(sanitized)) return sanitized;
            }
        }

        return null;
    }

    private static IEnumerable<JsonElement> FlattenSenses(JsonElement senses)
    {
        foreach (var sense in senses.EnumerateArray())
        {
            yield return sense;

            if (!sense.TryGetProperty("subsenses", out var subsenses) ||
                subsenses.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var subsense in FlattenSenses(subsenses))
                yield return subsense;
        }
    }

    private static bool IsVulgarSense(JsonElement sense)
    {
        if (!sense.TryGetProperty("tags", out var tags) || tags.ValueKind != JsonValueKind.Array)
            return false;

        foreach (var tag in tags.EnumerateArray())
        {
            if (tag.ValueKind != JsonValueKind.String) continue;
            if (string.Equals(tag.GetString(), "vulgar", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static string? ReadString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.String)
            return null;

        return property.GetString();
    }

    private static string NormalizeWordForCache(string word) => word.Trim().ToLowerInvariant();

    private bool TryGetCachedValue(string cacheKey, out string? definition)
    {
        definition = null;
        if (!_definitionCache.TryGetValue(cacheKey, out var entry))
            return false;

        if (entry.ExpiresUtc <= DateTimeOffset.UtcNow)
        {
            _definitionCache.TryRemove(cacheKey, out _);
            return false;
        }

        definition = entry.Definition;
        return true;
    }

    private void SetCachedValue(string cacheKey, string? definition, int ttlSeconds)
    {
        var ttl = Math.Max(ttlSeconds, 1);
        _definitionCache[cacheKey] = new CacheEntry(definition, DateTimeOffset.UtcNow.AddSeconds(ttl));
    }

    private sealed record CacheEntry(string? Definition, DateTimeOffset ExpiresUtc);
}
