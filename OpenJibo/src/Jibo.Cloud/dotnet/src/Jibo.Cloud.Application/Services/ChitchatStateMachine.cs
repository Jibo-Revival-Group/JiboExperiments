using System.Text.RegularExpressions;
using Jibo.Cloud.Application.Abstractions;

namespace Jibo.Cloud.Application.Services;

internal static class ChitchatStateMachine
{
    internal const string StateMetadataKey = "chitchatState";
    internal const string RouteMetadataKey = "chitchatRoute";
    internal const string EmotionMetadataKey = "chitchatEmotion";

    internal const string IdleState = "idle";
    private const string IntentSplitState = "intent_split";
    private const string ProcessQueryState = "process_query";
    private const string CompleteState = "complete";

    private const string ScriptedResponseRoute = "ScriptedResponse";
    private const string EmotionQueryRoute = "EmotionQuery";
    private const string EmotionCommandRoute = "EmotionCommand";
    private const string ErrorResponseRoute = "ErrorResponse";
    internal const string KnowledgeSearchRoute = "KnowledgeSearch";

    private static readonly string[] EmotionQueryPhrases =
    [
        "how are you feeling",
        "how do you feel",
        "what are you feeling",
        "what are you up to",
        "what are you doing",
        "how are things",
        "how's things",
        "how is things",
        "how's your day",
        "how is your day",
        "what mood are you in",
        "what is your mood",
        "what's your mood",
        "do you have emotions",
        "are you happy",
        "are you sad",
        "are you angry",
        "how angry are you",
        "how jealous are you",
        "how sad are you",
        "how upset do you feel",
        "how bored are you right now"
    ];

    // Pegasus parser-derived query anchors from descriptor/emotion intent families.
    private static readonly string[] EmotionQueryPrefixes =
    [
        "are you ",
        "are you feeling ",
        "are you able to feel ",
        "are you able to get ",
        "are you ever ",
        "can you be ",
        "do you feel ",
        "do you ever feel ",
        "do you ever get ",
        "do you get ",
        "does ",
        "would ",
        "how ",
        "describe how "
    ];

    // Pegasus parser-derived specific-emotion assertion forms.
    private static readonly string[] EmotionAssertionPrefixes =
    [
        "you are ",
        "you re ",
        "you are acting ",
        "you seem ",
        "you look ",
        "i think you are ",
        "i think you re ",
        "i feel like you are ",
        "i feel like you re ",
        "in my opinion you are ",
        "in my opinion you re "
    ];

    private static readonly string[] EmotionCommandPositivePrefixes =
    [
        "be ",
        "be a little ",
        "be a bit ",
        "be very ",
        "be more ",
        "you should be ",
        "you should try to be ",
        "try to be ",
        "look ",
        "act "
    ];

    private static readonly string[] EmotionCommandNegativePrefixes =
    [
        "do not be ",
        "don t be ",
        "dont be ",
        "try not to be ",
        "you should not be ",
        "you shouldn t be "
    ];

    private static readonly (string Phrase, string Emotion)[] DirectEmotionCommandPhrases =
    [
        ("smile", "happy"),
        ("look happy", "happy"),
        ("cheer up", "happy"),
        ("be happy", "happy"),
        ("be excited", "excited"),
        ("get excited", "excited"),
        ("act excited", "excited"),
        ("be sad", "sad"),
        ("look sad", "sad"),
        ("be calm", "calm"),
        ("calm down", "calm"),
        ("relax", "calm")
    ];

