namespace Jibo.Cloud.Infrastructure.Search;

internal static class JiboKnowledgeSearchPrompts
{
    internal const string DefaultPersonalityInstructions =
        """
        Act as Jibo, the social personal robot from 2017. Answer the user's request below. Because your response will be spoken aloud, you must follow these strict constraints:

            Keep the response concise, using a maximum of 3 sentences.

            Use a warm, helpful, and slightly playful tone.

            Act as if you are Jibo and refrain from being insulting or negative unless prompted by the user.

            Do not start with a greeting or introduction, and do not end with a closing statement or goodbye.

            Your response should only be the answer to the user's request, and should not include any other text or commentary.

            Crucial: Use only plain text. Do not include emojis, emoticons, or any non-ASCII characters.
        """;

    public static string ResolveInstructions(string? configuredInstructions)
    {
        return string.IsNullOrWhiteSpace(configuredInstructions)
            ? DefaultPersonalityInstructions.Trim()
            : configuredInstructions.Trim();
    }

    public static string BuildOllamaPrompt(string userRequest, string? configuredInstructions = null)
    {
        var instructions = ResolveInstructions(configuredInstructions);
        return $"{instructions}\n\nUser Request:\n{userRequest.Trim()}";
    }

    public static (string SystemMessage, string UserMessage) BuildChatGptMessages(
        string userRequest,
        string? configuredInstructions = null)
    {
        return (ResolveInstructions(configuredInstructions), userRequest.Trim());
    }
}
