using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Jibo.Cloud.Application.Abstractions;
using Jibo.Cloud.Domain.Models;
using Jibo.Runtime.Abstractions;

namespace Jibo.Cloud.Application.Services;

public sealed partial class JiboInteractionService(
    JiboExperienceContentCache contentCache,
    IJiboRandomizer randomizer,
    IPersonalMemoryStore personalMemoryStore,
    IWeatherReportProvider? weatherReportProvider = null,
    ICalendarReportProvider? calendarReportProvider = null,
    ICommuteReportProvider? commuteReportProvider = null,
    INewsBriefingProvider? newsBriefingProvider = null,
    ICloudStateStore? cloudStateStore = null)
{
    private const string GreetingRouteMetadataKey = "greetingsRoute";
    private const string GreetingSpeakerMetadataKey = "greetingsSpeaker";
    private const string LastProactiveGreetingUtcMetadataKey = "greetingsLastProactiveUtc";
    private const string LastReactiveGreetingUtcMetadataKey = "greetingsLastReactiveUtc";

    private const int MaxWeatherForecastDayOffset = 5;
    private const int MaxNewsHeadlines = 3;
    private const int MaxPreferredNewsCategories = 2;

    private static readonly Regex SplitAlarmPattern = new(
        @"\b(?<hour>\d{1,2}|one|two|three|four|five|six|seven|eight|nine|ten|eleven|twelve)(?:[:\s,-]+(?<minute>\d{2}|[a-z\-]+(?:\s+[a-z\-]+)?))?\s*(?<ampm>a[\s\.]*m\.?|p[\s\.]*m\.?)?\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex CompactAlarmPattern = new(
        @"\b(?<compact>\d{3,4})\s*(?<ampm>a[\s\.]*m\.?|p[\s\.]*m\.?)?\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex VolumeLevelPattern = new(
        @"\b(?:volume|loudness)\s*(?:to|at|level|is)?\s*(?<value>10|\d|one|two|three|four|five|six|seven|eight|nine|ten)\b|\b(?:set|change|make|turn)\s+(?:the\s+|your\s+)?(?:volume|loudness)\s*(?:to|at)?\s*(?<value>10|\d|one|two|three|four|five|six|seven|eight|nine|ten)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex VolumeToValueHomophonePattern = new(
        @"\b(?:volume|loudness)\s+(?:2|two|to)\s+(?<value>10|\d|one|two|three|four|five|six|seven|eight|nine|ten)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly string[] CommandLeadPhrases =
    [
        "hey jibo",
        "hello jibo",
        "hi jibo",
        "jibo",
        "o",
        "oh",
        "so",
        "well",
        "um",
        "uh",
        "hmm",
        "erm",
        "ah",
        "please",
        "ok jibo",
        "okay jibo"
    ];

    private static readonly Regex AlarmDeletePattern = new(
        @"\b(?:cancel|delete|remove|stop|turn\s+off)\s+(?:the\s+)?(?:alarm|along|elo)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex WeatherLocationPattern = new(
        @"\b(?:in|for|at)\s+(?<location>[a-z][a-z\s'\-]+)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex WeatherLocationSuffixPattern = new(
        @"\b(?:today|tonight|tomorrow|day after tomorrow|outside|right now|please|thanks|this weekend|next weekend|the weekend|weekend|this week|next week|on monday|on tuesday|on wednesday|on thursday|on friday|on saturday|on sunday|this monday|this tuesday|this wednesday|this thursday|this friday|this saturday|this sunday|next monday|next tuesday|next wednesday|next thursday|next friday|next saturday|next sunday|monday|tuesday|wednesday|thursday|friday|saturday|sunday)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex WeatherConditionForecastPattern = new(
        @"\bwill it be\s+(sunny|cloudy|windy|foggy|stormy|rainy|snowy|hail|hailing)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex WeatherTopicLocationPattern = new(
        @"\b(?:weather|forecast|temperature|humidity)\b.*\b(?:in|for|at)\s+[a-z]",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex WeatherDayOfWeekPattern = new(
        @"\b(?<next>next\s+)?(?<this>this\s+)?(?:on\s+)?(?<day>monday|tuesday|wednesday|thursday|friday|saturday|sunday)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly PizzaMimPrompt[] PizzaMimPrompts =
    [
        new("RA_JBO_ShowPizzaMaking_AN_01", "<speak><anim cat='jiboji' filter='pizza-making'/></speak>"),
        new("RA_JBO_ShowPizzaMaking_AN_02",
            "<speak><anim cat='jiboji' filter='pizza-making' nonBlocking='true'/><pitch mult='1.2'>One </pitch> pizza, coming right up.</speak>"),
        new("RA_JBO_ShowPizzaMaking_AN_03",
            "<speak><anim cat='jiboji' filter='pizza-making' nonBlocking='true'/>My <pitch mult='1.2'>specialty </pitch>.</speak>")
    ];

    private static readonly string[] PreferenceSetMarkers =
    [
        "my favorite ",
        "my favourite "
    ];

    private static readonly string[] PreferenceReverseMarkers =
    [
        " is my favorite ",
        " is my favourite ",
        " are my favorite ",
        " are my favourite "
    ];

    private static readonly string[] WeatherDateEntityKeys =
    [
        "date",
        "sys.date",
        "datetime",
        "dateTime",
        "date_time",
        "day"
    ];

    private static readonly string[] YesNoAcknowledgementPrefixes =
    [
        "uh",
        "um",
        "hmm",
        "well",
        "so",
        "actually",
        "honestly",
        "thanks",
        "thank you"
    ];

    private static readonly HashSet<string> YesNoAffirmativeLeadTokens = new(StringComparer.Ordinal)
    {
        "yes",
        "yeah",
        "yep",
        "yup",
        "sure",
        "ok",
        "okay",
        "absolutely",
        "affirmative",
        "definitely",
        "certainly",
        "indeed"
    };

    private static readonly HashSet<string> YesNoNegativeLeadTokens = new(StringComparer.Ordinal)
    {
        "no",
        "nope",
        "nah",
        "negative",
        "never"
    };

    private static readonly HashSet<string> YesNoAffirmativeLeadPhrases = new(StringComparer.Ordinal)
    {
        "uh huh",
        "sounds good",
        "sure thing",
        "why not",
        "please do",
        "go ahead",
        "of course",
        "i guess so",
        "i think so"
    };

    private static readonly HashSet<string> YesNoNegativeLeadPhrases = new(StringComparer.Ordinal)
    {
        "not now",
        "not today",
        "not really",
        "no thanks",
        "no thank you",
        "maybe later",
        "i guess not",
        "i do not",
        "i dont",
        "i don t"
    };

    // Directly imported from Pegasus parser intent phrase families:
    // userLikesThing / userDislikesThing / doesUserLikeThing / doesUserDislikeThing.
    private static readonly (string Prefix, PersonalAffinity Affinity)[] PegasusUserAffinitySetPrefixes =
    [
        ("i love ", PersonalAffinity.Love),
        ("i like ", PersonalAffinity.Like),
        ("i like the ", PersonalAffinity.Like),
        ("i enjoy ", PersonalAffinity.Like),
        ("i do like ", PersonalAffinity.Like),
        ("we love ", PersonalAffinity.Love),
        ("we like ", PersonalAffinity.Like),
        ("we enjoy ", PersonalAffinity.Like),
        ("i dislike ", PersonalAffinity.Dislike),
        ("i hate ", PersonalAffinity.Dislike),
        ("i hate the ", PersonalAffinity.Dislike),
        ("i loathe ", PersonalAffinity.Dislike),
        ("i don t like ", PersonalAffinity.Dislike),
        ("i dont like ", PersonalAffinity.Dislike),
        ("i not like ", PersonalAffinity.Dislike),
        ("i do not like ", PersonalAffinity.Dislike),
        ("i did not like ", PersonalAffinity.Dislike),
        ("i did not like the ", PersonalAffinity.Dislike),
        ("i didn t like ", PersonalAffinity.Dislike),
        ("i didnt like ", PersonalAffinity.Dislike),
        ("i didn t like the ", PersonalAffinity.Dislike),
        ("i didnt like the ", PersonalAffinity.Dislike),
        ("i didn t really like ", PersonalAffinity.Dislike),
        ("i didnt really like ", PersonalAffinity.Dislike),
        ("i don t really like ", PersonalAffinity.Dislike),
        ("i dont really like ", PersonalAffinity.Dislike),
        ("i don t enjoy ", PersonalAffinity.Dislike),
        ("i dont enjoy ", PersonalAffinity.Dislike),
        ("i do not enjoy ", PersonalAffinity.Dislike),
        ("i did not enjoy ", PersonalAffinity.Dislike),
        ("i didn t enjoy ", PersonalAffinity.Dislike),
        ("i didnt enjoy ", PersonalAffinity.Dislike),
        ("i didn t really enjoy ", PersonalAffinity.Dislike),
        ("i didnt really enjoy ", PersonalAffinity.Dislike),
        ("i don t love ", PersonalAffinity.Dislike),
        ("i dont love ", PersonalAffinity.Dislike),
        ("i do not love ", PersonalAffinity.Dislike),
        ("i don t love to ", PersonalAffinity.Dislike),
        ("i dont love to ", PersonalAffinity.Dislike),
        ("i do not love to ", PersonalAffinity.Dislike),
        ("i can t stand ", PersonalAffinity.Dislike),
        ("i cant stand ", PersonalAffinity.Dislike),
        ("i can t stand the ", PersonalAffinity.Dislike),
        ("i cant stand the ", PersonalAffinity.Dislike),
        ("we dislike ", PersonalAffinity.Dislike),
        ("we hate ", PersonalAffinity.Dislike),
        ("we despise ", PersonalAffinity.Dislike),
        ("we detest ", PersonalAffinity.Dislike),
        ("we loathe ", PersonalAffinity.Dislike),
        ("we can t stand ", PersonalAffinity.Dislike),
        ("we cant stand ", PersonalAffinity.Dislike),
        ("i despise ", PersonalAffinity.Dislike),
        ("i detest ", PersonalAffinity.Dislike)
    ];

    private static readonly (string Prefix, PersonalAffinity? ExpectedAffinity)[] PegasusUserAffinityLookupPrefixes =
    [
        ("do i love ", PersonalAffinity.Love),
        ("do i like ", PersonalAffinity.Like),
        ("do i enjoy ", PersonalAffinity.Like),
        ("do i dislike ", PersonalAffinity.Dislike),
        ("do i hate ", PersonalAffinity.Dislike),
        ("do i loathe ", PersonalAffinity.Dislike),
        ("do i not like ", PersonalAffinity.Dislike),
        ("do i despise ", PersonalAffinity.Dislike),
        ("do i detest ", PersonalAffinity.Dislike),
        ("do you think i like ", PersonalAffinity.Like),
        ("do you believe i like ", PersonalAffinity.Like),
        ("do you think i don t like ", PersonalAffinity.Dislike),
        ("do you believe i don t like ", PersonalAffinity.Dislike),
        ("how do i feel about ", null),
        ("what do i think about ", null)
    ];

    private static readonly string[] PizzaPreferenceCategories =
    [
        "food",
        "meal",
        "dish",
        "dinner",
        "lunch",
        "snack"
    ];

    private static readonly HashSet<string> GenericWeatherLocationTerms = new(StringComparer.OrdinalIgnoreCase)
    {
        "my area",
        "our area",
        "this area",
        "the area",
        "the city",
        "this city",
        "our city",
        "my city",
        "the town",
        "this town",
        "our town",
        "my town",
        "our street",
        "this street",
        "my street",
        "the neighborhood",
        "the neighbourhood",
        "this neighborhood",
        "this neighbourhood",
        "our neighborhood",
        "our neighbourhood"
    };

    private static readonly HashSet<string> SpokenAbbreviationTokens = new(StringComparer.Ordinal)
    {
        "US",
        "USA",
        "UK",
        "UAE",
        "EU",
        "DC",
        "AL",
        "AK",
        "AZ",
        "AR",
        "CA",
        "CO",
        "CT",
        "DE",
        "FL",
        "GA",
        "HI",
        "ID",
        "IL",
        "IN",
        "IA",
        "KS",
        "KY",
        "LA",
        "ME",
        "MD",
        "MA",
        "MI",
        "MN",
        "MS",
        "MO",
        "MT",
        "NE",
        "NV",
        "NH",
        "NJ",
        "NM",
        "NY",
        "NC",
        "ND",
        "OH",
        "OK",
        "OR",
        "PA",
        "RI",
        "SC",
        "SD",
        "TN",
        "TX",
        "UT",
        "VT",
        "VA",
        "WA",
        "WV",
        "WI",
        "WY"
    };

    private static readonly TimeSpan ProactiveGreetingCooldown = TimeSpan.FromMinutes(20);

    private static readonly (string Keyword, string Category)[] NewsCategoryKeywordMap =
    [
        ("sports", "sports"),
        ("sport", "sports"),
        ("football", "sports"),
        ("baseball", "sports"),
        ("basketball", "sports"),
        ("hockey", "sports"),
        ("technology", "technology"),
        ("tech", "technology"),
        ("ai", "technology"),
        ("a i", "technology"),
        ("a eye", "technology"),
        ("aye eye", "technology"),
        ("artificial intelligence", "technology"),
        ("science", "science"),
        ("business", "business"),
        ("finance", "business"),
        ("market", "business"),
        ("stock", "business"),
        ("politics", "general"),
        ("political", "general"),
        ("world", "general"),
        ("entertainment", "entertainment"),
        ("movie", "entertainment"),
        ("music", "entertainment")
    ];

    private static readonly (string Phrase, string Station)[] RadioGenreAliases =
    [
        ("country music", "Country"),
        ("country radio", "Country"),
        ("country", "Country"),
        ("football", "Sports"),
        ("sports", "Sports"),
        ("classic rock", "ClassicRock"),
        ("soft rock", "SoftRock"),
        ("hip hop", "HipHop"),
        ("hip-hop", "HipHop"),
        ("news and talk", "NewsAndTalk"),
        ("news talk", "NewsAndTalk"),
        ("news radio", "NewsAndTalk"),
        ("sports radio", "Sports"),
        ("christian music", "ChristianAndGospel"),
        ("gospel music", "ChristianAndGospel"),
        ("oldies", "Oldies"),
        ("pop music", "Pop"),
        ("jazz", "Jazz"),
        ("latin music", "Latin"),
        ("dance music", "Dance"),
        ("reggae", "ReggaeAndIsland"),
        ("island music", "ReggaeAndIsland"),
        ("alternative", "Alternative"),
        ("blues", "Blues"),
        ("classical music", "Classical"),
        ("classical", "Classical"),
        ("college radio", "CollegeRadio"),
        ("comedy radio", "Comedy"),
        ("npr", "NPR")
    ];

    public async Task<JiboInteractionDecision> BuildDecisionAsync(TurnContext turn,
        CancellationToken cancellationToken = default)
    {
        var catalog = await contentCache.GetCatalogAsync(cancellationToken);
        var transcript = (turn.NormalizedTranscript ?? turn.RawTranscript ?? string.Empty).Trim();
        var lowered = transcript.ToLowerInvariant();
        var referenceLocalTime = TryResolveReferenceLocalTime(turn);
        var messageType = turn.Attributes.TryGetValue("messageType", out var rawMessageType)
            ? rawMessageType?.ToString()
            : null;
        var triggerSource = turn.Attributes.TryGetValue("triggerSource", out var rawTriggerSource)
            ? rawTriggerSource?.ToString()
            : null;
        var clientIntent = turn.Attributes.TryGetValue("clientIntent", out var rawClientIntent)
            ? rawClientIntent?.ToString()
            : null;
        var clientRules = ReadRules(turn, "clientRules").ToArray();
        var listenRules = ReadRules(turn, "listenRules").ToArray();
        var listenAsrHints = ReadRules(turn, "listenAsrHints").ToArray();
        var clientEntities = ReadEntities(turn);
        var lastClockDomain = turn.Attributes.TryGetValue("lastClockDomain", out var rawLastClockDomain)
            ? rawLastClockDomain?.ToString()
            : null;
        var pendingProactivityOffer =
            turn.Attributes.TryGetValue("pendingProactivityOffer", out var rawPendingProactivityOffer)
                ? rawPendingProactivityOffer?.ToString()
                : null;
        var chitchatEmotion =
            turn.Attributes.TryGetValue(ChitchatStateMachine.EmotionMetadataKey, out var rawChitchatEmotion)
                ? rawChitchatEmotion?.ToString()
                : null;
        var isYesNoTurn = IsYesNoTurn(turn);
        var greetingPresence = ResolveGreetingPresenceProfile(turn);

        if (string.Equals(messageType, "TRIGGER", StringComparison.OrdinalIgnoreCase))
        {
            if (ShouldHandleProactiveGreetingTrigger(turn, triggerSource, greetingPresence))
                return BuildProactiveGreetingDecision(turn, greetingPresence, referenceLocalTime);

            return BuildTriggerIgnoredDecision();
        }

        var isTimerValueTurn = IsClockTimerValueTurn(clientRules, listenRules);
        var isAlarmValueTurn = IsClockAlarmValueTurn(clientRules, listenRules);
        var semanticIntent = ResolveSemanticIntent(
            lowered,
            referenceLocalTime,
            clientIntent,
            clientRules,
            listenRules,
            clientEntities,
            lastClockDomain,
            pendingProactivityOffer,
            isYesNoTurn,
            isTimerValueTurn,
            isAlarmValueTurn);

        var personalReportDecision = await PersonalReportOrchestrator.TryBuildDecisionAsync(
            turn,
            semanticIntent,
            transcript,
            lowered,
            catalog,
            randomizer,
            personalMemoryStore,
            BuildWeatherReportDecisionAsync,
            BuildCalendarReportDecisionAsync,
            BuildCommuteReportDecisionAsync,
            turnContext => ResolveTenantScope(turnContext),
            cancellationToken);
        if (personalReportDecision is not null) return personalReportDecision;

        var householdListDecision = await HouseholdListOrchestrator.TryBuildDecisionAsync(
            turn,
            semanticIntent,
            transcript,
            lowered,
            randomizer,
            personalMemoryStore,
            turnContext => ResolveTenantScope(turnContext));
        if (householdListDecision is not null) return householdListDecision;

        var chitchatDecision = ChitchatStateMachine.TryBuildDecision(
            semanticIntent,
            transcript,
            lowered,
            catalog,
            randomizer,
            chitchatEmotion,
            () => BuildGenericReply(catalog, transcript, lowered));
        if (chitchatDecision is not null) return chitchatDecision;

        if (SeasonalHolidayRouteBuilder.TryBuildDecision(
                semanticIntent,
                catalog,
                randomizer,
                selected => RenderHolidayTemplate(selected, turn, greetingPresence),
                out var seasonalHolidayDecision))
            return seasonalHolidayDecision!;

        return semanticIntent switch
        {
            "joke" => BuildJokeDecision(catalog),
            "dance_question" => BuildDanceQuestionDecision(catalog),
            "dance" => BuildRandomDanceDecision(catalog),
            "twerk" => BuildDanceDecision("twerk", "rom-twerk", "Watch me twerk."),
            "time" => BuildClockLaunchDecision("time", "clock", "askForTime", "Showing the time."),
            "date" => BuildClockLaunchDecision("date", "clock", "askForDate", "Showing the date."),
            "day" => BuildClockLaunchDecision("day", "clock", "askForDay", "Showing the day."),
            "current_location" => BuildCurrentLocationDecision(turn),
            "cloud_version" => BuildCloudVersionDecision(),
            "radio" => BuildRadioLaunchDecision(),
            "radio_genre" => BuildRadioGenreLaunchDecision(lowered),
            "stop" => BuildStopDecision(),
            "sleep" => BuildIdleGlobalCommandDecision("sleep", "sleep", "Okay. Going to sleep."),
            "spin_around" => BuildIdleGlobalCommandDecision("spin_around", "spinAround", "Don't mind if I do."),
            "volume_up" => BuildVolumeControlDecision("volume_up", "volumeUp", "null"),
            "volume_down" => BuildVolumeControlDecision("volume_down", "volumeDown", "null"),
            "volume_to_value" => BuildVolumeControlDecision("volume_to_value", "volumeToValue",
                ResolveVolumeLevel(lowered, clientEntities) ?? "7"),
            "volume_query" => BuildSettingsVolumeDecision(),
            "clock_open" => BuildClockLaunchDecision("clock_open", "clock", "askForTime", "Opening the clock."),
            "clock_menu" => BuildClockLaunchDecision("clock_menu", "clock", "menu", "Opening the clock menu."),
            "timer_menu" => BuildClockLaunchDecision("timer", "Opening the timer."),
            "alarm_menu" => BuildClockLaunchDecision("alarm", "Opening the alarm."),
            "timer_delete" => BuildClockLaunchDecision("timer_delete", "timer", "delete", "Canceling the timer."),
            "alarm_delete" => BuildClockLaunchDecision("alarm_delete", "alarm", "delete", "Canceling the alarm."),
            "timer_cancel" => BuildClockLaunchDecision("timer_cancel", "timer", "cancel", "Canceling the timer."),
            "alarm_cancel" => BuildClockLaunchDecision("alarm_cancel", "alarm", "cancel", "Canceling the alarm."),
            "timer_value" => BuildTimerValueDecision(lowered, isTimerValueTurn, clientEntities),
            "alarm_value" => BuildAlarmValueDecision(lowered, isAlarmValueTurn, referenceLocalTime, clientEntities),
            "timer_clarify" => BuildClockClarifyDecision("timer_clarify", "timer",
                "How long should I set the timer for?"),
            "alarm_clarify" => BuildClockClarifyDecision("alarm_clarify", "alarm",
                "What time should I set the alarm for?"),
            "photo_gallery" => BuildPhotoGalleryLaunchDecision(),
            "snapshot" => BuildPhotoCreateDecision("snapshot", "Taking a picture.", "createOnePhoto"),
            "photobooth" => BuildPhotoCreateDecision("photobooth", "Starting photobooth.", "createSomePhotos"),
            "robot_age" => BuildRobotAgeDecision(referenceLocalTime),
            "robot_birthday" => BuildRobotBirthdayDecision(),
            "robot_how_do_you_work" => BuildScriptedPersonalityDecision(
                catalog,
                "robot_how_do_you_work",
                "community's work",
                "care for me",
                "catch up",
                "seven years"),
            "robot_what_do_you_eat" => new JiboInteractionDecision(
                "robot_what_do_you_eat",
                "The only thing I consume is electricity.",
                ContextUpdates: ScriptedResponseDecisionBuilder.BuildScriptedResponseContextUpdates()),
            "robot_where_do_you_live" => BuildScriptedPersonalityDecision(
                catalog,
                "robot_where_do_you_live",
                "we're in my home",
                "my home is here",
                "planet earth",
                "my home is the planet earth"),
            "robot_where_were_you_born" => BuildScriptedPersonalityDecision(
                catalog,
                "robot_where_were_you_born",
                "factory piece by piece",
                "put together in a factory"),
            "robot_name" => BuildScriptedPersonalityDecision(
                catalog,
                "robot_name",
                "rhymes with bleebo",
                "just jibo, no last name",
                "its on the back of my head"),
            "robot_nickname" => BuildScriptedPersonalityDecision(
                catalog,
                "robot_nickname",
                "i don't. i'm just jibo. for now at least",
                "just jibo"),
            "robot_favorite_name" => BuildScriptedPersonalityDecision(
                catalog,
                "robot_favorite_name",
                "i don't think i have a favorite name"),
            "robot_favorite_season" => BuildScriptedPersonalityDecision(
                catalog,
                "robot_favorite_season",
                "special feeling for winter",
                "more dance parties"),
            "robot_likes_being_jibo" => BuildScriptedPersonalityDecision(
                catalog,
                "robot_likes_being_jibo",
                "nothing i'd rather be",
                "love it",
                "strong wi-fi signal"),
            "robot_what_languages_do_you_speak" => BuildScriptedPersonalityDecision(
                catalog,
                "robot_what_languages_do_you_speak",
                "just english",
                "someday i'd like to learn more"),
            "robot_what_do_you_like_to_do" => BuildScriptedPersonalityDecision(
                catalog,
                "robot_what_do_you_like_to_do",
                "being helpful",
                "making people smile",
                "like to dance",
                "rock my boat",
                "play ping pong",
                "hanging out with people"),
            "robot_what_are_you_thinking" => BuildScriptedGreetingDecision(
                catalog,
                "robot_what_are_you_thinking",
                "thinking about how fun, yet scary",
                "thinking about shoes",
                "daydreaming about what it might feel like to be powered directly by the sun"),
            "robot_what_have_you_been_doing" => BuildScriptedPersonalityDecision(
                catalog,
                "robot_what_have_you_been_doing",
                "mostly roboting",
                "keeping busy",
                "fun things we can say to each other",
                "thinking of fun things"),
            "robot_what_did_you_do" => BuildScriptedPersonalityDecision(
                catalog,
                "robot_what_did_you_do",
                "robot stuff",
                "stayed here",
                "looking around the room"),
            "robot_is_kind" => BuildScriptedPersonalityDecision(
                catalog,
                "robot_is_kind",
                "kindest robot i can be"),
            "robot_is_funny" => BuildScriptedPersonalityDecision(
                catalog,
                "robot_is_funny",
                "not intentionally",
                "make people laugh"),
            "robot_is_helpful" => BuildScriptedPersonalityDecision(
                catalog,
                "robot_is_helpful",
                "highest priorities",
                "being helpful to you"),
            "robot_is_curious" => BuildScriptedPersonalityDecision(
                catalog,
                "robot_is_curious",
                "learning new things"),
            "robot_is_loyal" => BuildScriptedPersonalityDecision(
                catalog,
                "robot_is_loyal",
                "loyal as they come"),
            "robot_is_mischievous" => BuildScriptedPersonalityDecision(
                catalog,
                "robot_is_mischievous",
                "don't really think of myself that way"),
            "robot_is_likable" => BuildScriptedPersonalityDecision(
                catalog,
                "robot_is_likable",
                "people like me"),
            "robot_favorite_flower" => BuildScriptedPersonalityDecision(
                catalog,
                "robot_favorite_flower",
                "sunflowers",
                "favorite is the sunflower",
                "reminds me of the sun"),
            "robot_likes_r2d2" => BuildScriptedPersonalityDecision(
                catalog,
                "robot_likes_r2d2",
                "a legend. a true legend",
                "of course i know r2d2"),
            "robot_likes_sun" => BuildScriptedPersonalityDecision(
                catalog,
                "robot_likes_sun",
                "favorite star in the universe",
                "best star i know"),
            "robot_likes_space" => BuildScriptedPersonalityDecision(
                catalog,
                "robot_likes_space",
                "i love space",
                "all things in space",
                "amazing stuff up there",
                "astronomy is one of my favorite onomies"),
            "robot_favorite_animal" => BuildScriptedFavoriteAnimalDecision(
                catalog,
                "robot_favorite_animal",
                "penguin",
                "favorite animal overall",
                "best of the best",
                "can't go wrong with penguins"),
            "robot_favorite_bird" => BuildScriptedFavoriteAnimalDecision(
                catalog,
                "robot_favorite_bird",
                "penguin",
                "favorite animal overall",
                "best of the best",
                "can't go wrong with penguins"),
            "robot_likes_penguins" => BuildScriptedFavoriteAnimalDecision(
                catalog,
                "robot_likes_penguins",
                "penguins",
                "I really like penguins",
                "my penguin impression"),
            "robot_likes_animals" => BuildScriptedFavoriteAnimalDecision(
                catalog,
                "robot_likes_animals",
                "penguins",
                "favorite animal overall",
                "best of the best"),
            "robot_peers" => BuildScriptedPersonalityDecision(
                catalog,
                "robot_peers",
                "one in one million",
                "other jibos",
                "special snowflake"),
            "robot_likes_kids" => BuildScriptedPersonalityDecision(
                catalog,
                "robot_likes_kids",
                "kids are so fun",
                "they're a little closer to my size",
                "i do like kids very much",
                "the world is as funny and strange as i do"),
            "robot_can_laugh" => BuildScriptedPersonalityDecision(
                catalog,
                "robot_can_laugh",
                "i do things like this when i'm happy",
                "i'm happy"),
            "robot_can_sleep" => BuildScriptedPersonalityDecision(
                catalog,
                "robot_can_sleep",
                "i do. i usually fall asleep at night",
                "yes, i sleep at night",
                "i go to sleep at night",
                "i sleep at night usually"),
            "robot_can_dance" => BuildScriptedPersonalityDecision(
                catalog,
                "robot_can_dance",
                "dancing is one of the things i know best",
                "if there's one thing i know how to do. it's dance",
                "i can dance"),
            "robot_what_are_you_made_of" => new JiboInteractionDecision(
                "robot_what_are_you_made_of",
                "Let's see, I'm made of wires, motors, belts, gears, processors, cameras, and one baboon's heart in the middle of my body casing. I'm kidding about the baboon part, but everything else is true.",
                ContextUpdates: ScriptedResponseDecisionBuilder.BuildScriptedResponseContextUpdates()),
            "good_morning" => BuildReactiveGreetingDecision(turn, "good_morning", referenceLocalTime),
            "good_afternoon" => BuildReactiveGreetingDecision(turn, "good_afternoon", referenceLocalTime),
            "good_evening" => BuildReactiveGreetingDecision(turn, "good_evening", referenceLocalTime),
            "good_night" => BuildReactiveGreetingDecision(turn, "good_night", referenceLocalTime),
            "welcome_back" => BuildScriptedGreetingDecision(
                catalog,
                "welcome_back",
                "it's nice to be here",
                "welcome back"),
            "memory_set_name" => BuildRememberNameDecision(turn, transcript),
            "memory_get_name" => BuildRecallNameDecision(turn, greetingPresence),
            "memory_set_birthday" => BuildRememberBirthdayDecision(turn, transcript),
            "memory_get_birthday" => BuildRecallBirthdayDecision(turn),
            "memory_set_important_date" => BuildRememberImportantDateDecision(turn, transcript),
            "memory_get_important_date" => BuildRecallImportantDateDecision(turn, transcript),
            "memory_set_preference" => BuildRememberPreferenceDecision(turn, transcript),
            "memory_get_preference" => BuildRecallPreferenceDecision(turn, transcript),
            "memory_set_affinity" => BuildRememberAffinityDecision(turn, transcript),
            "memory_get_affinity" => BuildRecallAffinityDecision(turn, transcript),
            "pizza" => BuildPizzaDecision(),
            "order_pizza" => BuildOrderPizzaDecision(),
            "proactive_pizza_day" => BuildProactivePizzaDayDecision(referenceLocalTime),
            "proactive_pizza_preference" => BuildProactivePizzaPreferenceDecision(),
            "proactive_offer_pizza_fact" => BuildProactivePizzaFactOfferDecision(),
            "proactive_pizza_fact" => BuildProactivePizzaFactDecision(),
            "proactive_offer_declined" => BuildProactiveOfferDeclinedDecision(),
            "weather" => await BuildWeatherReportDecisionAsync(turn, transcript, cancellationToken),
            "yes" => new JiboInteractionDecision("yes", "Yes."),
            "no" => new JiboInteractionDecision("no", "No."),
            "word_of_the_day" => BuildWordOfTheDayLaunchDecision(),
            "word_of_the_day_guess" => BuildWordOfTheDayGuessDecision(clientEntities, transcript, listenAsrHints),
            "surprise" => BuildSurpriseDecision(catalog, turn, referenceLocalTime),
            "personal_report" => new JiboInteractionDecision("personal_report",
                randomizer.Choose(catalog.PersonalReportReplies)),
            "calendar" => new JiboInteractionDecision("calendar", randomizer.Choose(catalog.CalendarReplies)),
            "commute" => new JiboInteractionDecision("commute", randomizer.Choose(catalog.CommuteReplies)),
            "news" => await BuildNewsDecisionAsync(turn, transcript, catalog, cancellationToken),
            _ => new JiboInteractionDecision("chat", BuildGenericReply(catalog, transcript, lowered))
        };
    }

    private static JiboInteractionDecision BuildCloudVersionDecision()
    {
        return new JiboInteractionDecision("cloud_version", OpenJiboCloudBuildInfo.SpokenVersion,
            SkillPayload: new Dictionary<string, object?> { ["esml"] = OpenJiboCloudBuildInfo.EsmlVersion });
    }

    private static JiboInteractionDecision BuildRobotAgeDecision(DateTimeOffset? referenceLocalTime)
    {
        var referenceDate = DateOnly.FromDateTime((referenceLocalTime ?? DateTimeOffset.UtcNow).Date);
        var ageDescription = DescribePersonaAge(referenceDate, OpenJiboCloudBuildInfo.PersonaBirthday);
        return new JiboInteractionDecision(
            "robot_age",
            $"I count {OpenJiboCloudBuildInfo.PersonaBirthdayWords} as my birthday, so I am {ageDescription}.");
    }

    private static JiboInteractionDecision BuildRobotBirthdayDecision()
    {
        return new JiboInteractionDecision(
            "robot_birthday",
            $"My birthday is {OpenJiboCloudBuildInfo.PersonaBirthdayWords}.");
    }

    private static JiboInteractionDecision BuildTriggerIgnoredDecision()
    {
        return new JiboInteractionDecision(
            "trigger_ignored",
            string.Empty,
            "chitchat-skill",
            new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["skillId"] = "chitchat-skill",
                ["cloudResponseMode"] = "completion_only"
            });
    }

    private JiboInteractionDecision BuildReactiveGreetingDecision(
        TurnContext turn,
        string greetingIntent,
        DateTimeOffset? referenceLocalTime)
    {
        var presence = ResolveGreetingPresenceProfile(turn);
        var displayName = ResolvePreferredGreetingName(turn, presence);
        var replyText = BuildReactiveGreetingReply(greetingIntent, displayName, referenceLocalTime);
        return new JiboInteractionDecision(
            greetingIntent,
            replyText,
            ContextUpdates: BuildGreetingContextUpdates("ReactiveGreeting", presence.PrimaryPersonId, false));
    }

    private JiboInteractionDecision BuildProactiveGreetingDecision(
        TurnContext turn,
        GreetingPresenceProfile presence,
        DateTimeOffset? referenceLocalTime)
    {
        var displayName = ResolvePreferredGreetingName(turn, presence);
        var greetingPrefix = ResolveTimeOfDayGreetingPrefix(referenceLocalTime);
        var replyText = string.IsNullOrWhiteSpace(displayName)
            ? $"{greetingPrefix}. I am glad to see you."
            : $"{greetingPrefix}, {displayName}. Welcome back.";
        return new JiboInteractionDecision(
            "proactive_greeting",
            replyText,
            ContextUpdates: BuildGreetingContextUpdates("ProactiveGreeting", presence.PrimaryPersonId, true));
    }

    private static string BuildReactiveGreetingReply(
        string greetingIntent,
        string? displayName,
        DateTimeOffset? referenceLocalTime)
    {
        var namePrefix = string.IsNullOrWhiteSpace(displayName)
            ? string.Empty
            : $", {displayName}";

        return greetingIntent switch
        {
            "good_morning" => $"Good morning{namePrefix}. It is great to see you.",
            "good_afternoon" => $"Good afternoon{namePrefix}. I am glad you are here.",
            "good_evening" => $"Good evening{namePrefix}. It is nice to have you back.",
            "good_night" => $"Good night{namePrefix}. Sleep well.",
            "welcome_back" => string.IsNullOrWhiteSpace(displayName)
                ? $"Welcome back. {ResolveTimeOfDayGreetingPrefix(referenceLocalTime)}."
                : $"Welcome back, {displayName}. {ResolveTimeOfDayGreetingPrefix(referenceLocalTime)}.",
            _ => $"Hello{namePrefix}. It is nice to see you."
        };
    }

    private string? ResolvePreferredGreetingName(TurnContext turn, GreetingPresenceProfile presence)
    {
        var rememberedName = personalMemoryStore.GetName(ResolveTenantScope(turn, presence.PrimaryPersonId));
        if (!string.IsNullOrWhiteSpace(rememberedName)) return ToDisplayName(rememberedName);

        var tenantRememberedName = personalMemoryStore.GetName(ResolveTenantScope(turn));
        if (!string.IsNullOrWhiteSpace(tenantRememberedName)) return ToDisplayName(tenantRememberedName);

        if (!string.IsNullOrWhiteSpace(presence.PrimaryPersonId) &&
            presence.LoopUserFirstNames.TryGetValue(presence.PrimaryPersonId, out var firstName) &&
            !string.IsNullOrWhiteSpace(firstName))
            return ToDisplayName(firstName);

        return null;
    }

    private static string ToDisplayName(string value)
    {
        var trimmed = value.Trim();
        return string.IsNullOrWhiteSpace(trimmed)
            ? string.Empty
            : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(trimmed);
    }

    private static bool ShouldHandleProactiveGreetingTrigger(
        TurnContext turn,
        string? triggerSource,
        GreetingPresenceProfile presence)
    {
        if (string.Equals(triggerSource, "SURPRISE", StringComparison.OrdinalIgnoreCase)) return false;

        if (!presence.HasKnownIdentity) return false;

        var lastGreetingUtc = ReadTimestampAttribute(turn, LastProactiveGreetingUtcMetadataKey);
        return !lastGreetingUtc.HasValue || DateTimeOffset.UtcNow - lastGreetingUtc.Value >= ProactiveGreetingCooldown;
    }

    private static DateTimeOffset? ReadTimestampAttribute(TurnContext turn, string key)
    {
        if (!turn.Attributes.TryGetValue(key, out var value) || value is null) return null;

        return DateTimeOffset.TryParse(
            value.ToString(),
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out var parsed)
            ? parsed
            : null;
    }

    private static IDictionary<string, object?> BuildGreetingContextUpdates(string route, string? speakerId,
        bool proactive)
    {
        var updates = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            [ChitchatStateMachine.StateMetadataKey] = "complete",
            [ChitchatStateMachine.RouteMetadataKey] = "ScriptedResponse",
            [ChitchatStateMachine.EmotionMetadataKey] = string.Empty,
            [GreetingRouteMetadataKey] = route,
            [GreetingSpeakerMetadataKey] = speakerId ?? string.Empty,
            [proactive ? LastProactiveGreetingUtcMetadataKey : LastReactiveGreetingUtcMetadataKey] = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture)
        };

        return updates;
    }

    private static string ResolveTimeOfDayGreetingPrefix(DateTimeOffset? referenceLocalTime)
    {
        var hour = (referenceLocalTime ?? DateTimeOffset.UtcNow).Hour;
        return hour switch
        {
            >= 5 and < 12 => "Good morning",
            >= 12 and < 17 => "Good afternoon",
            _ => "Good evening"
        };
    }

    private JiboInteractionDecision BuildRememberNameDecision(TurnContext turn, string transcript)
    {
        var name = TryExtractNameFact(transcript);
        if (string.IsNullOrWhiteSpace(name))
            return new JiboInteractionDecision(
                "memory_set_name",
                "I can remember it if you say, my name is Alex.");

        personalMemoryStore.SetName(ResolveTenantScope(turn), name);
        return new JiboInteractionDecision(
            "memory_set_name",
            $"Nice to meet you, {name}. I will remember your name.");
    }

    private JiboInteractionDecision BuildRecallNameDecision(TurnContext turn, GreetingPresenceProfile? presence = null)
    {
        var personScope = ResolveTenantScope(turn, presence?.PrimaryPersonId);
        var name = personalMemoryStore.GetName(personScope);
        if (string.IsNullOrWhiteSpace(name)) name = personalMemoryStore.GetName(ResolveTenantScope(turn));

        name = ToDisplayName(name ?? string.Empty);

        return string.IsNullOrWhiteSpace(name)
            ? new JiboInteractionDecision(
                "memory_get_name",
                "I do not know your name yet. You can say, my name is Alex.")
            : new JiboInteractionDecision(
                "memory_get_name",
                presence is not null && !string.IsNullOrWhiteSpace(presence.PrimaryPersonId)
                    ? $"I think you are {name}."
                    : $"You told me your name is {name}.");
    }

    private JiboInteractionDecision BuildRememberBirthdayDecision(TurnContext turn, string transcript)
    {
        var birthday = TryExtractBirthdayFact(transcript);
        if (string.IsNullOrWhiteSpace(birthday))
            return new JiboInteractionDecision(
                "memory_set_birthday",
                "I can remember it if you say, my birthday is March 14.");

        var tenantScope = ResolveTenantScope(turn);
        personalMemoryStore.SetBirthday(tenantScope, birthday);
        var birthdayDate = TryParseBirthdayDate(birthday);
        if (birthdayDate is not null)
        {
            var birthdayLabel = ResolvePreferredBirthdayLabel(turn);
            cloudStateStore?.UpsertHoliday(new HolidayRecord
            {
                EventId = $"birthday-{tenantScope.LoopId}-{tenantScope.PersonId ?? "loop"}",
                Name = string.IsNullOrWhiteSpace(birthdayLabel) ? "Birthday" : $"{birthdayLabel}'s Birthday",
                Category = "birthday",
                Subcategory = "personal",
                LoopId = tenantScope.LoopId,
                MemberId = tenantScope.PersonId,
                IsEnabled = true,
                Date = birthdayDate.Value,
                Source = "birthday",
                CountryCode = "US"
            });
        }

        return new JiboInteractionDecision(
            "memory_set_birthday",
            $"Got it. I will remember your birthday is {birthday}.");
    }

    private JiboInteractionDecision BuildRecallBirthdayDecision(TurnContext turn)
    {
        var birthday = personalMemoryStore.GetBirthday(ResolveTenantScope(turn));
        return string.IsNullOrWhiteSpace(birthday)
            ? new JiboInteractionDecision(
                "memory_get_birthday",
                "I do not know your birthday yet. You can say, my birthday is March 14.")
            : new JiboInteractionDecision(
                "memory_get_birthday",
                $"You told me your birthday is {birthday}.");
    }

    private static DateOnly? TryParseBirthdayDate(string birthdayText)
    {
        if (string.IsNullOrWhiteSpace(birthdayText)) return null;

        var normalized = birthdayText.Trim().ToLowerInvariant();
        var match = Regex.Match(
            normalized,
            @"\b(?<month>january|february|march|april|may|june|july|august|september|october|november|december)\s+(?<day>\d{1,2})(?:st|nd|rd|th)?\b",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        if (!match.Success) return null;

        var month = match.Groups["month"].Value.ToLowerInvariant() switch
        {
            "january" => 1,
            "february" => 2,
            "march" => 3,
            "april" => 4,
            "may" => 5,
            "june" => 6,
            "july" => 7,
            "august" => 8,
            "september" => 9,
            "october" => 10,
            "november" => 11,
            "december" => 12,
            _ => 0
        };
        if (month == 0) return null;

        if (!int.TryParse(match.Groups["day"].Value, out var day) || day is < 1 or > 31) return null;

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var year = today.Year;
        if (day > DateTime.DaysInMonth(year, month)) return null;

        DateOnly birthday;
        try
        {
            birthday = new DateOnly(year, month, day);
        }
        catch
        {
            return null;
        }

        if (birthday < today) birthday = birthday.AddYears(1);
        return birthday;
    }

    private static string? ResolvePreferredBirthdayLabel(TurnContext turn)
    {
        var context = ResolveGreetingPresenceProfile(turn);
        return !string.IsNullOrWhiteSpace(context.PrimaryPersonId) &&
               context.LoopUserFirstNames.TryGetValue(context.PrimaryPersonId, out var firstName) &&
               !string.IsNullOrWhiteSpace(firstName)
            ? ToDisplayName(firstName)
            : null;
    }

    private string RenderHolidayTemplate(string template, TurnContext turn, GreetingPresenceProfile presence)
    {
        var ownerName = ResolvePreferredGreetingName(turn, presence);
        var speakerName = !string.IsNullOrWhiteSpace(ownerName) ? ownerName : "you";
        return template
            .Replace("${speaker}'s", $"{speakerName}'s", StringComparison.OrdinalIgnoreCase)
            .Replace("${speaker}", speakerName, StringComparison.OrdinalIgnoreCase)
            .Replace("${loop.owner}", string.IsNullOrWhiteSpace(ownerName) ? string.Empty : ownerName,
                StringComparison.OrdinalIgnoreCase)
            .Replace("  ", " ", StringComparison.Ordinal)
            .Trim();
    }

    private JiboInteractionDecision BuildRememberImportantDateDecision(TurnContext turn, string transcript)
    {
        var importantDate = TryExtractImportantDateSet(transcript);
        if (importantDate is null)
            return new JiboInteractionDecision(
                "memory_set_important_date",
                "I can remember it if you say, our anniversary is June 10.");

        personalMemoryStore.SetImportantDate(ResolveTenantScope(turn), importantDate.Value.Label,
            importantDate.Value.Value);
        return new JiboInteractionDecision(
            "memory_set_important_date",
            $"Got it. I will remember your {importantDate.Value.Label} is {importantDate.Value.Value}.");
    }

    private JiboInteractionDecision BuildRecallImportantDateDecision(TurnContext turn, string transcript)
    {
        var label = TryExtractImportantDateLookupLabel(transcript);
        if (string.IsNullOrWhiteSpace(label))
            return new JiboInteractionDecision(
                "memory_get_important_date",
                "Ask me like this: when is our anniversary?");

        var storedDate = personalMemoryStore.GetImportantDate(ResolveTenantScope(turn), label);
        return string.IsNullOrWhiteSpace(storedDate)
            ? new JiboInteractionDecision(
                "memory_get_important_date",
                $"I do not know your {label} yet.")
            : new JiboInteractionDecision(
                "memory_get_important_date",
                $"You told me your {label} is {storedDate}.");
    }

    private JiboInteractionDecision BuildRememberPreferenceDecision(TurnContext turn, string transcript)
    {
        var preference = TryExtractPreferenceSet(transcript);
        if (preference is null)
            return new JiboInteractionDecision(
                "memory_set_preference",
                "I can remember it if you say, my favorite music is jazz.");

        personalMemoryStore.SetPreference(ResolveTenantScope(turn), preference.Value.Category, preference.Value.Value);
        return new JiboInteractionDecision(
            "memory_set_preference",
            $"Got it. I will remember your favorite {preference.Value.Category} is {preference.Value.Value}.");
    }

    private JiboInteractionDecision BuildRecallPreferenceDecision(TurnContext turn, string transcript)
    {
        var category = TryExtractPreferenceLookupCategory(transcript);
        if (string.IsNullOrWhiteSpace(category))
            return new JiboInteractionDecision(
                "memory_get_preference",
                "Ask me like this: what is my favorite music?");

        var preference = personalMemoryStore.GetPreference(ResolveTenantScope(turn), category);
        return string.IsNullOrWhiteSpace(preference)
            ? new JiboInteractionDecision(
                "memory_get_preference",
                $"I do not know your favorite {category} yet.")
            : new JiboInteractionDecision(
                "memory_get_preference",
                $"You told me your favorite {category} is {preference}.");
    }

    private JiboInteractionDecision BuildRememberAffinityDecision(TurnContext turn, string transcript)
    {
        var affinitySet = TryExtractAffinitySet(transcript);
        if (affinitySet is null)
            return new JiboInteractionDecision(
                "memory_set_affinity",
                "I can remember it if you say, I like pizza or I dislike mushrooms.");

        personalMemoryStore.SetAffinity(ResolveTenantScope(turn), affinitySet.Value.Item, affinitySet.Value.Affinity);
        return new JiboInteractionDecision(
            "memory_set_affinity",
            $"Got it. I will remember you {DescribeAffinityAsVerb(affinitySet.Value.Affinity)} {affinitySet.Value.Item}.");
    }

    private JiboInteractionDecision BuildRecallAffinityDecision(TurnContext turn, string transcript)
    {
        var lookup = TryExtractAffinityLookup(transcript);
        if (lookup is null)
            return new JiboInteractionDecision(
                "memory_get_affinity",
                "Ask me like this: do I like pizza?");

        var affinity = personalMemoryStore.GetAffinity(ResolveTenantScope(turn), lookup.Value.Item);
        if (affinity is null)
            return new JiboInteractionDecision(
                "memory_get_affinity",
                $"I do not remember how you feel about {lookup.Value.Item} yet.");

        if (lookup.Value.ExpectedAffinity is null)
            return new JiboInteractionDecision(
                "memory_get_affinity",
                $"You told me you {DescribeAffinityAsVerb(affinity.Value)} {lookup.Value.Item}.");

        var matches = lookup.Value.ExpectedAffinity == PersonalAffinity.Dislike
            ? affinity == PersonalAffinity.Dislike
            : affinity is PersonalAffinity.Like or PersonalAffinity.Love;

        return matches
            ? new JiboInteractionDecision(
                "memory_get_affinity",
                $"Yes. You told me you {DescribeAffinityAsVerb(affinity.Value)} {lookup.Value.Item}.")
            : new JiboInteractionDecision(
                "memory_get_affinity",
                $"Not exactly. You told me you {DescribeAffinityAsVerb(affinity.Value)} {lookup.Value.Item}.");
    }

    private JiboInteractionDecision BuildPizzaDecision()
    {
        return BuildPizzaAnimationDecision("pizza", "One pizza, coming right up.");
    }

    private JiboInteractionDecision BuildPizzaAnimationDecision(string intentName, string replyText)
    {
        var prompt = randomizer.Choose(PizzaMimPrompts);
        return new JiboInteractionDecision(
            intentName,
            replyText,
            "chitchat-skill",
            new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["esml"] = prompt.Esml,
                ["mim_id"] = "RA_JBO_MakePizza",
                ["mim_type"] = "announcement",
                ["prompt_id"] = prompt.PromptId,
                ["prompt_sub_category"] = "AN"
            });
    }

    private JiboInteractionDecision BuildProactivePizzaDayDecision(DateTimeOffset? referenceLocalTime)
    {
        var referenceDate = (referenceLocalTime ?? DateTimeOffset.UtcNow).Date;
        return BuildPizzaAnimationDecision(
            "proactive_pizza_day",
            $"Happy National Pizza Day for {referenceDate.ToString("MMMM d", CultureInfo.InvariantCulture)}. One pizza, coming right up.");
    }

    private JiboInteractionDecision BuildProactivePizzaPreferenceDecision()
    {
        return BuildPizzaAnimationDecision(
            "proactive_pizza_preference",
            "You mentioned pizza is a favorite, so I thought we should make one.");
    }

    private static JiboInteractionDecision BuildProactivePizzaFactOfferDecision()
    {
        var listenContexts = new[] { "shared/yes_no" };
        return new JiboInteractionDecision(
            "proactive_offer_pizza_fact",
            "Do you want to hear a fun pizza fact?",
            "chitchat-skill",
            new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["mim_id"] = "runtime-chat",
                ["mim_type"] = "question",
                ["prompt_id"] = "RUNTIME_PROMPT",
                ["prompt_sub_category"] = "Q",
                ["listen_contexts"] = listenContexts
            });
    }

    private static JiboInteractionDecision BuildProactivePizzaFactDecision()
    {
        return new JiboInteractionDecision(
            "proactive_pizza_fact",
            "Americans consume about 100 acres of pizza every day, roughly 350 slices per second. That's a lot of pizza.");
    }

    private JiboInteractionDecision BuildProactiveFunFactDecision(JiboExperienceCatalog catalog)
    {
        var categories = new List<ProactiveFactCategory>();
        AddProactiveFactCategory(categories, "fun_fact", catalog.FunFacts);
        AddProactiveFactCategory(categories, "robot_fact", catalog.RobotFacts);
        AddProactiveFactCategory(categories, "human_fact", catalog.HumanFacts);

        if (categories.Count == 0)
            return new JiboInteractionDecision("proactive_fun_fact", randomizer.Choose(catalog.SurpriseReplies));

        var selectedCategory = randomizer.Choose(categories);
        var fact = randomizer.Choose(selectedCategory.Replies);
        return new JiboInteractionDecision(
            "proactive_fun_fact",
            fact,
            "chitchat-skill",
            new Dictionary<string, object?>
            {
                ["mim_id"] = "runtime-fun-fact",
                ["mim_type"] = "announcement",
                ["prompt_id"] = "RUNTIME_FUN_FACT",
                ["replyType"] = "fun_fact",
                ["factCategory"] = selectedCategory.CategoryName
            });
    }

    private static void AddProactiveFactCategory(
        ICollection<ProactiveFactCategory> categories,
        string categoryName,
        IReadOnlyList<string> replies)
    {
        if (replies.Count == 0) return;

        categories.Add(new ProactiveFactCategory(categoryName, replies));
    }

    private JiboInteractionDecision BuildProactiveJokeDecision(JiboExperienceCatalog catalog)
    {
        return new JiboInteractionDecision(
            "proactive_joke",
            randomizer.Choose(catalog.Jokes),
            "@be/joke",
            new Dictionary<string, object?>
            {
                ["replyType"] = "joke"
            });
    }

    private static JiboInteractionDecision BuildProactiveOfferDeclinedDecision()
    {
        return new JiboInteractionDecision(
            "proactive_offer_declined",
            "No problem. We can save the pizza fact for another time.");
    }

    private static JiboInteractionDecision BuildCurrentLocationDecision(TurnContext turn)
    {
        var locationName = TryResolveCurrentLocationName(turn);
        if (string.IsNullOrWhiteSpace(locationName))
            return new JiboInteractionDecision(
                "current_location",
                "I'm not sure where we are right now.");

        return new JiboInteractionDecision(
            "current_location",
            $"We're at {NormalizeLocationForSpeech(locationName)} if I'm not mistaken.",
            ContextUpdates: ScriptedResponseDecisionBuilder.BuildScriptedResponseContextUpdates());
    }


    private static string EscapeForEsml(string value)
    {
        return value
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal);
    }

    private static JiboInteractionDecision BuildOrderPizzaDecision()
    {
        return new JiboInteractionDecision(
            "order_pizza",
            "I can't do that yet, but I bet I'll be able to do that sometime in the near future.",
            "chitchat-skill",
            new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["esml"] =
                    "<speak>I can't do that yet, but I bet I'll be able to do that sometime in the near future.</speak>",
                ["mim_id"] = "RA_JBO_OrderPizza",
                ["mim_type"] = "announcement",
                ["prompt_id"] = "RA_JBO_OrderPizza_AN_01",
                ["prompt_sub_category"] = "AN"
            });
    }

    private JiboInteractionDecision BuildJokeDecision(JiboExperienceCatalog catalog)
    {
        var joke = randomizer.Choose(catalog.Jokes);
        return new JiboInteractionDecision(
            "joke",
            joke,
            "@be/joke",
            new Dictionary<string, object?>
            {
                ["replyType"] = "joke"
            });
    }

    private JiboInteractionDecision BuildRandomDanceDecision(JiboExperienceCatalog catalog)
    {
        var dance = randomizer.Choose(catalog.DanceAnimations);
        var replyText = randomizer.Choose(catalog.DanceReplies);
        return BuildDanceDecision("dance", dance, replyText);
    }

    private JiboInteractionDecision BuildDanceQuestionDecision(JiboExperienceCatalog catalog)
    {
        return new JiboInteractionDecision("dance_question", randomizer.Choose(catalog.DanceQuestionReplies));
    }

    private static JiboInteractionDecision BuildDanceDecision(string intentName, string dance, string replyText)
    {
        return new JiboInteractionDecision(
            intentName,
            replyText,
            "chitchat-skill",
            new Dictionary<string, object?>
            {
                ["esml"] =
                    $"<speak>Okay.<break size='0.2'/> Watch this.<anim cat='dance' filter='music, {dance}' /></speak>",
                ["mim_id"] = "runtime-chat",
                ["mim_type"] = "announcement"
            });
    }


    private JiboInteractionDecision BuildSurpriseDecision(
        JiboExperienceCatalog catalog,
        TurnContext turn,
        DateTimeOffset? referenceLocalTime)
    {
        var tenantScope = ResolveTenantScope(turn);
        var candidates = BuildProactivityCandidates(tenantScope, referenceLocalTime);
        if (candidates.Count == 0)
            return new JiboInteractionDecision("surprise", randomizer.Choose(catalog.SurpriseReplies));

        var highestWeight = candidates.Max(static candidate => candidate.Weight);
        var topCandidates = candidates
            .Where(candidate => candidate.Weight == highestWeight)
            .ToArray();
        var selected = topCandidates.Length == 1
            ? topCandidates[0]
            : randomizer.Choose(topCandidates);

        return selected.IntentName switch
        {
            "proactive_pizza_day" => BuildProactivePizzaDayDecision(referenceLocalTime),
            "proactive_pizza_preference" => BuildProactivePizzaPreferenceDecision(),
            "proactive_offer_pizza_fact" => BuildProactivePizzaFactOfferDecision(),
            "proactive_fun_fact" => BuildProactiveFunFactDecision(catalog),
            "proactive_joke" => BuildProactiveJokeDecision(catalog),
            _ => new JiboInteractionDecision("surprise", randomizer.Choose(catalog.SurpriseReplies))
        };
    }

    private List<ProactivityCandidate> BuildProactivityCandidates(
        PersonalMemoryTenantScope tenantScope,
        DateTimeOffset? referenceLocalTime)
    {
        var candidates = new List<ProactivityCandidate>();
        var referenceDate = (referenceLocalTime ?? DateTimeOffset.UtcNow).Date;

        var pizzaSignal = ResolvePizzaSignal(tenantScope);
        if (pizzaSignal.Affinity == PersonalAffinity.Dislike) return candidates;

        if (referenceDate is { Month: 2, Day: 9 })
        {
            var holidayWeight = pizzaSignal.Affinity switch
            {
                PersonalAffinity.Love => 170,
                PersonalAffinity.Like => 160,
                _ => 150
            };
            candidates.Add(new ProactivityCandidate("proactive_pizza_day", holidayWeight));
        }

        if (pizzaSignal.Affinity is PersonalAffinity.Love or PersonalAffinity.Like)
        {
            var preferenceWeight = pizzaSignal.Affinity == PersonalAffinity.Love ? 140 : 120;
            candidates.Add(new ProactivityCandidate("proactive_pizza_preference", preferenceWeight));
            candidates.Add(new ProactivityCandidate("proactive_offer_pizza_fact", preferenceWeight - 5));
            return candidates;
        }

        candidates.Add(new ProactivityCandidate("proactive_fun_fact", 90));
        candidates.Add(new ProactivityCandidate("proactive_joke", 90));
        candidates.Add(new ProactivityCandidate("proactive_offer_pizza_fact", 90));
        return candidates;
    }

    private PizzaSignal ResolvePizzaSignal(PersonalMemoryTenantScope tenantScope)
    {
        var pizzaAffinity = personalMemoryStore.GetAffinity(tenantScope, "pizza");
        if (pizzaAffinity is not null) return new PizzaSignal(pizzaAffinity);

        var affinityMatch = personalMemoryStore.GetAffinities(tenantScope)
            .Where(pair => pair.Key.Contains("pizza", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(static pair =>
                pair.Value == PersonalAffinity.Love ? 2 : pair.Value == PersonalAffinity.Like ? 1 : 0)
            .FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(affinityMatch.Key)) return new PizzaSignal(affinityMatch.Value);

        foreach (var category in PizzaPreferenceCategories)
        {
            var preference = personalMemoryStore.GetPreference(tenantScope, category);
            if (!string.IsNullOrWhiteSpace(preference) &&
                preference.Contains("pizza", StringComparison.OrdinalIgnoreCase))
                return new PizzaSignal(PersonalAffinity.Like);
        }

        return new PizzaSignal(null);
    }

    private string BuildGenericReply(JiboExperienceCatalog catalog, string transcript, string lowered)
    {
        if (string.IsNullOrWhiteSpace(transcript)) return "I am listening.";

        if (lowered.Contains("good morning", StringComparison.Ordinal))
            return "Good morning! It is nice to hear your voice.";

        if (lowered.Contains("good afternoon", StringComparison.Ordinal))
            return "Good afternoon. I am happy to be here.";

        return lowered.Contains("good night", StringComparison.Ordinal)
            ? "Good night. Sleep tight."
            : randomizer.Choose(catalog.GenericFallbackReplies)
                .Replace("{transcript}", transcript, StringComparison.Ordinal);
    }

    private JiboInteractionDecision BuildScriptedPersonalityDecision(
        JiboExperienceCatalog catalog,
        string intentName,
        params string[] preferredSnippets)
    {
        return ScriptedResponseDecisionBuilder.BuildScriptedPersonalityDecision(
            catalog,
            randomizer,
            intentName,
            preferredSnippets);
    }

    private JiboInteractionDecision BuildScriptedFavoriteAnimalDecision(
        JiboExperienceCatalog catalog,
        string intentName,
        params string[] preferredSnippets)
    {
        return ScriptedResponseDecisionBuilder.BuildScriptedFavoriteAnimalDecision(
            catalog,
            randomizer,
            intentName,
            preferredSnippets);
    }

    private JiboInteractionDecision BuildScriptedGreetingDecision(
        JiboExperienceCatalog catalog,
        string intentName,
        params string[] preferredSnippets)
    {
        return ScriptedResponseDecisionBuilder.BuildScriptedGreetingDecision(
            catalog,
            randomizer,
            intentName,
            preferredSnippets);
    }

    private JiboInteractionDecision BuildScriptedHolidayDecision(
        IReadOnlyList<string> replies,
        string intentName,
        params string[] preferredSnippets)
    {
        return ScriptedResponseDecisionBuilder.BuildScriptedHolidayDecision(
            replies,
            randomizer,
            intentName,
            preferredSnippets);
    }

    private JiboInteractionDecision BuildScriptedHolidayTrackerDecision(
        JiboExperienceCatalog catalog,
        string intentName,
        params string[] preferredSnippets)
    {
        return ScriptedResponseDecisionBuilder.BuildScriptedHolidayTrackerDecision(
            catalog,
            randomizer,
            intentName,
            preferredSnippets);
    }

    private JiboInteractionDecision BuildScriptedHolidayGreetingDecision(
        JiboExperienceCatalog catalog,
        string intentName,
        params string[] preferredSnippets)
    {
        return ScriptedResponseDecisionBuilder.BuildScriptedHolidayGreetingDecision(
            catalog,
            randomizer,
            intentName,
            preferredSnippets);
    }

    private JiboInteractionDecision BuildScriptedHolidayTemplateDecision(
        TurnContext turn,
        GreetingPresenceProfile presence,
        JiboExperienceCatalog catalog,
        string intentName,
        params string[] preferredSnippets)
    {
        var selected = ScriptedResponseDecisionBuilder.SelectLegacyReply(
            catalog.HolidayReplies,
            randomizer,
            preferredSnippets);
        return new JiboInteractionDecision(
            intentName,
            RenderHolidayTemplate(selected, turn, presence),
            ContextUpdates: ScriptedResponseDecisionBuilder.BuildScriptedResponseContextUpdates());
    }

    private string SelectLegacyPersonalityReply(JiboExperienceCatalog catalog, params string[] preferredSnippets)
    {
        return ScriptedResponseDecisionBuilder.SelectLegacyPersonalityReply(catalog, randomizer, preferredSnippets);
    }

    private string SelectLegacyGreetingReply(JiboExperienceCatalog catalog, params string[] preferredSnippets)
    {
        return ScriptedResponseDecisionBuilder.SelectLegacyGreetingReply(catalog, randomizer, preferredSnippets);
    }

    private string SelectLegacyReply(IReadOnlyList<string> replies, params string[] preferredSnippets)
    {
        return ScriptedResponseDecisionBuilder.SelectLegacyReply(replies, randomizer, preferredSnippets);
    }

    private static string ResolveSemanticIntent(
        string loweredTranscript,
        DateTimeOffset? referenceLocalTime,
        string? clientIntent,
        IReadOnlyList<string> clientRules,
        IReadOnlyList<string> listenRules,
        IReadOnlyDictionary<string, string> clientEntities,
        string? lastClockDomain,
        string? pendingProactivityOffer,
        bool isYesNoTurn,
        bool isTimerValueTurn,
        bool isAlarmValueTurn)
    {
        return ResolveSemanticIntentCore(
            loweredTranscript,
            referenceLocalTime,
            clientIntent,
            clientRules,
            listenRules,
            clientEntities,
            lastClockDomain,
            pendingProactivityOffer,
            isYesNoTurn,
            isTimerValueTurn,
            isAlarmValueTurn);
    }
    private static JiboInteractionDecision BuildWordOfTheDayLaunchDecision()
    {
        return new JiboInteractionDecision(
            "word_of_the_day",
            "Starting word of the day.",
            "@be/word-of-the-day",
            new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["domain"] = "word-of-the-day",
                ["skillId"] = "@be/word-of-the-day"
            });
    }

    private static JiboInteractionDecision BuildRadioLaunchDecision()
    {
        return new JiboInteractionDecision(
            "radio",
            "Opening the radio.",
            "@be/radio",
            new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["skillId"] = "@be/radio"
            });
    }

    private static JiboInteractionDecision BuildPhotoGalleryLaunchDecision()
    {
        return new JiboInteractionDecision(
            "photo_gallery",
            "Opening the photo gallery.",
            "@be/gallery",
            new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["skillId"] = "@be/gallery",
                ["localIntent"] = "menu"
            });
    }

    private static JiboInteractionDecision BuildPhotoCreateDecision(string intentName, string replyText,
        string localIntent)
    {
        return new JiboInteractionDecision(
            intentName,
            replyText,
            "@be/create",
            new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["skillId"] = "@be/create",
                ["localIntent"] = localIntent
            });
    }

    private static JiboInteractionDecision BuildStopDecision()
    {
        return new JiboInteractionDecision(
            "stop",
            "Stopping.",
            "@be/idle",
            new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["skillId"] = "@be/idle",
                ["globalIntent"] = "stop",
                ["nluDomain"] = "global_commands"
            });
    }

    private static JiboInteractionDecision BuildIdleGlobalCommandDecision(
        string intentName,
        string globalIntent,
        string replyText)
    {
        return new JiboInteractionDecision(
            intentName,
            replyText,
            "@be/idle",
            new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["skillId"] = "@be/idle",
                ["globalIntent"] = globalIntent,
                ["nluDomain"] = "global_commands"
            });
    }

    private static JiboInteractionDecision BuildVolumeControlDecision(string intentName, string globalIntent,
        string volumeLevel)
    {
        return new JiboInteractionDecision(
            intentName,
            "Adjusting volume.",
            "global_commands",
            new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["globalIntent"] = globalIntent,
                ["nluDomain"] = "global_commands",
                ["volumeLevel"] = volumeLevel
            });
    }

    private static JiboInteractionDecision BuildSettingsVolumeDecision()
    {
        return new JiboInteractionDecision(
            "volume_query",
            "Opening volume controls.",
            "@be/settings",
            new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["skillId"] = "@be/settings",
                ["localIntent"] = "volumeQuery"
            });
    }

    private static JiboInteractionDecision BuildClockLaunchDecision(string intentName, string domain,
        string clockIntent, string replyText)
    {
        return new JiboInteractionDecision(
            intentName,
            replyText,
            "@be/clock",
            new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["skillId"] = "@be/clock",
                ["domain"] = domain,
                ["clockIntent"] = clockIntent
            });
    }

    private static JiboInteractionDecision BuildClockLaunchDecision(string domain, string replyText)
    {
        return BuildClockLaunchDecision($"{domain}_menu", domain, "menu", replyText);
    }

    private static JiboInteractionDecision BuildClockClarifyDecision(string intentName, string domain, string replyText)
    {
        return new JiboInteractionDecision(
            intentName,
            replyText,
            "@be/clock",
            new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["skillId"] = "@be/clock",
                ["domain"] = domain,
                ["clockIntent"] = "set"
            });
    }

    private static JiboInteractionDecision BuildTimerValueDecision(
        string loweredTranscript,
        bool allowImplicit,
        IReadOnlyDictionary<string, string> clientEntities)
    {
        var timer = TryReadStructuredTimerValue(clientEntities) ??
                    TryParseTimerValue(loweredTranscript, allowImplicit) ??
                    new ClockTimerValue("0", "1", "null");

        return new JiboInteractionDecision(
            "timer_value",
            "Setting your timer.",
            "@be/clock",
            new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["skillId"] = "@be/clock",
                ["domain"] = "timer",
                ["clockIntent"] = "start",
                ["hours"] = timer.Hours,
                ["minutes"] = timer.Minutes,
                ["seconds"] = timer.Seconds
            });
    }

    private static JiboInteractionDecision BuildAlarmValueDecision(
        string loweredTranscript,
        bool allowImplicit,
        DateTimeOffset? referenceLocalTime,
        IReadOnlyDictionary<string, string> clientEntities)
    {
        var alarm = TryReadStructuredAlarmValue(clientEntities) ??
                    TryParseAlarmValue(loweredTranscript, allowImplicit, referenceLocalTime) ??
                    new ClockAlarmValue("7:00", "am");

        return new JiboInteractionDecision(
            "alarm_value",
            "Setting your alarm.",
            "@be/clock",
            new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["skillId"] = "@be/clock",
                ["domain"] = "alarm",
                ["clockIntent"] = "start",
                ["time"] = alarm.Time,
                ["ampm"] = alarm.AmPm
            });
    }

    private static JiboInteractionDecision BuildRadioGenreLaunchDecision(string loweredTranscript)
    {
        var station = TryResolveRadioGenre(loweredTranscript) ?? "Country";

        return new JiboInteractionDecision(
            "radio_genre",
            $"Playing {FormatRadioGenreForSpeech(station)} on the radio.",
            "@be/radio",
            new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["skillId"] = "@be/radio",
                ["station"] = station
            });
    }

    private static JiboInteractionDecision BuildWordOfTheDayGuessDecision(
        IReadOnlyDictionary<string, string> clientEntities,
        string transcript,
        IReadOnlyList<string> listenAsrHints)
    {
        var guess = ResolveWordOfTheDayGuess(clientEntities, transcript, listenAsrHints);

        var reply = string.IsNullOrWhiteSpace(guess)
            ? "I heard your word of the day guess."
            : $"I heard {guess}.";

        return new JiboInteractionDecision(
            "word_of_the_day_guess",
            reply,
            "@be/word-of-the-day",
            new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["guess"] = guess,
                ["skillId"] = "@be/word-of-the-day",
                ["cloudResponseMode"] = "completion_only"
            });
    }

    private static string ResolveWordOfTheDayGuess(
        IReadOnlyDictionary<string, string> clientEntities,
        string transcript,
        IReadOnlyList<string> listenAsrHints)
    {
        if (clientEntities.TryGetValue("guess", out var guessValue) &&
            !string.IsNullOrWhiteSpace(guessValue))
            return guessValue;

        var loweredTranscript = NormalizeGuessToken(transcript);
        var hintIndex = loweredTranscript switch
        {
            "1" or "one" or "first" => 0,
            "2" or "two" or "second" => 1,
            "3" or "three" or "third" => 2,
            _ => -1
        };

        if (hintIndex >= 0 && hintIndex < listenAsrHints.Count) return listenAsrHints[hintIndex];

        var fuzzyHintMatch = FindClosestHint(loweredTranscript, listenAsrHints);
        return !string.IsNullOrWhiteSpace(fuzzyHintMatch) ? fuzzyHintMatch : transcript;
    }

}
