namespace Jibo.Cloud.Application.Services;

public static class SpellSpokenReplyFormatter
{
    private const string MissingWordReply =
        "I didn't catch what word you wanted me to spell. Can you ask me again with a hey jibo?";

    public static string Format(string? word)
    {
        if (string.IsNullOrWhiteSpace(word)) return MissingWordReply;

        var pronunciations = new List<string>();
        foreach (var character in word)
        {
            if (!JiboLetterPronunciation.TryGetPronunciation(character, out var pronunciation))
                continue;

            pronunciations.Add(pronunciation);
        }

        if (pronunciations.Count == 0) return MissingWordReply;

        return $"{word} is spelt with. {string.Join(", ", pronunciations)}.";
    }
}
