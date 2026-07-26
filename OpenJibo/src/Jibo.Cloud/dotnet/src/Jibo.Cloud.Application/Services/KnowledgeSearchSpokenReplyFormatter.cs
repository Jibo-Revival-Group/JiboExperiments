using System.Text.RegularExpressions;
using Jibo.Cloud.Application.Abstractions;

namespace Jibo.Cloud.Application.Services;

internal static partial class KnowledgeSearchSpokenReplyFormatter
{
    private static readonly (Regex Pattern, string Replacement)[] SpokenTermReplacements =
    [
        (WolframAlphaPattern(), "wolf ram alpha"),
        (WolframPattern(), "wolf ram"),
        (ChatGptPattern(), "chat gee pee tee"),
        (OpenAiPattern(), "open ae eye"),
        (GptNumberedPattern(), "gee pee tee $1"),
        (GptPattern(), "gee pee tee"),
        (LlmsPattern(), "ell ell ems"),
        (LlmPattern(), "ell ell em"),
        (OllamaPattern(), "ollama"),
        (AiPattern(), "ae eye")
    ];

    public static string FormatReply(string answerText, SearchBackendKind backendKind)
    {
        if (string.IsNullOrWhiteSpace(answerText)) return string.Empty;

        var spokenBody = ReplaceSpokenTerms(answerText.Trim());
        return $"According to {DescribeSource(backendKind)}. {spokenBody}";
    }

    public static string FormatNotFoundReply() => "I can't find anything.";

    public static string FormatUnavailableReply() =>
        "Huh, it seems like my info sources are down. Try asking me again a little later.";

    internal static string DescribeSource(SearchBackendKind backendKind)
    {
        return backendKind switch
        {
            SearchBackendKind.Wolfram => "wolf ram alpha",
            SearchBackendKind.ChatGPT => "chat gee pee tee",
            SearchBackendKind.Ollama => "ollama",
            SearchBackendKind.Wikipedia => "wikipedia",
            _ => "my sources"
        };
    }

    private static string ReplaceSpokenTerms(string text)
    {
        var spoken = text;
        foreach (var (pattern, replacement) in SpokenTermReplacements)
            spoken = pattern.Replace(spoken, replacement);

        return spoken;
    }

    [GeneratedRegex(@"\bWolfram\s*Alpha\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex WolframAlphaPattern();

    [GeneratedRegex(@"\bWolfram\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex WolframPattern();

    [GeneratedRegex(@"\bChat\s*GPT\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ChatGptPattern();

    [GeneratedRegex(@"\bOpen\s*AI\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex OpenAiPattern();

    [GeneratedRegex(@"\bGPT-(\d+)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex GptNumberedPattern();

    [GeneratedRegex(@"\bGPT\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex GptPattern();

    [GeneratedRegex(@"\bLLMs\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LlmsPattern();

    [GeneratedRegex(@"\bLLM\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LlmPattern();

    [GeneratedRegex(@"\bOllama\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex OllamaPattern();

    [GeneratedRegex(@"\bAI\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AiPattern();
}