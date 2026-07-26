using System.Net;
using System.Text;
using Jibo.Cloud.Application.Services;
using Jibo.Cloud.Infrastructure.Wikipedia;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jibo.Cloud.Tests.Infrastructure;

public sealed class WikipediaSummaryProviderTests
{
    [Fact]
    public async Task GetSummaryAsync_ReturnsExtract_WhenTitleMatches()
    {
        var handler = new RoutingHttpMessageHandler(request =>
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;
            if (path.Contains("api.php", StringComparison.OrdinalIgnoreCase) ||
                request.RequestUri?.Query.Contains("action=opensearch", StringComparison.OrdinalIgnoreCase) == true)
            {
                return JsonResponse(
                    """
                    ["James Garfield",["James A. Garfield"],["President"],["https://en.wikipedia.org/wiki/James_A._Garfield"]]
                    """);
            }

            return JsonResponse(
                """
                {
                  "type": "standard",
                  "title": "James A. Garfield",
                  "extract": "James Abram Garfield was the 20th president of the United States."
                }
                """);
        });
        var provider = CreateProvider(handler);

        var summary = await provider.GetSummaryAsync("James Garfield");

        Assert.Equal("James Abram Garfield was the 20th president of the United States.", summary);
        Assert.Equal(1, handler.GetCallCountContaining("opensearch"));
        Assert.Equal(1, handler.GetCallCountContaining("page/summary"));
    }

    [Fact]
    public async Task GetSummaryAsync_ReturnsNull_WhenOpenSearchTitleDoesNotMatch()
    {
        var handler = new RoutingHttpMessageHandler(_ => JsonResponse(
            """
            ["Jibo",["Cynthia Breazeal"],["Scientist"],["https://en.wikipedia.org/wiki/Cynthia_Breazeal"]]
            """));
        var provider = CreateProvider(handler);

        var summary = await provider.GetSummaryAsync("Jibo");

        Assert.Null(summary);
        Assert.Equal(1, handler.GetCallCountContaining("opensearch"));
        Assert.Equal(0, handler.GetCallCountContaining("page/summary"));
    }

    [Fact]
    public async Task GetSummaryAsync_ReturnsNull_WhenSummaryTitleDoesNotMatchSubject()
    {
        var handler = new RoutingHttpMessageHandler(request =>
        {
            if (request.RequestUri?.Query.Contains("action=opensearch", StringComparison.OrdinalIgnoreCase) == true)
            {
                return JsonResponse(
                    """
                    ["Jibo",["Jibo"],["Robot"],["https://en.wikipedia.org/wiki/Jibo"]]
                    """);
            }

            return JsonResponse(
                """
                {
                  "type": "standard",
                  "title": "Cynthia Breazeal",
                  "extract": "Cynthia Breazeal is an American AI and robotics scientist."
                }
                """);
        });
        var provider = CreateProvider(handler);

        var summary = await provider.GetSummaryAsync("Jibo");

        Assert.Null(summary);
    }

    [Fact]
    public async Task GetSummaryAsync_SkipsDisambiguationPages()
    {
        var handler = new RoutingHttpMessageHandler(request =>
        {
            if (request.RequestUri?.Query.Contains("action=opensearch", StringComparison.OrdinalIgnoreCase) == true)
            {
                return JsonResponse(
                    """
                    ["Mercury",["Mercury"],["Element"],["https://en.wikipedia.org/wiki/Mercury"]]
                    """);
            }

            return JsonResponse(
                """
                {
                  "type": "disambiguation",
                  "title": "Mercury",
                  "extract": "Mercury may refer to."
                }
                """);
        });
        var provider = CreateProvider(handler);

        var summary = await provider.GetSummaryAsync("Mercury");

        Assert.Null(summary);
    }

    [Fact]
    public async Task GetSummaryAsync_CachesSuccessfulResponses()
    {
        var handler = new RoutingHttpMessageHandler(request =>
        {
            if (request.RequestUri?.Query.Contains("action=opensearch", StringComparison.OrdinalIgnoreCase) == true)
            {
                return JsonResponse(
                    """
                    ["Jibo",["Jibo"],["Robot"],["https://en.wikipedia.org/wiki/Jibo"]]
                    """);
            }

            return JsonResponse(
                """
                {
                  "type": "standard",
                  "title": "Jibo",
                  "extract": "Jibo was a social robot."
                }
                """);
        });
        var provider = CreateProvider(handler);

        var first = await provider.GetSummaryAsync("Jibo");
        var second = await provider.GetSummaryAsync("Jibo");

        Assert.Equal("Jibo was a social robot.", first);
        Assert.Equal(first, second);
        Assert.Equal(1, handler.GetCallCountContaining("opensearch"));
        Assert.Equal(1, handler.GetCallCountContaining("page/summary"));
    }

    [Fact]
    public async Task GetSummaryAsync_SendsCloudVersionUserAgent_ByDefault()
    {
        string? userAgent = null;
        var handler = new RoutingHttpMessageHandler(request =>
        {
            userAgent = request.Headers.UserAgent.ToString();
            if (string.IsNullOrWhiteSpace(userAgent) &&
                request.Headers.TryGetValues("User-Agent", out var values))
                userAgent = string.Join(' ', values);

            if (request.RequestUri?.Query.Contains("action=opensearch", StringComparison.OrdinalIgnoreCase) == true)
            {
                return JsonResponse(
                    """
                    ["Jibo",["Jibo"],["Robot"],["https://en.wikipedia.org/wiki/Jibo"]]
                    """);
            }

            return JsonResponse(
                """
                {
                  "type": "standard",
                  "title": "Jibo",
                  "extract": "Jibo was a social robot."
                }
                """);
        });
        var provider = CreateProvider(handler);

        await provider.GetSummaryAsync("Jibo");

        Assert.Equal($"OpenJibo/{OpenJiboCloudBuildInfo.Version} (jiborevived.com)", userAgent);
    }

    [Fact]
    public async Task GetSummaryAsync_UsesConfiguredUserAgentOverride()
    {
        string? userAgent = null;
        var handler = new RoutingHttpMessageHandler(request =>
        {
            userAgent = request.Headers.UserAgent.ToString();
            if (string.IsNullOrWhiteSpace(userAgent) &&
                request.Headers.TryGetValues("User-Agent", out var values))
                userAgent = string.Join(' ', values);

            if (request.RequestUri?.Query.Contains("action=opensearch", StringComparison.OrdinalIgnoreCase) == true)
            {
                return JsonResponse(
                    """
                    ["Jibo",["Jibo"],["Robot"],["https://en.wikipedia.org/wiki/Jibo"]]
                    """);
            }

            return JsonResponse(
                """
                {
                  "type": "standard",
                  "title": "Jibo",
                  "extract": "Jibo was a social robot."
                }
                """);
        });
        var provider = CreateProvider(handler, userAgent: "OpenJibo/custom (jiborevived.com)");

        await provider.GetSummaryAsync("Jibo");

        Assert.Equal("OpenJibo/custom (jiborevived.com)", userAgent);
    }

    private static WikipediaSummaryProvider CreateProvider(
        HttpMessageHandler handler,
        string? userAgent = null)
    {
        return new WikipediaSummaryProvider(
            new HttpClient(handler),
            new WikipediaSummaryOptions
            {
                ApiBaseUrl = "https://en.wikipedia.org/w/api.php",
                RestBaseUrl = "https://en.wikipedia.org/api/rest_v1",
                UserAgent = userAgent,
                FailureCacheTtlSeconds = 45,
                SuccessCacheTtlSeconds = 300
            },
            NullLogger<WikipediaSummaryProvider>.Instance);
    }

    private static HttpResponseMessage JsonResponse(string body)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
    }

    private sealed class RoutingHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        : HttpMessageHandler
    {
        private readonly Dictionary<string, int> _callsByNeedle = new(StringComparer.OrdinalIgnoreCase);
        private readonly Lock _gate = new();

        public int GetCallCountContaining(string needle)
        {
            lock (_gate)
            {
                return _callsByNeedle.GetValueOrDefault(needle, 0);
            }
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var uri = request.RequestUri?.ToString() ?? string.Empty;
            lock (_gate)
            {
                foreach (var needle in new[] { "opensearch", "page/summary" })
                {
                    if (uri.Contains(needle, StringComparison.OrdinalIgnoreCase))
                        _callsByNeedle[needle] = _callsByNeedle.TryGetValue(needle, out var count) ? count + 1 : 1;
                }
            }

            return Task.FromResult(responseFactory(request));
        }
    }
}
