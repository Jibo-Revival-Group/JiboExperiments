using System.Net;
using System.Text;
using System.Text.Json;
using Jibo.Cloud.Application.Abstractions;
using Jibo.Cloud.Infrastructure.Search;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jibo.Cloud.Tests.Infrastructure;

public sealed class ChatGptSearchProviderTests
{
    [Fact]
    public async Task SearchAsync_ReturnsAnswer_WhenOpenAiResponds()
    {
        HttpRequestMessage? capturedRequest = null;
        var handler = new RecordingHttpMessageHandler(request =>
        {
            capturedRequest = request;
            return JsonResponse("""
                {
                  "choices": [
                    {
                      "message": {
                        "role": "assistant",
                        "content": "Donald Trump was born on June 14, 1946. As of June 19, 2026, he is 80 years old."
                      }
                    }
                  ]
                }
                """);
        });
        var provider = CreateProvider(handler);

        var result = await provider.SearchAsync(new KnowledgeSearchRequest(
            "How old is Donald Trump",
            SearchBackendKind.ChatGPT,
            UseFallbackSettings: false));

        Assert.NotNull(result);
        Assert.Equal(SearchBackendKind.ChatGPT, result!.BackendKind);
        Assert.Contains("80 years old", result.AnswerText);
        Assert.NotNull(capturedRequest);
        Assert.Equal(HttpMethod.Post, capturedRequest!.Method);
        Assert.Equal("https://api.openai.com/v1/chat/completions", capturedRequest.RequestUri!.ToString());
        Assert.Equal("Bearer", capturedRequest.Headers.Authorization!.Scheme);
        Assert.Equal("test-api-key", capturedRequest.Headers.Authorization.Parameter);

        var body = await capturedRequest.Content!.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);
        Assert.Equal("gpt-5.4-nano", json.RootElement.GetProperty("model").GetString());
        Assert.Equal("How old is Donald Trump",
            json.RootElement.GetProperty("messages")[0].GetProperty("content").GetString());
        Assert.False(json.RootElement.GetProperty("stream").GetBoolean());
    }

    [Fact]
    public async Task SearchAsync_UsesConfiguredModel_WhenModelProvided()
    {
        HttpRequestMessage? capturedRequest = null;
        var handler = new RecordingHttpMessageHandler(request =>
        {
            capturedRequest = request;
            return JsonResponse("""{"choices":[{"message":{"role":"assistant","content":"Hi"}}]}""");
        });
        var provider = CreateProvider(handler, model: "gpt-5.4-nano");

        await provider.SearchAsync(new KnowledgeSearchRequest(
            "Hello",
            SearchBackendKind.ChatGPT,
            UseFallbackSettings: false));

        var body = await capturedRequest!.Content!.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);
        Assert.Equal("gpt-5.4-nano", json.RootElement.GetProperty("model").GetString());
    }

    [Fact]
    public async Task SearchAsync_StripsMarkdownBold_ForSpeech()
    {
        var handler = new RecordingHttpMessageHandler(_ => JsonResponse("""
            {
              "choices": [
                {
                  "message": {
                    "role": "assistant",
                    "content": "Donald Trump was born on **June 14, 1946**."
                  }
                }
              ]
            }
            """));
        var provider = CreateProvider(handler);

        var result = await provider.SearchAsync(new KnowledgeSearchRequest(
            "How old is Donald Trump",
            SearchBackendKind.ChatGPT,
            UseFallbackSettings: false));

        Assert.NotNull(result);
        Assert.Equal("Donald Trump was born on June 14, 1946.", result!.AnswerText);
    }

    [Fact]
    public async Task SearchAsync_ReturnsNull_WhenApiKeyMissing()
    {
        var provider = CreateProvider(
            new RecordingHttpMessageHandler(_ => JsonResponse("""{"choices":[]}""")),
            apiKey: null);

        var result = await provider.SearchAsync(new KnowledgeSearchRequest(
            "Hello",
            SearchBackendKind.ChatGPT,
            UseFallbackSettings: false));

        Assert.Null(result);
    }

    private static ChatGptSearchProvider CreateProvider(
        HttpMessageHandler handler,
        string? apiKey = "test-api-key",
        string? model = null,
        string endpoint = "https://api.openai.com/v1/chat/completions")
    {
        return new ChatGptSearchProvider(
            new HttpClient(handler),
            new SearchBackendOptions
            {
                ApiKey = apiKey,
                ApiEndpoint = endpoint,
                Model = model,
                CacheTtlSeconds = 300,
                FailureCacheTtlSeconds = 45
            },
            NullLogger<ChatGptSearchProvider>.Instance);
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
