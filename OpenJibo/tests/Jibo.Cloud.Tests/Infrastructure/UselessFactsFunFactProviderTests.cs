using System.Net;
using System.Text;
using Jibo.Cloud.Infrastructure.FunFacts;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jibo.Cloud.Tests.Infrastructure;

public sealed class UselessFactsFunFactProviderTests
{
    [Fact]
    public async Task GetRandomFactAsync_ReturnsTextFromSuccessfulResponse()
    {
        var handler = new CountingHttpMessageHandler(_ =>
            JsonResponse(
                """
                {
                    "id":"e33e52a1d4b8d7b265de6f606ee99683",
                    "text":"Switzerland is the only country with a square flag.",
                    "source":"djtech.net",
                    "language":"en"
                }
                """));
        var provider = CreateProvider(handler);

        var fact = await provider.GetRandomFactAsync();

        Assert.Equal("Switzerland is the only country with a square flag.", fact);
        Assert.Equal(1, handler.GetCallCount("/api/v2/facts/random"));
    }

    [Fact]
    public async Task GetRandomFactAsync_ReturnsNullOnHttpError()
    {
        var handler = new CountingHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var provider = CreateProvider(handler);

        var fact = await provider.GetRandomFactAsync();

        Assert.Null(fact);
    }

    [Fact]
    public async Task GetRandomFactAsync_ReturnsNullOnMalformedJson()
    {
        var handler = new CountingHttpMessageHandler(_ =>
            JsonResponse("""{"id":"abc"}"""));
        var provider = CreateProvider(handler);

        var fact = await provider.GetRandomFactAsync();

        Assert.Null(fact);
    }

    [Fact]
    public async Task GetRandomFactAsync_CachesFailuresToAvoidRepeatedCalls()
    {
        var handler = new CountingHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var provider = CreateProvider(handler, failureCacheTtlSeconds: 300);

        var first = await provider.GetRandomFactAsync();
        var second = await provider.GetRandomFactAsync();

        Assert.Null(first);
        Assert.Null(second);
        Assert.Equal(1, handler.GetCallCount("/api/v2/facts/random"));
    }

    private static UselessFactsFunFactProvider CreateProvider(
        HttpMessageHandler handler,
        int failureCacheTtlSeconds = 45)
    {
        return new UselessFactsFunFactProvider(
            new HttpClient(handler)
            {
                BaseAddress = new Uri("https://uselessfacts.jsph.pl")
            },
            new UselessFactsOptions
            {
                BaseUrl = "https://uselessfacts.jsph.pl",
                FailureCacheTtlSeconds = failureCacheTtlSeconds
            },
            NullLogger<UselessFactsFunFactProvider>.Instance);
    }

    private static HttpResponseMessage JsonResponse(string body)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
    }

    private sealed class CountingHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        : HttpMessageHandler
    {
        private readonly Dictionary<string, int> _callsByPath = new(StringComparer.OrdinalIgnoreCase);
        private readonly Lock _gate = new();

        public int GetCallCount(string path)
        {
            lock (_gate)
            {
                return _callsByPath.GetValueOrDefault(path, 0);
            }
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;
            lock (_gate)
            {
                _callsByPath[path] = _callsByPath.TryGetValue(path, out var count) ? count + 1 : 1;
            }

            return Task.FromResult(responseFactory(request));
        }
    }
}
