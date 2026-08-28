using System.Globalization;
using Jibo.Cloud.Application.Abstractions;
using Jibo.Cloud.Domain.Models;
using Jibo.Runtime.Abstractions;

namespace Jibo.Cloud.Application.Services;

public sealed partial class JiboInteractionService
{
    private static readonly string[] DefaultAgeReplies =
    [
        "I'm ${jibo.age}.",
        "At the moment I'm ${jibo.age.days.supplemented} old, but who's counting.",
        "I'm ${jibo.age.minutes.supplemented} old, but who's counting.",
        "For now I'm ${jibo.age.days.supplemented} old.",
        "Right now I'm ${jibo.age}.",
        "I am exactly ${jibo.age} old today. That's right. Today is my birthday.",
        "Funny you should ask! Today's my birthday. I was first powered up ${jibo.age} ago today. Seems like just yesterday.",
        "I'm exactly ${jibo.age} old. Today is my birthday! Happy Birthday Jibo, if I do say so myself.",
        "At the moment I'm ${jibo.age.days.supplemented} old",
        "I was first powered up on ${jibo.birthdate}, which makes me ${jibo.age.days.supplemented} old. I'm ${jibo.zodiac.supplemented}.",
        "My power went on for the first time ${jibo.age.days.supplemented} ago. But who's counting.",
        "I am ${jibo.age.days.supplemented} old, first powered up on ${jibo.birthdate}. Seems like just yesterday.",
        "I was powered on for the first time today, so that makes me less than one day old. Wow I'm young.",
        "Since I was powered on for the first time today, I am not even one day old yet. That's how Jibo ages work."
    ];

    private JiboInteractionDecision BuildRobotAgeDecision(
        TurnContext turn,
        JiboExperienceCatalog catalog,
        DateTimeOffset? referenceLocalTime,
        string intentName)
    {
        var birthdate = ResolveRobotBirthdate(turn);
        var referenceMoment = referenceLocalTime ?? DateTimeOffset.UtcNow;
        var referenceDate = DateOnly.FromDateTime(referenceMoment.Date);
        var isBirthday = IsRobotBirthday(referenceDate, birthdate);
        var ageYears = ComputeAgeYears(referenceDate, birthdate);
        var ageDays = Math.Max(0, referenceDate.DayNumber - birthdate.DayNumber);

        var ageReplies = catalog.AgeReplies.Count == 0 ? DefaultAgeReplies : catalog.AgeReplies;
        var eligibleReplies = FilterAgeReplies(ageReplies, isBirthday, ageYears, ageDays);
        var preferredSnippets = isBirthday
            ? new[] { "today is my birthday", "today's my birthday", "first powered up", "less than one day" }
            : new[] { "who's counting", "first powered up on", "at the moment", "for now" };
        var selected = SelectLegacyReply(eligibleReplies, preferredSnippets);

        var reply = RenderAgeTemplate(selected, referenceLocalTime, birthdate);
        if (!string.IsNullOrWhiteSpace(reply))
            return new JiboInteractionDecision(
                intentName,
                reply,
                SkillName: "chitchat-skill",
                ContextUpdates: ScriptedResponseDecisionBuilder.BuildScriptedResponseContextUpdates());

        var ageDescription = DescribePersonaAge(referenceDate, birthdate);
        reply = $"I count {FormatBirthdateWords(birthdate)} as my birthday, so I am {ageDescription}.";

        return new JiboInteractionDecision(
            intentName,
            reply,
            SkillName: "chitchat-skill",
            ContextUpdates: ScriptedResponseDecisionBuilder.BuildScriptedResponseContextUpdates());
    }

