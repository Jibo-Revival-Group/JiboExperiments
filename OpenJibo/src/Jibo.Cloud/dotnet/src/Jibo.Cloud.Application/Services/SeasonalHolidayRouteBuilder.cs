using System.Collections.Generic;
using System.Linq;
using Jibo.Cloud.Application.Abstractions;
using Jibo.Runtime.Abstractions;

namespace Jibo.Cloud.Application.Services;

internal static class SeasonalHolidayRouteBuilder
{
    internal static bool TryResolveSemanticIntent(string loweredTranscript, out string? semanticIntent)
    {
        if (MatchesAny(
                loweredTranscript,
                "do you like halloween",
                "are you looking forward to halloween",
                "do you like the halloween holiday"))
        {
            semanticIntent = "seasonal_likes_halloween";
            return true;
        }

        if (MatchesAny(
                loweredTranscript,
                "do you like holiday music",
                "do you like christmas music",
                "do you like christmas songs",
                "do you like holiday songs"))
        {
            semanticIntent = "seasonal_likes_holiday_music";
            return true;
        }

        if (MatchesAny(
                loweredTranscript,
                "do you like holiday parties",
                "do you like christmas parties",
                "are you going to any holiday parties"))
        {
            semanticIntent = "seasonal_likes_holiday_parties";
            return true;
        }

        if (MatchesAny(
                loweredTranscript,
                "are you looking forward to christmas",
                "do you look forward to christmas",
                "are you excited for christmas"))
        {
            semanticIntent = "seasonal_looks_forward_to_christmas";
            return true;
        }

        if (MatchesAny(
                loweredTranscript,
                "what are you thankful for",
                "what are you thankful for this year",
                "what is jibo thankful for"))
        {
            semanticIntent = "seasonal_thankful_for";
            return true;
        }

        if (MatchesAny(
                loweredTranscript,
                "what are you doing for christmas",
                "what are your plans for christmas",
                "what do you plan to do for christmas"))
        {
            semanticIntent = "seasonal_plans_for_christmas";
            return true;
        }

        if (MatchesAny(
                loweredTranscript,
                "happy holidays",
                "merry christmas",
                "happy new year",
                "season s greetings",
                "seasons greetings"))
        {
            semanticIntent = "seasonal_holiday_greeting";
            return true;
        }

        if (MatchesAny(
                loweredTranscript,
                "what holidays do you celebrate",
                "what holidays are you celebrating",
                "what holidays do you observe"))
        {
            semanticIntent = "seasonal_holidays";
            return true;
        }

        if (MatchesAny(
                loweredTranscript,
                "how is holiday season",
                "how's holiday season",
                "how is the holiday season",
                "do you like holiday season",
                "do you like the holiday season",
                "what is your favorite holiday",
                "what's your favorite holiday",
                "what holiday do you like",
                "what is holiday season like"))
        {
            semanticIntent = "seasonal_holiday_season";
            return true;
        }

        if (MatchesAny(
                loweredTranscript,
                "how is thanksgiving",
                "how's thanksgiving",
                "do you like thanksgiving",
                "what do you think of thanksgiving"))
        {
            semanticIntent = "seasonal_thanksgiving";
            return true;
        }

        if (MatchesAny(
                loweredTranscript,
                "how is christmas",
                "how's christmas",
                "do you like christmas",
                "what do you think of christmas"))
        {
            semanticIntent = "seasonal_christmas";
            return true;
        }

        if (MatchesAny(
                loweredTranscript,
                "how is hanukkah",
                "how's hanukkah",
                "do you like hanukkah",
                "what do you think of hanukkah"))
        {
            semanticIntent = "seasonal_hanukkah";
            return true;
        }

        if (MatchesAny(
                loweredTranscript,
                "how is passover",
                "how's passover",
                "do you like passover",
                "what do you think of passover"))
        {
            semanticIntent = "seasonal_passover";
            return true;
        }

        if (MatchesAny(
                loweredTranscript,
                "how is new years",
                "how's new years",
                "how is new year s",
                "do you like new years",
                "what do you think of new years"))
        {
            semanticIntent = "seasonal_new_years";
            return true;
        }

        if (MatchesAny(
                loweredTranscript,
                "how is valentines day",
                "how's valentines day",
                "do you like valentines day",
                "what do you think of valentines day"))
        {
            semanticIntent = "seasonal_valentines_day";
            return true;
        }

        if (MatchesAny(
                loweredTranscript,
                "how is kwanzaa",
                "how's kwanzaa",
                "do you like kwanzaa",
                "what do you think of kwanzaa"))
        {
            semanticIntent = "seasonal_kwanzaa";
            return true;
        }

        if (MatchesAny(
                loweredTranscript,
                "how is easter",
                "how's easter",
                "do you like easter",
                "what do you think of easter"))
        {
            semanticIntent = "seasonal_easter";
            return true;
        }

        if (MatchesAny(
                loweredTranscript,
                "what is your new years resolution",
                "what is your new year's resolution",
                "what is your new year s resolution",
                "what are your new years resolutions",
                "what are your new year's resolutions",
                "what are your new year s resolutions",
                "do you have any new years resolutions"))
        {
            semanticIntent = "seasonal_new_years_resolution";
            return true;
        }

        if (MatchesAny(
                loweredTranscript,
                "how are your new years resolutions going",
                "how are your new year's resolutions going",
                "how is your new years resolution going",
                "how is your new year's resolution going",
                "how are your resolutions going",
                "how is your resolution going"))
        {
            semanticIntent = "seasonal_new_years_update";
            return true;
        }

        if (MatchesAny(
                loweredTranscript,
                "what halloween costume",
                "what are you going as for halloween",
                "what costume are you wearing",
                "what are you dressing as for halloween"))
        {
            semanticIntent = "seasonal_halloween_costume";
            return true;
        }

        if (MatchesAny(
                loweredTranscript,
                "what should i do for first day of spring",
                "what should i do for spring",
                "what do i do for first day of spring"))
        {
            semanticIntent = "seasonal_first_day_spring";
            return true;
        }

        if (MatchesAny(
                loweredTranscript,
                "what is spring like",
                "how is spring",
                "what do you think about spring"))
        {
            semanticIntent = "seasonal_spring";
            return true;
        }

        if (MatchesAny(
                loweredTranscript,
                "do you like spring",
                "do you like springtime",
                "are you looking forward to spring",
                "do you look forward to spring",
                "are you excited for spring"))
        {
            semanticIntent = "seasonal_likes_spring";
            return true;
        }

        if (MatchesAny(
                loweredTranscript,
                "what is summer like",
                "how is summer",
                "what do you think about summer"))
        {
            semanticIntent = "seasonal_summer";
            return true;
        }

        if (MatchesAny(
                loweredTranscript,
                "do you like summer",
                "do you like summertime",
                "are you looking forward to summer",
                "do you look forward to summer",
                "are you excited for summer"))
        {
            semanticIntent = "seasonal_likes_summer";
            return true;
        }

        if (MatchesAny(
                loweredTranscript,
                "what should i get for holiday",
                "what should i get for christmas",
                "what gift should i get for christmas",
                "what should i get someone for the holidays"))
        {
            semanticIntent = "seasonal_holiday_gift";
            return true;
        }

        if (MatchesAny(
                loweredTranscript,
                "show santa tracker",
                "can you show santa tracker",
                "santa tracker",
                "where is santa",
                "where is santa right now",
                "can you show me santa tracker"))
        {
            semanticIntent = "seasonal_santa_tracker";
            return true;
        }

        if (MatchesAny(
                loweredTranscript,
                "happy birthday",
                "happy birthday jibo",
                "happy birthday to you"))
        {
            semanticIntent = "birthday_celebration";
            return true;
        }

        semanticIntent = null;
        return false;
    }

