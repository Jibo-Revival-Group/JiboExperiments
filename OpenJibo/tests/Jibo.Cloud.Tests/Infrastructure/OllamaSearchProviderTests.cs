using System.Net;
using System.Text;
using System.Text.Json;
using Jibo.Cloud.Application.Abstractions;
using Jibo.Cloud.Infrastructure.Search;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jibo.Cloud.Tests.Infrastructure;

public sealed class OllamaSearchProviderTests
{
    [Fact]
    public async Task SearchAsync_ReturnsAnswer_WhenOllamaResponds()
    {
        HttpRequestMessage? capturedRequest = null;
        var handler = new RecordingHttpMessageHandler(request =>
        {
            capturedRequest = request;
            return JsonResponse("""
                {
                  "model": "llava:7b",
                  "response": "Donald Trump was born on June 14, 1946.",
                  "done": true
                }
                """);
        });
        var provider = CreateProvider(handler, model: "llava:7b");

        var result = await provider.SearchAsync(new KnowledgeSearchRequest(
            "How old is Donald Trump",
            SearchBackendKind.Ollama,
            UseFallbackSettings: false));

        Assert.NotNull(result);
        Assert.Equal(SearchBackendKind.Ollama, result!.BackendKind);
        Assert.Contains("June 14, 1946", result.AnswerText);
        Assert.NotNull(capturedRequest);
        Assert.Equal(HttpMethod.Post, capturedRequest!.Method);
        Assert.Equal("http://192.168.7.108:11434/api/generate", capturedRequest.RequestUri!.ToString());

        var body = await capturedRequest.Content!.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);
        Assert.Equal("llava:7b", json.RootElement.GetProperty("model").GetString());
        Assert.Equal("How old is Donald Trump", json.RootElement.GetProperty("prompt").GetString());
        Assert.False(json.RootElement.GetProperty("stream").GetBoolean());
    }

    [Fact]
    public async Task SearchAsync_UsesDefaultModel_WhenModelNotConfigured()
    {
        HttpRequestMessage? capturedRequest = null;
        var handler = new RecordingHttpMessageHandler(request =>
        {
            capturedRequest = request;
            return JsonResponse("""{"response":"42","done":true}""");
        });
        var provider = CreateProvider(handler);

        await provider.SearchAsync(new KnowledgeSearchRequest(
            "What is six times seven",
            SearchBackendKind.Ollama,
            UseFallbackSettings: false));

        var body = await capturedRequest!.Content!.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);
        Assert.Equal(SearchBackendSettingsResolver.DefaultOllamaModel, json.RootElement.GetProperty("model").GetString());
    }

    [Fact]
    public async Task SearchAsync_RetriesWithFallbackModel_WhenPrimaryModelUnavailable()
    {
        var attempts = 0;
        var handler = new RecordingHttpMessageHandler(request =>
        {
            attempts += 1;
            var body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            using var json = JsonDocument.Parse(body);
            var model = json.RootElement.GetProperty("model").GetString();
            if (model == "missing-model")
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound)
                {
                    Content = new StringContent("""{"error":"model 'missing-model' not found"}""", Encoding.UTF8,
                        "application/json")
                };
            }

            return JsonResponse("""{"response":"Recovered with fallback model.","done":true}""");
        });
        var provider = CreateProvider(
            handler,
            model: "missing-model",
            fallbackModel: "llava:7b");

        var result = await provider.SearchAsync(new KnowledgeSearchRequest(
            "How old is Donald Trump",
            SearchBackendKind.Ollama,
            UseFallbackSettings: false));

        Assert.NotNull(result);
        Assert.Equal("Recovered with fallback model.", result!.AnswerText);
        Assert.Equal(2, attempts);
    }

    [Fact]
    public async Task SearchAsync_UsesFallbackModel_WhenFallbackSettingsRequested()
    {
        HttpRequestMessage? capturedRequest = null;
        var handler = new RecordingHttpMessageHandler(request =>
        {
            capturedRequest = request;
            return JsonResponse("""{"response":"Fallback settings model.","done":true}""");
        });
        var provider = CreateProvider(
            handler,
            model: "llava:7b",
            fallbackModel: "llama3.1:8b");

        await provider.SearchAsync(new KnowledgeSearchRequest(
            "How old is Donald Trump",
            SearchBackendKind.Ollama,
            UseFallbackSettings: true));

        var body = await capturedRequest!.Content!.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);
        Assert.Equal("llama3.1:8b", json.RootElement.GetProperty("model").GetString());
    }

    private static OllamaSearchProvider CreateProvider(
        HttpMessageHandler handler,
        string? model = null,
        string? fallbackModel = null,
        string endpoint = "http://192.168.7.108:11434")
    {
        return new OllamaSearchProvider(
            new HttpClient(handler),
            new SearchBackendOptions
            {
                ApiEndpoint = endpoint,
                Model = model,
                FallbackModel = fallbackModel,
                CacheTtlSeconds = 300,
                FailureCacheTtlSeconds = 45
            },
            NullLogger<OllamaSearchProvider>.Instance);
    }

    private static HttpResponseMessage JsonResponse(string body)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
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
}
