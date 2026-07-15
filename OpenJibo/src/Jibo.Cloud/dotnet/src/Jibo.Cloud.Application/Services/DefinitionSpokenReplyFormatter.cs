namespace Jibo.Cloud.Application.Services;

public static class DefinitionSpokenReplyFormatter
{
    private const string MissingWordReply =
        "I didn't catch what word you wanted me to define. Can you ask me again with a hey jibo?";

    private const string NotFoundReply = "I couldn't find a definition for that word.";

    public static string FormatMissingWord() => MissingWordReply;

    public static string Format(string? definition)
    {
        return string.IsNullOrWhiteSpace(definition)
            ? NotFoundReply
            : $"The definition is. {definition.Trim()}";
    }
}
