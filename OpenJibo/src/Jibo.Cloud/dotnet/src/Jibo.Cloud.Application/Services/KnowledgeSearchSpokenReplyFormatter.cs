using System.Text.RegularExpressions;
using Jibo.Cloud.Application.Abstractions;

namespace Jibo.Cloud.Application.Services;

internal static partial class KnowledgeSearchSpokenReplyFormatter
{
    /// <summary>
    /// Common all-caps English words that should stay as words, not letter-spelled.
    /// </summary>
    private static readonly HashSet<string> AcronymDenylist = new(StringComparer.Ordinal)
    {
        "AM", "AN", "AS", "AT", "BE", "BY", "DO", "GO", "HE", "IF", "IN", "IS", "IT", "ME", "MY",
        "NO", "OF", "ON", "OR", "SO", "TO", "UP", "US", "WE",
        "ALL", "AND", "ARE", "BUT", "CAN", "DID", "FOR", "GET", "HAD", "HAS", "HER", "HIM", "HIS",
        "HOW", "ITS", "LET", "MAY", "NEW", "NOT", "NOW", "OLD", "ONE", "OUR", "OUT", "OWN", "PUT",
        "SAY", "SEE", "SHE", "THE", "TOO", "TWO", "USE", "WAS", "WAY", "WHO", "YOU"
    };

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
            SearchBackendKind.Wikipedia => "wikipedia dot org",
            _ => "my sources"
        };
    }

    private static string ReplaceSpokenTerms(string text)
    {
        var spoken = text;
        foreach (var (pattern, replacement) in SpokenTermReplacements)
            spoken = pattern.Replace(spoken, replacement);

        spoken = ParentheticalAcronymPattern().Replace(spoken, ExpandParentheticalAcronym);
        spoken = StandaloneAcronymPattern().Replace(spoken, ExpandStandaloneAcronym);
        return spoken;
    }

    private static string ExpandParentheticalAcronym(Match match)
    {
        var letters = match.Groups[1].Value;
        if (AcronymDenylist.Contains(letters)) return match.Value;

        var spelled = JiboLetterPronunciation.SpellAcronym(letters);
        if (string.IsNullOrEmpty(spelled)) return match.Value;

        var plural = match.Groups[2].Success ? "s" : string.Empty;
        return $"({spelled}{plural})";
    }

    private static string ExpandStandaloneAcronym(Match match)
    {
        var letters = match.Groups[1].Value;
        if (AcronymDenylist.Contains(letters)) return match.Value;

        var spelled = JiboLetterPronunciation.SpellAcronym(letters);
        if (string.IsNullOrEmpty(spelled)) return match.Value;

        var plural = match.Groups[2].Success ? "s" : string.Empty;
        return spelled + plural;
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

    // ALL-CAPS acronyms in parentheses, optional lowercase plural s: (GPU) / (CPUs)
    [GeneratedRegex(@"\(([A-Z]{2,8})(s)?\)", RegexOptions.CultureInvariant)]
    private static partial Regex ParentheticalAcronymPattern();

    // Standalone ALL-CAPS acronyms (2–8 letters), optional lowercase plural s
    [GeneratedRegex(@"\b([A-Z]{2,8})(s)?\b", RegexOptions.CultureInvariant)]
    private static partial Regex StandaloneAcronymPattern();
}