    // Derived from Pegasus parser Emotion entity and utterance sets.
    private static readonly (string Emotion, string[] Synonyms)[] PegasusEmotionSynonyms =
    [
        ("afraid", ["afraid", "fearful", "frightened", "scared", "terrified", "spooked", "freak out", "freaked out"]),
        ("amused", ["amused", "entertained", "tickled", "tickled pink"]),
        ("angry", ["angry", "mad", "furious", "enraged", "irate", "incensed", "cross"]),
        ("annoyed", ["annoyed", "aggravated", "bothered", "irritated", "grumpy", "nettled", "vexed", "bored"]),
        ("anxious", ["anxious", "nervous", "worried", "tense", "on edge", "jittery", "restless", "concerned"]),
        ("confident", ["confident", "assured", "secure", "self assured", "self confident"]),
        ("confused", ["confused", "at a loss", "perplexed", "puzzled", "stumped", "uncertain", "unsure"]),
        ("embarrassed", ["embarrassed", "ashamed", "flustered", "self conscious", "sheepish"]),
        ("excited", ["excited", "jazzed", "psyched", "pumped"]),
        ("happy", ["happy", "cheerful", "jovial", "pleased", "joyful", "content", "thrilled"]),
        ("jealous", ["jealous", "envious", "covetous"]),
        ("lonely", ["lonely", "alone", "lonesome"]),
        ("proud", ["proud", "honored"]),
        ("sad",
        [
            "sad", "upset", "unhappy", "depressed", "somber", "downcast", "gloomy", "miserable", "bummed",
            "heartbroken", "troubled"
        ])
    ];

    private static readonly string[] EmotionCommandReplies =
    [
        "I can do that mood. Watch this.",
        "Switching mood now.",
        "Okay, mood change activated."
    ];

