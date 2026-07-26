using System.Net;
using System.Text;
using Jibo.Cloud.Application.Abstractions;
using Jibo.Cloud.Infrastructure.Search;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jibo.Cloud.Tests.Infrastructure;

public sealed class WolframAlphaSearchProviderTests
{
    [Fact]
    public async Task SearchAsync_ReturnsSpokenAnswer_WhenWolframResponds()
    {
        HttpRequestMessage? capturedRequest = null;
        var handler = new RecordingHttpMessageHandler(request =>
        {
            capturedRequest = request;
            return TextResponse(
                "The 20th president of the United States was James Garfield from March 4, 1881, to September 19, 1881");
        });
        var provider = CreateProvider(handler);

        var result = await provider.SearchAsync(CreateRequest("What is the 20th president of the United States"));

        Assert.NotNull(result);
        Assert.Equal(SearchBackendKind.Wolfram, result!.BackendKind);
        Assert.Contains("James Garfield", result.AnswerText);
        Assert.NotNull(capturedRequest);
        var query = capturedRequest!.RequestUri!.Query;
        Assert.Contains("appid=test-app-id", query, StringComparison.Ordinal);
        Assert.Contains("What", query, StringComparison.Ordinal);
        Assert.Contains("20th", query, StringComparison.Ordinal);
        Assert.Contains("president", query, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SearchAsync_ReturnsNull_WhenApiKeyMissing()
    {
        var provider = CreateProvider(new RecordingHttpMessageHandler(_ => TextResponse("answer")), null);

        var result = await provider.SearchAsync(CreateRequest("What is two plus two", null));

        Assert.Null(result);
    }

    [Fact]
    public async Task SearchAsync_ReturnsUnavailable_WhenHttpUnauthorized()
    {
        var handler = new RecordingHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        var provider = CreateProvider(handler);

        var result = await provider.SearchAsync(CreateRequest("What is two plus two"));

        Assert.NotNull(result);
        Assert.Equal(KnowledgeSearchOutcome.Unavailable, result!.Outcome);
    }

    [Fact]
    public async Task SearchAsync_ReturnsNotFound_WhenWolframDoesNotUnderstand()
    {
        var handler = new RecordingHttpMessageHandler(_ =>
            TextResponse("Wolfram Alpha did not understand your input"));
        var provider = CreateProvider(handler);

        var result = await provider.SearchAsync(CreateRequest("blargh"));

        Assert.NotNull(result);
        Assert.Equal(KnowledgeSearchOutcome.NotFound, result!.Outcome);
    }

    [Fact]
    public async Task SearchAsync_ReturnsNotFound_WhenResponseEmpty()
    {
        var handler = new RecordingHttpMessageHandler(_ => TextResponse("   "));
        var provider = CreateProvider(handler);

        var result = await provider.SearchAsync(CreateRequest("What is two plus two"));

        Assert.NotNull(result);
        Assert.Equal(KnowledgeSearchOutcome.NotFound, result!.Outcome);
    }

    [Fact]
    public async Task KnowledgeSearchService_UsesFallbackBackend_WhenPrimaryFails()
    {
        KnowledgeSearchRequest? fallbackRequest = null;
        var primary = new StubKnowledgeSearchProvider(SearchBackendKind.Wolfram, _ => null);
        var fallback = new StubKnowledgeSearchProvider(SearchBackendKind.Ollama, request =>
        {
            fallbackRequest = request;
            return new KnowledgeSearchResult("Fallback answer.", SearchBackendKind.Ollama);
        });
        var service = new KnowledgeSearchService(
            SearchBackendOptions.Create(
                "Wolfram!test-key",
                "Ollama!http://127.0.0.1:11434!llava:7b",
                300,
                45),
            [primary, fallback],
            NullLogger<KnowledgeSearchService>.Instance);

        var result = await service.SearchAsync("What is two plus two");

        Assert.NotNull(result);
        Assert.Equal("Fallback answer.", result!.AnswerText);
        Assert.NotNull(fallbackRequest);
        Assert.Equal(SearchBackendKind.Ollama, fallbackRequest!.BackendSpec.Kind);
        Assert.Equal("llava:7b", fallbackRequest.BackendSpec.Model);
    }

    private static KnowledgeSearchRequest CreateRequest(string query, string? apiKey = "test-app-id")
    {
        return new KnowledgeSearchRequest(
            query,
            new SearchBackendSpec(SearchBackendKind.Wolfram, apiKey, null));
    }

    private static WolframAlphaSearchProvider CreateProvider(
        HttpMessageHandler handler,
        string? apiKey = "test-app-id")
    {
        return new WolframAlphaSearchProvider(
            new HttpClient(handler),
            SearchBackendOptions.Create($"Wolfram!{apiKey}", null, 300, 45),
            NullLogger<WolframAlphaSearchProvider>.Instance);
    }

    private static HttpResponseMessage TextResponse(string body)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "text/plain")
        };
    }

    private sealed class RecordingHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(responseFactory(request));
        }
    }

    private sealed class StubKnowledgeSearchProvider(
        SearchBackendKind kind,
        Func<KnowledgeSearchRequest, KnowledgeSearchResult?> resultFactory)
        : IKnowledgeSearchProvider
    {
        public SearchBackendKind Kind { get; } = kind;

        public Task<KnowledgeSearchResult?> SearchAsync(
            KnowledgeSearchRequest request,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(resultFactory(request));
        }
    }
}