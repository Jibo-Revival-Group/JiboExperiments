namespace Jibo.Cloud.Application.Services;

public static class JiboLetterPronunciation
{
    private static readonly Dictionary<char, string> Pronunciations = new()
    {
        ['a'] = "ae",
        ['b'] = "b",
        ['c'] = "see",
        ['d'] = "dee",
        ['e'] = "e",
        ['f'] = "f",
        ['g'] = "jee",
        ['h'] = "h",
        ['i'] = "eye",
        ['j'] = "jay",
        ['k'] = "kay",
        ['l'] = "l",
        ['m'] = "m",
        ['n'] = "en",
        ['o'] = "hoh",
        ['p'] = "pee",
        ['q'] = "queue",
        ['r'] = "are",
        ['s'] = "es",
        ['t'] = "tea",
        ['u'] = "you",
        ['v'] = "vee",
        ['w'] = "double you",
        ['x'] = "ex",
        ['y'] = "why",
        ['z'] = "zee"
    };

    public static bool TryGetPronunciation(char letter, out string pronunciation)
    {
        return Pronunciations.TryGetValue(char.ToLowerInvariant(letter), out pronunciation!);
    }

    /// <summary>
    /// Spells an acronym letter-by-letter for TTS (e.g. "GPU" → "jee pee you").
    /// Non-letter characters are skipped.
    /// </summary>
    public static string SpellAcronym(string acronym)
    {
        if (string.IsNullOrWhiteSpace(acronym)) return string.Empty;

        var parts = new List<string>();
        foreach (var character in acronym)
        {
            if (TryGetPronunciation(character, out var pronunciation))
                parts.Add(pronunciation);
        }

        return string.Join(' ', parts);
    }
}
