namespace Jibo.Cloud.Application.Services;

public static class PegasusQuipRoutes
{
    private sealed record Route(
        string Intent, string MimId, string[] Phrases, string ClientIntent,
        string? Entity = null, string? Value = null, bool PreserveAnimation = false, bool PreservePitch = false);

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
            "requestWink", PreserveAnimation: true),
        new("robot_beam_me_up", "RA_JBO_BeamMeUp",
            ["beam me up", "beam us up", "beam me up scotty", "beam me up scottie"], "referenceBeamMeUp"),
        new("robot_ask_question", "RA_JBO_AskQuestion",
            ["ask me a question", "ask us a question", "can you ask a question", "can you ask me a question"],
            "requestAskQuestion"),
        new("robot_do_impression", "RA_JBO_DoImpression",
            ["do an impression", "can you do an impression", "can you do an impression of tom hanks"],
            "requestDoImpression"),
        new("robot_show_off", "RA_JBO_ShowOff",
            ["show off", "showoff", "do something cool", "do something awesome", "do something epic"],
            "requestShowOff", PreserveAnimation: true),
        new("robot_count_to_ten", "RA_JBO_CountTo10",
            ["count to ten", "count to 10", "count up to ten", "count up to 10", "can you count to ten",
             "can you count to 10", "would you please count up to ten"],
            "requestCountToNumber", "CountNumber", "10", PreserveAnimation: true, PreservePitch: true),
        new("robot_tell_something", "RA_JBO_TellSomething",
            ["tell me something", "tell us something"], "requestTellSomething", PreservePitch: true),
        new("robot_tell_robot_fact", "RA_JBO_TellRobotFact",
            ["give me a fact about robots", "tell me a fact about robots", "tell me a robot fact"],
            "requestTellJiboContent", "JiboContent", "RobotFact"),
        new("robot_tell_human_fact", "RA_JBO_TellHumanFact",
            ["tell a human fact", "tell me a human fact", "give me a human fact",
             "tell me a fact about people", "tell me a fact about humans"],
            "requestTellJiboContent", "JiboContent", "HumanFact"),
        new("robot_tell_scifi_quote", "RA_JBO_TellScifiQuote",
            ["tell me a sci fi quote", "give me a sci fi quote", "tell me a science fiction quote"],
            "requestTellJiboContent", "JiboContent", "ScifiQuote"),
        new("robot_give_high_five", "RA_JBO_GiveHighfive",
            ["give me five", "give me a high five", "high five", "can you give me a high five"],
            "requestGiveHighfive", PreservePitch: true),
        new("robot_wave", "RA_JBO_ShowWaveEmoji",
            ["wave", "wave at me", "can you wave", "wave hello"],
            "requestShowImage", "Emoji", "Wave", PreserveAnimation: true, PreservePitch: true),
        new("robot_make_fox_sound", "RA_JBO_MakeSoundFox",
            ["bark like a fox", "what did the fox say", "what does the fox say", "what sound does a fox make"],
            "requestMakeSound", "Animal", "Fox"),
        new("robot_tell_new_year_joke", "RA_JBO_TellANewYearsJoke",
            ["give me a new year joke", "tell me a new years joke", "tell me a new year's joke",
             "tell me jokes about new year"],
            "requestTellJiboContent", "Joke", "NewYearsJoke"),
        new("robot_tell_about_yourself", "RA_JBO_TellAboutYourself",
            ["tell me about yourself", "tell us about yourself", "can you tell me about yourself"],
            "requestTellAboutYourself", PreserveAnimation: true),
        new("robot_blow_bubbles", "RA_JBO_BlowBubble",
            ["blow a bubble", "blow bubbles", "can you blow bubbles", "can you blow some bubbles for us",
             "do you have any bubbles", "show us your bubble blowing"],
            "requestBlowBubble", PreserveAnimation: true, PreservePitch: true),
        new("robot_show_mustache", "RA_JBO_ShowMustache",
            ["grow mustache", "grow your mustache", "show me your mustache", "show mustache"],
            "requestShowImage", "Emoji", "Mustache", PreserveAnimation: true),
        new("robot_show_sunglasses", "RA_JBO_ShowSunglassesEmoji",
            ["show me your sunglasses", "put on your sunglasses"],
            "requestShowImage", "Emoji", "Sunglasses", PreserveAnimation: true),
        new("robot_do_the_robot", "RA_JBO_DoTheRobot",
            ["do the robot", "can you do the robot"], "requestDoTheRobot", PreservePitch: true),
        new("robot_staring_contest", "RA_JBO_StaringContest",
            ["let's have a staring contest", "lets have a staring contest", "staring contest"],
            "requestStaringContest"),
        new("robot_misses_user", "RI_JBO_IsMissingUser",
            ["do you miss me", "did you miss me"],
            "isJiboDoingAction", "Action", "MissUser", PreserveAnimation: true),
        new("robot_believes_in_magic", "RI_JBO_BelievesInMagic",
            ["do you think magic is real", "do you believe in magic"],
            "doesJiboBelieveInThing", "GeneralLikes", "Magic", PreserveAnimation: true)
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

    public static bool PreservesPitch(string? mimId) =>
        mimId is not null && ByMimId.GetValueOrDefault(mimId)?.PreservePitch == true;
}
