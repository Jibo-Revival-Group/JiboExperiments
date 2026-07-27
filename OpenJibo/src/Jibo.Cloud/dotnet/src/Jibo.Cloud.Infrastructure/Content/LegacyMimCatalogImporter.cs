using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Jibo.Cloud.Application.Abstractions;
using Jibo.Cloud.Application.Services;

// ReSharper disable UnusedMember.Local

namespace Jibo.Cloud.Infrastructure.Content;

public static class LegacyMimCatalogImporter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true
    };

    private static readonly Regex LegacyMarkupPattern = new(
        "<[^>]+>",
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

            var bucket = ResolveBucket(filePath) ?? ResolveBucketFromMimId(definition.MimId);
            if (bucket is null) continue;

            foreach (var prompt in definition.Prompts)
            {
                var preservePlaceholders = IsSpeakerTemplateBucket(bucket.Value);
                var preserveTtsMarkup = PreservesTtsMarkup(bucket.Value);
                var normalizedPrompt = LegacyMimPromptNormalizer.Normalize(
                    prompt.Prompt,
                    preservePlaceholders,
                    preserveTtsMarkup);
                if (string.IsNullOrWhiteSpace(normalizedPrompt.Text)) continue;

                var condition = NormalizeImportedCondition(prompt.Condition, filePath, bucket.Value);
                var mimId = prompt.MimId ?? definition.MimId;
                var emotion = normalizedPrompt.Emotion;

                builder.Add(
                    bucket.Value,
                    condition,
                    normalizedPrompt.Text,
                    prompt.Weight,
                    mimId,
                    prompt.PromptId,
                    emotion);
            }
        }

        return builder.Build();
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

        if (fileName.StartsWith("RA_JBO_SingChristmasSongUnknown", StringComparison.OrdinalIgnoreCase))
            return LegacyMimBucket.HolidaySing;

        if (fileName.StartsWith("RA_JBO_Sing", StringComparison.OrdinalIgnoreCase))
            return LegacyMimBucket.Sing;

        if (fileName.StartsWith("RA_JBO_TellRobotFact", StringComparison.OrdinalIgnoreCase))
            return LegacyMimBucket.RobotFacts;

        if (fileName.StartsWith("RA_JBO_Shuffle", StringComparison.OrdinalIgnoreCase) ||
            fileName.StartsWith("RA_JBO_TellSomething", StringComparison.OrdinalIgnoreCase))
            return LegacyMimBucket.FunFactSource;

        if (fileName.StartsWith("RA_JBO_ShowSantaTracker", StringComparison.OrdinalIgnoreCase))
            return LegacyMimBucket.HolidayTracker;

        if (fileName.StartsWith("RA_JBO_Story", StringComparison.OrdinalIgnoreCase))
            return LegacyMimBucket.Story;

        if (fileName.StartsWith("RA_JBO_RecommendMovie", StringComparison.OrdinalIgnoreCase))
            return LegacyMimBucket.RecommendMovie;

        if (fileName.StartsWith("RA_JBO_SearchWeb", StringComparison.OrdinalIgnoreCase))
            return LegacyMimBucket.SearchWeb;

        if (fileName.StartsWith("RI_JBO_CelebratesBlackHistoryMonth", StringComparison.OrdinalIgnoreCase) ||
            fileName.StartsWith("RI_JBO_LikesBlackHistoryMonth", StringComparison.OrdinalIgnoreCase) ||
            fileName.StartsWith("RI_JBO_LooksForwardToBlackHistoryMonth", StringComparison.OrdinalIgnoreCase) ||
            fileName.StartsWith("RI_JBO_PlansForBlackHistoryMonth", StringComparison.OrdinalIgnoreCase) ||
            fileName.StartsWith("RI_JBO_HasOpinionAboutBlackHistoryMonth", StringComparison.OrdinalIgnoreCase) ||
            fileName.StartsWith("RI_JBO_HowIsBlackHistoryMonth", StringComparison.OrdinalIgnoreCase) ||
            fileName.StartsWith("RI_USR_WhatShouldDoForBlackHistoryMonth", StringComparison.OrdinalIgnoreCase))
            return LegacyMimBucket.BlackHistoryMonth;

        if (fileName.StartsWith("RA_JBO_TellBlackHistoryMonthFact", StringComparison.OrdinalIgnoreCase))
            return LegacyMimBucket.BlackHistoryMonthFact;

        if (normalizedPath.Contains("/emotion-responses/", StringComparison.OrdinalIgnoreCase) ||
            normalizedPath.Contains("/gqa-responses/", StringComparison.OrdinalIgnoreCase))
            return LegacyMimBucket.Emotion;

        if (fileName.StartsWith("JBO_WhatHolidaysDoYouCelebrate", StringComparison.OrdinalIgnoreCase))
            return LegacyMimBucket.Holiday;

        if (fileName.StartsWith("RI_JBO_HasFavoriteHoliday", StringComparison.OrdinalIgnoreCase) ||
            IsHolidaySeasonFile(fileName))
            return LegacyMimBucket.HolidaySeason;

        if (fileName.StartsWith("RI_JBO_HasFavoriteAnimal", StringComparison.OrdinalIgnoreCase) ||
            fileName.StartsWith("RI_JBO_HasFavoriteBird", StringComparison.OrdinalIgnoreCase) ||
            fileName.StartsWith("RI_JBO_LikesPenguins", StringComparison.OrdinalIgnoreCase) ||
            fileName.StartsWith("RI_JBO_LikesDogs", StringComparison.OrdinalIgnoreCase) ||
            fileName.StartsWith("RI_JBO_LikesCats", StringComparison.OrdinalIgnoreCase) ||
            fileName.StartsWith("RI_JBO_LikesWhales", StringComparison.OrdinalIgnoreCase) ||
            fileName.StartsWith("RI_JBO_LikesAnimals", StringComparison.OrdinalIgnoreCase))
            return LegacyMimBucket.FavoriteAnimal;

        if (fileName.StartsWith("RI_JBO_HasFriends", StringComparison.OrdinalIgnoreCase) ||
            fileName.StartsWith("RI_JBO_IsFriendsWithUser", StringComparison.OrdinalIgnoreCase) ||
            fileName.StartsWith("RI_JBO_IsFriendsWithLM", StringComparison.OrdinalIgnoreCase) ||
            fileName.StartsWith("RI_JBO_IsFriendsWithNonLM", StringComparison.OrdinalIgnoreCase) ||
            fileName.StartsWith("RI_JBO_IsFriendsWithToaster", StringComparison.OrdinalIgnoreCase))
            return LegacyMimBucket.Friend;

        if (fileName.StartsWith("RI_JBO_IsBestFriendsWithUser", StringComparison.OrdinalIgnoreCase))
            return LegacyMimBucket.BestFriend;

        if (fileName.StartsWith("RN_HappyHolidays", StringComparison.OrdinalIgnoreCase))
            return LegacyMimBucket.HolidayGreeting;

        if (fileName.StartsWith("RI_USR_WhatShouldGetForHoliday", StringComparison.OrdinalIgnoreCase))
            return LegacyMimBucket.HolidayGift;

        if (fileName.StartsWith("RN_HappyBirthdayToJibo", StringComparison.OrdinalIgnoreCase) ||
            fileName.StartsWith("OI_USR_CelebratesLoopMemberAskedAboutBirthday", StringComparison.OrdinalIgnoreCase) ||
            fileName.StartsWith("OI_USR_CelebratesJiboBirthday", StringComparison.OrdinalIgnoreCase) ||
            fileName.StartsWith("RI_JBO_CelebratesLoopMemberAskedAboutBirthday", StringComparison.OrdinalIgnoreCase) ||
            fileName.StartsWith("RI_JBO_CelebratesSpeakerBirthday", StringComparison.OrdinalIgnoreCase) ||
            fileName.StartsWith("RI_JBO_CelebratesJiboBirthday", StringComparison.OrdinalIgnoreCase))
            return LegacyMimBucket.BirthdayCelebration;

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

        if (fileName.StartsWith("CalendarServiceDown", StringComparison.OrdinalIgnoreCase))
            return LegacyMimBucket.CalendarServiceDown;

        if (fileName.StartsWith("CalendarOutro", StringComparison.OrdinalIgnoreCase))
            return LegacyMimBucket.CalendarOutro;

        if (fileName.StartsWith("CommuteAppSetup", StringComparison.OrdinalIgnoreCase))
            return LegacyMimBucket.CommuteAppSetup;

        if (fileName.StartsWith("CommuteConfirmSpeaker", StringComparison.OrdinalIgnoreCase))
            return LegacyMimBucket.CommuteConfirmSpeaker;

        if (fileName.StartsWith("CommuteNow", StringComparison.OrdinalIgnoreCase)) return LegacyMimBucket.CommuteNow;

        if (fileName.StartsWith("CommuteMinutesLeft", StringComparison.OrdinalIgnoreCase))
            return LegacyMimBucket.CommuteMinutesLeft;

        if (fileName.StartsWith("CommuteDepartTimeNormal", StringComparison.OrdinalIgnoreCase))
            return LegacyMimBucket.CommuteDepartTimeNormal;

        if (fileName.StartsWith("CommuteDepartTimeNotNormal", StringComparison.OrdinalIgnoreCase))
            return LegacyMimBucket.CommuteDepartTimeNotNormal;

        if (fileName.StartsWith("CommuteDriveNormal", StringComparison.OrdinalIgnoreCase))
            return LegacyMimBucket.CommuteDriveNormal;

        if (fileName.StartsWith("CommuteDriveLate", StringComparison.OrdinalIgnoreCase))
            return LegacyMimBucket.CommuteDriveLate;

        if (fileName.StartsWith("CommuteDriveHurry", StringComparison.OrdinalIgnoreCase))
            return LegacyMimBucket.CommuteDriveHurry;

        if (fileName.StartsWith("CommuteDrivePoor", StringComparison.OrdinalIgnoreCase))
            return LegacyMimBucket.CommuteDrivePoor;

        if (fileName.StartsWith("CommuteDriveTerrible", StringComparison.OrdinalIgnoreCase))
            return LegacyMimBucket.CommuteDriveTerrible;

        if (fileName.StartsWith("CommuteTransportNormal", StringComparison.OrdinalIgnoreCase))
            return LegacyMimBucket.CommuteTransportNormal;

        if (fileName.StartsWith("CommuteTransportLate", StringComparison.OrdinalIgnoreCase))
            return LegacyMimBucket.CommuteTransportLate;

        if (fileName.StartsWith("CommuteTransportHurry", StringComparison.OrdinalIgnoreCase))
            return LegacyMimBucket.CommuteTransportHurry;

        if (fileName.StartsWith("CommuteServiceDown", StringComparison.OrdinalIgnoreCase))
            return LegacyMimBucket.CommuteServiceDown;

        if (fileName.StartsWith("NewsIntroCategory", StringComparison.OrdinalIgnoreCase))
            return LegacyMimBucket.NewsCategoryIntro;

        if (fileName.StartsWith("NewsIntro", StringComparison.OrdinalIgnoreCase)) return LegacyMimBucket.NewsIntro;

        if (fileName.StartsWith("NewsOutro", StringComparison.OrdinalIgnoreCase)) return LegacyMimBucket.NewsOutro;

        if (fileName.StartsWith("Weather", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(fileName, "WetNowDryLater", StringComparison.OrdinalIgnoreCase))
            return LegacyMimBucket.ReportSkillTemplate;

        if (fileName.StartsWith("SUP_GEN_HowBackUpData", StringComparison.OrdinalIgnoreCase))
            return LegacyMimBucket.BackupHow;

        if (fileName.StartsWith("SUP_GEN_HowRestoreBackup", StringComparison.OrdinalIgnoreCase))
            return LegacyMimBucket.RestoreHow;

        if (fileName.StartsWith("SUP_UPDATE_WhenIsNextUpdate", StringComparison.OrdinalIgnoreCase))
            return LegacyMimBucket.UpdateNext;

        if (fileName.StartsWith("SUP_UPDATE_WhenWasLastUpdate", StringComparison.OrdinalIgnoreCase))
            return LegacyMimBucket.UpdateLast;

        if (fileName.StartsWith("RA_JBO_StopMoving", StringComparison.OrdinalIgnoreCase))
            return LegacyMimBucket.StopMoving;

        if (fileName.StartsWith("RA_JBO_StopMakingThatNoise", StringComparison.OrdinalIgnoreCase))
            return LegacyMimBucket.StopMakingThatNoise;

        if (fileName.StartsWith("RA_JBO_StopIgnoringMe", StringComparison.OrdinalIgnoreCase))
            return LegacyMimBucket.StopIgnoringMe;

        if (fileName.StartsWith("RA_JBO_StopStaring", StringComparison.OrdinalIgnoreCase))
            return LegacyMimBucket.StopStaring;

        if (fileName.StartsWith("RI_JBO_CanWalkDog", StringComparison.OrdinalIgnoreCase))
            return LegacyMimBucket.CanWalkDog;

        if (fileName.StartsWith("RI_JBO_CanWalk", StringComparison.OrdinalIgnoreCase))
            return LegacyMimBucket.CanWalk;

        if (fileName.StartsWith("RI_JBO_CanWatchMovies", StringComparison.OrdinalIgnoreCase))
            return LegacyMimBucket.CanWatchMovies;

        if (fileName.StartsWith("RI_JBO_CanWatchTV", StringComparison.OrdinalIgnoreCase))
            return LegacyMimBucket.CanWatchTV;

        if (fileName.StartsWith("RI_JBO_CanDream", StringComparison.OrdinalIgnoreCase))
            return LegacyMimBucket.CanDream;

        if (fileName.StartsWith("RI_JBO_CanExercise", StringComparison.OrdinalIgnoreCase))
            return LegacyMimBucket.CanExercise;

        if (fileName.StartsWith("RI_JBO_CanFly", StringComparison.OrdinalIgnoreCase))
            return LegacyMimBucket.CanFly;

        if (fileName.StartsWith("RI_JBO_CanLearn", StringComparison.OrdinalIgnoreCase))
            return LegacyMimBucket.CanLearn;

        if (fileName.StartsWith("RI_JBO_CanLaugh", StringComparison.OrdinalIgnoreCase))
            return LegacyMimBucket.CanLaugh;

        if (fileName.StartsWith("RI_JBO_CanRead", StringComparison.OrdinalIgnoreCase))
            return LegacyMimBucket.CanRead;

        if (fileName.StartsWith("RI_JBO_CanHear", StringComparison.OrdinalIgnoreCase))
            return LegacyMimBucket.CanHear;

        if (fileName.StartsWith("RI_JBO_CanTalk", StringComparison.OrdinalIgnoreCase))
            return LegacyMimBucket.CanTalk;

        if (fileName.StartsWith("RI_JBO_CanSee", StringComparison.OrdinalIgnoreCase))
            return LegacyMimBucket.CanSee;

        if (fileName.StartsWith("RI_JBO_CanWink", StringComparison.OrdinalIgnoreCase))
            return LegacyMimBucket.CanWink;

        if (fileName.StartsWith("RI_JBO_CanMove", StringComparison.OrdinalIgnoreCase))
            return LegacyMimBucket.CanMove;

        if (fileName.StartsWith("RI_JBO_CanWork", StringComparison.OrdinalIgnoreCase))
            return LegacyMimBucket.CanWork;

        if (fileName.StartsWith("RI_JBO_CanBreathe", StringComparison.OrdinalIgnoreCase))
            return LegacyMimBucket.CanBreathe;

        if (fileName.StartsWith("RI_JBO_CanGetTired", StringComparison.OrdinalIgnoreCase))
            return LegacyMimBucket.CanGetTired;

        if (fileName.StartsWith("RI_JBO_CanHaveEmotions", StringComparison.OrdinalIgnoreCase))
            return LegacyMimBucket.CanHaveEmotions;

        if (fileName.StartsWith("RI_JBO_CanWhistle", StringComparison.OrdinalIgnoreCase))
            return LegacyMimBucket.CanWhistle;

        if (fileName.StartsWith("RI_JBO_CanCook", StringComparison.OrdinalIgnoreCase))
            return LegacyMimBucket.CanCook;

        if (fileName.StartsWith("RI_JBO_CanMakeCoffee", StringComparison.OrdinalIgnoreCase))
            return LegacyMimBucket.CanMakeCoffee;

        if (fileName.StartsWith("RI_JBO_CanMakeBreakfast", StringComparison.OrdinalIgnoreCase))
            return LegacyMimBucket.CanMakeBreakfast;

        if (fileName.StartsWith("RI_JBO_CanJump", StringComparison.OrdinalIgnoreCase))
            return LegacyMimBucket.CanJump;

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
            fileName.StartsWith("JBO_WhatItLikeBeRobot", StringComparison.OrdinalIgnoreCase) ||
            fileName.StartsWith("JBO_WhatItLikeHaveNoLegs", StringComparison.OrdinalIgnoreCase) ||
            fileName.StartsWith("JBO_HowDoYouWork", StringComparison.OrdinalIgnoreCase) ||
            fileName.StartsWith("JBO_HowMuchDoYouKnow", StringComparison.OrdinalIgnoreCase) ||
            fileName.StartsWith("JBO_HowOldAreYou", StringComparison.OrdinalIgnoreCase) ||
            fileName.StartsWith("JBO_WhenWereYouBorn", StringComparison.OrdinalIgnoreCase) ||
            fileName.StartsWith("JBO_WhatsYourName", StringComparison.OrdinalIgnoreCase) ||
            fileName.StartsWith("JBO_WhereDoYouGetInfo", StringComparison.OrdinalIgnoreCase) ||
            fileName.StartsWith("JBO_WhatDoYouLikeToDo", StringComparison.OrdinalIgnoreCase))
            return fileName.StartsWith("JBO_HowOldAreYou", StringComparison.OrdinalIgnoreCase)
                ? LegacyMimBucket.Age
                : LegacyMimBucket.Personality;

        if (fileName.StartsWith("OI_JBO_Is", StringComparison.OrdinalIgnoreCase) ||
            fileName.StartsWith("OI_JBO_Seems", StringComparison.OrdinalIgnoreCase) ||
            fileName.StartsWith("RI_JBO_IsHappy", StringComparison.OrdinalIgnoreCase) ||
            fileName.StartsWith("RI_JBO_IsSad", StringComparison.OrdinalIgnoreCase) ||
            fileName.StartsWith("RI_JBO_IsAngry", StringComparison.OrdinalIgnoreCase) ||
            fileName.StartsWith("RN_WhatAreYouFeeling", StringComparison.OrdinalIgnoreCase))
            return LegacyMimBucket.Emotion;

        if (fileName.StartsWith("PartOfDayCorrection", StringComparison.OrdinalIgnoreCase))
            return LegacyMimBucket.PartOfDayCorrection;

        if (fileName.StartsWith("NotHoliday", StringComparison.OrdinalIgnoreCase))
            return LegacyMimBucket.NotHoliday;

        if (fileName.StartsWith("HolidayResponse", StringComparison.OrdinalIgnoreCase))
            return LegacyMimBucket.HolidayResponse;

        if (fileName.StartsWith("WhatsUpResp", StringComparison.OrdinalIgnoreCase))
            return LegacyMimBucket.WhatsUp;

        if (fileName.StartsWith("GoodbyeResp", StringComparison.OrdinalIgnoreCase))
            return LegacyMimBucket.Goodbye;

        if (fileName.StartsWith("GenericMorningSalutation", StringComparison.OrdinalIgnoreCase) ||
            fileName.StartsWith("GenericAfternoonSalutation", StringComparison.OrdinalIgnoreCase) ||
            fileName.StartsWith("GenericEveningSalutation", StringComparison.OrdinalIgnoreCase) ||
            fileName.StartsWith("GenericNightSalutation", StringComparison.OrdinalIgnoreCase))
            return LegacyMimBucket.ReactiveGreeting;

        if (fileName.Contains("Greeting", StringComparison.OrdinalIgnoreCase) ||
            fileName.StartsWith("RN_", StringComparison.OrdinalIgnoreCase) ||
            fileName.Contains("Welcome", StringComparison.OrdinalIgnoreCase))
            return LegacyMimBucket.Greeting;

        if (normalizedPath.Contains("/scripted-responses/", StringComparison.OrdinalIgnoreCase))
            return LegacyMimBucket.Personality;

        return null;
    }

    private static LegacyMimBucket? ResolveBucketFromMimId(string? mimId)
    {
        if (string.IsNullOrWhiteSpace(mimId)) return null;

        if (mimId.StartsWith("WhatsUpResp", StringComparison.OrdinalIgnoreCase)) return LegacyMimBucket.WhatsUp;
        if (mimId.StartsWith("GoodbyeResp", StringComparison.OrdinalIgnoreCase)) return LegacyMimBucket.Goodbye;
        if (mimId.StartsWith("GenericMorningSalutation", StringComparison.OrdinalIgnoreCase) ||
            mimId.StartsWith("GenericAfternoonSalutation", StringComparison.OrdinalIgnoreCase) ||
            mimId.StartsWith("GenericEveningSalutation", StringComparison.OrdinalIgnoreCase) ||
            mimId.StartsWith("GenericNightSalutation", StringComparison.OrdinalIgnoreCase))
            return LegacyMimBucket.ReactiveGreeting;
        if (mimId.StartsWith("PartOfDayCorrection", StringComparison.OrdinalIgnoreCase))
            return LegacyMimBucket.PartOfDayCorrection;
        if (mimId.StartsWith("NotHoliday", StringComparison.OrdinalIgnoreCase)) return LegacyMimBucket.NotHoliday;
        if (mimId.StartsWith("HolidayResponse", StringComparison.OrdinalIgnoreCase))
            return LegacyMimBucket.HolidayResponse;

        return null;
    }

    private static string NormalizeImportedCondition(string? condition, string filePath, LegacyMimBucket bucket)
    {
        var normalized = NormalizeCondition(condition);
        normalized = Regex.Replace(
            normalized,
            @"\$\{POD\s*==\s*'([^']+)'\}",
            "POD=='$1'",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        normalized = Regex.Replace(
            normalized,
            @"PODclaim\s*==\s*'([^']+)'",
            "PODclaim=='$1'",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        if (bucket != LegacyMimBucket.ReactiveGreeting || !string.IsNullOrWhiteSpace(normalized))
            return normalized;

        var fileName = Path.GetFileNameWithoutExtension(filePath);
        if (fileName.Contains("Morning", StringComparison.OrdinalIgnoreCase)) return "POD=='morning'";
        if (fileName.Contains("Afternoon", StringComparison.OrdinalIgnoreCase)) return "POD=='afternoon'";
        if (fileName.Contains("Evening", StringComparison.OrdinalIgnoreCase)) return "POD=='evening'";
        if (fileName.Contains("Night", StringComparison.OrdinalIgnoreCase)) return "POD=='night'";

        return normalized;
    }

    private static string NormalizePrompt(string? prompt, bool preservePlaceholders = false)
    {
        return LegacyMimPromptNormalizer.Normalize(prompt, preservePlaceholders).Text;
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
            FunFactFallbacks = baseCatalog.FunFactFallbacks,
            FavoriteAnimalReplies = Merge(baseCatalog.FavoriteAnimalReplies, importedCatalog.FavoriteAnimalReplies),
            FriendReplies = Merge(baseCatalog.FriendReplies, importedCatalog.FriendReplies),
            BestFriendReplies = Merge(baseCatalog.BestFriendReplies, importedCatalog.BestFriendReplies),
            SingReplies = Merge(baseCatalog.SingReplies, importedCatalog.SingReplies),
            HolidaySingReplies = Merge(baseCatalog.HolidaySingReplies, importedCatalog.HolidaySingReplies),
            DanceAnimations = Merge(baseCatalog.DanceAnimations, importedCatalog.DanceAnimations),
            GreetingReplies = Merge(baseCatalog.GreetingReplies, importedCatalog.GreetingReplies),
            PartOfDayCorrectionReplies = Merge(baseCatalog.PartOfDayCorrectionReplies,
                importedCatalog.PartOfDayCorrectionReplies),
            NotHolidayReplies = Merge(baseCatalog.NotHolidayReplies, importedCatalog.NotHolidayReplies),
            HolidayResponseReplies = Merge(baseCatalog.HolidayResponseReplies,
                importedCatalog.HolidayResponseReplies),
            ReactiveGreetingReplies = Merge(baseCatalog.ReactiveGreetingReplies,
                importedCatalog.ReactiveGreetingReplies),
            WhatsUpReplies = Merge(baseCatalog.WhatsUpReplies, importedCatalog.WhatsUpReplies),
            GoodbyeReplies = Merge(baseCatalog.GoodbyeReplies, importedCatalog.GoodbyeReplies),
            HolidayReplies = Merge(baseCatalog.HolidayReplies, importedCatalog.HolidayReplies),
            HolidaySeasonReplies = Merge(baseCatalog.HolidaySeasonReplies, importedCatalog.HolidaySeasonReplies),
            HolidayGreetingReplies = Merge(baseCatalog.HolidayGreetingReplies, importedCatalog.HolidayGreetingReplies),
            HolidayGiftReplies = Merge(baseCatalog.HolidayGiftReplies, importedCatalog.HolidayGiftReplies),
            HolidayTrackerReplies = Merge(baseCatalog.HolidayTrackerReplies, importedCatalog.HolidayTrackerReplies),
            BirthdayCelebrationReplies = Merge(baseCatalog.BirthdayCelebrationReplies,
                importedCatalog.BirthdayCelebrationReplies),
            StopMovingReplies = Merge(baseCatalog.StopMovingReplies, importedCatalog.StopMovingReplies),
            StopMakingThatNoiseReplies = Merge(baseCatalog.StopMakingThatNoiseReplies,
                importedCatalog.StopMakingThatNoiseReplies),
            StopIgnoringMeReplies = Merge(baseCatalog.StopIgnoringMeReplies, importedCatalog.StopIgnoringMeReplies),
            StopStaringReplies = Merge(baseCatalog.StopStaringReplies, importedCatalog.StopStaringReplies),
            CanWalkReplies = Merge(baseCatalog.CanWalkReplies, importedCatalog.CanWalkReplies),
            CanWalkDogReplies = Merge(baseCatalog.CanWalkDogReplies, importedCatalog.CanWalkDogReplies),
            CanWatchMoviesReplies = Merge(baseCatalog.CanWatchMoviesReplies, importedCatalog.CanWatchMoviesReplies),
            CanWatchTVReplies = Merge(baseCatalog.CanWatchTVReplies, importedCatalog.CanWatchTVReplies),
            CanDreamReplies = Merge(baseCatalog.CanDreamReplies, importedCatalog.CanDreamReplies),
            CanExerciseReplies = Merge(baseCatalog.CanExerciseReplies, importedCatalog.CanExerciseReplies),
            CanFlyReplies = Merge(baseCatalog.CanFlyReplies, importedCatalog.CanFlyReplies),
            CanLearnReplies = Merge(baseCatalog.CanLearnReplies, importedCatalog.CanLearnReplies),
            CanLaughReplies = Merge(baseCatalog.CanLaughReplies, importedCatalog.CanLaughReplies),
            CanReadReplies = Merge(baseCatalog.CanReadReplies, importedCatalog.CanReadReplies),
            CanHearReplies = Merge(baseCatalog.CanHearReplies, importedCatalog.CanHearReplies),
            CanTalkReplies = Merge(baseCatalog.CanTalkReplies, importedCatalog.CanTalkReplies),
            CanSeeReplies = Merge(baseCatalog.CanSeeReplies, importedCatalog.CanSeeReplies),
            CanWinkReplies = Merge(baseCatalog.CanWinkReplies, importedCatalog.CanWinkReplies),
            CanMoveReplies = Merge(baseCatalog.CanMoveReplies, importedCatalog.CanMoveReplies),
            CanWorkReplies = Merge(baseCatalog.CanWorkReplies, importedCatalog.CanWorkReplies),
            CanBreatheReplies = Merge(baseCatalog.CanBreatheReplies, importedCatalog.CanBreatheReplies),
            CanGetTiredReplies = Merge(baseCatalog.CanGetTiredReplies, importedCatalog.CanGetTiredReplies),
            CanHaveEmotionsReplies = Merge(baseCatalog.CanHaveEmotionsReplies, importedCatalog.CanHaveEmotionsReplies),
            CanWhistleReplies = Merge(baseCatalog.CanWhistleReplies, importedCatalog.CanWhistleReplies),
            CanCookReplies = Merge(baseCatalog.CanCookReplies, importedCatalog.CanCookReplies),
            CanMakeCoffeeReplies = Merge(baseCatalog.CanMakeCoffeeReplies, importedCatalog.CanMakeCoffeeReplies),
            CanMakeBreakfastReplies =
                Merge(baseCatalog.CanMakeBreakfastReplies, importedCatalog.CanMakeBreakfastReplies),
            CanJumpReplies = Merge(baseCatalog.CanJumpReplies, importedCatalog.CanJumpReplies),
            BlackHistoryMonthReplies =
                Merge(baseCatalog.BlackHistoryMonthReplies, importedCatalog.BlackHistoryMonthReplies),
            BlackHistoryMonthFactReplies = Merge(baseCatalog.BlackHistoryMonthFactReplies,
                importedCatalog.BlackHistoryMonthFactReplies),
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
            CalendarServiceDownReplies = Merge(baseCatalog.CalendarServiceDownReplies,
                importedCatalog.CalendarServiceDownReplies),
            CalendarOutroReplies = Merge(baseCatalog.CalendarOutroReplies, importedCatalog.CalendarOutroReplies),
            CommuteAppSetupReplies = Merge(baseCatalog.CommuteAppSetupReplies, importedCatalog.CommuteAppSetupReplies),
            CommuteConfirmSpeakerReplies = Merge(baseCatalog.CommuteConfirmSpeakerReplies,
                importedCatalog.CommuteConfirmSpeakerReplies),
            CommuteNowReplies = Merge(baseCatalog.CommuteNowReplies, importedCatalog.CommuteNowReplies),
            CommuteMinutesLeftReplies = Merge(baseCatalog.CommuteMinutesLeftReplies,
                importedCatalog.CommuteMinutesLeftReplies),
            CommuteDepartTimeNormalReplies = Merge(baseCatalog.CommuteDepartTimeNormalReplies,
                importedCatalog.CommuteDepartTimeNormalReplies),
            CommuteDepartTimeNotNormalReplies = Merge(baseCatalog.CommuteDepartTimeNotNormalReplies,
                importedCatalog.CommuteDepartTimeNotNormalReplies),
            CommuteDriveNormalReplies = Merge(baseCatalog.CommuteDriveNormalReplies,
                importedCatalog.CommuteDriveNormalReplies),
            CommuteDriveLateReplies =
                Merge(baseCatalog.CommuteDriveLateReplies, importedCatalog.CommuteDriveLateReplies),
            CommuteDriveHurryReplies =
                Merge(baseCatalog.CommuteDriveHurryReplies, importedCatalog.CommuteDriveHurryReplies),
            CommuteDrivePoorReplies =
                Merge(baseCatalog.CommuteDrivePoorReplies, importedCatalog.CommuteDrivePoorReplies),
            CommuteDriveTerribleReplies = Merge(baseCatalog.CommuteDriveTerribleReplies,
                importedCatalog.CommuteDriveTerribleReplies),
            CommuteTransportNormalReplies = Merge(baseCatalog.CommuteTransportNormalReplies,
                importedCatalog.CommuteTransportNormalReplies),
            CommuteTransportLateReplies = Merge(baseCatalog.CommuteTransportLateReplies,
                importedCatalog.CommuteTransportLateReplies),
            CommuteTransportHurryReplies = Merge(baseCatalog.CommuteTransportHurryReplies,
                importedCatalog.CommuteTransportHurryReplies),
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
            DanceQuestionReplies = Merge(baseCatalog.DanceQuestionReplies, importedCatalog.DanceQuestionReplies)
        };
    }

    private static string[] Merge(IReadOnlyList<string> baseList, IReadOnlyList<string> importedList)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        return (from value in baseList.Concat(importedList)
            where !string.IsNullOrWhiteSpace(value)
            select value.Trim()
            into normalized
            where seen.Add(normalized)
            select normalized).ToArray();
    }

    private static JiboConditionedReply[] Merge(
        IReadOnlyList<JiboConditionedReply> baseList,
        IReadOnlyList<JiboConditionedReply> importedList)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        return (from value in baseList.Concat(importedList)
            where !string.IsNullOrWhiteSpace(value.Reply)
            let normalizedCondition = NormalizeCondition(value.Condition)
            let normalizedReply = value.Reply.Trim()
            let key = $"{normalizedCondition}::{normalizedReply}"
            where seen.Add(key)
            select new JiboConditionedReply
            {
                Condition = normalizedCondition,
                Reply = normalizedReply,
                Weight = value.Weight,
                MimId = value.MimId,
                PromptId = value.PromptId,
                Emotion = value.Emotion
            }).ToArray();
    }

    private static string NormalizeCondition(string? condition)
    {
        return string.IsNullOrWhiteSpace(condition) ? string.Empty : WhitespacePattern.Replace(condition.Trim(), " ");
    }

    private static bool PreservesTtsMarkup(LegacyMimBucket bucket)
    {
        return bucket is LegacyMimBucket.PartOfDayCorrection
            or LegacyMimBucket.NotHoliday
            or LegacyMimBucket.HolidayResponse
            or LegacyMimBucket.ReactiveGreeting
            or LegacyMimBucket.WhatsUp
            or LegacyMimBucket.Goodbye;
    }

    private static bool IsSpeakerTemplateBucket(LegacyMimBucket bucket)
    {
        return IsTemplateBucket(bucket) ||
               bucket is LegacyMimBucket.PartOfDayCorrection
                   or LegacyMimBucket.NotHoliday
                   or LegacyMimBucket.HolidayResponse
                   or LegacyMimBucket.ReactiveGreeting
                   or LegacyMimBucket.WhatsUp
                   or LegacyMimBucket.Goodbye;
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
            or LegacyMimBucket.ReportSkillTemplate
            or LegacyMimBucket.CommuteNow
            or LegacyMimBucket.CommuteMinutesLeft
            or LegacyMimBucket.CommuteDepartTimeNormal
            or LegacyMimBucket.CommuteDepartTimeNotNormal
            or LegacyMimBucket.CommuteDriveNormal
            or LegacyMimBucket.CommuteDriveLate
            or LegacyMimBucket.CommuteDriveHurry
            or LegacyMimBucket.CommuteDrivePoor
            or LegacyMimBucket.CommuteDriveTerrible
            or LegacyMimBucket.CommuteTransportNormal
            or LegacyMimBucket.CommuteTransportLate
            or LegacyMimBucket.CommuteTransportHurry
            or LegacyMimBucket.CommuteConfirmSpeaker
            or LegacyMimBucket.Age
            or LegacyMimBucket.Holiday
            or LegacyMimBucket.HolidayTracker;
    }

    private static bool IsHolidaySeasonFile(string fileName)
    {
        return fileName.StartsWith("RI_JBO_HowIsHolidaySeason", StringComparison.OrdinalIgnoreCase) ||
               fileName.StartsWith("RI_JBO_LikesHolidaySeason", StringComparison.OrdinalIgnoreCase) ||
               fileName.StartsWith("RI_JBO_HowIsThanksgiving", StringComparison.OrdinalIgnoreCase) ||
               fileName.StartsWith("RI_JBO_LikesThanksgiving", StringComparison.OrdinalIgnoreCase) ||
               fileName.StartsWith("RI_JBO_LooksForwardToThanksgiving", StringComparison.OrdinalIgnoreCase) ||
               fileName.StartsWith("RI_JBO_PlansForThanksgiving", StringComparison.OrdinalIgnoreCase) ||
               fileName.StartsWith("RI_JBO_HowIsChristmas", StringComparison.OrdinalIgnoreCase) ||
               fileName.StartsWith("RI_JBO_LikesChristmas", StringComparison.OrdinalIgnoreCase) ||
               fileName.StartsWith("RI_JBO_LooksForwardToChristmas", StringComparison.OrdinalIgnoreCase) ||
               fileName.StartsWith("RI_JBO_PlansForChristmas", StringComparison.OrdinalIgnoreCase) ||
               fileName.StartsWith("RI_JBO_HowIsHanukkah", StringComparison.OrdinalIgnoreCase) ||
               fileName.StartsWith("RI_JBO_LikesHanukkah", StringComparison.OrdinalIgnoreCase) ||
               fileName.StartsWith("RI_JBO_LooksForwardToHanukkah", StringComparison.OrdinalIgnoreCase) ||
               fileName.StartsWith("RI_JBO_PlansForHanukkah", StringComparison.OrdinalIgnoreCase) ||
               fileName.StartsWith("RI_JBO_HowIsPassover", StringComparison.OrdinalIgnoreCase) ||
               fileName.StartsWith("RI_JBO_LikesPassover", StringComparison.OrdinalIgnoreCase) ||
               fileName.StartsWith("RI_JBO_LooksForwardToPassover", StringComparison.OrdinalIgnoreCase) ||
               fileName.StartsWith("RI_JBO_PlansForPassover", StringComparison.OrdinalIgnoreCase) ||
               fileName.StartsWith("RI_JBO_HowIsNewYears", StringComparison.OrdinalIgnoreCase) ||
               fileName.StartsWith("RI_JBO_LikesNewYears", StringComparison.OrdinalIgnoreCase) ||
               fileName.StartsWith("RI_JBO_LooksForwardToNewYears", StringComparison.OrdinalIgnoreCase) ||
               fileName.StartsWith("RI_JBO_PlansForNewYears", StringComparison.OrdinalIgnoreCase) ||
               fileName.StartsWith("RI_JBO_HowIsValentinesDay", StringComparison.OrdinalIgnoreCase) ||
               fileName.StartsWith("RI_JBO_LikesValentinesDay", StringComparison.OrdinalIgnoreCase) ||
               fileName.StartsWith("RI_JBO_LooksForwardToValentinesDay", StringComparison.OrdinalIgnoreCase) ||
               fileName.StartsWith("RI_JBO_PlansForValentinesDay", StringComparison.OrdinalIgnoreCase) ||
               fileName.StartsWith("RI_JBO_HowIsKwanzaa", StringComparison.OrdinalIgnoreCase) ||
               fileName.StartsWith("RI_JBO_LikesKwanzaa", StringComparison.OrdinalIgnoreCase) ||
               fileName.StartsWith("RI_JBO_LooksForwardToKwanzaa", StringComparison.OrdinalIgnoreCase) ||
               fileName.StartsWith("RI_JBO_PlansForKwanzaa", StringComparison.OrdinalIgnoreCase) ||
               fileName.StartsWith("RI_JBO_HowIsEaster", StringComparison.OrdinalIgnoreCase) ||
               fileName.StartsWith("RI_JBO_LikesEaster", StringComparison.OrdinalIgnoreCase) ||
               fileName.StartsWith("RI_JBO_LooksForwardToEaster", StringComparison.OrdinalIgnoreCase) ||
               fileName.StartsWith("RI_JBO_PlansForEaster", StringComparison.OrdinalIgnoreCase) ||
               fileName.StartsWith("RI_JBO_HowIsOrthodoxEaster", StringComparison.OrdinalIgnoreCase) ||
               fileName.StartsWith("RI_JBO_LikesOrthodoxEaster", StringComparison.OrdinalIgnoreCase) ||
               fileName.StartsWith("RI_JBO_LooksForwardToOrthodoxEaster", StringComparison.OrdinalIgnoreCase) ||
               fileName.StartsWith("RI_JBO_PlansForOrthodoxEaster", StringComparison.OrdinalIgnoreCase);
    }

    private enum LegacyMimBucket
    {
        GenericFallback,
        Greeting,
        PartOfDayCorrection,
        NotHoliday,
        HolidayResponse,
        ReactiveGreeting,
        WhatsUp,
        Goodbye,
        Holiday,
        HolidaySeason,
        HolidayGreeting,
        HolidayGift,
        HolidayTracker,
        BirthdayCelebration,
        StopMoving,
        StopMakingThatNoise,
        StopIgnoringMe,
        StopStaring,
        CanWalk,
        CanWalkDog,
        CanWatchMovies,
        CanWatchTV,
        CanDream,
        CanExercise,
        CanFly,
        CanLearn,
        CanLaugh,
        CanRead,
        CanHear,
        CanTalk,
        CanSee,
        CanWink,
        CanMove,
        CanWork,
        CanBreathe,
        CanGetTired,
        CanHaveEmotions,
        CanWhistle,
        CanCook,
        CanMakeCoffee,
        CanMakeBreakfast,
        CanJump,
        BackupHow,
        RestoreHow,
        UpdateNext,
        UpdateLast,
        Story,
        RecommendMovie,
        SearchWeb,
        BlackHistoryMonth,
        BlackHistoryMonthFact,
        Jokes,
        RobotFacts,
        HumanFacts,
        HowAreYou,
        Emotion,
        FunFacts,
        FavoriteAnimal,
        Friend,
        BestFriend,
        Sing,
        HolidaySing,
        FunFactSource,
        Age,
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
        CalendarServiceDown,
        CalendarOutro,
        CommuteNow,
        CommuteMinutesLeft,
        CommuteDepartTimeNormal,
        CommuteDepartTimeNotNormal,
        CommuteAppSetup,
        CommuteConfirmSpeaker,
        CommuteDriveNormal,
        CommuteDriveLate,
        CommuteDriveHurry,
        CommuteDrivePoor,
        CommuteDriveTerrible,
        CommuteTransportNormal,
        CommuteTransportLate,
        CommuteTransportHurry,
        CommuteServiceDown,
        NewsIntro,
        NewsCategoryIntro,
        NewsOutro,
        ReportSkillTemplate
    }

    private sealed class LegacyMimCatalogBuilder
    {
        private readonly List<string> _ages = [];
        private readonly List<string> _backupHowReplies = [];
        private readonly List<string> _bestFriendReplies = [];
        private readonly List<string> _birthdayCelebrationReplies = [];
        private readonly List<string> _blackHistoryMonthFactReplies = [];
        private readonly List<JiboConditionedReply> _blackHistoryMonthReplies = [];
        private readonly List<string> _calendarNothingReplies = [];
        private readonly List<string> _calendarNothingTodayReplies = [];
        private readonly List<string> _calendarOutroReplies = [];
        private readonly List<string> _calendarServiceDownReplies = [];
        private readonly List<string> _canBreatheReplies = [];
        private readonly List<string> _canCookReplies = [];
        private readonly List<string> _canDreamReplies = [];
        private readonly List<string> _canExerciseReplies = [];
        private readonly List<string> _canFlyReplies = [];
        private readonly List<string> _canGetTiredReplies = [];
        private readonly List<string> _canHaveEmotionsReplies = [];
        private readonly List<string> _canHearReplies = [];
        private readonly List<string> _canJumpReplies = [];
        private readonly List<string> _canLaughReplies = [];
        private readonly List<string> _canLearnReplies = [];
        private readonly List<string> _canMakeBreakfastReplies = [];
        private readonly List<string> _canMakeCoffeeReplies = [];
        private readonly List<string> _canMoveReplies = [];
        private readonly List<string> _canReadReplies = [];
        private readonly List<string> _canSeeReplies = [];
        private readonly List<string> _canTalkReplies = [];
        private readonly List<string> _canWalkDogReplies = [];
        private readonly List<string> _canWalkReplies = [];
        private readonly List<string> _canWatchMoviesReplies = [];
        private readonly List<string> _canWatchTVReplies = [];
        private readonly List<string> _canWhistleReplies = [];
        private readonly List<string> _canWinkReplies = [];
        private readonly List<string> _canWorkReplies = [];
        private readonly List<string> _commuteAppSetupReplies = [];
        private readonly List<string> _commuteConfirmSpeakerReplies = [];
        private readonly List<string> _commuteDepartTimeNormalReplies = [];
        private readonly List<string> _commuteDepartTimeNotNormalReplies = [];
        private readonly List<string> _commuteDriveHurryReplies = [];
        private readonly List<string> _commuteDriveLateReplies = [];
        private readonly List<string> _commuteDriveNormalReplies = [];
        private readonly List<string> _commuteDrivePoorReplies = [];
        private readonly List<string> _commuteDriveTerribleReplies = [];
        private readonly List<string> _commuteMinutesLeftReplies = [];
        private readonly List<string> _commuteNowReplies = [];
        private readonly List<string> _commuteServiceDownReplies = [];
        private readonly List<string> _commuteTransportHurryReplies = [];
        private readonly List<string> _commuteTransportLateReplies = [];
        private readonly List<string> _commuteTransportNormalReplies = [];
        private readonly List<JiboConditionedReply> _emotionReplies = [];
        private readonly List<string> _fallbacks = [];
        private readonly List<string> _favoriteAnimalReplies = [];
        private readonly List<string> _friendReplies = [];
        private readonly List<string> _funFacts = [];
        private readonly List<string> _greetings = [];
        private readonly List<string> _holidayGiftReplies = [];
        private readonly List<string> _holidayGreetingReplies = [];
        private readonly List<string> _holidayReplies = [];
        private readonly List<string> _holidaySeasonReplies = [];
        private readonly List<string> _holidaySingReplies = [];
        private readonly List<string> _holidayTrackerReplies = [];
        private readonly List<string> _howAreYous = [];
        private readonly List<string> _humanFacts = [];
        private readonly List<string> _jokes = [];
        private readonly List<string> _newsCategoryIntroReplies = [];
        private readonly List<string> _newsIntroReplies = [];
        private readonly List<string> _newsOutroReplies = [];
        private readonly List<JiboConditionedReply> _partOfDayCorrectionReplies = [];
        private readonly List<JiboConditionedReply> _notHolidayReplies = [];
        private readonly List<JiboConditionedReply> _holidayResponseReplies = [];
        private readonly List<JiboConditionedReply> _reactiveGreetingReplies = [];
        private readonly List<JiboConditionedReply> _whatsUpReplies = [];
        private readonly List<JiboConditionedReply> _goodbyeReplies = [];
        private readonly List<string> _personalities = [];
        private readonly List<string> _personalReportKickOffReplies = [];
        private readonly List<string> _personalReportOutroReplies = [];
        private readonly List<string> _recommendMovieReplies = [];
        private readonly List<string> _reportSkillTemplates = [];
        private readonly List<string> _restoreHowReplies = [];
        private readonly List<string> _robotFacts = [];
        private readonly List<string> _searchWebReplies = [];
        private readonly List<string> _singReplies = [];
        private readonly List<string> _stopIgnoringMeReplies = [];
        private readonly List<string> _stopMakingThatNoiseReplies = [];
        private readonly List<string> _stopMovingReplies = [];
        private readonly List<string> _stopStaringReplies = [];
        private readonly List<string> _storyReplies = [];
        private readonly List<string> _updateLastReplies = [];
        private readonly List<string> _updateNextReplies = [];
        private readonly List<string> _weatherIntroReplies = [];
        private readonly List<string> _weatherServiceDownReplies = [];
        private readonly List<string> _weatherTodayHighLowReplies = [];
        private readonly List<string> _weatherTomorrowHighLowReplies = [];
        private readonly List<string> _weatherTomorrowIntroReplies = [];

        public void Add(
            LegacyMimBucket bucket,
            string? condition,
            string text,
            double? weight = null,
            string? mimId = null,
            string? promptId = null,
            string? emotion = null)
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
                case LegacyMimBucket.PartOfDayCorrection:
                    AddDistinct(_partOfDayCorrectionReplies, condition, text, weight, mimId, promptId, emotion);
                    return;
                case LegacyMimBucket.NotHoliday:
                    AddDistinct(_notHolidayReplies, condition, text, weight, mimId, promptId, emotion);
                    return;
                case LegacyMimBucket.HolidayResponse:
                    AddDistinct(_holidayResponseReplies, condition, text, weight, mimId, promptId, emotion);
                    return;
                case LegacyMimBucket.ReactiveGreeting:
                    AddDistinct(_reactiveGreetingReplies, condition, text, weight, mimId, promptId, emotion);
                    return;
                case LegacyMimBucket.WhatsUp:
                    AddDistinct(_whatsUpReplies, condition, text, weight, mimId, promptId, emotion);
                    return;
                case LegacyMimBucket.Goodbye:
                    AddDistinct(_goodbyeReplies, condition, text, weight, mimId, promptId, emotion);
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
                        Reply = text,
                        Weight = weight ?? 1.0,
                        MimId = mimId,
                        PromptId = promptId,
                        Emotion = emotion
                    });
                    return;
                case LegacyMimBucket.Age:
                    AddDistinct(_ages, text);
                    return;
                case LegacyMimBucket.Holiday:
                    AddDistinct(_holidayReplies, text);
                    return;
                case LegacyMimBucket.HolidaySeason:
                    AddDistinct(_holidaySeasonReplies, text);
                    return;
                case LegacyMimBucket.HolidayGreeting:
                    AddDistinct(_holidayGreetingReplies, text);
                    return;
                case LegacyMimBucket.HolidayGift:
                    AddDistinct(_holidayGiftReplies, text);
                    return;
                case LegacyMimBucket.HolidayTracker:
                    AddDistinct(_holidayTrackerReplies, text);
                    return;
                case LegacyMimBucket.BirthdayCelebration:
                    AddDistinct(_birthdayCelebrationReplies, text);
                    return;
                case LegacyMimBucket.StopMoving:
                    AddDistinct(_stopMovingReplies, text);
                    return;
                case LegacyMimBucket.StopMakingThatNoise:
                    AddDistinct(_stopMakingThatNoiseReplies, text);
                    return;
                case LegacyMimBucket.StopIgnoringMe:
                    AddDistinct(_stopIgnoringMeReplies, text);
                    return;
                case LegacyMimBucket.StopStaring:
                    AddDistinct(_stopStaringReplies, text);
                    return;
                case LegacyMimBucket.CanWalk:
                    AddDistinct(_canWalkReplies, text);
                    return;
                case LegacyMimBucket.CanWalkDog:
                    AddDistinct(_canWalkDogReplies, text);
                    return;
                case LegacyMimBucket.CanWatchMovies:
                    AddDistinct(_canWatchMoviesReplies, text);
                    return;
                case LegacyMimBucket.CanWatchTV:
                    AddDistinct(_canWatchTVReplies, text);
                    return;
                case LegacyMimBucket.CanDream:
                    AddDistinct(_canDreamReplies, text);
                    return;
                case LegacyMimBucket.CanExercise:
                    AddDistinct(_canExerciseReplies, text);
                    return;
                case LegacyMimBucket.CanFly:
                    AddDistinct(_canFlyReplies, text);
                    return;
                case LegacyMimBucket.CanLearn:
                    AddDistinct(_canLearnReplies, text);
                    return;
                case LegacyMimBucket.CanLaugh:
                    AddDistinct(_canLaughReplies, text);
                    return;
                case LegacyMimBucket.CanRead:
                    AddDistinct(_canReadReplies, text);
                    return;
                case LegacyMimBucket.CanHear:
                    AddDistinct(_canHearReplies, text);
                    return;
                case LegacyMimBucket.CanTalk:
                    AddDistinct(_canTalkReplies, text);
                    return;
                case LegacyMimBucket.CanSee:
                    AddDistinct(_canSeeReplies, text);
                    return;
                case LegacyMimBucket.CanWink:
                    AddDistinct(_canWinkReplies, text);
                    return;
                case LegacyMimBucket.CanMove:
                    AddDistinct(_canMoveReplies, text);
                    return;
                case LegacyMimBucket.CanWork:
                    AddDistinct(_canWorkReplies, text);
                    return;
                case LegacyMimBucket.CanBreathe:
                    AddDistinct(_canBreatheReplies, text);
                    return;
                case LegacyMimBucket.CanGetTired:
                    AddDistinct(_canGetTiredReplies, text);
                    return;
                case LegacyMimBucket.CanHaveEmotions:
                    AddDistinct(_canHaveEmotionsReplies, text);
                    return;
                case LegacyMimBucket.CanWhistle:
                    AddDistinct(_canWhistleReplies, text);
                    return;
                case LegacyMimBucket.CanCook:
                    AddDistinct(_canCookReplies, text);
                    return;
                case LegacyMimBucket.CanMakeCoffee:
                    AddDistinct(_canMakeCoffeeReplies, text);
                    return;
                case LegacyMimBucket.CanMakeBreakfast:
                    AddDistinct(_canMakeBreakfastReplies, text);
                    return;
                case LegacyMimBucket.CanJump:
                    AddDistinct(_canJumpReplies, text);
                    return;
                case LegacyMimBucket.BackupHow:
                    AddDistinct(_backupHowReplies, text);
                    return;
                case LegacyMimBucket.RestoreHow:
                    AddDistinct(_restoreHowReplies, text);
                    return;
                case LegacyMimBucket.UpdateNext:
                    AddDistinct(_updateNextReplies, text);
                    return;
                case LegacyMimBucket.UpdateLast:
                    AddDistinct(_updateLastReplies, text);
                    return;
                case LegacyMimBucket.Story:
                    AddDistinct(_storyReplies, text);
                    return;
                case LegacyMimBucket.RecommendMovie:
                    AddDistinct(_recommendMovieReplies, text);
                    return;
                case LegacyMimBucket.SearchWeb:
                    AddDistinct(_searchWebReplies, text);
                    return;
                case LegacyMimBucket.BlackHistoryMonth:
                    AddDistinct(_blackHistoryMonthReplies, condition, text);
                    return;
                case LegacyMimBucket.BlackHistoryMonthFact:
                    AddDistinct(_blackHistoryMonthFactReplies, text);
                    return;
                case LegacyMimBucket.Personality:
                    if (_personalities.Any(value => string.Equals(value, text, StringComparison.OrdinalIgnoreCase)))
                        return;

                    _personalities.Add(text);
                    return;
                case LegacyMimBucket.Sing:
                    AddDistinct(_singReplies, text);
                    return;
                case LegacyMimBucket.HolidaySing:
                    AddDistinct(_holidaySingReplies, text);
                    return;
                case LegacyMimBucket.FunFactSource:
                    switch (ResolveFunFactTarget(text))
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
                case LegacyMimBucket.FavoriteAnimal:
                    AddDistinct(_favoriteAnimalReplies, text);
                    return;
                case LegacyMimBucket.Friend:
                    AddDistinct(_friendReplies, text);
                    return;
                case LegacyMimBucket.BestFriend:
                    AddDistinct(_bestFriendReplies, text);
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
                case LegacyMimBucket.CalendarServiceDown:
                    AddDistinct(_calendarServiceDownReplies, text);
                    return;
                case LegacyMimBucket.CalendarOutro:
                    AddDistinct(_calendarOutroReplies, text);
                    return;
                case LegacyMimBucket.CommuteAppSetup:
                    AddDistinct(_commuteAppSetupReplies, text);
                    return;
                case LegacyMimBucket.CommuteConfirmSpeaker:
                    AddDistinct(_commuteConfirmSpeakerReplies, text);
                    return;
                case LegacyMimBucket.CommuteNow:
                    AddDistinct(_commuteNowReplies, text);
                    return;
                case LegacyMimBucket.CommuteMinutesLeft:
                    AddDistinct(_commuteMinutesLeftReplies, text);
                    return;
                case LegacyMimBucket.CommuteDepartTimeNormal:
                    AddDistinct(_commuteDepartTimeNormalReplies, text);
                    return;
                case LegacyMimBucket.CommuteDepartTimeNotNormal:
                    AddDistinct(_commuteDepartTimeNotNormalReplies, text);
                    return;
                case LegacyMimBucket.CommuteDriveNormal:
                    AddDistinct(_commuteDriveNormalReplies, text);
                    return;
                case LegacyMimBucket.CommuteDriveLate:
                    AddDistinct(_commuteDriveLateReplies, text);
                    return;
                case LegacyMimBucket.CommuteDriveHurry:
                    AddDistinct(_commuteDriveHurryReplies, text);
                    return;
                case LegacyMimBucket.CommuteDrivePoor:
                    AddDistinct(_commuteDrivePoorReplies, text);
                    return;
                case LegacyMimBucket.CommuteDriveTerrible:
                    AddDistinct(_commuteDriveTerribleReplies, text);
                    return;
                case LegacyMimBucket.CommuteTransportNormal:
                    AddDistinct(_commuteTransportNormalReplies, text);
                    return;
                case LegacyMimBucket.CommuteTransportLate:
                    AddDistinct(_commuteTransportLateReplies, text);
                    return;
                case LegacyMimBucket.CommuteTransportHurry:
                    AddDistinct(_commuteTransportHurryReplies, text);
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

        public JiboExperienceCatalog Build()
        {
            return new JiboExperienceCatalog
            {
                Jokes = [.. _jokes],
                RobotFacts = [.. _robotFacts],
                HumanFacts = [.. _humanFacts],
                FunFacts = [.. _funFacts],
                FavoriteAnimalReplies = [.. _favoriteAnimalReplies],
                FriendReplies = [.. _friendReplies],
                BestFriendReplies = [.. _bestFriendReplies],
                SingReplies = [.. _singReplies],
                HolidaySingReplies = [.. _holidaySingReplies],
                GreetingReplies = [.. _greetings],
                PartOfDayCorrectionReplies = [.. _partOfDayCorrectionReplies],
                NotHolidayReplies = [.. _notHolidayReplies],
                HolidayResponseReplies = [.. _holidayResponseReplies],
                ReactiveGreetingReplies = [.. _reactiveGreetingReplies],
                WhatsUpReplies = [.. _whatsUpReplies],
                GoodbyeReplies = [.. _goodbyeReplies],
                HolidayReplies = [.. _holidayReplies],
                HolidaySeasonReplies = [.. _holidaySeasonReplies],
                HolidayGreetingReplies = [.. _holidayGreetingReplies],
                HolidayGiftReplies = [.. _holidayGiftReplies],
                HolidayTrackerReplies = [.. _holidayTrackerReplies],
                BirthdayCelebrationReplies = [.. _birthdayCelebrationReplies],
                StopMovingReplies = [.. _stopMovingReplies],
                StopMakingThatNoiseReplies = [.. _stopMakingThatNoiseReplies],
                StopIgnoringMeReplies = [.. _stopIgnoringMeReplies],
                StopStaringReplies = [.. _stopStaringReplies],
                CanWalkReplies = [.. _canWalkReplies],
                CanWalkDogReplies = [.. _canWalkDogReplies],
                CanWatchMoviesReplies = [.. _canWatchMoviesReplies],
                CanWatchTVReplies = [.. _canWatchTVReplies],
                CanDreamReplies = [.. _canDreamReplies],
                CanExerciseReplies = [.. _canExerciseReplies],
                CanFlyReplies = [.. _canFlyReplies],
                CanLearnReplies = [.. _canLearnReplies],
                CanLaughReplies = [.. _canLaughReplies],
                CanReadReplies = [.. _canReadReplies],
                CanHearReplies = [.. _canHearReplies],
                CanTalkReplies = [.. _canTalkReplies],
                CanSeeReplies = [.. _canSeeReplies],
                CanWinkReplies = [.. _canWinkReplies],
                CanMoveReplies = [.. _canMoveReplies],
                CanWorkReplies = [.. _canWorkReplies],
                CanBreatheReplies = [.. _canBreatheReplies],
                CanGetTiredReplies = [.. _canGetTiredReplies],
                CanHaveEmotionsReplies = [.. _canHaveEmotionsReplies],
                CanWhistleReplies = [.. _canWhistleReplies],
                CanCookReplies = [.. _canCookReplies],
                CanMakeCoffeeReplies = [.. _canMakeCoffeeReplies],
                CanMakeBreakfastReplies = [.. _canMakeBreakfastReplies],
                CanJumpReplies = [.. _canJumpReplies],
                BlackHistoryMonthReplies = [.. _blackHistoryMonthReplies],
                BlackHistoryMonthFactReplies = [.. _blackHistoryMonthFactReplies],
                BackupHowReplies = [.. _backupHowReplies],
                HowAreYouReplies = [.. _howAreYous],
                EmotionReplies = [.. _emotionReplies],
                PersonalityReplies = [.. _personalities],
                GenericFallbackReplies = [.. _fallbacks],
                AgeReplies = [.. _ages],
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
                CalendarServiceDownReplies = [.. _calendarServiceDownReplies],
                CalendarOutroReplies = [.. _calendarOutroReplies],
                CommuteAppSetupReplies = [.. _commuteAppSetupReplies],
                CommuteConfirmSpeakerReplies = [.. _commuteConfirmSpeakerReplies],
                CommuteNowReplies = [.. _commuteNowReplies],
                CommuteMinutesLeftReplies = [.. _commuteMinutesLeftReplies],
                CommuteDepartTimeNormalReplies = [.. _commuteDepartTimeNormalReplies],
                CommuteDepartTimeNotNormalReplies = [.. _commuteDepartTimeNotNormalReplies],
                CommuteDriveNormalReplies = [.. _commuteDriveNormalReplies],
                CommuteDriveLateReplies = [.. _commuteDriveLateReplies],
                CommuteDriveHurryReplies = [.. _commuteDriveHurryReplies],
                CommuteDrivePoorReplies = [.. _commuteDrivePoorReplies],
                CommuteDriveTerribleReplies = [.. _commuteDriveTerribleReplies],
                CommuteTransportNormalReplies = [.. _commuteTransportNormalReplies],
                CommuteTransportLateReplies = [.. _commuteTransportLateReplies],
                CommuteTransportHurryReplies = [.. _commuteTransportHurryReplies],
                CommuteServiceDownReplies = [.. _commuteServiceDownReplies],
                NewsIntroReplies = [.. _newsIntroReplies],
                NewsCategoryIntroReplies = [.. _newsCategoryIntroReplies],
                NewsOutroReplies = [.. _newsOutroReplies],
                RestoreHowReplies = [.. _restoreHowReplies],
                UpdateNextReplies = [.. _updateNextReplies],
                UpdateLastReplies = [.. _updateLastReplies],
                StoryReplies = [.. _storyReplies],
                RecommendMovieReplies = [.. _recommendMovieReplies],
                SearchWebReplies = [.. _searchWebReplies]
            };
        }

        private static void AddDistinct(List<string> target, string text)
        {
            if (target.Any(value => string.Equals(value, text, StringComparison.OrdinalIgnoreCase))) return;

            target.Add(text);
        }

        private static void AddDistinct(
            List<JiboConditionedReply> target,
            string? condition,
            string text,
            double? weight = null,
            string? mimId = null,
            string? promptId = null,
            string? emotion = null)
        {
            var normalizedCondition = NormalizeCondition(condition);
            if (target.Any(value =>
                    string.Equals(NormalizeCondition(value.Condition), normalizedCondition,
                        StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(value.Reply, text, StringComparison.OrdinalIgnoreCase)))
                return;

            target.Add(new JiboConditionedReply
            {
                Condition = normalizedCondition,
                Reply = text,
                Weight = weight ?? 1.0,
                MimId = mimId,
                PromptId = promptId,
                Emotion = emotion
            });
        }

        private static LegacyMimBucket ResolveFunFactTarget(string prompt)
        {
            var lowered = NormalizePrompt(prompt).ToLowerInvariant();
            if (ContainsAny(lowered, "robot", "humanoid", "machine", "about me", "my cameras", "turing", "deep blue",
                    "rossum"))
                return LegacyMimBucket.RobotFacts;

            return ContainsAny(lowered, "human", "people", "grown ups", "human being", "humans")
                ? LegacyMimBucket.HumanFacts
                : LegacyMimBucket.FunFacts;
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