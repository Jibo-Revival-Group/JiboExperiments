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
        var provider = CreateProvider(
            handler,
            "http://192.168.7.108:11434",
            "llava:7b");

        var result = await provider.SearchAsync(new KnowledgeSearchRequest(
            "How old is Donald Trump",
            new SearchBackendSpec(
                SearchBackendKind.Ollama,
                "http://192.168.7.108:11434",
                "llava:7b")));

        Assert.NotNull(result);
        Assert.Equal(SearchBackendKind.Ollama, result!.BackendKind);
        Assert.Contains("June 14, 1946", result.AnswerText);
        Assert.NotNull(capturedRequest);
        Assert.Equal(HttpMethod.Post, capturedRequest!.Method);
        Assert.Equal("http://192.168.7.108:11434/api/generate", capturedRequest.RequestUri!.ToString());

        var body = await capturedRequest.Content!.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);
        Assert.Equal("llava:7b", json.RootElement.GetProperty("model").GetString());
        var prompt = json.RootElement.GetProperty("prompt").GetString();
        Assert.Contains("Act as Jibo", prompt, StringComparison.Ordinal);
        Assert.Contains("User Request:", prompt, StringComparison.Ordinal);
        Assert.Contains("How old is Donald Trump", prompt, StringComparison.Ordinal);
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
        var provider = CreateProvider(handler, "http://127.0.0.1:11434", model: null);

        await provider.SearchAsync(new KnowledgeSearchRequest(
            "What is six times seven",
            new SearchBackendSpec(SearchBackendKind.Ollama, "http://127.0.0.1:11434", null)));

        var body = await capturedRequest!.Content!.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);
        Assert.Equal(SearchBackendSettingsResolver.DefaultOllamaModel, json.RootElement.GetProperty("model").GetString());
    }

    [Fact]
    public async Task SearchAsync_UsesCustomInstructions_WhenConfigured()
    {
        HttpRequestMessage? capturedRequest = null;
        var handler = new RecordingHttpMessageHandler(request =>
        {
            capturedRequest = request;
            return JsonResponse("""{"response":"42","done":true}""");
        });
        var provider = CreateProvider(
            handler,
            "http://127.0.0.1:11434",
            "llava:7b",
            llmInstructions: "Custom robot persona.\nBe brief.");

        await provider.SearchAsync(new KnowledgeSearchRequest(
            "What is six times seven",
            new SearchBackendSpec(SearchBackendKind.Ollama, "http://127.0.0.1:11434", "llava:7b")));

        var body = await capturedRequest!.Content!.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);
        var prompt = json.RootElement.GetProperty("prompt").GetString();
        Assert.Contains("Custom robot persona.", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("Act as Jibo", prompt, StringComparison.Ordinal);
        Assert.Contains("What is six times seven", prompt, StringComparison.Ordinal);
    }

    private static OllamaSearchProvider CreateProvider(
        HttpMessageHandler handler,
        string endpoint,
        string? model,
        string? llmInstructions = null)
    {
        var primary = model is null
            ? $"Ollama!{endpoint}"
            : $"Ollama!{endpoint}!{model}";

        return new OllamaSearchProvider(
            new HttpClient(handler),
            SearchBackendOptions.Create(primary, null, 300, 45, llmInstructions),
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
