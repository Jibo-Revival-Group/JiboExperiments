using Jibo.Cloud.Application.Abstractions;

namespace Jibo.Cloud.Infrastructure.Search;

public static class SearchBackendSettingsResolver
{
    public const string DefaultWolframEndpoint = "https://api.wolframalpha.com/v1/spoken";
    public const string DefaultOllamaBaseUrl = "http://127.0.0.1:11434";
    public const string DefaultChatGptEndpoint = "https://api.openai.com/v1/chat/completions";
    public const string DefaultOllamaModel = "llama3.1:8b";
    public const string DefaultChatGptModel = "gpt-5.4-nano";

    public static string? ResolveEndpoint(SearchBackendSpec spec)
    {
        return spec.Kind switch
        {
            SearchBackendKind.Wolfram => DefaultWolframEndpoint,
            SearchBackendKind.Ollama => NormalizeOllamaEndpoint(
                string.IsNullOrWhiteSpace(spec.Credential) ? DefaultOllamaBaseUrl : spec.Credential),
            SearchBackendKind.ChatGPT => DefaultChatGptEndpoint,
            _ => null
        };
    }

    public static string ResolveModel(SearchBackendSpec spec)
    {
        if (spec.Kind is not (SearchBackendKind.Ollama or SearchBackendKind.ChatGPT))
            return string.Empty;

        if (!string.IsNullOrWhiteSpace(spec.Model))
            return spec.Model.Trim();

        return spec.Kind switch
        {
            SearchBackendKind.Ollama => DefaultOllamaModel,
            SearchBackendKind.ChatGPT => DefaultChatGptModel,
            _ => string.Empty
        };
    }

    private static string NormalizeOllamaEndpoint(string endpoint)
    {
        var trimmed = endpoint.Trim();
        return trimmed.EndsWith("/api/generate", StringComparison.OrdinalIgnoreCase)
            ? trimmed
            : $"{trimmed.TrimEnd('/')}/api/generate";
    }
}
