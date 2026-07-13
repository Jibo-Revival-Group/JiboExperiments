using System.Net;
using System.Text;
using Jibo.Cloud.Infrastructure.Dictionary;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jibo.Cloud.Tests.Infrastructure;

public sealed class FreeDictionaryApiDefinitionProviderTests
{
    [Fact]
    public async Task GetDefinitionAsync_ReturnsFirstSanitizedDefinition()
    {
        var handler = new CountingHttpMessageHandler(_ => JsonResponse(
            """
            {
                "word": "holiday",
                "entries": [
                    {
                        "senses": [
                            {
                                "definition": "A day on which a religious event or secular celebration is traditionally observed.",
                                "tags": []
                            }
                        ]
                    }
                ]
            }
            """));
        var provider = CreateProvider(handler);

        var definition = await provider.GetDefinitionAsync("holiday");

        Assert.Equal(
            "A day on which a religious event or secular celebration is traditionally observed.",
            definition);
        Assert.Equal(1, handler.GetCallCount("/api/v1/entries/en/holiday"));
    }

    [Fact]
    public async Task GetDefinitionAsync_SkipsVulgarTaggedSenseAndReturnsNextCleanDefinition()
    {
        var handler = new CountingHttpMessageHandler(_ => JsonResponse(
            """
            {
                "word": "example",
                "entries": [
                    {
                        "senses": [
                            {
                                "definition": "(vulgar) A bad definition.",
                                "tags": ["vulgar"]
                            },
                            {
                                "definition": "(countable) A safe definition.",
                                "tags": ["countable"]
                            }
                        ]
                    }
                ]
            }
            """));
        var provider = CreateProvider(handler);

        var definition = await provider.GetDefinitionAsync("example");

        Assert.Equal("A safe definition.", definition);
    }

    [Fact]
    public async Task GetDefinitionAsync_ReturnsNullWhenAllSensesAreVulgar()
    {
        var handler = new CountingHttpMessageHandler(_ => JsonResponse(
            """
            {
                "word": "example",
                "entries": [
                    {
                        "senses": [
                            {
                                "definition": "(vulgar) A bad definition.",
                                "tags": ["vulgar"]
                            }
                        ]
                    }
                ]
            }
            """));
        var provider = CreateProvider(handler);

        var definition = await provider.GetDefinitionAsync("example");

        Assert.Null(definition);
    }

    [Fact]
    public async Task GetDefinitionAsync_ReturnsNullOnHttpError()
    {
        var handler = new CountingHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.NotFound));
        var provider = CreateProvider(handler);

        var definition = await provider.GetDefinitionAsync("missingword");

        Assert.Null(definition);
    }

    [Fact]
    public async Task GetDefinitionAsync_CachesSuccessfulResponses()
    {
        var handler = new CountingHttpMessageHandler(_ => JsonResponse(
            """
            {
                "word": "holiday",
                "entries": [
                    {
                        "senses": [
                            {
                                "definition": "A day on which a religious event or secular celebration is traditionally observed.",
                                "tags": []
                            }
                        ]
                    }
                ]
            }
            """));
        var provider = CreateProvider(handler, successCacheTtlSeconds: 300);

        var first = await provider.GetDefinitionAsync("holiday");
        var second = await provider.GetDefinitionAsync("holiday");

        Assert.NotNull(first);
        Assert.Equal(first, second);
        Assert.Equal(1, handler.GetCallCount("/api/v1/entries/en/holiday"));
    }

    private static FreeDictionaryApiDefinitionProvider CreateProvider(
        HttpMessageHandler handler,
        int successCacheTtlSeconds = 300)
    {
        return new FreeDictionaryApiDefinitionProvider(
            new HttpClient(handler)
            {
                BaseAddress = new Uri("https://freedictionaryapi.com")
            },
            new FreeDictionaryApiOptions
            {
                BaseUrl = "https://freedictionaryapi.com",
                FailureCacheTtlSeconds = 45,
                SuccessCacheTtlSeconds = successCacheTtlSeconds
            },
            NullLogger<FreeDictionaryApiDefinitionProvider>.Instance);
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
