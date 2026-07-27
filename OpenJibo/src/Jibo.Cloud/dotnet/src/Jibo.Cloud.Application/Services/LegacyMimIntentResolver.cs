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
            ["robot_what_are_you_made_of"] = "JBO_WhatAreYou",
            ["robot_how_do_you_work"] = "JBO_HowDoYouWork",
            ["robot_age"] = "JBO_HowOldAreYou",
            ["robot_birthday"] = "JBO_WhenWereYouBorn",
            ["robot_name"] = "JBO_WhatsYourName",
            ["robot_taxes"] = "JBO_DoYouPayTaxes",
            ["robot_job"] = "JBO_DoYouLikeBeingJibo",
            ["robot_origin_created"] = "JBO_WhatAreYou",
            ["robot_origin_from"] = "JBO_WhereDoYouLive",
            ["robot_where_do_you_live"] = "JBO_WhereDoYouLive",
            ["robot_where_were_you_born"] = "JBO_WhenWereYouBorn",
            ["robot_what_do_you_eat"] = "JBO_WhatDoYouLikeToDo",
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
