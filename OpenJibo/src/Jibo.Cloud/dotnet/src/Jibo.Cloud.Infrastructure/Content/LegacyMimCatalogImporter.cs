using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Jibo.Cloud.Application.Abstractions;

namespace Jibo.Cloud.Infrastructure.Content;

public static class LegacyMimCatalogImporter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true
    };

    private static readonly Regex LegacyMarkupPattern = new(
        @"<[^>]+>",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex PlaceholderPattern = new(
        @"\$\{[^}]+\}",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex WhitespacePattern = new(
        @"\s+",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex SpaceBeforePunctuationPattern = new(
        @"\s+([,.;:!?])",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    // Splits CamelCase words, e.g. "CanMakeCoffee" → ["Can", "Make", "Coffee"]
    private static readonly Regex CamelCaseSplitPattern = new(
        @"(?<=[a-z])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    // Known file prefixes to strip when deriving trigger phrases
    private static readonly string[] KnownPrefixes =
    [
        "RI_JBO_", "OI_JBO_", "JBO_", "RA_JBO_", "RN_JBO_", "RN_",
        "KU_JBO_", "KU_", "JF_JBO_", "JF_", "SUP_JBO_", "SUP_",
        "SRS_JBO_", "SRS_", "USR_JBO_", "USR_", "PR_JBO_", "PR_",
        "CC_", "RA_", "OI_", "RI_"
    ];

    public static JiboExperienceCatalog MergeInto(
        JiboExperienceCatalog baseCatalog,
        string? rootDirectory)
    {
        if (baseCatalog is null) throw new ArgumentNullException(nameof(baseCatalog));

        if (string.IsNullOrWhiteSpace(rootDirectory) || !Directory.Exists(rootDirectory)) return baseCatalog;

        var importedCatalog = ImportCatalog(rootDirectory);
        return MergeCatalogs(baseCatalog, importedCatalog);
    }

    public static JiboExperienceCatalog ImportCatalog(string rootDirectory)
    {
        if (string.IsNullOrWhiteSpace(rootDirectory) || !Directory.Exists(rootDirectory))
            return new JiboExperienceCatalog();

        var builder = new LegacyMimCatalogBuilder();
        foreach (var filePath in Directory.EnumerateFiles(rootDirectory, "*.mim", SearchOption.AllDirectories)
                     .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase))
        {
            if (!TryLoadDefinition(filePath, out var definition)) continue;

            var bucket = ResolveBucket(filePath);
            if (bucket is null) continue;

            var fileName = Path.GetFileNameWithoutExtension(filePath);
            var isScriptedResponse = IsScriptedResponsePath(filePath);

            var texts = new List<string>();
            foreach (var prompt in definition.Prompts)
            {
                var text = NormalizePrompt(prompt.Prompt, IsTemplateBucket(bucket.Value));
                if (string.IsNullOrWhiteSpace(text)) continue;

                builder.Add(bucket.Value, prompt.Condition, text, prompt.Prompt);
                texts.Add(text);
            }

            // Build named lookup for all scripted-response files
            if (isScriptedResponse && texts.Count > 0)
                builder.AddNamed(fileName, texts);
        }

        return builder.Build();
    }

    private static bool IsScriptedResponsePath(string filePath)
    {
        var normalizedPath = filePath.Replace('\\', '/');
        return normalizedPath.Contains("/scripted-responses/", StringComparison.OrdinalIgnoreCase)
            || normalizedPath.Contains("/emotion-responses/", StringComparison.OrdinalIgnoreCase)
            || normalizedPath.Contains("/gqa-responses/", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryLoadDefinition(string filePath, out LegacyMimDefinition definition)
    {
        definition = new LegacyMimDefinition();
        try
        {
            var json = File.ReadAllText(filePath);
            var parsed = JsonSerializer.Deserialize<LegacyMimDefinition>(json, JsonOptions);
            if (parsed is null) return false;

            definition = parsed;
            return definition.Prompts.Count > 0;
        }
        catch
        {
            return false;
        }
    }

    private static LegacyMimBucket? ResolveBucket(string filePath)
    {
        var normalizedPath = filePath.Replace('\\', '/');
        var fileName = Path.GetFileNameWithoutExtension(filePath);

        if (normalizedPath.Contains("/core-responses/", StringComparison.OrdinalIgnoreCase) &&
            fileName.Contains("Error", StringComparison.OrdinalIgnoreCase))
            return LegacyMimBucket.GenericFallback;

        if (normalizedPath.Contains("/core-responses/deflector/", StringComparison.OrdinalIgnoreCase) ||
            fileName.Contains("Deflector", StringComparison.OrdinalIgnoreCase))
            return LegacyMimBucket.Personality;

        if (fileName.StartsWith("RA_JBO_TellAJoke", StringComparison.OrdinalIgnoreCase))
            return LegacyMimBucket.Jokes;

        if (fileName.StartsWith("RA_JBO_TellRobotFact", StringComparison.OrdinalIgnoreCase))
            return LegacyMimBucket.RobotFacts;

        if (fileName.StartsWith("RA_JBO_Shuffle", StringComparison.OrdinalIgnoreCase) ||
            fileName.StartsWith("RA_JBO_TellSomething", StringComparison.OrdinalIgnoreCase))
            return LegacyMimBucket.FunFactSource;

        if (normalizedPath.Contains("/emotion-responses/", StringComparison.OrdinalIgnoreCase) ||
            normalizedPath.Contains("/gqa-responses/", StringComparison.OrdinalIgnoreCase))
            return LegacyMimBucket.Emotion;

        if (fileName.StartsWith("WeatherIntroTomorrow", StringComparison.OrdinalIgnoreCase))
            return LegacyMimBucket.WeatherTomorrowIntro;

        if (fileName.StartsWith("WeatherIntro", StringComparison.OrdinalIgnoreCase))
            return LegacyMimBucket.WeatherIntro;

        if (fileName.StartsWith("WeatherTomorrowHighLow", StringComparison.OrdinalIgnoreCase))
            return LegacyMimBucket.WeatherTomorrowHighLow;

        if (fileName.StartsWith("WeatherTodayHighLow", StringComparison.OrdinalIgnoreCase))
            return LegacyMimBucket.WeatherTodayHighLow;

        if (fileName.StartsWith("WeatherServiceDown", StringComparison.OrdinalIgnoreCase))
            return LegacyMimBucket.WeatherServiceDown;

        if (fileName.StartsWith("CalendarNothingToday", StringComparison.OrdinalIgnoreCase))
            return LegacyMimBucket.CalendarNothingToday;

        if (fileName.StartsWith("CalendarNothing", StringComparison.OrdinalIgnoreCase))
            return LegacyMimBucket.CalendarNothing;

        if (fileName.StartsWith("CalendarOutro", StringComparison.OrdinalIgnoreCase))
            return LegacyMimBucket.CalendarOutro;

        if (fileName.StartsWith("CommuteNow", StringComparison.OrdinalIgnoreCase)) return LegacyMimBucket.CommuteNow;

        if (fileName.StartsWith("CommuteServiceDown", StringComparison.OrdinalIgnoreCase))
            return LegacyMimBucket.CommuteServiceDown;

        if (fileName.StartsWith("NewsIntroCategory", StringComparison.OrdinalIgnoreCase))
            return LegacyMimBucket.NewsCategoryIntro;

        if (fileName.StartsWith("NewsIntro", StringComparison.OrdinalIgnoreCase)) return LegacyMimBucket.NewsIntro;

        if (fileName.StartsWith("NewsOutro", StringComparison.OrdinalIgnoreCase)) return LegacyMimBucket.NewsOutro;

        if (fileName.StartsWith("Weather", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(fileName, "WetNowDryLater", StringComparison.OrdinalIgnoreCase))
            return LegacyMimBucket.ReportSkillTemplate;

        if (fileName.StartsWith("PersonalReportKickOff", StringComparison.OrdinalIgnoreCase))
            return LegacyMimBucket.PersonalReportKickOff;

        if (fileName.StartsWith("PersonalReportOutro", StringComparison.OrdinalIgnoreCase))
            return LegacyMimBucket.PersonalReportOutro;

        if (fileName.StartsWith("PersonalReport", StringComparison.OrdinalIgnoreCase) ||
            fileName.Contains("Calendar", StringComparison.OrdinalIgnoreCase) ||
            fileName.Contains("Commute", StringComparison.OrdinalIgnoreCase) ||
            fileName.Contains("News", StringComparison.OrdinalIgnoreCase))
            return LegacyMimBucket.ReportSkillTemplate;

        if (fileName.StartsWith("JBO_DoYouLikeBeingJibo", StringComparison.OrdinalIgnoreCase) ||
            fileName.StartsWith("JBO_WhatIsJibo", StringComparison.OrdinalIgnoreCase) ||
            fileName.StartsWith("JBO_WhoAreYou", StringComparison.OrdinalIgnoreCase) ||
            fileName.StartsWith("JBO_WhatAreYou", StringComparison.OrdinalIgnoreCase) ||
            fileName.StartsWith("JBO_HowDoYouWork", StringComparison.OrdinalIgnoreCase) ||
            fileName.StartsWith("JBO_HowMuchDoYouKnow", StringComparison.OrdinalIgnoreCase) ||
            fileName.StartsWith("JBO_HowOldAreYou", StringComparison.OrdinalIgnoreCase) ||
            fileName.StartsWith("JBO_WhenWereYouBorn", StringComparison.OrdinalIgnoreCase) ||
            fileName.StartsWith("JBO_WhatsYourName", StringComparison.OrdinalIgnoreCase) ||
            fileName.StartsWith("JBO_WhereDoYouGetInfo", StringComparison.OrdinalIgnoreCase) ||
            fileName.StartsWith("JBO_WhatDoYouLikeToDo", StringComparison.OrdinalIgnoreCase))
            return LegacyMimBucket.Personality;

        if (fileName.StartsWith("OI_JBO_Is", StringComparison.OrdinalIgnoreCase) ||
            fileName.StartsWith("OI_JBO_Seems", StringComparison.OrdinalIgnoreCase) ||
            fileName.StartsWith("RI_JBO_IsHappy", StringComparison.OrdinalIgnoreCase) ||
            fileName.StartsWith("RI_JBO_IsSad", StringComparison.OrdinalIgnoreCase) ||
            fileName.StartsWith("RI_JBO_IsAngry", StringComparison.OrdinalIgnoreCase) ||
            fileName.StartsWith("RN_WhatAreYouFeeling", StringComparison.OrdinalIgnoreCase))
            return LegacyMimBucket.Emotion;

        if (fileName.Contains("Greeting", StringComparison.OrdinalIgnoreCase) ||
            fileName.StartsWith("RN_", StringComparison.OrdinalIgnoreCase) ||
            fileName.Contains("Welcome", StringComparison.OrdinalIgnoreCase))
            return LegacyMimBucket.Greeting;

        if (normalizedPath.Contains("/scripted-responses/", StringComparison.OrdinalIgnoreCase))
            return LegacyMimBucket.Personality;

        return null;
    }

    /// <summary>
    /// Derives natural-language trigger phrases from a MIM filename stem.
    /// E.g. "RI_JBO_CanMakeCoffee" → ["can make coffee", "can you make coffee", "are you able to make coffee"]
    /// </summary>
    internal static IReadOnlyList<string> DeriveTriggerPhrases(string fileName)
    {
        var name = fileName;

        // Strip known prefix
        foreach (var prefix in KnownPrefixes)
        {
            if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                name = name[prefix.Length..];
                break;
            }
        }

        if (string.IsNullOrWhiteSpace(name)) return [];

        // Split CamelCase and lowercase
        var parts = CamelCaseSplitPattern.Split(name);
        var lowered = parts.Select(static p => p.ToLowerInvariant()).Where(static p => !string.IsNullOrEmpty(p)).ToArray();
        if (lowered.Length == 0) return [];

        var joined = string.Join(" ", lowered);
        var rest = lowered.Length > 1 ? string.Join(" ", lowered.Skip(1)) : string.Empty;

        var triggers = new List<string> { joined };

        var first = lowered[0];

        switch (first)
        {
            case "can":
                if (!string.IsNullOrEmpty(rest))
                {
                    triggers.Add($"can you {rest}");
                    triggers.Add($"are you able to {rest}");
                    triggers.Add($"could you {rest}");
                }
                break;

            case "is":
                if (!string.IsNullOrEmpty(rest))
                {
                    triggers.Add($"are you {rest}");
                    triggers.Add($"is jibo {rest}");
                }
                break;

            case "are":
                if (!string.IsNullOrEmpty(rest))
                    triggers.Add($"are you {rest}");
                break;

            case "likes" or "like":
                if (!string.IsNullOrEmpty(rest))
                {
                    triggers.Add($"do you like {rest}");
                    triggers.Add($"do you enjoy {rest}");
                    triggers.Add($"does jibo like {rest}");
                }
                break;

            case "loves" or "love":
                if (!string.IsNullOrEmpty(rest))
                {
                    triggers.Add($"do you love {rest}");
                    triggers.Add($"do you like {rest}");
                }
                break;

            case "believes" or "believe":
                if (!string.IsNullOrEmpty(rest))
                {
                    triggers.Add($"do you believe {rest}");
                    // "BelievesInSanta" → rest = "in santa" → already covered, but also add without "in"
                    if (rest.StartsWith("in ", StringComparison.Ordinal))
                        triggers.Add($"do you believe {rest["in ".Length..]}");
                }
                break;

            case "knows" or "know":
                if (!string.IsNullOrEmpty(rest))
                {
                    triggers.Add($"do you know {rest}");
                    triggers.Add($"do you know about {rest}");
                }
                break;

            case "has" or "have":
                if (!string.IsNullOrEmpty(rest))
                {
                    triggers.Add($"do you have {rest}");
                    triggers.Add($"have you {rest}");
                }
                break;

            case "wants" or "want":
                if (!string.IsNullOrEmpty(rest))
                    triggers.Add($"do you want {rest}");
                break;

            case "what":
            case "who":
            case "where":
            case "when":
            case "why":
            case "how":
                // Already in question form — keep as-is, no extra variants needed
                break;

            default:
                // Generic: emit "do you [all words]" as a fallback variant
                triggers.Add($"do you {joined}");
                break;
        }

        return triggers.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string NormalizePrompt(string? prompt)
    {
        return NormalizePrompt(prompt, false);
    }

    private static string NormalizePrompt(string? prompt, bool preservePlaceholders)
    {
        if (string.IsNullOrWhiteSpace(prompt)) return string.Empty;

        var text = WebUtility.HtmlDecode(prompt);
        if (!preservePlaceholders) text = PlaceholderPattern.Replace(text, " ");
        text = LegacyMarkupPattern.Replace(text, " ");
        text = WhitespacePattern.Replace(text, " ").Trim();
        text = SpaceBeforePunctuationPattern.Replace(text, "$1");
        text = WhitespacePattern.Replace(text, " ").Trim();
        text = text.TrimStart('.', ',', ';', ':', '!', '?', ' ');
        return text.Trim();
    }

    private static JiboExperienceCatalog MergeCatalogs(
        JiboExperienceCatalog baseCatalog,
        JiboExperienceCatalog importedCatalog)
    {
        return new JiboExperienceCatalog
        {
            Jokes = Merge(baseCatalog.Jokes, importedCatalog.Jokes),
            RobotFacts = Merge(baseCatalog.RobotFacts, importedCatalog.RobotFacts),
            HumanFacts = Merge(baseCatalog.HumanFacts, importedCatalog.HumanFacts),
            FunFacts = Merge(baseCatalog.FunFacts, importedCatalog.FunFacts),
            DanceAnimations = Merge(baseCatalog.DanceAnimations, importedCatalog.DanceAnimations),
            GreetingReplies = Merge(baseCatalog.GreetingReplies, importedCatalog.GreetingReplies),
            HowAreYouReplies = Merge(baseCatalog.HowAreYouReplies, importedCatalog.HowAreYouReplies),
            EmotionReplies = Merge(baseCatalog.EmotionReplies, importedCatalog.EmotionReplies),
            PersonalityReplies = Merge(baseCatalog.PersonalityReplies, importedCatalog.PersonalityReplies),
            PizzaReplies = Merge(baseCatalog.PizzaReplies, importedCatalog.PizzaReplies),
            SurpriseReplies = Merge(baseCatalog.SurpriseReplies, importedCatalog.SurpriseReplies),
            PersonalReportReplies = Merge(baseCatalog.PersonalReportReplies, importedCatalog.PersonalReportReplies),
            PersonalReportKickOffReplies = Merge(baseCatalog.PersonalReportKickOffReplies,
                importedCatalog.PersonalReportKickOffReplies),
            PersonalReportOutroReplies = Merge(baseCatalog.PersonalReportOutroReplies,
                importedCatalog.PersonalReportOutroReplies),
            ReportSkillTemplates = Merge(baseCatalog.ReportSkillTemplates, importedCatalog.ReportSkillTemplates),
            WeatherIntroReplies = Merge(baseCatalog.WeatherIntroReplies, importedCatalog.WeatherIntroReplies),
            WeatherTomorrowIntroReplies = Merge(baseCatalog.WeatherTomorrowIntroReplies,
                importedCatalog.WeatherTomorrowIntroReplies),
            WeatherTodayHighLowReplies = Merge(baseCatalog.WeatherTodayHighLowReplies,
                importedCatalog.WeatherTodayHighLowReplies),
            WeatherTomorrowHighLowReplies = Merge(baseCatalog.WeatherTomorrowHighLowReplies,
                importedCatalog.WeatherTomorrowHighLowReplies),
            WeatherServiceDownReplies = Merge(baseCatalog.WeatherServiceDownReplies,
                importedCatalog.WeatherServiceDownReplies),
            CalendarNothingTodayReplies = Merge(baseCatalog.CalendarNothingTodayReplies,
                importedCatalog.CalendarNothingTodayReplies),
            CalendarNothingReplies = Merge(baseCatalog.CalendarNothingReplies, importedCatalog.CalendarNothingReplies),
            CalendarOutroReplies = Merge(baseCatalog.CalendarOutroReplies, importedCatalog.CalendarOutroReplies),
            CommuteNowReplies = Merge(baseCatalog.CommuteNowReplies, importedCatalog.CommuteNowReplies),
            CommuteServiceDownReplies = Merge(baseCatalog.CommuteServiceDownReplies,
                importedCatalog.CommuteServiceDownReplies),
            NewsIntroReplies = Merge(baseCatalog.NewsIntroReplies, importedCatalog.NewsIntroReplies),
            NewsCategoryIntroReplies =
                Merge(baseCatalog.NewsCategoryIntroReplies, importedCatalog.NewsCategoryIntroReplies),
            NewsOutroReplies = Merge(baseCatalog.NewsOutroReplies, importedCatalog.NewsOutroReplies),
            WeatherReplies = Merge(baseCatalog.WeatherReplies, importedCatalog.WeatherReplies),
            CalendarReplies = Merge(baseCatalog.CalendarReplies, importedCatalog.CalendarReplies),
            CommuteReplies = Merge(baseCatalog.CommuteReplies, importedCatalog.CommuteReplies),
            NewsReplies = Merge(baseCatalog.NewsReplies, importedCatalog.NewsReplies),
            NewsBriefings = Merge(baseCatalog.NewsBriefings, importedCatalog.NewsBriefings),
            GenericFallbackReplies = Merge(baseCatalog.GenericFallbackReplies, importedCatalog.GenericFallbackReplies),
            DanceReplies = Merge(baseCatalog.DanceReplies, importedCatalog.DanceReplies),
            DanceQuestionReplies = Merge(baseCatalog.DanceQuestionReplies, importedCatalog.DanceQuestionReplies),
            NamedScriptedReplies = MergeNamed(baseCatalog.NamedScriptedReplies, importedCatalog.NamedScriptedReplies),
            NamedScriptedTriggers = MergeTriggers(baseCatalog.NamedScriptedTriggers, importedCatalog.NamedScriptedTriggers)
        };
    }

    private static IReadOnlyList<string> Merge(IReadOnlyList<string> baseList, IReadOnlyList<string> importedList)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var merged = new List<string>();

        foreach (var value in baseList.Concat(importedList))
        {
            if (string.IsNullOrWhiteSpace(value)) continue;

            var normalized = value.Trim();
            if (!seen.Add(normalized)) continue;

            merged.Add(normalized);
        }

        return merged;
    }

    private static IReadOnlyList<JiboConditionedReply> Merge(
        IReadOnlyList<JiboConditionedReply> baseList,
        IReadOnlyList<JiboConditionedReply> importedList)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var merged = new List<JiboConditionedReply>();

        foreach (var value in baseList.Concat(importedList))
        {
            if (string.IsNullOrWhiteSpace(value.Reply)) continue;

            var normalizedCondition = NormalizeCondition(value.Condition);
            var normalizedReply = value.Reply.Trim();
            var key = $"{normalizedCondition}::{normalizedReply}";
            if (!seen.Add(key)) continue;

            merged.Add(new JiboConditionedReply
            {
                Condition = normalizedCondition,
                Reply = normalizedReply
            });
        }

        return merged;
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> MergeNamed(
        IReadOnlyDictionary<string, IReadOnlyList<string>> baseDict,
        IReadOnlyDictionary<string, IReadOnlyList<string>> importedDict)
    {
        var result = new Dictionary<string, IReadOnlyList<string>>(
            baseDict, StringComparer.OrdinalIgnoreCase);

        foreach (var (key, importedReplies) in importedDict)
        {
            if (result.TryGetValue(key, out var existing))
            {
                // Merge reply lists, deduplicating
                var seen = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase);
                var merged = new List<string>(existing);
                foreach (var reply in importedReplies)
                {
                    if (!string.IsNullOrWhiteSpace(reply) && seen.Add(reply.Trim()))
                        merged.Add(reply.Trim());
                }
                result[key] = merged;
            }
            else
            {
                result[key] = importedReplies;
            }
        }

        return result;
    }

    private static IReadOnlyDictionary<string, string> MergeTriggers(
        IReadOnlyDictionary<string, string> baseDict,
        IReadOnlyDictionary<string, string> importedDict)
    {
        // Base catalog's explicit triggers win; imported fills gaps
        var result = new Dictionary<string, string>(baseDict, StringComparer.OrdinalIgnoreCase);
        foreach (var (trigger, stem) in importedDict)
        {
            if (!result.ContainsKey(trigger))
                result[trigger] = stem;
        }
        return result;
    }

    private static string NormalizeCondition(string? condition)
    {
        return string.IsNullOrWhiteSpace(condition) ? string.Empty : WhitespacePattern.Replace(condition.Trim(), " ");
    }

    private static bool IsTemplateBucket(LegacyMimBucket bucket)
    {
        return bucket is LegacyMimBucket.PersonalReportKickOff
            or LegacyMimBucket.PersonalReportOutro
            or LegacyMimBucket.WeatherIntro
            or LegacyMimBucket.WeatherTomorrowIntro
            or LegacyMimBucket.WeatherTodayHighLow
            or LegacyMimBucket.WeatherTomorrowHighLow
            or LegacyMimBucket.WeatherServiceDown
            or LegacyMimBucket.ReportSkillTemplate;
    }

    private enum LegacyMimBucket
    {
        GenericFallback,
        Greeting,
        Jokes,
        RobotFacts,
        HumanFacts,
        HowAreYou,
        Emotion,
        FunFacts,
        FunFactSource,
        Personality,
        PersonalReportKickOff,
        PersonalReportOutro,
        WeatherIntro,
        WeatherTomorrowIntro,
        WeatherTodayHighLow,
        WeatherTomorrowHighLow,
        WeatherServiceDown,
        CalendarNothingToday,
        CalendarNothing,
        CalendarOutro,
        CommuteNow,
        CommuteServiceDown,
        NewsIntro,
        NewsCategoryIntro,
        NewsOutro,
        ReportSkillTemplate
    }

    private sealed class LegacyMimCatalogBuilder
    {
        private readonly List<string> _calendarNothingReplies = [];
        private readonly List<string> _calendarNothingTodayReplies = [];
        private readonly List<string> _calendarOutroReplies = [];
        private readonly List<string> _commuteNowReplies = [];
        private readonly List<string> _commuteServiceDownReplies = [];
        private readonly List<JiboConditionedReply> _emotionReplies = [];
        private readonly List<string> _fallbacks = [];
        private readonly List<string> _greetings = [];
        private readonly List<string> _jokes = [];
        private readonly List<string> _robotFacts = [];
        private readonly List<string> _humanFacts = [];
        private readonly List<string> _howAreYous = [];
        private readonly List<string> _funFacts = [];
        private readonly List<string> _newsCategoryIntroReplies = [];
        private readonly List<string> _newsIntroReplies = [];
        private readonly List<string> _newsOutroReplies = [];
        private readonly List<string> _personalities = [];
        private readonly List<string> _personalReportKickOffReplies = [];
        private readonly List<string> _personalReportOutroReplies = [];
        private readonly List<string> _reportSkillTemplates = [];
        private readonly List<string> _weatherIntroReplies = [];
        private readonly List<string> _weatherServiceDownReplies = [];
        private readonly List<string> _weatherTodayHighLowReplies = [];
        private readonly List<string> _weatherTomorrowHighLowReplies = [];
        private readonly List<string> _weatherTomorrowIntroReplies = [];

        // Named MIM dictionaries
        private readonly Dictionary<string, List<string>> _namedReplies =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _namedTriggers =
            new(StringComparer.OrdinalIgnoreCase);

        public void Add(LegacyMimBucket bucket, string? condition, string text, string? sourcePrompt = null)
        {
            switch (bucket)
            {
                case LegacyMimBucket.GenericFallback:
                    if (_fallbacks.Any(value => string.Equals(value, text, StringComparison.OrdinalIgnoreCase))) return;

                    _fallbacks.Add(text);
                    return;
                case LegacyMimBucket.Greeting:
                    if (_greetings.Any(value => string.Equals(value, text, StringComparison.OrdinalIgnoreCase))) return;

                    _greetings.Add(text);
                    return;
                case LegacyMimBucket.Jokes:
                    if (_jokes.Any(value => string.Equals(value, text, StringComparison.OrdinalIgnoreCase))) return;

                    _jokes.Add(text);
                    return;
                case LegacyMimBucket.RobotFacts:
                    AddDistinct(_robotFacts, text);
                    return;
                case LegacyMimBucket.HumanFacts:
                    AddDistinct(_humanFacts, text);
                    return;
                case LegacyMimBucket.HowAreYou:
                    if (_howAreYous.Any(value => string.Equals(value, text, StringComparison.OrdinalIgnoreCase)))
                        return;

                    _howAreYous.Add(text);
                    return;
                case LegacyMimBucket.Emotion:
                    var normalizedCondition = NormalizeCondition(condition);
                    if (_emotionReplies.Any(value =>
                            string.Equals(NormalizeCondition(value.Condition), normalizedCondition,
                                StringComparison.OrdinalIgnoreCase) &&
                            string.Equals(value.Reply, text, StringComparison.OrdinalIgnoreCase)))
                        return;

                    _emotionReplies.Add(new JiboConditionedReply
                    {
                        Condition = normalizedCondition,
                        Reply = text
                    });
                    return;
                case LegacyMimBucket.Personality:
                    if (_personalities.Any(value => string.Equals(value, text, StringComparison.OrdinalIgnoreCase)))
                        return;

                    _personalities.Add(text);
                    return;
                case LegacyMimBucket.FunFactSource:
                    switch (ResolveFunFactTarget(sourcePrompt ?? text))
                    {
                        case LegacyMimBucket.RobotFacts:
                            AddDistinct(_robotFacts, text);
                            return;
                        case LegacyMimBucket.HumanFacts:
                            AddDistinct(_humanFacts, text);
                            return;
                        default:
                            AddDistinct(_funFacts, text);
                            return;
                    }
                case LegacyMimBucket.FunFacts:
                    if (_funFacts.Any(value => string.Equals(value, text, StringComparison.OrdinalIgnoreCase))) return;

                    _funFacts.Add(text);
                    return;
                case LegacyMimBucket.PersonalReportKickOff:
                    AddDistinct(_personalReportKickOffReplies, text);
                    return;
                case LegacyMimBucket.PersonalReportOutro:
                    AddDistinct(_personalReportOutroReplies, text);
                    return;
                case LegacyMimBucket.WeatherIntro:
                    AddDistinct(_weatherIntroReplies, text);
                    return;
                case LegacyMimBucket.WeatherTomorrowIntro:
                    AddDistinct(_weatherTomorrowIntroReplies, text);
                    return;
                case LegacyMimBucket.WeatherTodayHighLow:
                    AddDistinct(_weatherTodayHighLowReplies, text);
                    return;
                case LegacyMimBucket.WeatherTomorrowHighLow:
                    AddDistinct(_weatherTomorrowHighLowReplies, text);
                    return;
                case LegacyMimBucket.WeatherServiceDown:
                    AddDistinct(_weatherServiceDownReplies, text);
                    return;
                case LegacyMimBucket.CalendarNothingToday:
                    AddDistinct(_calendarNothingTodayReplies, text);
                    return;
                case LegacyMimBucket.CalendarNothing:
                    AddDistinct(_calendarNothingReplies, text);
                    return;
                case LegacyMimBucket.CalendarOutro:
                    AddDistinct(_calendarOutroReplies, text);
                    return;
                case LegacyMimBucket.CommuteNow:
                    AddDistinct(_commuteNowReplies, text);
                    return;
                case LegacyMimBucket.CommuteServiceDown:
                    AddDistinct(_commuteServiceDownReplies, text);
                    return;
                case LegacyMimBucket.NewsIntro:
                    AddDistinct(_newsIntroReplies, text);
                    return;
                case LegacyMimBucket.NewsCategoryIntro:
                    AddDistinct(_newsCategoryIntroReplies, text);
                    return;
                case LegacyMimBucket.NewsOutro:
                    AddDistinct(_newsOutroReplies, text);
                    return;
                case LegacyMimBucket.ReportSkillTemplate:
                    AddDistinct(_reportSkillTemplates, text);
                    return;
                default:
                    throw new ArgumentOutOfRangeException(nameof(bucket), bucket, null);
            }
        }

        public void AddNamed(string fileName, IReadOnlyList<string> replies)
        {
            if (!_namedReplies.TryGetValue(fileName, out var list))
            {
                list = [];
                _namedReplies[fileName] = list;
            }

            var seen = new HashSet<string>(list, StringComparer.OrdinalIgnoreCase);
            foreach (var reply in replies)
            {
                if (!string.IsNullOrWhiteSpace(reply) && seen.Add(reply.Trim()))
                    list.Add(reply.Trim());
            }

            // Derive and register trigger phrases
            var triggers = DeriveTriggerPhrases(fileName);
            foreach (var trigger in triggers)
            {
                if (!string.IsNullOrWhiteSpace(trigger) && !_namedTriggers.ContainsKey(trigger))
                    _namedTriggers[trigger] = fileName;
            }
        }

        public JiboExperienceCatalog Build()
        {
            var namedReplies = _namedReplies.ToDictionary(
                static kv => kv.Key,
                static kv => (IReadOnlyList<string>)kv.Value,
                StringComparer.OrdinalIgnoreCase);

            return new JiboExperienceCatalog
            {
                Jokes = [.. _jokes],
                RobotFacts = [.. _robotFacts],
                HumanFacts = [.. _humanFacts],
                FunFacts = [.. _funFacts],
                GreetingReplies = [.. _greetings],
                HowAreYouReplies = [.. _howAreYous],
                EmotionReplies = [.. _emotionReplies],
                PersonalityReplies = [.. _personalities],
                GenericFallbackReplies = [.. _fallbacks],
                PersonalReportKickOffReplies = [.. _personalReportKickOffReplies],
                PersonalReportOutroReplies = [.. _personalReportOutroReplies],
                ReportSkillTemplates = [.. _reportSkillTemplates],
                WeatherIntroReplies = [.. _weatherIntroReplies],
                WeatherTomorrowIntroReplies = [.. _weatherTomorrowIntroReplies],
                WeatherTodayHighLowReplies = [.. _weatherTodayHighLowReplies],
                WeatherTomorrowHighLowReplies = [.. _weatherTomorrowHighLowReplies],
                WeatherServiceDownReplies = [.. _weatherServiceDownReplies],
                CalendarNothingTodayReplies = [.. _calendarNothingTodayReplies],
                CalendarNothingReplies = [.. _calendarNothingReplies],
                CalendarOutroReplies = [.. _calendarOutroReplies],
                CommuteNowReplies = [.. _commuteNowReplies],
                CommuteServiceDownReplies = [.. _commuteServiceDownReplies],
                NewsIntroReplies = [.. _newsIntroReplies],
                NewsCategoryIntroReplies = [.. _newsCategoryIntroReplies],
                NewsOutroReplies = [.. _newsOutroReplies],
                NamedScriptedReplies = namedReplies,
                NamedScriptedTriggers = new Dictionary<string, string>(_namedTriggers, StringComparer.OrdinalIgnoreCase)
            };
        }

        private static void AddDistinct(List<string> target, string text)
        {
            if (target.Any(value => string.Equals(value, text, StringComparison.OrdinalIgnoreCase))) return;

            target.Add(text);
        }

        private LegacyMimBucket ResolveFunFactTarget(string prompt)
        {
            var lowered = NormalizePrompt(prompt, false).ToLowerInvariant();
            if (ContainsAny(lowered, "robot", "humanoid", "machine", "about me", "my cameras", "turing", "deep blue", "rossum"))
                return LegacyMimBucket.RobotFacts;

            if (ContainsAny(lowered, "human", "people", "grown ups", "human being", "humans"))
                return LegacyMimBucket.HumanFacts;

            return LegacyMimBucket.FunFacts;
        }

        private static bool ContainsAny(string text, params string[] values)
        {
            return values.Any(value => text.Contains(value, StringComparison.OrdinalIgnoreCase));
        }
    }

    private sealed class LegacyMimDefinition
    {
        [JsonPropertyName("skill_id")] public string? SkillId { get; init; }

        [JsonPropertyName("mim_id")] public string? MimId { get; init; }

        [JsonPropertyName("mim_type")] public string? MimType { get; init; }

        [JsonPropertyName("prompts")] public List<LegacyMimPrompt> Prompts { get; init; } = [];
    }

    private sealed class LegacyMimPrompt
    {
        [JsonPropertyName("mim_id")] public string? MimId { get; init; }

        [JsonPropertyName("prompt_category")] public string? PromptCategory { get; init; }

        [JsonPropertyName("prompt_sub_category")]
        public string? PromptSubCategory { get; init; }

        [JsonPropertyName("condition")] public string? Condition { get; init; }

        [JsonPropertyName("prompt")] public string? Prompt { get; init; }

        [JsonPropertyName("prompt_id")] public string? PromptId { get; init; }

        [JsonPropertyName("weight")] public double? Weight { get; init; }
    }
}
