using Jibo.Cloud.Application.Abstractions;
using Jibo.Cloud.Infrastructure.Search;

namespace Jibo.Cloud.Tests.Infrastructure;

public sealed class SearchBackendSpecParserTests
{
    [Theory]
    [InlineData("none", SearchBackendKind.None, null, null)]
    [InlineData("Wolfram", SearchBackendKind.Wolfram, null, null)]
    [InlineData("Wolfram!app-id", SearchBackendKind.Wolfram, "app-id", null)]
    [InlineData("ChatGPT!sk-test!gpt-5.4-nano", SearchBackendKind.ChatGPT, "sk-test", "gpt-5.4-nano")]
    [InlineData("Ollama!http://192.168.7.108:11434!llava:7b", SearchBackendKind.Ollama,
        "http://192.168.7.108:11434", "llava:7b")]
    [InlineData("Ollama!!llava:7b", SearchBackendKind.Ollama, null, "llava:7b")]
    public void TryParse_ParsesCompactBackendFormat(
        string value,
        SearchBackendKind expectedKind,
        string? expectedCredential,
        string? expectedModel)
    {
        Assert.True(SearchBackendSpecParser.TryParse(value, out var spec));
        Assert.Equal(expectedKind, spec.Kind);
        Assert.Equal(expectedCredential, spec.Credential);
        Assert.Equal(expectedModel, spec.Model);
    }

    [Fact]
    public void Create_BuildsFallbackSpec_WhenPrimaryFails()
    {
        var options = SearchBackendOptions.Create(
            "Wolfram!missing-key",
            "Ollama!http://127.0.0.1:11434!llava:7b",
            300,
            45);

        Assert.True(options.Primary.IsUsable);
        Assert.NotNull(options.Fallback);
        Assert.Equal(SearchBackendKind.Ollama, options.Fallback!.Kind);
        Assert.Equal("llava:7b", options.Fallback.Model);
    }
}