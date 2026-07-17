using System.Text.Json;
using System.Text.RegularExpressions;
using Jibo.Cloud.Application.Abstractions;
using Jibo.Runtime.Abstractions;

// ReSharper disable MemberCanBePrivate.Global

namespace Jibo.Cloud.Application.Services;

internal static partial class PersonalReportOrchestrator
{
    internal const string StateMetadataKey = "personalReportState";
    internal const string NoMatchCountMetadataKey = "personalReportNoMatchCount";
    internal const string NoInputCountMetadataKey = "personalReportNoInputCount";
    internal const string UserNameMetadataKey = "personalReportUserName";
    internal const string UserVerifiedMetadataKey = "personalReportUserVerified";
    internal const string WeatherEnabledMetadataKey = "personalReportWeatherEnabled";
    internal const string CalendarEnabledMetadataKey = "personalReportCalendarEnabled";
    internal const string CommuteEnabledMetadataKey = "personalReportCommuteEnabled";
    internal const string NewsEnabledMetadataKey = "personalReportNewsEnabled";
    internal const string LastServiceErrorMetadataKey = "personalReportLastServiceError";

    internal const string IdleState = "idle";
    private const string AwaitingOptInState = "awaiting_opt_in";
    private const string AwaitingIdentityConfirmationState = "awaiting_identity_confirmation";
    private const string AwaitingIdentityNameState = "awaiting_identity_name";

    private const int MaxNoMatchCount = 2;
    private const int MaxNoInputCount = 2;

