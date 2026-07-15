using System.Collections.Concurrent;
using System.Text.Json;
using Jibo.Cloud.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace Jibo.Cloud.Infrastructure.FunFacts;

public sealed class UselessFactsFunFactProvider(
    HttpClient httpClient,
    UselessFactsOptions options,
    ILogger<UselessFactsFunFactProvider> logger)
    : IFunFactProvider
{
    private const string FailureCacheKey = "random";

    private readonly ConcurrentDictionary<string, CacheEntry> _failureCache =
        new(StringComparer.OrdinalIgnoreCase);

    public async Task<string?> GetRandomFactAsync(CancellationToken cancellationToken = default)
    {
        if (IsFailureCached())
            return null;

        try
        {
            var uri = BuildRandomFactUri();
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.TryAddWithoutValidation("User-Agent", ResolveUserAgent());

            using var response = await httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Useless Facts API request failed. StatusCode={StatusCode} Reason={ReasonPhrase}",
                    (int)response.StatusCode,
                    response.ReasonPhrase);
                SetFailureCache();
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var text = ReadString(document.RootElement, "text");
            if (string.IsNullOrWhiteSpace(text))
            {
                logger.LogWarning("Useless Facts API returned an empty fact.");
                SetFailureCache();
                return null;
            }

            return text.Trim();
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Useless Facts API lookup failed.");
            SetFailureCache();
            return null;
        }
    }

    private Uri BuildRandomFactUri()
    {
        var baseUrl = string.IsNullOrWhiteSpace(options.BaseUrl)
            ? "https://uselessfacts.jsph.pl"
            : options.BaseUrl.Trim().TrimEnd('/');
        return new Uri($"{baseUrl}/api/v2/facts/random");
    }

    private string ResolveUserAgent()
    {
        return string.IsNullOrWhiteSpace(options.UserAgent) ? "OpenJiboCloud/1.0" : options.UserAgent.Trim();
    }

    private bool IsFailureCached()
    {
        if (!_failureCache.TryGetValue(FailureCacheKey, out var entry))
            return false;

        if (entry.ExpiresUtc > DateTimeOffset.UtcNow)
            return true;

        _failureCache.TryRemove(FailureCacheKey, out _);
        return false;
    }

    private void SetFailureCache()
    {
        var ttlSeconds = Math.Max(options.FailureCacheTtlSeconds, 1);
        _failureCache[FailureCacheKey] = new CacheEntry(DateTimeOffset.UtcNow.AddSeconds(ttlSeconds));
    }

    private static string? ReadString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.String)
            return null;

        return property.GetString();
    }

    private sealed record CacheEntry(DateTimeOffset ExpiresUtc);
}
