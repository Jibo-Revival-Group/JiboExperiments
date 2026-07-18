using Microsoft.Extensions.Logging;

namespace Jibo.Cloud.Infrastructure.Calendar;

public interface IIcalFeedFetcher
{
    Task<IcalFeedFetchResult> FetchAsync(string icalUrl, CancellationToken cancellationToken = default);
}

public sealed record IcalFeedFetchResult(
    bool Ok,
    string? Body,
    string? Error,
    int? HttpStatusCode = null);

public sealed class IcalFeedFetcher(HttpClient httpClient, ILogger<IcalFeedFetcher> logger) : IIcalFeedFetcher
{
    public const int MaxResponseBytes = 2_000_000;
    private const int MaxRedirects = 3;

    public async Task<IcalFeedFetchResult> FetchAsync(string icalUrl, CancellationToken cancellationToken = default)
    {
        if (!IcalUrlValidator.TryValidateHttpsPublicUrl(
                icalUrl,
                out var uri,
                out var validationError,
                requireDnsResolution: true))
            return new IcalFeedFetchResult(false, null, validationError);

        var currentUri = uri;
        for (var redirectCount = 0; redirectCount <= MaxRedirects; redirectCount++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, currentUri);
            request.Headers.TryAddWithoutValidation("Accept", "text/calendar, text/plain, */*");
            request.Headers.TryAddWithoutValidation("User-Agent", "OpenJiboCloud/1.0");

            using var response = await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if ((int)response.StatusCode is >= 300 and < 400)
            {
                var location = response.Headers.Location;
                if (location is null)
                    return new IcalFeedFetchResult(false, null, "iCal feed redirect was missing a location.",
                        (int)response.StatusCode);

                var nextUri = location.IsAbsoluteUri ? location : new Uri(currentUri, location);
                if (!IcalUrlValidator.IsRedirectTargetAllowed(nextUri, out var redirectError))
                    return new IcalFeedFetchResult(false, null, redirectError, (int)response.StatusCode);

                currentUri = nextUri;
                continue;
            }

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "iCal feed fetch failed. Host={Host} StatusCode={StatusCode}",
                    currentUri.Host,
                    (int)response.StatusCode);
                return new IcalFeedFetchResult(
                    false,
                    null,
                    "iCal feed could not be loaded.",
                    (int)response.StatusCode);
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var memory = new MemoryStream();
            var buffer = new byte[8192];
            var total = 0;
            while (true)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
                if (read <= 0) break;
                total += read;
                if (total > MaxResponseBytes)
                    return new IcalFeedFetchResult(false, null, "iCal feed is too large.", (int)response.StatusCode);

                memory.Write(buffer, 0, read);
            }

            var body = System.Text.Encoding.UTF8.GetString(memory.ToArray());
            if (string.IsNullOrWhiteSpace(body))
                return new IcalFeedFetchResult(false, null, "iCal feed was empty.", (int)response.StatusCode);

            return new IcalFeedFetchResult(true, body, null, (int)response.StatusCode);
        }

        return new IcalFeedFetchResult(false, null, "iCal feed exceeded redirect limit.");
    }
}