    private static readonly string[] CancelPhrases =
    [
        "cancel",
        "stop",
        "never mind",
        "nevermind",
        "forget it"
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

    public static async Task<JiboInteractionDecision?> TryBuildDecisionAsync(
        TurnContext turn,
        string semanticIntent,
        string loweredTranscript,
        JiboExperienceCatalog catalog,
        IPersonalMemoryStore personalMemoryStore,
        Func<TurnContext, string, CancellationToken, Task<JiboInteractionDecision>> buildWeatherDecisionAsync,
        Func<TurnContext, CancellationToken, Task<JiboInteractionDecision>> buildCalendarDecisionAsync,
        Func<TurnContext, CancellationToken, Task<JiboInteractionDecision>> buildCommuteDecisionAsync,
        Func<TurnContext, CancellationToken, Task<JiboInteractionDecision>> buildNewsDecisionAsync,
        Func<TurnContext, PersonalMemoryTenantScope> tenantScopeResolver,
        CancellationToken cancellationToken)
    {
        var state = ReadState(turn);
        var isActiveState = !string.Equals(state, IdleState, StringComparison.OrdinalIgnoreCase);
        if (!isActiveState &&
            !string.Equals(semanticIntent, "personal_report", StringComparison.OrdinalIgnoreCase)) return null;

        var toggles = ApplyInlineToggleHints(
            ReadServiceToggles(turn),
            loweredTranscript,
            out var inlineToggleSummary);

        if (ContainsAnyPhrase(loweredTranscript, CancelPhrases)) return BuildCancelledDecision(toggles);

        if (!isActiveState)
        {
            var contextUpdates = BuildContextUpdates(
                AwaitingOptInState,
                0,
                0,
                toggles,
                ReadString(turn, UserNameMetadataKey),
                ReadBool(turn, UserVerifiedMetadataKey) ?? false,
                string.Empty);

            var reply = string.IsNullOrWhiteSpace(inlineToggleSummary)
                ? "Would you like your personal report now?"
                : $"{inlineToggleSummary} Would you like your personal report now?";

            return BuildYesNoPromptDecision("personal_report_opt_in", reply, contextUpdates);
        }

        if (string.IsNullOrWhiteSpace(loweredTranscript)) return BuildNoInputDecision(turn, state, toggles);

        var yesNoReply = ClassifyYesNoReply(loweredTranscript);
        switch (state)
        {
            case AwaitingOptInState:
                switch (yesNoReply)
                {
                    case YesNoReply.Affirmative:
                    {
                        var scope = tenantScopeResolver(turn);
                        var knownName = ReadString(turn, UserNameMetadataKey) ?? personalMemoryStore.GetName(scope);
                        if (!string.IsNullOrWhiteSpace(knownName))
                            return BuildYesNoPromptDecision(
                                "personal_report_verify_user",
                                $"I think this is {knownName}. Is that right?",
                                BuildContextUpdates(
                                    AwaitingIdentityConfirmationState,
                                    0,
                                    0,
                                    toggles,
                                    knownName,
                                    false,
                                    string.Empty));

                        return new JiboInteractionDecision(
                            "personal_report_request_name",
                            "Who is this?",
                            ContextUpdates: BuildContextUpdates(
                                AwaitingIdentityNameState,
                                0,
                                0,
                                toggles,
                                null,
                                false,
                                string.Empty));
                    }
                    case YesNoReply.Negative:
                        return BuildDeclinedDecision(toggles);
                    case YesNoReply.Ambiguous:
                        return BuildNoMatchDecision(
                            turn,
                            state,
                            "I heard both yes and no. Could you say that again?",
                            toggles,
                            ReadString(turn, UserNameMetadataKey),
                            false);
                }

                if (!string.IsNullOrWhiteSpace(inlineToggleSummary))
                    return BuildYesNoPromptDecision(
                        "personal_report_opt_in",
                        $"{inlineToggleSummary} Would you like your personal report now?",
                        BuildContextUpdates(
                            AwaitingOptInState,
                            0,
                            0,
                            toggles,
                            ReadString(turn, UserNameMetadataKey),
                            false,
                            string.Empty));

                return BuildNoMatchDecision(
                    turn,
                    state,
                    "Please say yes to start your personal report, or no to skip it.",
                    toggles,
                    ReadString(turn, UserNameMetadataKey),
                    false);

            case AwaitingIdentityConfirmationState:
            {
                var currentName = ReadString(turn, UserNameMetadataKey);
                if (string.IsNullOrWhiteSpace(currentName))
                    return new JiboInteractionDecision(
                        "personal_report_request_name",
                        "Who is this?",
                        ContextUpdates: BuildContextUpdates(
                            AwaitingIdentityNameState,
                            0,
                            0,
                            toggles,
                            null,
                            false,
                            string.Empty));

                return yesNoReply switch
                {
                    YesNoReply.Affirmative => await BuildDeliveredReportDecisionAsync(turn, catalog,
                        toggles, currentName, buildWeatherDecisionAsync, buildCalendarDecisionAsync,
                        buildCommuteDecisionAsync, buildNewsDecisionAsync, cancellationToken),
                    YesNoReply.Negative => new JiboInteractionDecision("personal_report_request_name",
                        "Okay, who is this?",
                        ContextUpdates: BuildContextUpdates(AwaitingIdentityNameState, 0, 0, toggles, null, false,
                            string.Empty)),
                    _ => BuildNoMatchDecision(turn, state,
                        yesNoReply == YesNoReply.Ambiguous
                            ? $"I heard both yes and no. Is this {currentName}?"
                            : $"Please answer yes or no. Is this {currentName}?", toggles, currentName, false)
                };
            }

            case AwaitingIdentityNameState:
            {
                var parsedName = TryExtractName(loweredTranscript);
                if (string.IsNullOrWhiteSpace(parsedName))
                    return BuildNoMatchDecision(
                        turn,
                        state,
                        "Tell me your name like this: my name is Alex.",
                        toggles,
                        null,
                        false);

                personalMemoryStore.SetName(tenantScopeResolver(turn), parsedName);
                return await BuildDeliveredReportDecisionAsync(
                    turn,
                    catalog,
                    toggles,
                    parsedName,
                    buildWeatherDecisionAsync,
                    buildCalendarDecisionAsync,
                    buildCommuteDecisionAsync,
                    buildNewsDecisionAsync,
                    cancellationToken);
            }

            default:
                return BuildDeclinedDecision(toggles);
        }
    }

    private static async Task<JiboInteractionDecision> BuildDeliveredReportDecisionAsync(
        TurnContext turn,
        JiboExperienceCatalog catalog,
        PersonalReportServiceToggles toggles,
        string userName,
        Func<TurnContext, string, CancellationToken, Task<JiboInteractionDecision>> buildWeatherDecisionAsync,
        Func<TurnContext, CancellationToken, Task<JiboInteractionDecision>> buildCalendarDecisionAsync,
        Func<TurnContext, CancellationToken, Task<JiboInteractionDecision>> buildCommuteDecisionAsync,
        Func<TurnContext, CancellationToken, Task<JiboInteractionDecision>> buildNewsDecisionAsync,
        CancellationToken cancellationToken)
    {
        var spokenSections = new List<string>();
        var sequenceSections = new List<IDictionary<string, object?>>();
        var serviceError = string.Empty;
        IDictionary<string, object?>? weatherSkillPayload = null;
        IDictionary<string, object?>? newsSkillPayload = null;

        var kickOff = RenderPersonalReportTemplate(
            ChoosePersonalReportTemplate(
                catalog.PersonalReportKickOffReplies,
                "Okay. Here's your personal report."),
            userName);

        var weatherBlockParts = new List<string> { kickOff };
        if (toggles.WeatherEnabled)
        {
            var weatherDecision = await buildWeatherDecisionAsync(turn, "weather", cancellationToken);
            weatherSkillPayload = weatherDecision.SkillPayload;
            if (!string.IsNullOrWhiteSpace(weatherDecision.ReplyText))
                weatherBlockParts.Add(weatherDecision.ReplyText);
            if (IsWeatherErrorReply(weatherDecision.ReplyText)) serviceError = "weather";
        }

        var weatherBlockText = string.Join(" ", weatherBlockParts);
        spokenSections.Add(weatherBlockText);
        sequenceSections.Add(BuildReportSequenceSection(
            "kickoff_weather",
            weatherBlockText,
            "weather",
            weatherSkillPayload is not null
                ? weatherSkillPayload.TryGetValue("weather_icon", out var weatherIcon)
                    ? weatherIcon?.ToString() ?? "cloudy"
                    : "cloudy"
                : null));

        if (toggles.CalendarEnabled)
        {
            var calendarReply = (await buildCalendarDecisionAsync(turn, cancellationToken)).ReplyText;
            if (!string.IsNullOrWhiteSpace(calendarReply))
            {
                // Full-report calendar never plays CalendarOutro (Pegasus single-skill only).
                spokenSections.Add(calendarReply);
                sequenceSections.Add(BuildReportSequenceSection("calendar", calendarReply));
            }
        }

        if (toggles.CommuteEnabled)
        {
            var commuteDecision = await buildCommuteDecisionAsync(turn, cancellationToken);
            var commuteReply = commuteDecision.ReplyText;
            if (!string.IsNullOrWhiteSpace(commuteReply))
            {
                spokenSections.Add(commuteReply.Trim());
                var animMeta = commuteDecision.SkillPayload is not null &&
                               commuteDecision.SkillPayload.TryGetValue("commute_anim_meta", out var meta)
                    ? meta?.ToString()
                    : "commute-normal, no-eye-end";
                sequenceSections.Add(BuildReportSequenceSection(
                    "commute",
                    commuteReply.Trim(),
                    "commute",
                    animMeta));
            }
        }

        if (toggles.NewsEnabled)
        {
            var newsDecision = await buildNewsDecisionAsync(turn, cancellationToken);
            newsSkillPayload = newsDecision.SkillPayload;
            // Full-report news never plays NewsOutro (Pegasus single-skill only).
            // Match Pegasus mim order: NewsIntro (news-intro) then one NewsHeadline (news-stinger) per story.
            if (TryBuildPersonalReportNewsSections(
                    newsDecision,
                    newsSkillPayload,
                    out var newsSpoken,
                    out var newsSequenceSections))
            {
                spokenSections.Add(newsSpoken);
                sequenceSections.AddRange(newsSequenceSections);
            }
        }

        var outro = RenderPersonalReportTemplate(
            ChoosePersonalReportOutroTemplate(catalog.PersonalReportOutroReplies, toggles),
            userName);
        spokenSections.Add(outro);
        sequenceSections.Add(BuildReportSequenceSection("outro", outro));

        var reportText = string.Join(" ", spokenSections);
        return new JiboInteractionDecision(
            "personal_report_delivered",
            reportText,
            "report-skill",
            BuildPersonalReportSkillPayload(reportText, weatherSkillPayload, newsSkillPayload, sequenceSections),
            BuildContextUpdates(
                IdleState,
                0,
                0,
                toggles,
                userName,
                true,
                serviceError));
    }

    private static IDictionary<string, object?> BuildReportSequenceSection(
        string kind,
        string text,
        string? animCat = null,
        string? animMeta = null,
        IDictionary<string, object?>? extraPayload = null)
    {
        var section = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["kind"] = kind,
            ["text"] = text
        };
        if (!string.IsNullOrWhiteSpace(animCat)) section["anim_cat"] = animCat;
        if (!string.IsNullOrWhiteSpace(animMeta)) section["anim_meta"] = animMeta;
        if (extraPayload is null) return section;

        foreach (var (key, value) in extraPayload)
            if (!string.Equals(key, "esml", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(key, "skillId", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(key, "cloudSkill", StringComparison.OrdinalIgnoreCase) &&
                !section.ContainsKey(key))
                section[key] = value;

        return section;
    }

    private static bool TryBuildPersonalReportNewsSections(
        JiboInteractionDecision newsDecision,
        IDictionary<string, object?>? newsSkillPayload,
        out string spokenNews,
        out List<IDictionary<string, object?>> sequenceSections)
    {
        spokenNews = string.Empty;
        sequenceSections = [];

        var headlineTitles = ExtractNewsHeadlineTitles(newsSkillPayload);
        if (headlineTitles.Count > 0)
        {
            // Pegasus: NewsIntro with news-intro anim, then one NewsHeadline MIM per story.
            var leadIn = "Here's today's news.";
            var spokenParts = new List<string> { leadIn };
            sequenceSections.Add(BuildReportSequenceSection(
                "news_intro",
                leadIn,
                "news",
                "news-intro, no-eye-end"));

            foreach (var title in headlineTitles)
            {
                var headlineText = title.EndsWith(".", StringComparison.Ordinal) ? title : $"{title}.";
                spokenParts.Add(headlineText);
                sequenceSections.Add(BuildReportSequenceSection(
                    "news_headline",
                    headlineText,
                    "news",
                    "news-stinger"));
            }

            spokenNews = string.Join(" ", spokenParts);
            return true;
        }

        if (string.IsNullOrWhiteSpace(newsDecision.ReplyText)) return false;

        var newsText = newsDecision.ReplyText.Trim();
        spokenNews = newsText;
        sequenceSections.Add(BuildReportSequenceSection(
            "news",
            newsText,
            "news",
            "news-stinger",
            newsSkillPayload));
        return true;
    }

    private static IReadOnlyList<string> ExtractNewsHeadlineTitles(IDictionary<string, object?>? newsSkillPayload)
    {
        if (newsSkillPayload is null ||
            !newsSkillPayload.TryGetValue("news_headlines", out var raw) ||
            raw is null)
            return [];

        var titles = new List<string>();
        if (raw is IEnumerable<object?> objectItems)
        {
            foreach (var item in objectItems)
            {
                if (item is IDictionary<string, object?> dict &&
                    dict.TryGetValue("title", out var titleObj) &&
                    titleObj is string title &&
                    !string.IsNullOrWhiteSpace(title))
                    titles.Add(title.Trim());
                else if (item is IReadOnlyDictionary<string, object?> readOnlyDict &&
                         readOnlyDict.TryGetValue("title", out var readOnlyTitle) &&
                         readOnlyTitle is string readOnlyTitleText &&
                         !string.IsNullOrWhiteSpace(readOnlyTitleText))
                    titles.Add(readOnlyTitleText.Trim());
            }

            return titles;
        }

        if (raw is System.Collections.IEnumerable enumerable)
            foreach (var item in enumerable)
            {
                if (item is IDictionary<string, object?> dict &&
                    dict.TryGetValue("title", out var titleObj) &&
                    titleObj is string title &&
                    !string.IsNullOrWhiteSpace(title))
                    titles.Add(title.Trim());
            }

        return titles;
    }

    private static IDictionary<string, object?> BuildPersonalReportSkillPayload(
        string reportText,
        IDictionary<string, object?>? weatherSkillPayload,
        IDictionary<string, object?>? newsSkillPayload,
        IReadOnlyList<IDictionary<string, object?>> sequenceSections)
    {
        var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["skillId"] = "report-skill",
            ["cloudSkill"] = "personal_report",
            ["mim_id"] = "runtime-personal-report",
            ["mim_type"] = "announcement",
            ["prompt_id"] = "PersonalReport_AN_01",
            ["prompt_sub_category"] = "AN",
            ["esml"] =
                $"<speak><es cat='neutral' filter='!ssa-only, !sfx-only' endNeutral='true'>{EscapeForEsml(reportText)}</es></speak>",
            ["personal_report_report_text"] = reportText,
            ["personal_report_sections"] = sequenceSections
        };

        MergeSkillPayload(payload, weatherSkillPayload);
        MergeSkillPayload(payload, newsSkillPayload);
        return payload;
    }

    private static void MergeSkillPayload(
        IDictionary<string, object?> payload,
        IDictionary<string, object?>? skillPayload)
    {
        if (skillPayload is null) return;

        foreach (var (key, value) in skillPayload)
            if (!string.Equals(key, "esml", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(key, "skillId", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(key, "cloudSkill", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(key, "mim_id", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(key, "mim_type", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(key, "prompt_id", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(key, "prompt_sub_category", StringComparison.OrdinalIgnoreCase) &&
                !payload.ContainsKey(key))
                payload[key] = value;
    }

    private static string ChoosePersonalReportOutroTemplate(
        IReadOnlyList<string> templates,
        PersonalReportServiceToggles toggles)
    {
        const string configuredFallback =
            "That wraps up your report for the day. Hope you have a good one.";
        const string unconfiguredFallback =
            "And that's the report. Next time I can make it more personal in the Jibo app.";

        var isConfigured = toggles.WeatherEnabled ||
                           toggles.CalendarEnabled ||
                           toggles.CommuteEnabled ||
                           toggles.NewsEnabled;
        var filtered = templates
            .Where(static template => !string.IsNullOrWhiteSpace(template) &&
                                      !template.Contains("${dt.", StringComparison.OrdinalIgnoreCase))
            .Where(template => isConfigured
                ? !IsUnconfiguredOutroTemplate(template)
                : IsUnconfiguredOutroTemplate(template))
            .ToArray();

        if (filtered.Length == 0)
            return isConfigured ? configuredFallback : unconfiguredFallback;

        return ChoosePersonalReportTemplate(filtered, isConfigured ? configuredFallback : unconfiguredFallback);
    }

    private static bool IsUnconfiguredOutroTemplate(string template)
    {
        return template.Contains("Jibo app", StringComparison.OrdinalIgnoreCase) ||
               template.Contains("Loop tab", StringComparison.OrdinalIgnoreCase) ||
               template.Contains("set that up", StringComparison.OrdinalIgnoreCase) ||
               template.Contains("set <pitch", StringComparison.OrdinalIgnoreCase) ||
               template.Contains("more personalized report", StringComparison.OrdinalIgnoreCase);
    }

    private static JiboInteractionDecision BuildNoInputDecision(
        TurnContext turn,
        string state,
        PersonalReportServiceToggles toggles)
    {
        var noInputCount = Math.Max(0, ReadInt(turn, NoInputCountMetadataKey)) + 1;
        if (noInputCount >= MaxNoInputCount) return BuildDeclinedDecision(toggles);

        var contextUpdates = BuildContextUpdates(
            state,
            ReadInt(turn, NoMatchCountMetadataKey),
            noInputCount,
            toggles,
            ReadString(turn, UserNameMetadataKey),
            ReadBool(turn, UserVerifiedMetadataKey) ?? false,
            string.Empty);

        if (IsYesNoPromptState(state))
            return BuildYesNoPromptDecision(
                "personal_report_no_input",
                "I am still here. Do you want your personal report?",
                contextUpdates);

        return new JiboInteractionDecision(
            "personal_report_no_input",
            "I am still here. Do you want your personal report?",
            ContextUpdates: contextUpdates);
    }

    private static JiboInteractionDecision BuildNoMatchDecision(
        TurnContext turn,
        string state,
        string repromptText,
        PersonalReportServiceToggles toggles,
        string? userName,
        bool userVerified)
    {
        var noMatchCount = Math.Max(0, ReadInt(turn, NoMatchCountMetadataKey)) + 1;
        if (noMatchCount >= MaxNoMatchCount) return BuildDeclinedDecision(toggles);

        var contextUpdates = BuildContextUpdates(
            state,
            noMatchCount,
            0,
            toggles,
            userName,
            userVerified,
            string.Empty);

        if (IsYesNoPromptState(state))
            return BuildYesNoPromptDecision("personal_report_no_match", repromptText, contextUpdates);

        return new JiboInteractionDecision(
            "personal_report_no_match",
            repromptText,
            ContextUpdates: contextUpdates);
    }

    private static JiboInteractionDecision BuildDeclinedDecision(PersonalReportServiceToggles toggles)
    {
        return new JiboInteractionDecision(
            "personal_report_declined",
            "No problem. We can do your personal report another time.",
            ContextUpdates: BuildContextUpdates(
                IdleState,
                0,
                0,
                toggles,
                null,
                false,
                string.Empty));
    }

    private static JiboInteractionDecision BuildCancelledDecision(PersonalReportServiceToggles toggles)
    {
        return new JiboInteractionDecision(
            "personal_report_cancelled",
            "Okay, canceling personal report.",
            ContextUpdates: BuildContextUpdates(
                IdleState,
                0,
                0,
                toggles,
                null,
                false,
                string.Empty));
    }

    private static IDictionary<string, object?> BuildContextUpdates(
        string state,
        int noMatchCount,
        int noInputCount,
        PersonalReportServiceToggles toggles,
        string? userName,
        bool userVerified,
        string lastServiceError)
    {
        return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            [StateMetadataKey] = state,
            [NoMatchCountMetadataKey] = noMatchCount,
            [NoInputCountMetadataKey] = noInputCount,
            [UserNameMetadataKey] = userName,
            [UserVerifiedMetadataKey] = userVerified,
            [WeatherEnabledMetadataKey] = toggles.WeatherEnabled,
            [CalendarEnabledMetadataKey] = toggles.CalendarEnabled,
            [CommuteEnabledMetadataKey] = toggles.CommuteEnabled,
            [NewsEnabledMetadataKey] = toggles.NewsEnabled,
            [LastServiceErrorMetadataKey] = lastServiceError
        };
    }

    private static bool IsYesNoPromptState(string state)
    {
        return string.Equals(state, AwaitingOptInState, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(state, AwaitingIdentityConfirmationState, StringComparison.OrdinalIgnoreCase);
    }

    private static JiboInteractionDecision BuildYesNoPromptDecision(
        string intentName,
        string reply,
        IDictionary<string, object?> contextUpdates)
    {
        return new JiboInteractionDecision(
            intentName,
            reply,
            "chitchat-skill",
            BuildYesNoPromptPayload(),
            contextUpdates);
    }

    private static IDictionary<string, object?> BuildYesNoPromptPayload()
    {
        return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["mim_id"] = "runtime-chat",
            ["mim_type"] = "question",
            ["prompt_id"] = "RUNTIME_PROMPT",
            ["prompt_sub_category"] = "Q",
            ["listen_contexts"] = new[] { "shared/yes_no" }
        };
    }

    private static YesNoReply ClassifyYesNoReply(string loweredTranscript)
    {
        return YesNoTranscriptClassifier.Classify(loweredTranscript) switch
        {
            YesNoTranscriptClassification.Affirmative => YesNoReply.Affirmative,
            YesNoTranscriptClassification.Negative => YesNoReply.Negative,
            YesNoTranscriptClassification.Ambiguous => YesNoReply.Ambiguous,
            _ => YesNoReply.None
        };
    }

    private static string NormalizeYesNoTranscript(string transcript)
    {
        if (string.IsNullOrWhiteSpace(transcript)) return string.Empty;

        var normalized = NameNoiseRegex().Replace(transcript, " ").ToLowerInvariant();
        return Regex.Replace(normalized, @"\s+", " ").Trim();
    }

    private static bool TryTrimLeadingAcknowledgement(string normalizedTranscript, out string trimmedTranscript)
    {
        foreach (var acknowledgement in YesNoAcknowledgementPrefixes)
        {
            if (string.Equals(acknowledgement, "uh", StringComparison.Ordinal) &&
                (string.Equals(normalizedTranscript, "uh huh", StringComparison.Ordinal) ||
                 normalizedTranscript.StartsWith("uh huh ", StringComparison.Ordinal)))
                continue;

            if (string.Equals(normalizedTranscript, acknowledgement, StringComparison.Ordinal))
            {
                trimmedTranscript = string.Empty;
                return true;
            }

            if (!normalizedTranscript.StartsWith($"{acknowledgement} ", StringComparison.Ordinal)) continue;

            trimmedTranscript = normalizedTranscript[(acknowledgement.Length + 1)..].TrimStart();
            return true;
        }

        trimmedTranscript = normalizedTranscript;
        return false;
    }

    private static bool ContainsAnyPhrase(string loweredTranscript, IEnumerable<string> phrases)
    {
        return phrases.Any(phrase =>
            string.Equals(loweredTranscript, phrase, StringComparison.Ordinal) ||
            loweredTranscript.StartsWith($"{phrase} ", StringComparison.Ordinal) ||
            loweredTranscript.Contains($" {phrase}", StringComparison.Ordinal));
    }

    private static bool IsWeatherErrorReply(string replyText)
    {
        if (string.IsNullOrWhiteSpace(replyText)) return false;

        return replyText.Contains("couldn't fetch the weather", StringComparison.OrdinalIgnoreCase) ||
               replyText.Contains("weather service is connected", StringComparison.OrdinalIgnoreCase);
    }

    private static PersonalReportServiceToggles ReadServiceToggles(TurnContext turn)
    {
        return new PersonalReportServiceToggles(
            ReadBool(turn, WeatherEnabledMetadataKey) ?? true,
            ReadBool(turn, CalendarEnabledMetadataKey) ?? true,
            ReadBool(turn, CommuteEnabledMetadataKey) ?? true,
            ReadBool(turn, NewsEnabledMetadataKey) ?? true);
    }

    private static PersonalReportServiceToggles ApplyInlineToggleHints(
        PersonalReportServiceToggles toggles,
        string loweredTranscript,
        out string summary)
    {
        summary = string.Empty;
        var updated = toggles;

        updated = ApplyToggleHint(updated, loweredTranscript, "weather",
            static value => value with { WeatherEnabled = false },
            static value => value with { WeatherEnabled = true });
        updated = ApplyToggleHint(updated, loweredTranscript, "calendar",
            static value => value with { CalendarEnabled = false },
            static value => value with { CalendarEnabled = true });
        updated = ApplyToggleHint(updated, loweredTranscript, "commute",
            static value => value with { CommuteEnabled = false },
            static value => value with { CommuteEnabled = true });
        updated = ApplyToggleHint(updated, loweredTranscript, "news",
            static value => value with { NewsEnabled = false }, static value => value with { NewsEnabled = true });

        var changes = new List<string>();
        if (updated.WeatherEnabled != toggles.WeatherEnabled)
            changes.Add(updated.WeatherEnabled ? "including weather" : "skipping weather");

        if (updated.CalendarEnabled != toggles.CalendarEnabled)
            changes.Add(updated.CalendarEnabled ? "including calendar" : "skipping calendar");

        if (updated.CommuteEnabled != toggles.CommuteEnabled)
            changes.Add(updated.CommuteEnabled ? "including commute" : "skipping commute");

        if (updated.NewsEnabled != toggles.NewsEnabled)
            changes.Add(updated.NewsEnabled ? "including news" : "skipping news");

        if (changes.Count > 0) summary = $"Got it, {string.Join(", ", changes)}.";

        return updated;
    }

    private static PersonalReportServiceToggles ApplyToggleHint(
        PersonalReportServiceToggles toggles,
        string loweredTranscript,
        string serviceLabel,
        Func<PersonalReportServiceToggles, PersonalReportServiceToggles> disable,
        Func<PersonalReportServiceToggles, PersonalReportServiceToggles> enable)
    {
        if (loweredTranscript.Contains($"without {serviceLabel}", StringComparison.Ordinal) ||
            loweredTranscript.Contains($"skip {serviceLabel}", StringComparison.Ordinal) ||
            loweredTranscript.Contains($"no {serviceLabel}", StringComparison.Ordinal))
            return disable(toggles);

        if (loweredTranscript.Contains($"with {serviceLabel}", StringComparison.Ordinal) ||
            loweredTranscript.Contains($"include {serviceLabel}", StringComparison.Ordinal))
            return enable(toggles);

        return toggles;
    }

    private static string ReadState(TurnContext turn)
    {
        return ReadString(turn, StateMetadataKey) ?? IdleState;
    }

    private static string? ReadString(TurnContext turn, string key)
    {
        if (!turn.Attributes.TryGetValue(key, out var value) || value is null) return null;

        return value switch
        {
            string text => string.IsNullOrWhiteSpace(text) ? null : text.Trim(),
            _ => value.ToString()
        };
    }

    private static bool? ReadBool(TurnContext turn, string key)
    {
        if (!turn.Attributes.TryGetValue(key, out var value) || value is null) return null;

        return value switch
        {
            bool flag => flag,
            string text when bool.TryParse(text, out var parsed) => parsed,
            JsonElement { ValueKind: JsonValueKind.True } => true,
            JsonElement { ValueKind: JsonValueKind.False } => false,
            JsonElement { ValueKind: JsonValueKind.String } json when bool.TryParse(json.GetString(), out var parsed) =>
                parsed,
            _ => null
        };
    }

    private static int ReadInt(TurnContext turn, string key)
    {
        if (!turn.Attributes.TryGetValue(key, out var value) || value is null) return 0;

        return value switch
        {
            int integer => integer,
            long whole and <= int.MaxValue and >= int.MinValue => (int)whole,
            string text when int.TryParse(text, out var parsed) => parsed,
            JsonElement { ValueKind: JsonValueKind.Number } number when number.TryGetInt32(out var parsed) => parsed,
            JsonElement { ValueKind: JsonValueKind.String } json when int.TryParse(json.GetString(), out var parsed) =>
                parsed,
            _ => 0
        };
    }

    private static string? TryExtractName(string loweredTranscript)
    {
        var normalized = NameNoiseRegex().Replace(loweredTranscript, " ")
            .Replace("  ", " ", StringComparison.Ordinal)
            .Trim();
        if (string.IsNullOrWhiteSpace(normalized)) return null;

        var prefixes = new[]
        {
            "my name is ",
            "it is ",
            "it s ",
            "it's ",
            "i am ",
            "im "
        };

        foreach (var prefix in prefixes)
        {
            if (!normalized.StartsWith(prefix, StringComparison.Ordinal)) continue;

            var candidate = normalized[prefix.Length..].Trim();
            return NormalizeNameCandidate(candidate);
        }

        return NormalizeNameCandidate(normalized);
    }

    private static string? NormalizeNameCandidate(string candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate)) return null;

        var cleaned = NameNoiseRegex().Replace(candidate, " ")
            .Replace("  ", " ", StringComparison.Ordinal)
            .Trim();
        if (string.IsNullOrWhiteSpace(cleaned)) return null;

        if (cleaned.Length is < 2 or > 32) return null;

        var words = cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length > 4) return null;

        return words.Any(static word => word.Any(char.IsDigit)) ? null : cleaned;
    }

    private static string ChoosePersonalReportTemplate(
        IReadOnlyList<string> templates,
        string fallback)
    {
        var usableTemplates = templates
            .Where(static template => !string.IsNullOrWhiteSpace(template) &&
                                      !template.Contains("${dt.", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (usableTemplates.Length == 0) return fallback;

        var speakerAwareTemplate = usableTemplates.FirstOrDefault(static template =>
            template.Contains("${speaker}", StringComparison.OrdinalIgnoreCase));
        return ChooseShortestTemplate(speakerAwareTemplate is not null ? [speakerAwareTemplate] : usableTemplates)
               ?? fallback;
    }

    private static string RenderPersonalReportTemplate(string template, string userName)
    {
        return template
            .Replace("${speaker}", userName, StringComparison.OrdinalIgnoreCase)
            .Replace("${speaker}'s", $"{userName}'s", StringComparison.OrdinalIgnoreCase)
            .Replace("  ", " ", StringComparison.Ordinal)
            .Trim();
    }

    private static string? ChooseShortestTemplate(IEnumerable<string> templates)
    {
        var selected = templates
            .Where(static template => !string.IsNullOrWhiteSpace(template))
            .OrderBy(static template => template.Length)
            .FirstOrDefault();

        return selected;
    }

    private static string EscapeForEsml(string value)
    {
        return value
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal)
            .Replace("'", "&apos;", StringComparison.Ordinal);
    }

    [GeneratedRegex(@"[^a-zA-Z\-\s']", RegexOptions.Compiled)]
    private static partial Regex NameNoiseRegex();

    private enum YesNoReply
    {
        None,
        Affirmative,
        Negative,
        Ambiguous
    }

    private readonly record struct PersonalReportServiceToggles(
        bool WeatherEnabled,
        bool CalendarEnabled,
        bool CommuteEnabled,
        bool NewsEnabled);
}