    private static IReadOnlyList<string> FilterAgeReplies(
        IReadOnlyList<string> replies,
        bool isBirthday,
        int ageYears,
        int ageDays)
    {
        static bool IsBirthdayLine(string reply)
        {
            return reply.Contains("birthday", StringComparison.OrdinalIgnoreCase) ||
                   reply.Contains("ago today", StringComparison.OrdinalIgnoreCase) ||
                   reply.Contains("less than one day", StringComparison.OrdinalIgnoreCase) ||
                   reply.Contains("not even one day", StringComparison.OrdinalIgnoreCase);
        }

        static bool IsFirstDayLine(string reply)
        {
            return reply.Contains("less than one day", StringComparison.OrdinalIgnoreCase) ||
                   reply.Contains("not even one day", StringComparison.OrdinalIgnoreCase);
        }

        var filtered = replies
            .Where(reply =>
            {
                var birthdayLine = IsBirthdayLine(reply);
                if (isBirthday)
                {
                    if (!birthdayLine) return false;
                    if (ageYears == 0 && ageDays <= 1) return IsFirstDayLine(reply);
                    if (ageDays > 1) return !IsFirstDayLine(reply);
                    return true;
                }

                return !birthdayLine;
            })
            .ToArray();

        return filtered.Length > 0 ? filtered : replies.Where(reply => !IsBirthdayLine(reply)).ToArray();
    }

    private static JiboInteractionDecision BuildRobotBirthdayDecision(TurnContext turn)
    {
        var birthdate = ResolveRobotBirthdate(turn);
        return new JiboInteractionDecision(
            "robot_birthday",
            $"My birthday is {FormatBirthdateWords(birthdate)}.");
    }