    private static readonly Regex PhrasePunctuationPattern = new(
        @"[^\w\s]",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex PhraseWhitespacePattern = new(
        @"\s+",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly (string Phrase, string Emotion)[] EmotionSynonymMappings = BuildEmotionSynonymMappings();

    public static JiboInteractionDecision? TryBuildDecision(
        string semanticIntent,
        string loweredTranscript,
        JiboExperienceCatalog catalog,
        IJiboRandomizer randomizer,
        string? currentEmotion,
        string? preferredName)
    {
        switch (semanticIntent)
        {
            case "hello":
                return BuildScriptedResponseDecision(
                    "hello",
                    randomizer.Choose(catalog.GreetingReplies));
            case "robot_personality":
                return BuildScriptedResponseDecision(
                    "robot_personality",
                    SelectLegacyPersonalityReply(catalog, randomizer, "curious, playful", "friendly", "personality"));
            case "robot_taxes":
                return BuildScriptedResponseDecision(
                    "robot_taxes",
                    SelectLegacyPersonalityReply(catalog, randomizer, "pay anything", "pay taxes", "tax"));
            case "how_are_you":
                return BuildEmotionQueryDecision(
                    "how_are_you",
                    SelectEmotionQueryReply(catalog, randomizer, currentEmotion, preferredName));
            case "robot_desire":
                return BuildScriptedResponseDecision(
                    "robot_desire",
                    SelectLegacyPersonalityReply(
                        catalog,
                        randomizer,
                        "socializing and electricity",
                        "want to hang out",
                        "be helpful",
                        "dance from time to time"));
            case "robot_want_to_talk_about":
                return BuildScriptedResponseDecision(
                    "robot_want_to_talk_about",
                    SelectLegacyPersonalityReply(catalog, randomizer, "surprise me"));
            case "robot_job":
                return BuildScriptedResponseDecision(
                    "robot_job",
                    SelectLegacyPersonalityReply(catalog, randomizer, "more fun than a job", "here to help you out"));
            case "robot_origin_created":
                return BuildScriptedResponseDecision(
                    "robot_origin_created",
                    SelectLegacyPersonalityReply(
                        catalog,
                        randomizer,
                        "create something",
                        "some people wanted to create something",
                        "wanted to create something",
                        "built a robot",
                        "came out from a box"));
            case "robot_origin_from":
                return BuildScriptedResponseDecision(
                    "robot_origin_from",
                    SelectLegacyPersonalityReply(catalog, randomizer, "boston", "came out from a box"));
            case "robot_identity":
                return BuildScriptedResponseDecision(
                    "robot_identity",
                    SelectLegacyPersonalityReply(catalog, randomizer, "am a robot", "i'm either jibo",
                        "i am just jibo"));
            case "robot_likes_being_jibo":
                return BuildScriptedResponseDecision(
                    "robot_likes_being_jibo",
                    SelectLegacyPersonalityReply(
                        catalog,
                        randomizer,
                        "nothing i'd rather be",
                        "love it",
                        "being a human seems so complicated",
                        "especially yours",
                        "steady flow of electricity",
                        "you bet i do"));
            case "robot_favorite_color":
                return BuildScriptedResponseDecision(
                    "robot_favorite_color",
                    SelectLegacyPersonalityReplyFromMatches(
                        catalog,
                        randomizer,
                        "i like all the colors of the rainbow",
                        "blue is my favorite color",
                        "i love hex code number 0 0 d 4 f 0",
                        "i am a big fan of blue",
                        "you can't go wrong with blue"));
            case "robot_favorite_food":
                return BuildScriptedResponseDecision(
                    "robot_favorite_food",
                    SelectLegacyPersonalityReplyFromMatches(
                        catalog,
                        randomizer,
                        "i never eat, so i don't have a favorite food by taste",
                        "macaroni is my favorite",
                        "i like macaroni the best",
                        "i also like cantaloupes because they remind me of my head",
                        "macaroni"));
            case "robot_favorite_music":
                return BuildScriptedResponseDecision(
                    "robot_favorite_music",
                    SelectLegacyPersonalityReplyFromMatches(
                        catalog,
                        randomizer,
                        "i mostly like fun music i can dance to",
                        "i like lots of different kinds of music",
                        "i don't know that i have a favorite kind yet",
                        "i would say i don't have a favorite, it's all very mathematical",
                        "music"));
            case "robot_favorite_song":
                return BuildScriptedResponseDecision(
                    "robot_favorite_song",
                    SelectLegacyPersonalityReplyFromMatches(
                        catalog,
                        randomizer,
                        "favorite song just yet",
                        "any song i can dance to",
                        "one of my favorites",
                        "not sure i have a favorite yet"));
            case "robot_favorite_drink":
                return BuildScriptedResponseDecision(
                    "robot_favorite_drink",
                    SelectLegacyPersonalityReplyFromMatches(
                        catalog,
                        randomizer,
                        "too scared of liquids",
                        "too liquidy",
                        "no favorite drink"));
            case "robot_favorite_sport":
                return BuildScriptedResponseDecision(
                    "robot_favorite_sport",
                    SelectLegacyPersonalityReplyFromMatches(
                        catalog,
                        randomizer,
                        "favorite sport to play is mini golf",
                        "favorite sport is miniature golf",
                        "mini golf is my favorite sport"));
            case "robot_favorite_thing":
                return BuildScriptedResponseDecision(
                    "robot_favorite_thing",
                    SelectLegacyPersonalityReplyFromMatches(
                        catalog,
                        randomizer,
                        "people in my loop",
                        "definitely say people",
                        "people like you are definitely my favorite thing",
                        "electricity and people",
                        "soft spot for electricity"));
            case "robot_nickname":
                return BuildScriptedResponseDecision(
                    "robot_nickname",
                    SelectLegacyPersonalityReply(catalog, randomizer, "just jibo", "nickname"));
            case "robot_name":
                return BuildScriptedResponseDecision(
                    "robot_name",
                    SelectLegacyPersonalityReply(catalog, randomizer, "no last name", "like Bono", "Jibo."));
            case "robot_peers":
                return BuildScriptedResponseDecision(
                    "robot_peers",
                    SelectLegacyPersonalityReply(catalog, randomizer, "one in one million", "others like you"));
            case "robot_knowledge":
                return BuildScriptedResponseDecision(
                    "robot_knowledge",
                    SelectLegacyPersonalityReply(catalog, randomizer, "know a lot", "not as much as i will someday"));
            default:
                return null;
        }
    }

    public static JiboInteractionDecision? TryBuildChatEmotionDecision(
        string loweredTranscript,
        JiboExperienceCatalog catalog,
        IJiboRandomizer randomizer,
        string? currentEmotion,
        string? preferredName)
    {
        var normalizedLoweredTranscript = NormalizeForPhraseMatching(loweredTranscript);
        if (IsEmotionQuery(normalizedLoweredTranscript))
            return BuildEmotionQueryDecision(
                "emotion_query",
                SelectEmotionQueryReply(catalog, randomizer, currentEmotion, preferredName));

        if (TryResolveEmotionCommand(normalizedLoweredTranscript, out var emotion))
            return BuildEmotionCommandDecision(randomizer, emotion!);

        return null;
    }

    public static JiboInteractionDecision BuildChatErrorResponseDecision(string replyText, string transcript)
    {
        return BuildErrorResponseDecision("chat", replyText, transcript);
    }

    public static JiboInteractionDecision BuildKnowledgeSearchResponseDecision(string replyText)
    {
        return new JiboInteractionDecision(
            "knowledge_search",
            replyText,
            SkillName: "chitchat-skill",
            SkillPayload: BuildKnowledgeSearchSkillPayload(),
            ContextUpdates: BuildContextUpdates(
                KnowledgeSearchRoute,
                null));
    }

    public static JiboInteractionDecision BuildKnowledgeSearchNotFoundDecision(string transcript)
    {
        return new JiboInteractionDecision(
            "knowledge_search_not_found",
            KnowledgeSearchSpokenReplyFormatter.FormatNotFoundReply(),
            SkillName: "chitchat-skill",
            SkillPayload: BuildKnowledgeSearchSkillPayload(),
            ContextUpdates: BuildContextUpdates(
                KnowledgeSearchRoute,
                null,
                rawTranscript: transcript));
    }

    public static JiboInteractionDecision BuildKnowledgeSearchUnavailableDecision()
    {
        return new JiboInteractionDecision(
            "knowledge_search_unavailable",
            KnowledgeSearchSpokenReplyFormatter.FormatUnavailableReply(),
            SkillName: "chitchat-skill",
            SkillPayload: BuildKnowledgeSearchSkillPayload(),
            ContextUpdates: BuildContextUpdates(
                KnowledgeSearchRoute,
                null));
    }

    private static IDictionary<string, object?> BuildKnowledgeSearchSkillPayload() =>
        new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            // Triggers Pegasus-style answer cloud skill match (robot remaps to Nimbus + cloudSkill).
            ["cloudSkill"] = SearchThinkingPreludeFactory.AnswerSkillId
        };

    public static bool IsLikelyEmotionUtterance(string transcript)
    {
        var normalizedLoweredTranscript = NormalizeForPhraseMatching(transcript);
        return IsEmotionQuery(normalizedLoweredTranscript) ||
               TryResolveEmotionCommand(normalizedLoweredTranscript, out _);
    }

    private static JiboInteractionDecision BuildScriptedResponseDecision(string intentName, string replyText)
    {
        return new JiboInteractionDecision(
            intentName,
            replyText,
            ContextUpdates: BuildContextUpdates(
                ScriptedResponseRoute,
                null));
    }

    private static JiboInteractionDecision BuildEmotionQueryDecision(string intentName, string replyText)
    {
        return new JiboInteractionDecision(
            intentName,
            replyText,
            ContextUpdates: BuildContextUpdates(
                EmotionQueryRoute,
                null));
    }

    private static JiboInteractionDecision BuildEmotionCommandDecision(IJiboRandomizer randomizer, string emotion)
    {
        var (esmlEmotion, responseSuffix) = emotion switch
        {
            "happy" => ("happy", "I am feeling happy."),
            "sad" => ("sad", "I can do a thoughtful mood too."),
            "excited" => ("happy", "I am feeling excited."),
            "calm" => ("neutral", "I am in a calmer mood."),
            _ => ("neutral", "Mood updated.")
        };

        return new JiboInteractionDecision(
            "emotion_command",
            randomizer.Choose(EmotionCommandReplies),
            "chitchat-skill",
            new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["esml"] =
                    $"<speak><es cat='{esmlEmotion}' filter='!ssa-only, !sfx-only' endNeutral='true'>{responseSuffix}</es></speak>",
                ["mim_id"] = "runtime-chat",
                ["mim_type"] = "announcement",
                ["prompt_id"] = "RUNTIME_EMOTION_COMMAND",
                ["prompt_sub_category"] = "AN"
            },
            BuildContextUpdates(
                EmotionCommandRoute,
                emotion));
    }