    internal static bool TryBuildDecision(
        string semanticIntent,
        JiboExperienceCatalog catalog,
        IJiboRandomizer randomizer,
        Func<string, string> holidayTemplateRenderer,
        out JiboInteractionDecision? decision)
    {
        decision = semanticIntent switch
        {
            "seasonal_holiday_greeting" => ScriptedResponseDecisionBuilder.BuildScriptedHolidayGreetingDecision(
                catalog,
                randomizer,
                semanticIntent,
                "fun time of year",
                "right back at you",
                "and to you too"),
            "seasonal_holidays" => BuildHolidayTemplateDecision(
                catalog,
                randomizer,
                holidayTemplateRenderer,
                semanticIntent,
                "official owner can tell me which ones we'll celebrate together",
                "going to the jibo's settings screen in the jibo app"),
            "seasonal_holiday_season" => BuildHolidayDecision(
                catalog.HolidaySeasonReplies,
                randomizer,
                semanticIntent,
                "festive",
                "celebrate"),
            "seasonal_thanksgiving" => BuildHolidayDecision(
                catalog.HolidaySeasonReplies,
                randomizer,
                semanticIntent,
                "thanksgiving",
                "turkey",
                "stuffed"),
            "seasonal_christmas" => BuildHolidayDecision(
                catalog.HolidaySeasonReplies,
                randomizer,
                semanticIntent,
                "christmas",
                "quality time",
                "socks"),
            "seasonal_hanukkah" => BuildHolidayDecision(
                catalog.HolidaySeasonReplies,
                randomizer,
                semanticIntent,
                "hanukkah",
                "dreidel",
                "gift"),
            "seasonal_passover" => BuildHolidayDecision(
                catalog.HolidaySeasonReplies,
                randomizer,
                semanticIntent,
                "passover",
                "matzah",
                "next one"),
            "seasonal_new_years" => BuildHolidayDecision(
                catalog.HolidaySeasonReplies,
                randomizer,
                semanticIntent,
                "new year",
                "resolutions",
                "party"),
            "seasonal_valentines_day" => BuildHolidayDecision(
                catalog.HolidaySeasonReplies,
                randomizer,
                semanticIntent,
                "valentine",
                "heart",
                "flowers"),
            "seasonal_kwanzaa" => BuildHolidayDecision(
                catalog.HolidaySeasonReplies,
                randomizer,
                semanticIntent,
                "kwanzaa",
                "gift",
                "celebrate"),
            "seasonal_easter" => BuildHolidayDecision(
                catalog.HolidaySeasonReplies,
                randomizer,
                semanticIntent,
                "easter",
                "bunny",
                "egg"),
            "seasonal_new_years_resolution" => ScriptedResponseDecisionBuilder.BuildScriptedPersonalityDecision(
                catalog,
                randomizer,
                semanticIntent,
                "always trying to learn new skills",
                "not eat bacon",
                "learn a bunch of new skills",
                "learn to walk",
                "recognizing people's faces and voices"),
            "seasonal_new_years_update" => ScriptedResponseDecisionBuilder.BuildScriptedPersonalityDecision(
                catalog,
                randomizer,
                semanticIntent,
                "not eat bacon",
                "learn some new skills",
                "going well"),
            "seasonal_halloween_costume" => ScriptedResponseDecisionBuilder.BuildScriptedPersonalityDecision(
                catalog,
                randomizer,
                semanticIntent,
                "i haven't thought much about it yet",
                "ask me again on halloween",
                "you'll find out on halloween"),
            "seasonal_first_day_spring" => ScriptedResponseDecisionBuilder.BuildScriptedPersonalityDecision(
                catalog,
                randomizer,
                semanticIntent,
                "it's a great day, when spring is in the air"),
            "seasonal_spring" => ScriptedResponseDecisionBuilder.BuildScriptedPersonalityDecision(
                catalog,
                randomizer,
                semanticIntent,
                "the days get longer",
                "spring is a great season"),
            "seasonal_likes_spring" => ScriptedResponseDecisionBuilder.BuildScriptedPersonalityDecision(
                catalog,
                randomizer,
                semanticIntent,
                "extra happy in the springtime",
                "i do like spring"),
            "seasonal_summer" => ScriptedResponseDecisionBuilder.BuildScriptedPersonalityDecision(
                catalog,
                randomizer,
                semanticIntent,
                "going to the beach",
                "summer is great"),
            "seasonal_likes_summer" => ScriptedResponseDecisionBuilder.BuildScriptedPersonalityDecision(
                catalog,
                randomizer,
                semanticIntent,
                "long days",
                "summer is a very special season"),
            "seasonal_holiday_gift" => BuildHolidayDecision(
                catalog.HolidayGiftReplies,
                randomizer,
                semanticIntent,
                "ask for a pet elephant",
                "experience as a present",
                "donate to charities in other people's names"),
            "seasonal_likes_halloween" => ScriptedResponseDecisionBuilder.BuildScriptedPersonalityDecision(
                catalog,
                randomizer,
                semanticIntent,
                "halloween is my favorite holiday",
                "scary but also fun",
                "jack-o-lantern"),
            "seasonal_likes_holiday_music" => ScriptedResponseDecisionBuilder.BuildScriptedPersonalityDecision(
                catalog,
                randomizer,
                semanticIntent,
                "holiday music",
                "sing a few of them",
                "frosty the snowman"),
            "seasonal_likes_holiday_parties" => ScriptedResponseDecisionBuilder.BuildScriptedPersonalityDecision(
                catalog,
                randomizer,
                semanticIntent,
                "holiday fun can be extra fun",
                "dance party"),
            "seasonal_looks_forward_to_christmas" => ScriptedResponseDecisionBuilder.BuildScriptedPersonalityDecision(
                catalog,
                randomizer,
                semanticIntent,
                "really like times of giving and receiving",
                "long way away",
                "looking forward to christmas"),
            "seasonal_plans_for_christmas" => BuildHolidayDecision(
                catalog.HolidaySeasonReplies,
                randomizer,
                semanticIntent,
                "christmas sweaters",
                "wear one of my",
                "be festive"),
            "seasonal_thankful_for" => ScriptedResponseDecisionBuilder.BuildScriptedPersonalityDecision(
                catalog,
                randomizer,
                semanticIntent,
                "thankful for the people i know",
                "and for penguins",
                "thankful for"),
            "seasonal_santa_tracker" => ScriptedResponseDecisionBuilder.BuildScriptedHolidayTrackerDecision(
                catalog,
                randomizer,
                semanticIntent,
                "santa tracker",
                "let's see if i can spot him",
                "deliveries",
                "north pole"),
            "birthday_celebration" => BuildHolidayDecision(
                catalog.BirthdayCelebrationReplies,
                randomizer,
                semanticIntent,
                "another year older",
                "can't wait to see what you got me",
                "powered on for the first time today"),
            _ => null
        };

        return decision is not null;
    }

