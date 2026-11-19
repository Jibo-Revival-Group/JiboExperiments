namespace Jibo.Cloud.Application.Services;

public static class PegasusQuipRoutes
{
    private sealed record Route(
        string Intent, string MimId, string[] Phrases, string ClientIntent,
        string? Entity = null, string? Value = null, bool PreserveAnimation = false);

    private static readonly Route[] Routes =
    [
        new("robot_turn_off_eye", "RA_JBO_TurnOffEye",
            ["can you turn off your eye", "can you turn off your eyes", "turn off your eye", "turn off your eyes",
             "turn your eye off", "turn your eyes off", "switch off your eye", "switch off your eyes"],
            "requestTurnOffEye"),
        new("robot_tell_fortune", "RA_JBO_TellFortune",
            ["tell me a fortune", "tell me my fortune", "can you tell my fortune", "can you tell me my fortune"],
            "requestTellJiboContent", "JiboContent", "Fortune"),
        new("robot_tell_riddle", "RA_JBO_TellRiddle",
            ["give me a riddle", "tell a riddle", "tell me a riddle", "tell us a riddle", "can you tell me a riddle"],
            "requestTellJiboContent", "JiboContent", "Riddle"),
        new("robot_tell_haiku", "RA_JBO_TellHaiku",
            ["tell me a haiku", "read me a haiku", "recite a haiku", "can you tell me a haiku"],
            "requestTellJiboContent", "JiboContent", "Haiku"),
        new("robot_tell_deep_thought", "RA_JBO_TellDeepThought",
            ["give me a deep thought", "tell me a deep thought", "share a deep thought"],
            "requestTellJiboContent", "JiboContent", "DeepThought"),
        new("robot_tell_palindrome", "RA_JBO_TellPalindrome",
            ["give me a palindrome", "tell me a palindrome", "do you know a palindrome"],
            "requestTellJiboContent", "JiboContent", "Palindrome"),
        new("robot_tell_weird_word", "RA_JBO_TellWeirdWord",
            ["give me a weird word", "tell me a weird word", "do you know a weird word"],
            "requestTellJiboContent", "JiboContent", "WeirdWord"),
        new("robot_tell_secrets", "RA_JBO_TellSecrets",
            ["tell me your secrets", "tell me a secret", "tell me one of your secrets"], "requestTellSecrets"),
        new("robot_words_of_wisdom", "RA_JBO_TellWordsOfWisdom",
            ["do you have any words of wisdom", "give me some words of wisdom", "words of wisdom"],
            "requestTellJiboContent", "JiboContent", "WordsOfWisdom"),
        new("robot_is_alive", "RI_JBO_IsAlive",
            ["are you alive", "do you feel living"], "isJiboDescriptor", "GeneralDescriptor", "Alive"),
        new("robot_can_get_lonely", "RI_JBO_CanGetLonely",
            ["do you get lonely", "can you get lonely", "do you ever get lonely"],
            "canJiboBeDescriptor", "Emotion", "Lonely"),
        new("robot_beatbox", "RA_JBO_Beatbox",
            ["beatbox", "beat box", "can you beatbox", "can you beat box", "beatbox for me"], "requestBeatbox"),
        new("robot_magic_trick", "RA_JBO_MagicTrick",
            ["can you do magic", "do a magic trick", "can you do a magic trick", "show me a magic trick"],
            "requestMagicTrick", PreserveAnimation: true),
        new("robot_make_cat_sound", "RA_JBO_MakeSoundCat",
            ["meow", "meow like a cat", "can you meow like a cat", "make a sound like a cat", "talk like a cat",
             "what sound does a cat make", "what does the cat say"],
            "requestMakeSound", "Animal", "Cat", PreserveAnimation: true),
        new("robot_make_dog_sound", "RA_JBO_MakeSoundDog",
            ["bark", "bark like a dog", "can you bark like a dog", "make a sound like a dog", "talk like a dog",
             "what sound does a dog make", "what does the dog say"],
            "requestMakeSound", "Animal", "Dog", PreserveAnimation: true),
        new("robot_wink", "RA_JBO_Wink",
            ["wink", "wink for me", "can you wink for me", "give me a wink"],
            "requestWink", PreserveAnimation: true)
    ];

    private static readonly IReadOnlyDictionary<string, Route> ByIntent =
        Routes.ToDictionary(route => route.Intent, StringComparer.Ordinal);

    private static readonly IReadOnlyDictionary<string, Route> ByMimId =
        Routes.ToDictionary(route => route.MimId, StringComparer.OrdinalIgnoreCase);

    internal static string? Resolve(
        string phrase, string? clientIntent, IReadOnlyDictionary<string, string> entities)
    {
        if (string.Equals(clientIntent, "requestMakeSound", StringComparison.OrdinalIgnoreCase) &&
            !entities.ContainsKey("Animal") && entities.TryGetValue("AnimalSound", out var animalSound))
        {
            if (string.Equals(animalSound, "Cat", StringComparison.OrdinalIgnoreCase)) return "robot_make_cat_sound";
            if (string.Equals(animalSound, "Dog", StringComparison.OrdinalIgnoreCase)) return "robot_make_dog_sound";
        }

        foreach (var route in Routes)
        {
            if (route.Phrases.Contains(phrase, StringComparer.Ordinal))
                return route.Intent;

            if (string.Equals(route.ClientIntent, clientIntent, StringComparison.OrdinalIgnoreCase) &&
                (route.Entity is null || (entities.TryGetValue(route.Entity, out var value) &&
                                          string.Equals(value, route.Value, StringComparison.OrdinalIgnoreCase))))
                return route.Intent;
        }

        return null;
    }

    internal static string? GetMimId(string intent) => ByIntent.GetValueOrDefault(intent)?.MimId;

    public static bool PreservesSpeechMarkup(string mimId) => ByMimId.ContainsKey(mimId);

    public static bool PreservesAnimation(string? mimId) =>
        mimId is not null && ByMimId.GetValueOrDefault(mimId)?.PreserveAnimation == true;
}
