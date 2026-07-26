using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Jibo.Cloud.Application.Abstractions;
using Jibo.Cloud.Application.Services;
using Microsoft.Extensions.Logging;

namespace Jibo.Cloud.Infrastructure.Wikipedia;

public sealed class WikipediaSummaryProvider(
    HttpClient httpClient,
    WikipediaSummaryOptions options,
    ILogger<WikipediaSummaryProvider> logger)
    : IWikipediaSummaryProvider
{
    private readonly ConcurrentDictionary<string, CacheEntry> _cache =
        new(StringComparer.OrdinalIgnoreCase);

    public async Task<WikipediaSummaryResult> GetSummaryAsync(
        string subject,
        CancellationToken cancellationToken = default,
        bool bypassCache = false)
    {
        if (string.IsNullOrWhiteSpace(subject))
            return WikipediaSummaryResult.NotFound();

        var cacheKey = NormalizeSubjectForCache(subject);
        if (!bypassCache && TryGetCachedValue(cacheKey, out var cachedResult))
            return cachedResult;

        try
        {
            var titleLookup = await FindMatchingTitleAsync(subject, cancellationToken);
            if (titleLookup.Outcome == WikipediaSummaryOutcome.Unavailable)
            {
                SetCachedValue(cacheKey, WikipediaSummaryResult.Unavailable(), options.FailureCacheTtlSeconds);
                return WikipediaSummaryResult.Unavailable();
            }

            if (string.IsNullOrWhiteSpace(titleLookup.Title))
            {
                var notFound = WikipediaSummaryResult.NotFound();
                SetCachedValue(cacheKey, notFound, options.FailureCacheTtlSeconds);
                return notFound;
            }

            var summaryLookup = await FetchSummaryAsync(titleLookup.Title, subject, cancellationToken);
            SetCachedValue(
                cacheKey,
                summaryLookup,
                summaryLookup.Outcome == WikipediaSummaryOutcome.Found
                    ? options.SuccessCacheTtlSeconds
                    : options.FailureCacheTtlSeconds);
            return summaryLookup;
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(exception, "Wikipedia summary lookup failed for subject {Subject}.", subject);
            var unavailable = WikipediaSummaryResult.Unavailable();
            SetCachedValue(cacheKey, unavailable, options.FailureCacheTtlSeconds);
            return unavailable;
        }
    }

    private async Task<TitleLookupResult> FindMatchingTitleAsync(string subject, CancellationToken cancellationToken)
    {
        var requestUri = BuildOpenSearchUri(subject);
        using var request = CreateRequest(requestUri);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            if (IsNotFoundStatus(response.StatusCode))
                return TitleLookupResult.NotFound();

            logger.LogWarning(
                "Wikipedia OpenSearch failed for subject {Subject}. StatusCode={StatusCode}",
                subject,
                (int)response.StatusCode);
            return TitleLookupResult.Unavailable();
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (document.RootElement.ValueKind != JsonValueKind.Array ||
            document.RootElement.GetArrayLength() < 2)
            return TitleLookupResult.NotFound();

        var titlesElement = document.RootElement[1];
        if (titlesElement.ValueKind != JsonValueKind.Array)
            return TitleLookupResult.NotFound();

        foreach (var titleElement in titlesElement.EnumerateArray())
        {
            if (titleElement.ValueKind != JsonValueKind.String) continue;

            var title = titleElement.GetString();
            if (string.IsNullOrWhiteSpace(title)) continue;
            if (WikipediaTitleSimilarity.IsCloseMatch(subject, title))
                return TitleLookupResult.Found(title);
        }

        return TitleLookupResult.NotFound();
    }

    private async Task<WikipediaSummaryResult> FetchSummaryAsync(
        string title,
        string subject,
        CancellationToken cancellationToken)
    {
        var requestUri = BuildSummaryUri(title);
        using var request = CreateRequest(requestUri);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            // REST summary returns 404 when Wikipedia is up but the page/title does not exist.
            if (IsNotFoundStatus(response.StatusCode))
                return WikipediaSummaryResult.NotFound();

            logger.LogWarning(
                "Wikipedia summary request failed for title {Title}. StatusCode={StatusCode}",
                title,
                (int)response.StatusCode);
            return WikipediaSummaryResult.Unavailable();
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;

        var pageType = ReadString(root, "type");
        if (string.Equals(pageType, "disambiguation", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(pageType, "redirect", StringComparison.OrdinalIgnoreCase))
            return WikipediaSummaryResult.NotFound();

        var returnedTitle = ReadString(root, "title");
        if (string.IsNullOrWhiteSpace(returnedTitle) ||
            !WikipediaTitleSimilarity.IsCloseMatch(subject, returnedTitle))
            return WikipediaSummaryResult.NotFound();

        var extract = ReadString(root, "extract");
        if (string.IsNullOrWhiteSpace(extract))
            return WikipediaSummaryResult.NotFound();

        return WikipediaSummaryResult.Found(CollapseWhitespace(extract));
    }

    private Uri BuildOpenSearchUri(string subject)
    {
        var baseUrl = string.IsNullOrWhiteSpace(options.ApiBaseUrl)
            ? "https://en.wikipedia.org/w/api.php"
            : options.ApiBaseUrl.Trim();
        var limit = Math.Clamp(options.OpenSearchLimit, 1, 10);
        var builder = new UriBuilder(baseUrl)
        {
            Query =
                $"action=opensearch&search={Uri.EscapeDataString(subject.Trim())}&limit={limit}&namespace=0&format=json"
        };
        return builder.Uri;
    }

    private Uri BuildSummaryUri(string title)
    {
        var baseUrl = string.IsNullOrWhiteSpace(options.RestBaseUrl)
            ? "https://en.wikipedia.org/api/rest_v1"
            : options.RestBaseUrl.Trim().TrimEnd('/');
        var encodedTitle = Uri.EscapeDataString(title.Trim().Replace(' ', '_'));
        return new Uri($"{baseUrl}/page/summary/{encodedTitle}?redirect=false");
    }

    private HttpRequestMessage CreateRequest(Uri requestUri)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        request.Headers.TryAddWithoutValidation("User-Agent", ResolveUserAgent());
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    private string ResolveUserAgent()
    {
        if (!string.IsNullOrWhiteSpace(options.UserAgent))
            return options.UserAgent.Trim();

        return $"OpenJibo/{OpenJiboCloudBuildInfo.Version} (jiborevived.com)";
    }

    private static bool IsNotFoundStatus(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone;

    private static string? ReadString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.String)
            return null;

        return property.GetString();
    }

    private static string CollapseWhitespace(string value)
    {
        return string.Join(
            ' ',
            value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    private static string NormalizeSubjectForCache(string subject) => subject.Trim().ToLowerInvariant();

    private bool TryGetCachedValue(string cacheKey, out WikipediaSummaryResult result)
    {
        result = WikipediaSummaryResult.NotFound();
        if (!_cache.TryGetValue(cacheKey, out var entry))
            return false;

        if (entry.ExpiresUtc <= DateTimeOffset.UtcNow)
        {
            _cache.TryRemove(cacheKey, out _);
            return false;
        }

        result = entry.Result;
        return true;
    }

    private void SetCachedValue(string cacheKey, WikipediaSummaryResult result, int ttlSeconds)
    {
        var ttl = Math.Max(ttlSeconds, 1);
        _cache[cacheKey] = new CacheEntry(result, DateTimeOffset.UtcNow.AddSeconds(ttl));
    }

    private sealed record CacheEntry(WikipediaSummaryResult Result, DateTimeOffset ExpiresUtc);

    private sealed record TitleLookupResult(string? Title, WikipediaSummaryOutcome Outcome)
    {
        public static TitleLookupResult Found(string title) =>
            new(title, WikipediaSummaryOutcome.Found);

        public static TitleLookupResult NotFound() =>
            new(null, WikipediaSummaryOutcome.NotFound);

        public static TitleLookupResult Unavailable() =>
            new(null, WikipediaSummaryOutcome.Unavailable);
    }
}