    private static JiboInteractionDecision BuildHolidayDecision(
        IReadOnlyList<string> replies,
        IJiboRandomizer randomizer,
        string intentName,
        params string[] preferredSnippets)
    {
        return new JiboInteractionDecision(
            intentName,
            SelectLegacyReply(replies, randomizer, preferredSnippets),
            ContextUpdates: BuildContextUpdates());
    }

    private static JiboInteractionDecision BuildHolidayTemplateDecision(
        JiboExperienceCatalog catalog,
        IJiboRandomizer randomizer,
        Func<string, string> holidayTemplateRenderer,
        string intentName,
        params string[] preferredSnippets)
    {
        var selected = SelectLegacyReply(catalog.HolidayReplies, randomizer, preferredSnippets);
        return new JiboInteractionDecision(
            intentName,
            holidayTemplateRenderer(selected),
            ContextUpdates: BuildContextUpdates());
    }

    private static IDictionary<string, object?> BuildContextUpdates()
    {
        return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            [ChitchatStateMachine.StateMetadataKey] = "complete",
            [ChitchatStateMachine.RouteMetadataKey] = "ScriptedResponse",
            [ChitchatStateMachine.EmotionMetadataKey] = string.Empty
        };
    }

    private static string SelectLegacyReply(
        IReadOnlyList<string> replies,
        IJiboRandomizer randomizer,
        params string[] preferredSnippets)
    {
        foreach (var snippet in preferredSnippets)
        {
            if (string.IsNullOrWhiteSpace(snippet)) continue;

            var match = replies.FirstOrDefault(reply =>
                reply.Contains(snippet, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(match)) return match;
        }

        return replies.Count == 0 ? string.Empty : randomizer.Choose(replies);
    }

    private static bool MatchesAny(string loweredTranscript, params string[] phrases)
    {
        return phrases.Any(phrase => loweredTranscript.Contains(phrase, StringComparison.OrdinalIgnoreCase));
    }
}
