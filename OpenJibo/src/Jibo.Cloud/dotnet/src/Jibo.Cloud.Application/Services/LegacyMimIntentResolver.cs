using Jibo.Cloud.Application.Abstractions;

namespace Jibo.Cloud.Application.Services;

internal static class LegacyMimIntentResolver
{
    private static readonly Dictionary<string, string> ExplicitMimIds =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["robot_identity"] = "JBO_WhoAreYou",
            ["robot_likes_being_jibo"] = "JBO_DoYouLikeBeingJibo",
            ["robot_what_do_you_like_to_do"] = "JBO_WhatDoYouLikeToDo",
            ["robot_what_are_you_made_of"] = "JBO_WhatYouMadeOf",
            ["robot_how_do_you_work"] = "JBO_HowDoYouWork",
            ["robot_age"] = "JBO_HowOldAreYou",
            ["robot_how_old_are_you"] = "JBO_HowOldAreYou",
            ["robot_birthday"] = "JBO_WhenWereYouBorn",
            ["robot_name"] = "JBO_WhatsYourName",
            ["robot_taxes"] = "JBO_DoYouPayTaxes",
            ["robot_job"] = "JBO_WhatIsYourJob",
            ["robot_origin_created"] = "JBO_WhoMadeYou",
            ["robot_origin_from"] = "JBO_WhereAreYouFrom",
            ["robot_where_do_you_live"] = "JBO_WhereDoYouLive",
            ["robot_where_were_you_born"] = "JBO_WhereWereYouBorn",
            ["robot_what_do_you_eat"] = "JBO_WhatDoYouEat",
            ["robot_what_do_you_drink"] = "JBO_WhatDoYouDrink",
            ["robot_what_is_your_iq"] = "JBO_WhatIsYourIQ",
            ["robot_what_is_be_a_maker"] = "JBO_WhatIsBeAMaker",
            ["robot_why_no_arms"] = "JBO_WhyNoArms",
            ["robot_why_only_one_eye"] = "JBO_WhyOnlyOneEye",
            ["robot_is_big_brother_watching"] = "JBO_IsBigBrotherWatching",
            ["robot_what_you_had_dinner_yesterday"] = "JBO_WhatYouHaveDinnerYesterday",
            ["robot_who_is_your_boss"] = "JBO_WhoIsYourBoss",
            ["robot_what_robots_look_up_to"] = "JBO_WhatRobotsLookUpTo",
            ["robot_what_is_groundhogs_name"] = "JBO_WhatIsYourGroundhogsName",
            ["robot_can_play_guitar"] = "RI_JBO_CanPlayGuitar",
            ["robot_can_swim"] = "RI_JBO_CanSwim",
            ["robot_can_cry"] = "RI_JBO_CanCry",
            ["robot_can_speak_language"] = "RI_JBO_CanSpeak_SS_Language",
            ["robot_likes_color"] = "RI_JBO_Likes_SS_Color",
            ["robot_likes_food_general"] = "RI_JBO_Likes_SS_FoodGeneral",
            ["robot_can_tell_people_apart"] = "RI_JBO_CanTellPeopleApart",
            ["robot_how_can_recognize_faces"] = "RI_JBO_HowCanRecognizeFaces",
            ["robot_can_read_lips"] = "RI_JBO_CanReadLips",
            ["robot_can_do_household_chore"] = "RI_JBO_CanDoHouseholdChore",
            ["robot_favorite_star_wars_character"] = "RI_JBO_HasFavoriteStarWarsCharacter",
            ["robot_favorite_star_wars_movie"] = "RI_JBO_HasFavoriteStarWarsMovie",
            ["robot_favorite_holiday"] = "RI_JBO_HasFavoriteHoliday",
            ["robot_what_is_your_favorite"] = "KU_WhatIsYourFavorite",
            ["robot_who_is_your_favorite"] = "KU_WhoIsYourFavorite",
            ["robot_did_you_have_a_favorite"] = "KU_WhatIsYourFavorite",
            ["robot_why_is_thing_favorite"] = "KU_WhyDoYouThinkThing",
            ["robot_favorite_team_should_be"] = "KU_FavoriteTeamShouldBe",
            ["robot_is_ready"] = "RI_JBO_IsReady",
            ["robot_is_ready_for_day"] = "RI_JBO_IsReadyForDay",
            ["user_is_ready"] = "RN_IAmReady",
            ["robot_what_kind_of_robot"] = "JBO_WhatKindOfRobotAreYou",
            ["robot_when_do_you_sleep"] = "JBO_WhenDoYouSleep",
            ["robot_is_awake"] = "RN_IsJiboAwake",
            ["robot_is_connected_to_internet"] = "SUP_GEN_AreYouConnectedToInternet",
            ["robot_is_personal_assistant"] = "RI_JBO_IsPersonalAssistant",
            ["robot_is_user_relation"] = "KU_AreYouMy",
            ["robot_is_related_to_alexa"] = "RI_JBO_IsRelatedToAlexa",
            ["robot_is_related_to_cortana"] = "RI_JBO_IsRelatedToCortana",
            ["robot_is_related_to_google_home"] = "RI_JBO_IsRelatedToGoogleHome",
            ["robot_is_related_to_siri"] = "RI_JBO_IsRelatedToSiri",
            ["robot_is_skilled_at_math"] = "RI_JBO_IsSkilledAtMath",
            ["robot_is_skilled_at_thing"] = "RI_JBO_IsSkilledAt_SS_SchoolSubject",
            ["robot_can_get_lonely"] = "RI_JBO_CanGetLonely",
            ["robot_can_get_depressed"] = "RI_JBO_CanGetDepressed",
            ["robot_can_get_cold"] = "RI_JBO_CanGetCold",
            ["robot_can_get_hungry"] = "RI_JBO_CanGetHungry",
            ["robot_can_get_tired"] = "RI_JBO_CanGetTired",
            ["robot_can_get_hot"] = "RI_JBO_CanGetHot",
            ["robot_is_boy_or_girl"] = "CCAreYouBoyOrGirl",
            ["robot_is_growing_mustache"] = "RI_JBO_IsGrowingMustache",
            ["robot_is_spy"] = "RI_JBO_IsSpy",
            ["robot_is_ticklish"] = "RI_JBO_IsTicklish",
            ["robot_is_naughty_or_nice"] = "RI_JBO_IsNaughtyOrNice",
            ["robot_is_on_tv"] = "RI_JBO_IsOnTV",
            ["robot_is_going_to_holiday_party"] = "RI_JBO_IsGoingToHolidayParty",
            ["robot_native_language"] = "RI_JBO_NativeLanguage",
            ["robot_how_can_recognize_voices"] = "RI_JBO_HowCanRecognizeVoices",
            ["robot_what_games"] = "RI_JBO_WhatGames",
            ["robot_what_sees_through_telescope"] = "RI_JBO_WhatSeesThroughTelescope",
            ["robot_what_wants_to_do"] = "RI_JBO_WhatWantsToDo",
            ["robot_when_wakes_up"] = "RI_JBO_WhenWakesUp",
            ["robot_what_halloween_costume"] = "RI_JBO_WhatHalloweenCostume",
            ["robot_on_time_magazine_cover"] = "OI_JBO_OnTimeMagazineCover",
            ["robot_saw_santa"] = "RI_JBO_SawSanta",
            ["robot_received_christmas_gift_from_santa"] = "RI_JBO_ReceivedChristmasGiftFromSanta",
            ["robot_who_is_your_master"] = "JBO_WhoIsYourMaster",
            ["robot_will_you_die"] = "JBO_WillYouDie",
            ["robot_will_you_go_on_date"] = "JBO_WillYouGoOnDate",
            ["robot_you_dont_understand_anything"] = "JBO_YouDontUnderstandAnything",
            ["robot_why_cannot_work"] = "RI_JBO_WhyCannotWork",
            ["robot_likes_blue"] = "RI_JBO_LikesBlue",
            ["robot_likes_pink"] = "RI_JBO_LikesPink",
            ["robot_has_flu"] = "RI_JBO_HasFlu",
            ["robot_has_ifttt_skill"] = "RI_JBO_HasIFTTTSkill",
            ["robot_how_get_help"] = "RI_JBO_HowGetHelp",
            ["robot_how_was_sleep"] = "RI_JBO_HowWasSleep",
            ["robot_is_asleep"] = "RI_JBO_IsAsleep",
            ["robot_has_kids"] = "RI_JBO_HasKids",
            ["robot_likes_2017"] = "RI_JBO_Likes2017",
            ["robot_have_you_been_in_love"] = "JBO_HaveYouBeenInLove",
            ["robot_is_climate_change_real"] = "JBO_IsClimateChangeReal",
            ["robot_know_about_tv"] = "JBO_KnowAboutTV",
            ["robot_should_i_shut_you_down"] = "JBO_ShouldIShutYouDown",
            ["robot_this_is_your_home"] = "CCThisIsYourHome",
            ["robot_welcome_to_family"] = "JBO_WelcomeToFamily",
            ["robot_why_are_you_sound_like_girl"] = "JBO_WhyAreYouSoundLikeGirl",
            ["robot_why_name_jibo"] = "CCWhyNameJibo",
            ["robot_your_eye_is_nice"] = "JBO_YourEyeIsNice",
            ["request_sneeze"] = "RA_JBO_Sneeze",
            ["robot_favorite_tv_show"] = "RI_JBO_HasFavoriteTVShow",
            ["robot_favorite_color"] = "RI_JBO_HasFavoriteColor",
            ["robot_favorite_animal"] = "RI_JBO_HasFavoriteAnimal",
            ["robot_favorite_bird"] = "RI_JBO_HasFavoriteBird",
            ["robot_likes_penguins"] = "RI_JBO_LikesPenguins",
            ["robot_likes_dogs"] = "RI_JBO_LikesDogs",
            ["robot_likes_cats"] = "RI_JBO_LikesCats",
            ["robot_likes_whales"] = "RI_JBO_LikesWhales",
            ["robot_likes_animals"] = "RI_JBO_LikesAnimals",
            ["robot_story"] = "RA_JBO_Story",
            ["update_next"] = "SUP_UPDATE_WhenIsNextUpdate",
            ["update_last"] = "SUP_UPDATE_WhenWasLastUpdate",
            ["robot_recommend_movie"] = "RA_JBO_RecommendMovie",
            ["robot_search_web"] = "RA_JBO_SearchWeb",
            ["robot_sing"] = "RA_JBO_Sing",
            ["robot_sing_christmas_song"] = "RA_JBO_SingChristmasSongUnknown",
            ["robot_joke"] = "RA_JBO_TellAJoke",
            ["robot_fact"] = "RA_JBO_TellRobotFact",
            ["robot_fun_fact"] = "RA_JBO_TellSomething",
            ["hello"] = "RN_Hello",
            ["how_are_you"] = "RN_WhatAreYouFeeling"
        };

    internal static IReadOnlyList<JiboConditionedReply>? TryResolveReplies(
        JiboExperienceCatalog catalog,
        string intentName,
        string? explicitMimId = null)
    {
        if (!string.IsNullOrWhiteSpace(explicitMimId) &&
            catalog.MimReplies.TryGetValue(explicitMimId, out var explicitReplies) &&
            explicitReplies.Count > 0)
            return explicitReplies;

        foreach (var candidate in GenerateMimIdCandidates(intentName))
        {
            if (catalog.MimReplies.TryGetValue(candidate, out var replies) && replies.Count > 0)
                return replies;
        }

        return TryFuzzyMatch(catalog, intentName);
    }

    private static IEnumerable<string> GenerateMimIdCandidates(string intentName)
    {
        if (ExplicitMimIds.TryGetValue(intentName, out var explicitId))
        {
            yield return explicitId;
            yield break;
        }

        if (!intentName.StartsWith("robot_", StringComparison.OrdinalIgnoreCase)) yield break;

        var remainder = intentName["robot_".Length..];

        if (remainder.StartsWith("favorite_", StringComparison.OrdinalIgnoreCase))
        {
            var suffix = ToPascalCase(remainder["favorite_".Length..]);
            yield return $"RI_JBO_HasFavorite{suffix}";
            yield return $"RI_JBO_HasFavorite{NormalizeAcronymSuffix(suffix)}";
        }

        if (remainder.StartsWith("least_favorite_", StringComparison.OrdinalIgnoreCase))
        {
            var suffix = ToPascalCase(remainder["least_favorite_".Length..]);
            yield return $"RI_JBO_HasLeastFavorite{suffix}";
            yield return $"RI_JBO_HasLeastFavorite{NormalizeAcronymSuffix(suffix)}";
        }

        if (remainder.StartsWith("likes_", StringComparison.OrdinalIgnoreCase))
        {
            var suffix = ToPascalCase(remainder["likes_".Length..]);
            yield return $"RI_JBO_Likes{suffix}";
        }

        if (remainder.StartsWith("can_", StringComparison.OrdinalIgnoreCase))
        {
            var suffix = ToPascalCase(remainder["can_".Length..]);
            yield return $"RI_JBO_Can{suffix}";
        }

        if (remainder.StartsWith("what_", StringComparison.OrdinalIgnoreCase) ||
            remainder.StartsWith("who_", StringComparison.OrdinalIgnoreCase) ||
            remainder.StartsWith("how_", StringComparison.OrdinalIgnoreCase) ||
            remainder.StartsWith("where_", StringComparison.OrdinalIgnoreCase) ||
            remainder.StartsWith("when_", StringComparison.OrdinalIgnoreCase) ||
            remainder.StartsWith("why_", StringComparison.OrdinalIgnoreCase))
        {
            yield return $"JBO_{ToPascalCase(remainder)}";
        }

        yield return $"RI_JBO_{ToPascalCase(remainder)}";
        yield return $"JBO_{ToPascalCase(remainder)}";
        yield return $"RA_JBO_{ToPascalCase(remainder)}";
    }

    private static IReadOnlyList<JiboConditionedReply>? TryFuzzyMatch(
        JiboExperienceCatalog catalog,
        string intentName)
    {
        var tokens = intentName
            .Replace("robot_", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Split('_', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (tokens.Length == 0) return null;

        string? bestKey = null;
        var bestScore = 0;

        foreach (var key in catalog.MimReplies.Keys)
        {
            var score = tokens.Count(token => key.Contains(token, StringComparison.OrdinalIgnoreCase));
            if (score <= bestScore || score < Math.Min(2, tokens.Length)) continue;

            bestScore = score;
            bestKey = key;
        }

        return bestKey is not null && catalog.MimReplies.TryGetValue(bestKey, out var replies) && replies.Count > 0
            ? replies
            : null;
    }

    private static string ToPascalCase(string snakeCase)
    {
        return string.Concat(
            snakeCase.Split('_', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(part => part.Length == 0
                    ? string.Empty
                    : char.ToUpperInvariant(part[0]) + part[1..]));
    }

    private static string NormalizeAcronymSuffix(string suffix) =>
        suffix
            .Replace("Tv", "TV", StringComparison.Ordinal)
            .Replace("Ncaa", "NCAA", StringComparison.Ordinal)
            .Replace("Nfl", "NFL", StringComparison.Ordinal)
            .Replace("Nba", "NBA", StringComparison.Ordinal);
}