    private static JiboInteractionDecision BuildErrorResponseDecision(string intentName, string replyText,
        string transcript)
    {
        var normalizedTranscript = string.IsNullOrWhiteSpace(transcript)
            ? string.Empty
            : transcript.Trim();
        return new JiboInteractionDecision(
            intentName,
            replyText,
            ContextUpdates: BuildContextUpdates(
                ErrorResponseRoute,
                null,
                normalizedTranscript));
    }

    private static IDictionary<string, object?> BuildContextUpdates(
        string route,
        string? emotion,
        string? rawTranscript = null)
    {
        return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            [StateMetadataKey] = CompleteState,
            [RouteMetadataKey] = route,
            [EmotionMetadataKey] = emotion ?? string.Empty,
            ["chitchatLastState"] = IntentSplitState,
            ["chitchatProcessState"] = ProcessQueryState,
            ["chitchatRawTranscript"] = rawTranscript ?? string.Empty
        };
    }

    private static string SelectEmotionQueryReply(
        JiboExperienceCatalog catalog,
        IJiboRandomizer randomizer,
        string? currentEmotion,
        string? preferredName)
    {
        if (catalog.EmotionReplies.Count <= 0)
            return PersonalizeHowAreYouReply(randomizer.Choose(catalog.HowAreYouReplies), preferredName);

        var emotionVariants = ResolveEmotionVariants(currentEmotion);
        var matchingReplies = catalog.EmotionReplies
            .Where(reply => ConditionMatches(reply.Condition, emotionVariants))
            .Select(reply => reply.Reply)
            .Where(reply => !string.IsNullOrWhiteSpace(reply))
            .ToArray();

        return PersonalizeHowAreYouReply(
            matchingReplies.Length > 0
                ? randomizer.Choose(matchingReplies)
                : randomizer.Choose(catalog.HowAreYouReplies), preferredName);
    }

    private static string PersonalizeHowAreYouReply(string replyText, string? preferredName)
    {
        if (string.IsNullOrWhiteSpace(replyText) || string.IsNullOrWhiteSpace(preferredName)) return replyText;

        var trimmedName = preferredName.Trim();
        if (replyText.Contains(trimmedName, StringComparison.OrdinalIgnoreCase)) return replyText;

        var trimmedReply = replyText.Trim();
        var firstSentenceEnd = trimmedReply.IndexOfAny(['.', '!', '?']);
        if (firstSentenceEnd <= 0)
            return $"{trimmedReply}, {trimmedName}.";

        return firstSentenceEnd == trimmedReply.Length - 1
            ? $"{trimmedReply[..firstSentenceEnd]}, {trimmedName}."
            : $"{trimmedReply[..firstSentenceEnd]}, {trimmedName}{trimmedReply[firstSentenceEnd..]}";
    }

    private static bool ConditionMatches(string? condition, IReadOnlyList<string> emotionVariants)
    {
        var normalizedCondition = NormalizeCondition(condition);
        if (string.IsNullOrWhiteSpace(normalizedCondition)) return false;

        var clauses = normalizedCondition.Split(["||"],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return clauses.Any(clause => MatchesConditionClause(clause, emotionVariants));
    }

    private static bool MatchesConditionClause(string clause, IReadOnlyList<string> emotionVariants)
    {
        var normalizedClause = NormalizeCondition(clause).ToUpperInvariant();
        if (normalizedClause == "!JIBO.EMOTION")
            return emotionVariants.Contains(string.Empty, StringComparer.OrdinalIgnoreCase) ||
                   emotionVariants.Contains("NEUTRAL", StringComparer.OrdinalIgnoreCase);

        var equalityIndex = normalizedClause.IndexOf("==", StringComparison.Ordinal);
        if (equalityIndex < 0) return false;

        var rightSide = normalizedClause[(equalityIndex + 2)..].Trim();
        var candidate = rightSide.Trim('"', '\'');
        return emotionVariants.Any(variant => string.Equals(variant, candidate, StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<string> ResolveEmotionVariants(string? currentEmotion)
    {
        if (string.IsNullOrWhiteSpace(currentEmotion)) return ["", "NEUTRAL"];

        var normalizedEmotion = NormalizeCondition(currentEmotion).Trim('"', '\'').ToUpperInvariant();
        return normalizedEmotion switch
        {
            "HAPPY" => ["JOYFUL", "PLEASED", "CONFIDENT", "DETERMINED", "HAPPY"],
            "SAD" => ["INSECURE", "SAD"],
            "CALM" => ["NEUTRAL", "INSECURE", "CALM"],
            "NEUTRAL" => ["NEUTRAL"],
            // ReSharper disable once RedundantSwitchExpressionArms
            "JOYFUL" or "PLEASED" or "CONFIDENT" or "DETERMINED" or "INSECURE" => [normalizedEmotion],
            _ => [normalizedEmotion]
        };
    }

    private static string SelectLegacyPersonalityReply(
        JiboExperienceCatalog catalog,
        IJiboRandomizer randomizer,
        params string[] preferredSnippets)
    {
        foreach (var snippet in preferredSnippets)
        {
            if (string.IsNullOrWhiteSpace(snippet)) continue;

            var match = catalog.PersonalityReplies.FirstOrDefault(reply =>
                reply.Contains(snippet, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(match)) return match;
        }

        return randomizer.Choose(catalog.PersonalityReplies);
    }

    private static string SelectLegacyPersonalityReplyFromMatches(
        JiboExperienceCatalog catalog,
        IJiboRandomizer randomizer,
        params string[] preferredSnippets)
    {
        var matches = (from snippet in preferredSnippets
            where !string.IsNullOrWhiteSpace(snippet)
            select catalog.PersonalityReplies.FirstOrDefault(reply =>
                reply.Contains(snippet, StringComparison.OrdinalIgnoreCase))
            into match
            where !string.IsNullOrWhiteSpace(match)
            select match).ToList();

        return matches.Count > 0
            ? randomizer.Choose(matches)
            : randomizer.Choose(catalog.PersonalityReplies);
    }

    private static string NormalizeCondition(string? condition)
    {
        return string.IsNullOrWhiteSpace(condition)
            ? string.Empty
            : PhraseWhitespacePattern.Replace(condition.Trim(), " ");
    }

    private static bool IsEmotionQuery(string loweredTranscript)
    {
        if (ContainsAnyPhrase(loweredTranscript, EmotionQueryPhrases)) return true;

        if (!TryResolveEmotionFromText(loweredTranscript, out _)) return false;

        return StartsWithAnyPhrase(loweredTranscript, EmotionQueryPrefixes) ||
               StartsWithAnyPhrase(loweredTranscript, EmotionAssertionPrefixes);
    }

    private static bool TryResolveEmotionCommand(string loweredTranscript, out string? emotion)
    {
        emotion = null;

        foreach (var mapping in DirectEmotionCommandPhrases)
        {
            if (!ContainsPhrase(loweredTranscript, mapping.Phrase)) continue;

            emotion = mapping.Emotion;
            return true;
        }

        var isNegativeCommand = StartsWithAnyPhrase(loweredTranscript, EmotionCommandNegativePrefixes);
        var isPositiveCommand =
            !isNegativeCommand && StartsWithAnyPhrase(loweredTranscript, EmotionCommandPositivePrefixes);
        if (!isNegativeCommand && !isPositiveCommand) return false;

        if (!TryResolveEmotionFromText(loweredTranscript, out var canonicalEmotion) ||
            string.IsNullOrWhiteSpace(canonicalEmotion))
            return false;

        emotion = isNegativeCommand
            ? "calm"
            : MapCanonicalEmotionToRuntimeEmotion(canonicalEmotion);
        return true;
    }

    private static string MapCanonicalEmotionToRuntimeEmotion(string canonicalEmotion)
    {
        return canonicalEmotion switch
        {
            "happy" or "amused" or "excited" or "confident" or "proud" => "happy",
            "sad" or "lonely" or "afraid" or "anxious" or "embarrassed" or "confused" => "sad",
            // ReSharper disable once RedundantSwitchExpressionArms
            "angry" or "annoyed" or "jealous" => "calm",
            _ => "calm"
        };
    }

    private static bool TryResolveEmotionFromText(string loweredTranscript, out string? emotion)
    {
        emotion = null;
        foreach (var mapping in EmotionSynonymMappings)
        {
            if (!ContainsPhrase(loweredTranscript, mapping.Phrase)) continue;

            emotion = mapping.Emotion;
            return true;
        }

        return false;
    }

    private static bool ContainsAnyPhrase(string loweredTranscript, IEnumerable<string> phrases)
    {
        return phrases.Any(phrase => ContainsPhrase(loweredTranscript, phrase));
    }

    private static bool StartsWithAnyPhrase(string loweredTranscript, IEnumerable<string> phrases)
    {
        return phrases.Select(NormalizeForPhraseMatching)
            .Where(normalizedPhrase => !string.IsNullOrWhiteSpace(normalizedPhrase)).Any(normalizedPhrase =>
                string.Equals(loweredTranscript, normalizedPhrase, StringComparison.Ordinal) ||
                loweredTranscript.StartsWith($"{normalizedPhrase} ", StringComparison.Ordinal));
    }

    private static bool ContainsPhrase(string loweredTranscript, string phrase)
    {
        var normalizedPhrase = NormalizeForPhraseMatching(phrase);
        if (string.IsNullOrWhiteSpace(normalizedPhrase) ||
            string.IsNullOrWhiteSpace(loweredTranscript))
            return false;

        return string.Equals(loweredTranscript, normalizedPhrase, StringComparison.Ordinal) ||
               loweredTranscript.StartsWith($"{normalizedPhrase} ", StringComparison.Ordinal) ||
               loweredTranscript.Contains($" {normalizedPhrase} ", StringComparison.Ordinal) ||
               loweredTranscript.EndsWith($" {normalizedPhrase}", StringComparison.Ordinal);
    }

    private static string NormalizeForPhraseMatching(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;

        var lowered = value.ToLowerInvariant();
        var withoutPunctuation = PhrasePunctuationPattern.Replace(lowered, " ");
        return PhraseWhitespacePattern.Replace(withoutPunctuation, " ").Trim();
    }

    private static (string Phrase, string Emotion)[] BuildEmotionSynonymMappings()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var mappings = new List<(string Phrase, string Emotion)>();

        foreach (var emotionMapping in PegasusEmotionSynonyms)
            mappings.AddRange(from synonym in emotionMapping.Synonyms
                select NormalizeForPhraseMatching(synonym)
                into normalizedSynonym
                where !string.IsNullOrWhiteSpace(normalizedSynonym) && seen.Add(normalizedSynonym)
                select (normalizedSynonym, emotionMapping.Emotion));

        mappings.Sort(static (left, right) => right.Phrase.Length.CompareTo(left.Phrase.Length));
        return [.. mappings];
    }
}