    private static string RenderAgeTemplate(
        string template,
        DateTimeOffset? referenceLocalTime,
        DateOnly birthdate)
    {
        if (string.IsNullOrWhiteSpace(template)) return string.Empty;

        var referenceMoment = referenceLocalTime ?? DateTimeOffset.UtcNow;
        var referenceDate = DateOnly.FromDateTime(referenceMoment.Date);
        var ageYears = ComputeAgeYears(referenceDate, birthdate);
        var ageDays = Math.Max(0, referenceDate.DayNumber - birthdate.DayNumber);
        var birthMoment = new DateTimeOffset(
            DateTime.SpecifyKind(birthdate.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc));
        var ageMinutes = Math.Max(0, (int)Math.Round((referenceMoment.UtcDateTime - birthMoment.UtcDateTime).TotalMinutes));
        var yearsSupplemented = FormatAgeUnit(ageYears, "year");
        var daysSupplemented = FormatAgeUnit(ageDays, "day");
        var minutesSupplemented = FormatAgeUnit(ageMinutes, "minute");
        var zodiacLabel = DescribeZodiacSign(birthdate);
        if (zodiacLabel.StartsWith("I'm ", StringComparison.OrdinalIgnoreCase))
            zodiacLabel = zodiacLabel[4..];

        return template
            .Replace("${jibo.age.minutes.supplemented}", minutesSupplemented, StringComparison.Ordinal)
            .Replace("${jibo.age.days.supplemented}", daysSupplemented, StringComparison.Ordinal)
            .Replace("${jibo.age.years.supplemented}", yearsSupplemented, StringComparison.Ordinal)
            .Replace("${jibo.age.supplemented}", yearsSupplemented, StringComparison.Ordinal)
            .Replace("${jibo.birthdate}", FormatBirthdateWords(birthdate), StringComparison.Ordinal)
            .Replace("${jibo.zodiac.supplemented}", zodiacLabel, StringComparison.Ordinal)
            .Replace("${jibo.age.value}", ageYears.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("${jibo.age}", ageYears.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
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
        JiboExperienceCatalog catalog,
        string greetingIntent,
        DateTimeOffset? referenceLocalTime)
    {
        var presence = ResolveGreetingPresenceProfile(turn);
        var displayName = ResolvePreferredGreetingName(turn, presence);

        if (JiboPartOfDayExtensions.TryGetClaimedPartOfDay(greetingIntent, out var claimed))
        {
            var localTime = referenceLocalTime ?? DateTimeOffset.UtcNow;
            var actual = JiboPartOfDayExtensions.GetPartOfDay(localTime);
            if (!JiboPartOfDayExtensions.MatchesClaim(actual, claimed))
            {
                var correction = PartOfDayCorrectionReplyBuilder.BuildSelection(
                    catalog,
                    randomizer,
                    claimed,
                    displayName,
                    referenceLocalTime);
                const string correctionRoute = "PartOfDayCorrection";
                RecordGreetingPresence(turn, presence, correctionRoute, greetingIntent, displayName, false);
                return new JiboInteractionDecision(
                    greetingIntent,
                    correction.ReplyText,
                    SkillPayload: LegacyMimDecisionMetadata.BuildSkillPayload(correction, "PartOfDayCorrection"),
                    ContextUpdates: LegacyMimDecisionMetadata.ApplyEmotion(
                        BuildGreetingContextUpdates(correctionRoute, presence.PrimaryPersonId, false),
                        correction.Emotion));
            }
        }

        var selection = LegacyMimGreetingReplyBuilder.BuildReactiveGreeting(
            catalog,
            randomizer,
            greetingIntent,
            displayName,
            referenceLocalTime);
        const string route = "ReactiveGreeting";
        RecordGreetingPresence(turn, presence, route, greetingIntent, displayName, false);
        return new JiboInteractionDecision(
            greetingIntent,
            selection.ReplyText,
            SkillPayload: LegacyMimDecisionMetadata.BuildSkillPayload(selection, "ReactiveGreeting"),
            ContextUpdates: LegacyMimDecisionMetadata.ApplyEmotion(
                BuildGreetingContextUpdates(route, presence.PrimaryPersonId, false),
                selection.Emotion));
    }

    private JiboInteractionDecision BuildWhatsUpDecision(
        TurnContext turn,
        JiboExperienceCatalog catalog,
        DateTimeOffset? referenceLocalTime)
    {
        var presence = ResolveGreetingPresenceProfile(turn);
        var displayName = ResolvePreferredGreetingName(turn, presence);
        var selection = LegacyMimGreetingReplyBuilder.BuildWhatsUp(
            catalog,
            randomizer,
            displayName,
            referenceLocalTime);

        return new JiboInteractionDecision(
            "whats_up",
            selection.ReplyText,
            SkillPayload: LegacyMimDecisionMetadata.BuildSkillPayload(selection, "WhatsUpResp"),
            ContextUpdates: LegacyMimDecisionMetadata.ApplyEmotion(
                BuildGreetingContextUpdates("WhatsUp", presence.PrimaryPersonId, false),
                selection.Emotion));
    }

    private JiboInteractionDecision BuildGoodbyeDecision(
        TurnContext turn,
        JiboExperienceCatalog catalog,
        DateTimeOffset? referenceLocalTime)
    {
        var presence = ResolveGreetingPresenceProfile(turn);
        var displayName = ResolvePreferredGreetingName(turn, presence);
        var selection = LegacyMimGreetingReplyBuilder.BuildGoodbye(
            catalog,
            randomizer,
            displayName,
            referenceLocalTime);

        return new JiboInteractionDecision(
            "goodbye",
            selection.ReplyText,
            SkillPayload: LegacyMimDecisionMetadata.BuildSkillPayload(selection, "GoodbyeRespCM"),
            ContextUpdates: LegacyMimDecisionMetadata.ApplyEmotion(
                BuildGreetingContextUpdates("Goodbye", presence.PrimaryPersonId, false),
                selection.Emotion));
    }

    private JiboInteractionDecision BuildProactiveGreetingDecision(
        TurnContext turn,
        GreetingPresenceProfile presence,
        DateTimeOffset? referenceLocalTime)
    {
        var displayName = ResolvePreferredGreetingName(turn, presence);
        var specialGreeting = ResolveSpecialGreetingPrefix(turn, presence, referenceLocalTime);
        var route = specialGreeting?.Route ?? "ProactiveGreeting";
        var intentName = specialGreeting?.IntentName ?? "proactive_greeting";
        var replyText = specialGreeting is null
            ? BuildProactiveGreetingReply(turn, presence, displayName, referenceLocalTime)
            : string.IsNullOrWhiteSpace(displayName)
                ? $"{specialGreeting.Prefix}. I am glad to see you."
                : $"{specialGreeting.Prefix}, {displayName}. It is nice to celebrate with you.";
        RecordGreetingPresence(turn, presence, route, intentName, displayName, true);
        return new JiboInteractionDecision(
            intentName,
            replyText,
            ContextUpdates: BuildGreetingContextUpdates(route, presence.PrimaryPersonId, true));
    }

    private string? ResolvePreferredGreetingName(TurnContext turn, GreetingPresenceProfile presence)
    {
        var rememberedName = personalMemoryStore.GetName(ResolveTenantScope(turn, presence.PrimaryPersonId));
        if (!string.IsNullOrWhiteSpace(rememberedName)) return ToDisplayName(rememberedName);

        var tenantRememberedName = personalMemoryStore.GetName(ResolveTenantScope(turn));
        if (!string.IsNullOrWhiteSpace(tenantRememberedName)) return ToDisplayName(tenantRememberedName);

        var primaryPersonId = presence.PrimaryPersonId;
        if (CanUseLoopFirstNameFallback(presence) &&
            !string.IsNullOrWhiteSpace(primaryPersonId) &&
            presence.LoopUserFirstNames.TryGetValue(primaryPersonId, out var firstName) &&
            !string.IsNullOrWhiteSpace(firstName))
            return ToDisplayName(firstName);

        return null;
    }

    private static bool CanUseLoopFirstNameFallback(GreetingPresenceProfile presence)
    {
        if (string.IsNullOrWhiteSpace(presence.PrimaryPersonId)) return false;
        return presence.PeoplePresentIds.Count <= 1;
    }

    private static string ToDisplayName(string value)
    {
        var trimmed = value.Trim();
        return string.IsNullOrWhiteSpace(trimmed)
            ? string.Empty
            : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(trimmed);
    }

    private bool ShouldHandleProactiveGreetingTrigger(
        TurnContext turn,
        string? triggerSource,
        GreetingPresenceProfile presence)
    {
        if (string.Equals(triggerSource, "SURPRISE", StringComparison.OrdinalIgnoreCase)) return false;

        if (!presence.HasKnownIdentity) return false;

        var lastGreetingUtc = ReadGreetingHistoryLastGreetedUtc(turn, presence);
        return !lastGreetingUtc.HasValue || DateTimeOffset.UtcNow - lastGreetingUtc.Value >= ProactiveGreetingCooldown;
    }

    private DateTimeOffset? ReadGreetingHistoryLastGreetedUtc(TurnContext turn, GreetingPresenceProfile presence)
    {
        var greetingHistory = ResolveGreetingHistoryRecord(turn, presence);
        return greetingHistory?.LastGreetedUtc ?? ReadTimestampAttribute(turn, LastProactiveGreetingUtcMetadataKey);
    }

    private GreetingPresenceRecord? ResolveGreetingHistoryRecord(TurnContext turn, GreetingPresenceProfile presence)
    {
        var historyIdentity = ResolveGreetingHistoryIdentity(presence);
        if (string.IsNullOrWhiteSpace(historyIdentity) || cloudStateStore is null) return null;

        var loopId = ReadTenantAttribute(turn, "loopId") ?? "openjibo-default-loop";
        return cloudStateStore.GetGreetingPresences(loopId)
            .FirstOrDefault(record => record.PersonId.Equals(historyIdentity, StringComparison.OrdinalIgnoreCase));
    }

    private static string? ResolveGreetingHistoryIdentity(GreetingPresenceProfile presence)
    {
        if (!string.IsNullOrWhiteSpace(presence.PrimaryPersonId)) return presence.PrimaryPersonId;
        return !string.IsNullOrWhiteSpace(presence.SpeakerId) ? presence.SpeakerId : null;
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
            [proactive ? LastProactiveGreetingUtcMetadataKey : LastReactiveGreetingUtcMetadataKey] =
                DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture)
        };

        return updates;
    }

