using System.Text.RegularExpressions;
using Jibo.Cloud.Application.Abstractions;
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
    IFunFactProvider? funFactProvider = null,
    IWordDefinitionProvider? wordDefinitionProvider = null,
    IKnowledgeSearchService? knowledgeSearchService = null,
    ICloudStateStore? cloudStateStore = null,
    IUserIntegrationStore? userIntegrationStore = null,
    JiboVerificationService? jiboVerificationService = null)
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
        "my favourite ",
        "my fave "
    ];

    private static readonly string[] PreferenceReverseMarkers =
    [
        " is my favorite ",
        " is my favourite ",
        " is my fave ",
        " are my favorite ",
        " are my favourite ",
        " are my fave "
    ];

    // Imported from Pegasus birthday/birth-date parser phrasing so user memory
    // stays ahead of generic date questions and robot-birthday personality routes.
    private static readonly string[] BirthdaySetMarkers =
    [
        "my birthday is ",
        "my birthday s ",
        "my birthday's ",
        "my bday is ",
        "my bday s ",
        "my bday's ",
        "my birth date is ",
        "my birth date s ",
        "my birthdate is ",
        "my birthdate s ",
        "my birthdate's ",
        "my birthday falls on ",
        "my bday falls on ",
        "my birth date falls on ",
        "my birthdate falls on "
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

    public Task<JiboInteractionDecision> BuildDecisionAsync(TurnContext turn,
        CancellationToken cancellationToken = default)
    {
        return BuildDecisionCoreAsync(turn, cancellationToken);
    }
}