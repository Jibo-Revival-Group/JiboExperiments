using Jibo.Cloud.Application.Abstractions;

namespace Jibo.Cloud.Infrastructure.Search;

internal static class SearchBackendSettingsResolver
{
    public const string DefaultWolframEndpoint = "http://api.wolframalpha.com/v1/spoken";
    public const string DefaultOllamaBaseUrl = "http://127.0.0.1:11434";
    public const string DefaultChatGptEndpoint = "https://api.openai.com/v1/chat/completions";
    public const string DefaultOllamaModel = "llama3.1:8b";
    public const string DefaultChatGptModel = "gpt-5.4-nano";

    public static string? ResolveEndpoint(
        SearchBackendOptions options,
        SearchBackendKind backend,
        bool useFallbackSettings)
    {
        var configured = useFallbackSettings ? options.FallbackApiEndpoint : options.ApiEndpoint;
        if (!string.IsNullOrWhiteSpace(configured))
            return NormalizeEndpoint(backend, configured);

        return backend switch
        {
            SearchBackendKind.Wolfram => DefaultWolframEndpoint,
            SearchBackendKind.Ollama => $"{DefaultOllamaBaseUrl.TrimEnd('/')}/api/generate",
            SearchBackendKind.ChatGPT => DefaultChatGptEndpoint,
            _ => null
        };
    }

    public static string ResolveModel(
        SearchBackendOptions options,
        SearchBackendKind backend,
        bool useFallbackSettings)
    {
        if (backend is not (SearchBackendKind.Ollama or SearchBackendKind.ChatGPT))
            return string.Empty;

        if (useFallbackSettings && !string.IsNullOrWhiteSpace(options.FallbackModel))
            return options.FallbackModel.Trim();

        if (!useFallbackSettings && !string.IsNullOrWhiteSpace(options.Model))
            return options.Model.Trim();

        return backend switch
        {
            SearchBackendKind.Ollama => DefaultOllamaModel,
            SearchBackendKind.ChatGPT => DefaultChatGptModel,
            _ => string.Empty
        };
    }

    public static string? ResolveAlternateModel(SearchBackendOptions options, SearchBackendKind backend, string primaryModel)
    {
        if (backend is not (SearchBackendKind.Ollama or SearchBackendKind.ChatGPT))
            return null;

        if (string.IsNullOrWhiteSpace(options.FallbackModel))
            return null;

        var fallbackModel = options.FallbackModel.Trim();
        return string.Equals(fallbackModel, primaryModel, StringComparison.OrdinalIgnoreCase)
            ? null
            : fallbackModel;
    }

    private static string NormalizeEndpoint(SearchBackendKind backend, string endpoint)
    {
        var trimmed = endpoint.Trim();
        if (backend != SearchBackendKind.Ollama) return trimmed;

        return trimmed.EndsWith("/api/generate", StringComparison.OrdinalIgnoreCase)
            ? trimmed
            : $"{trimmed.TrimEnd('/')}/api/generate";
    }
}