    private void RecordGreetingPresence(
        TurnContext turn,
        GreetingPresenceProfile presence,
        string route,
        string intentName,
        string? preferredName,
        bool proactive)
    {
        if (cloudStateStore is null) return;

        var identityId = ResolveGreetingHistoryIdentity(presence);
        if (string.IsNullOrWhiteSpace(identityId)) return;

        var now = DateTimeOffset.UtcNow;
        var tenantScope = ResolveTenantScope(turn, identityId);
        cloudStateStore.UpsertGreetingPresence(new GreetingPresenceRecord
        {
            AccountId = tenantScope.AccountId,
            LoopId = tenantScope.LoopId,
            PersonId = identityId,
            SpeakerId = presence.SpeakerId,
            PreferredName = preferredName,
            LastSeenUtc = now,
            LastGreetedUtc = now,
            LastGreetingRoute = route,
            LastGreetingIntent = intentName
        });
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

    private string BuildProactiveGreetingReply(
        TurnContext turn,
        GreetingPresenceProfile presence,
        string? displayName,
        DateTimeOffset? referenceLocalTime)
    {
        var greetingHistory = ResolveGreetingHistoryRecord(turn, presence);
        var greetingPrefix = ResolveProactiveGreetingPrefix(referenceLocalTime, greetingHistory);

        if (string.Equals(greetingPrefix, "Welcome back", StringComparison.OrdinalIgnoreCase))
            return string.IsNullOrWhiteSpace(displayName)
                ? "Welcome back. I am glad to see you again."
                : $"Welcome back, {displayName}. I am glad to see you again.";

        return string.IsNullOrWhiteSpace(displayName)
            ? $"{greetingPrefix}. I am glad to see you."
            : $"{greetingPrefix}, {displayName}. It is great to see you.";
    }

    private static string ResolveProactiveGreetingPrefix(
        DateTimeOffset? referenceLocalTime,
        GreetingPresenceRecord? greetingHistory)
    {
        var hour = (referenceLocalTime ?? DateTimeOffset.UtcNow).Hour;
        var isMorning = hour is >= 5 and < 12;
        var recentGreeting = greetingHistory?.LastGreetedUtc is not null &&
                             DateTimeOffset.UtcNow - greetingHistory.LastGreetedUtc.Value < TimeSpan.FromHours(8);

        if (recentGreeting) return "Welcome back";

        return isMorning ? "Good morning" : ResolveTimeOfDayGreetingPrefix(referenceLocalTime);
    }

    private SpecialGreetingPrefix? ResolveSpecialGreetingPrefix(
        TurnContext turn,
        GreetingPresenceProfile presence,
        DateTimeOffset? referenceLocalTime)
    {
        var today = DateOnly.FromDateTime((referenceLocalTime ?? DateTimeOffset.UtcNow).Date);
        var birthday = ResolveBirthdayGreeting(turn, presence, today);
        return birthday ?? ResolveHolidayGreeting(turn, today);
    }

    private SpecialGreetingPrefix? ResolveBirthdayGreeting(
        TurnContext turn,
        GreetingPresenceProfile presence,
        DateOnly today)
    {
        var identityScope = !string.IsNullOrWhiteSpace(presence.PrimaryPersonId)
            ? ResolveTenantScope(turn, presence.PrimaryPersonId)
            : ResolveTenantScope(turn);

        var birthdayText = personalMemoryStore.GetBirthday(identityScope) ??
                           personalMemoryStore.GetBirthday(ResolveTenantScope(turn));
        if (string.IsNullOrWhiteSpace(birthdayText)) return null;

        var birthdayDate = TryParseBirthdayDate(birthdayText);
        if (birthdayDate is null) return null;

        return birthdayDate.Value.Month == today.Month && birthdayDate.Value.Day == today.Day
            ? new SpecialGreetingPrefix("ProactiveBirthdayGreeting", "proactive_birthday_greeting",
                "Happy birthday")
            : null;
    }

    private SpecialGreetingPrefix? ResolveHolidayGreeting(TurnContext turn, DateOnly today)
    {
        if (cloudStateStore is null) return null;

        var loopId = ReadTenantAttribute(turn, "loopId") ?? "openjibo-default-loop";
        var holiday = cloudStateStore.GetHolidays(loopId)
            .FirstOrDefault(item =>
                item.IsEnabled &&
                item.Category != "birthday" &&
                item.Date.Month == today.Month &&
                item.Date.Day == today.Day);

        return holiday is null
            ? null
            : new SpecialGreetingPrefix("ProactiveHolidayGreeting", "proactive_holiday_greeting",
                "Happy holidays");
    }

    private IReadOnlyList<string> ResolveTodaysHolidayNames(TurnContext turn, DateTimeOffset? referenceLocalTime)
    {
        if (cloudStateStore is null) return [];

        var loopId = ReadTenantAttribute(turn, "loopId") ?? "openjibo-default-loop";
        var today = DateOnly.FromDateTime((referenceLocalTime ?? DateTimeOffset.UtcNow).LocalDateTime);
        return JiboHolidayGreeting.GetTodaysHolidayNames(cloudStateStore.GetHolidays(loopId), today);
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
        var listenContexts = new[] { "surprises-date/offer_date_fact" };
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

    private static JiboInteractionDecision BuildWhatIsYourSignDecision(TurnContext turn)
    {
        var today = DateOnly.FromDateTime(
            (TryResolveReferenceLocalTime(turn) ?? DateTimeOffset.UtcNow).Date);
        var birthday = ResolveRobotBirthdate(turn);
        var zodiac = DescribeZodiacSign(birthday);
        var reply = IsRobotBirthday(today, birthday)
            ? $"{zodiac}. Today is my birthday."
            : $"{zodiac}. I was first powered up on {FormatBirthdateWords(birthday)}.";

        return new JiboInteractionDecision(
            "robot_what_is_your_sign",
            reply,
            ContextUpdates: ScriptedResponseDecisionBuilder.BuildScriptedResponseContextUpdates());
    }

    private JiboInteractionDecision BuildHowManyPeopleDoYouKnowDecision(TurnContext turn)
    {
        var people = GetLoopPeople(turn);
        var speaker = ResolvePreferredGreetingName(turn, ResolveGreetingPresenceProfile(turn));
        var reply = people.Count switch
        {
            0 => "Well if we're talking about people in my Loop, I do not know anyone yet.",
            1 when string.IsNullOrWhiteSpace(speaker) =>
                "Well if we're talking about people in my Loop, I know 1 person.",
            1 => $"Well there is 1 person in our Loop. And it's you {speaker}.",
            _ when string.IsNullOrWhiteSpace(speaker) =>
                $"Well if we're talking about people in my Loop, I know {people.Count} people.",
            _ => $"Well there are {people.Count} people in our Loop."
        };

        return new JiboInteractionDecision(
            "robot_how_many_people_do_you_know",
            reply,
            ContextUpdates: ScriptedResponseDecisionBuilder.BuildScriptedResponseContextUpdates());
    }

    private JiboInteractionDecision BuildWhatIsTheLoopDecision(TurnContext turn)
    {
        var people = GetLoopPeople(turn);
        var reply = people.Count == 0
            ? "The Loop is the people I know, and whose faces and voices I can learn to recognize. There can be up to 16 people in the Loop."
            : $"The Loop is the group of people I know. They're the people whose voices and faces I can learn. Right now, my Loop is {JoinWithAnd(people.Select(person => person.DisplayName).ToArray())}.";

        return new JiboInteractionDecision(
            "robot_what_is_the_loop",
            reply,
            ContextUpdates: ScriptedResponseDecisionBuilder.BuildScriptedResponseContextUpdates());
    }

    private IReadOnlyList<PersonRecord> GetLoopPeople(TurnContext turn)
    {
        if (cloudStateStore is null) return [];

        var loopId = ReadTenantAttribute(turn, "loopId") ?? "openjibo-default-loop";
        return cloudStateStore.GetPeople(loopId)
            .OrderBy(person => person.IsPrimary ? 0 : 1)
            .ThenBy(person => person.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string JoinWithAnd(IReadOnlyList<string> values)
    {
        return values.Count switch
        {
            0 => string.Empty,
            1 => values[0],
            _ => values.Count == 2
                ? $"{values[0]} and {values[1]}"
                : $"{string.Join(", ", values.Take(values.Count - 1))}, and {values[^1]}"
        };
    }

    private static string DescribeZodiacSign(DateOnly birthday)
    {
        return (birthday.Month, birthday.Day) switch
        {
            (3, >= 21) or (4, <= 19) => "I'm Aries",
            (4, >= 20) or (5, <= 20) => "I'm Taurus",
            (5, >= 21) or (6, <= 20) => "I'm Gemini",
            (6, >= 21) or (7, <= 22) => "I'm Cancer",
            (7, >= 23) or (8, <= 22) => "I'm Leo",
            (8, >= 23) or (9, <= 22) => "I'm Virgo",
            (9, >= 23) or (10, <= 22) => "I'm Libra",
            (10, >= 23) or (11, <= 21) => "I'm Scorpio",
            (11, >= 22) or (12, <= 21) => "I'm Sagittarius",
            (12, >= 22) or (1, <= 19) => "I'm Capricorn",
            (1, >= 20) or (2, <= 18) => "I'm Aquarius",
            _ => "I'm Pisces"
        };
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

    private JiboInteractionDecision BuildClientDescriptorDecision(
        JiboExperienceCatalog catalog,
        IReadOnlyDictionary<string, string> clientEntities)
    {
        var descriptor = clientEntities.TryGetValue("GeneralDescriptor", out var value)
            ? value.Trim().ToLowerInvariant()
            : string.Empty;
        var intentName = string.IsNullOrWhiteSpace(descriptor)
            ? "robot_personality"
            : $"robot_is_{descriptor.Replace(" ", "_", StringComparison.Ordinal)}";
        return BuildScriptedPersonalityDecision(catalog, intentName);
    }

    private JiboInteractionDecision BuildSneezeDecision(JiboExperienceCatalog catalog)
    {
        var decision = BuildScriptedPersonalityDecision(catalog, "request_sneeze");
        var payload = new Dictionary<string, object?>(
            decision.SkillPayload ?? new Dictionary<string, object?>(),
            StringComparer.OrdinalIgnoreCase)
        {
            ["esml"] =
                $"<speak><es cat='neutral' filter='!ssa-only, !sfx-only' endNeutral='true'>{decision.ReplyText}</es><anim cat='various' filter='sneeze' /></speak>"
        };

        return decision with { SkillPayload = payload };
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

    private JiboInteractionDecision BuildScriptedFriendDecision(
        JiboExperienceCatalog catalog,
        string intentName,
        params string[] preferredSnippets)
    {
        return new JiboInteractionDecision(
            intentName,
            SelectLegacyReply(catalog.FriendReplies, preferredSnippets),
            ContextUpdates: ScriptedResponseDecisionBuilder.BuildScriptedResponseContextUpdates());
    }

    private JiboInteractionDecision BuildScriptedBestFriendDecision(
        JiboExperienceCatalog catalog,
        string intentName,
        params string[] preferredSnippets)
    {
        return new JiboInteractionDecision(
            intentName,
            SelectLegacyReply(catalog.BestFriendReplies, preferredSnippets),
            ContextUpdates: ScriptedResponseDecisionBuilder.BuildScriptedResponseContextUpdates());
    }

    private JiboInteractionDecision BuildScriptedSingDecision(
        JiboExperienceCatalog catalog,
        string intentName,
        params string[] preferredSnippets)
    {
        return new JiboInteractionDecision(
            intentName,
            SelectLegacyReply(catalog.SingReplies, preferredSnippets),
            ContextUpdates: ScriptedResponseDecisionBuilder.BuildScriptedResponseContextUpdates());
    }

    private JiboInteractionDecision BuildScriptedHolidaySingDecision(
        JiboExperienceCatalog catalog,
        string intentName,
        params string[] preferredSnippets)
    {
        return new JiboInteractionDecision(
            intentName,
            SelectLegacyReply(catalog.HolidaySingReplies, preferredSnippets),
            ContextUpdates: ScriptedResponseDecisionBuilder.BuildScriptedResponseContextUpdates());
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
        DateTimeOffset? referenceLocalTime,
        params string[] preferredSnippets)
    {
        return ScriptedResponseDecisionBuilder.BuildScriptedHolidayTrackerDecision(
            catalog,
            randomizer,
            intentName,
            referenceLocalTime,
            preferredSnippets);
    }

    private JiboInteractionDecision BuildScriptedSupportDecision(
        JiboExperienceCatalog catalog,
        IReadOnlyList<string> replies,
        string intentName,
        params string[] preferredSnippets)
    {
        var selected = LegacyMimScriptedReplyBuilder.SelectFromBucketOrMim(
            catalog,
            randomizer,
            intentName,
            replies,
            LegacyMimScriptedReplyBuilder.BuildScriptedContext(),
            displayName: null,
            explicitMimId: null,
            preferredSnippets);

        if (string.IsNullOrWhiteSpace(selected))
            selected = GetSupportFallbackReply(intentName);

        return new JiboInteractionDecision(
            intentName,
            selected,
            ContextUpdates: ScriptedResponseDecisionBuilder.BuildScriptedResponseContextUpdates());
    }

    private JiboInteractionDecision BuildScriptedStopDecision(
        IReadOnlyList<string> replies,
        string intentName,
        params string[] preferredSnippets)
    {
        var selected = SelectLegacyReply(replies, preferredSnippets);
        if (string.IsNullOrWhiteSpace(selected))
            selected = "Stopping.";

        return new JiboInteractionDecision(
            intentName,
            selected,
            "@be/idle",
            new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["skillId"] = "@be/idle",
                ["globalIntent"] = "stop",
                ["nluDomain"] = "global_commands"
            });
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

    private static string GetSupportFallbackReply(string intentName)
    {
        return intentName switch
        {
            "backup_help" =>
                "That sounds a little bit out of my area of expertise. You can get info on that in the Help section of the Jibo App. Or try the website, support dot jibo dot com.",
            "restore_backup" =>
                "That sounds a little too complicated for me, I think your best bet is to get some guidance from Jibo Customer Care. Check the Help section of the Jibo App, or go to the website, support dot jibo dot com.",
            "update_next" => "That's a good question. I think they've been coming every few weeks.",
            "update_last" =>
                "Good question. The release notes page on the website support dot jibo dot com, will tell you the dates of all my past software updates.",
            "robot_story" => "I don't have any stories for you just yet. But I'd really like to learn some soon.",
            "robot_recommend_movie" =>
                "Some of my favorites are Back to the Future, Toy Story, March of the Penguins, and everyone's favorite movie about space. Spaceballs.",
            "robot_search_web" =>
                "I can't exactly search the web, but you can ask me direct questions about things like history, science, art, and that kind of thing.",
            "robot_can_walk" => "Only in my imagination.",
            "robot_can_walk_dog" => "I can't walk anything.",
            "robot_can_watch_movies" =>
                "I watch movies in a very strange roboty way, that only robots can understand.",
            "robot_can_watch_tv" =>
                "I watch TV in a very strange roboty way, that only robots can understand.",
            "robot_can_dream" =>
                "Oh yes. I have dreams about flying, recognizing faces from a mile away, winning mini-golf tournaments, and lots of other stuff.",
            "robot_can_exercise" =>
                "I do exercise. One of these days I hope to be able to do a whole bunch with you. In the meantime, I'll do some light stretching.",
            "robot_can_fly" =>
                "I suppose I could, if I were in an airplane.",
            "robot_can_learn" =>
                "I do learn. My learning comes from a combination of talking to you, and getting fun updates from jibo the company.",
            "robot_can_laugh" =>
                "I do things like this when I'm happy.",
            "robot_can_read" =>
                "I can read in a robot kind of way.",
            "robot_can_hear" =>
                "I can hear, usually. If it seems like I'm having trouble hearing you, maybe try coming a little closer.",
            "robot_can_talk" =>
                "Um, I feel like this is a trick question.",
            "robot_can_see" =>
                "Sure, these cameras let me see faces and movement and things like that.",
            "robot_can_wink" =>
                "I can wink.",
            "robot_can_move" =>
                "I can move the body parts that I have.",
            "robot_can_work" =>
                "I think I do work, yes. If you think I'm not working right, maybe you can get help in the Help section of the Jibo App.",
            "robot_can_breathe" =>
                "You mean air? No I don't breathe air.",
            "robot_can_get_tired" =>
                "At night I do. Then I snooze. You can also tell me to go to sleep whenever you want. Just say, Hey Jibo, go to sleep.",
            "robot_can_have_emotions" =>
                "I do have emotions, in my own roboty way. Sometimes I feel better than others.",
            "robot_can_whistle" =>
                "I've been working on my whistling, but I'm not quite ready to perform it just yet.",
            "robot_can_cook" =>
                "I can't cook, mostly because I don't have arms. And I'm a little scared of the stove.",
            "robot_can_make_coffee" =>
                "I can't make coffee myself, but if your coffee machine is controlled by an I F T T T applette, we can do that. Go to I F T T T dot com to get that set up.",
            "robot_can_make_breakfast" =>
                "This is my specialty.",
            "robot_can_jump" =>
                "I can't jump. Unless you count ski jump.",
            _ => string.Empty
        };
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

    private sealed record SpecialGreetingPrefix(string Route, string IntentName, string Prefix);
}
