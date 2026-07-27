namespace Jibo.Cloud.Application.Abstractions;

public interface IJiboExperienceContentRepository
{
    Task<JiboExperienceCatalog> GetCatalogAsync(CancellationToken cancellationToken = default);
}

public sealed class JiboConditionedReply
{
    public string Condition { get; init; } = string.Empty;
    public string Reply { get; init; } = string.Empty;
}

public sealed class JiboExperienceCatalog
{
    public IReadOnlyList<string> Jokes { get; init; } = [];
    public IReadOnlyList<string> RobotFacts { get; init; } = [];
    public IReadOnlyList<string> HumanFacts { get; init; } = [];
    public IReadOnlyList<string> FunFacts { get; init; } = [];
    public IReadOnlyList<string> FunFactFallbacks { get; init; } = [];
    public IReadOnlyList<string> FavoriteAnimalReplies { get; init; } = [];
    public IReadOnlyList<string> FriendReplies { get; init; } = [];
    public IReadOnlyList<string> BestFriendReplies { get; init; } = [];
    public IReadOnlyList<string> SingReplies { get; init; } = [];
    public IReadOnlyList<string> HolidaySingReplies { get; init; } = [];
    public IReadOnlyList<string> DanceAnimations { get; init; } = [];
    public IReadOnlyList<string> GreetingReplies { get; init; } = [];
    public IReadOnlyList<JiboConditionedReply> PartOfDayCorrectionReplies { get; init; } = [];
    public IReadOnlyList<string> StoryReplies { get; init; } = [];
    public IReadOnlyList<string> HolidayReplies { get; init; } = [];
    public IReadOnlyList<string> HolidaySeasonReplies { get; init; } = [];
    public IReadOnlyList<string> HolidayGreetingReplies { get; init; } = [];
    public IReadOnlyList<string> HolidayGiftReplies { get; init; } = [];
    public IReadOnlyList<string> HolidayTrackerReplies { get; init; } = [];
    public IReadOnlyList<string> BirthdayCelebrationReplies { get; init; } = [];
    public IReadOnlyList<string> StopMovingReplies { get; init; } = [];
    public IReadOnlyList<string> StopMakingThatNoiseReplies { get; init; } = [];
    public IReadOnlyList<string> StopIgnoringMeReplies { get; init; } = [];
    public IReadOnlyList<string> StopStaringReplies { get; init; } = [];
    public IReadOnlyList<string> CanWalkReplies { get; init; } = [];
    public IReadOnlyList<string> CanWalkDogReplies { get; init; } = [];
    public IReadOnlyList<string> CanWatchMoviesReplies { get; init; } = [];
    public IReadOnlyList<string> CanWatchTVReplies { get; init; } = [];
    public IReadOnlyList<string> CanDreamReplies { get; init; } = [];
    public IReadOnlyList<string> CanExerciseReplies { get; init; } = [];
    public IReadOnlyList<string> CanFlyReplies { get; init; } = [];
    public IReadOnlyList<string> CanLearnReplies { get; init; } = [];
    public IReadOnlyList<string> CanLaughReplies { get; init; } = [];
    public IReadOnlyList<string> CanReadReplies { get; init; } = [];
    public IReadOnlyList<string> CanHearReplies { get; init; } = [];
    public IReadOnlyList<string> CanTalkReplies { get; init; } = [];
    public IReadOnlyList<string> CanSeeReplies { get; init; } = [];
    public IReadOnlyList<string> CanWinkReplies { get; init; } = [];
    public IReadOnlyList<string> CanMoveReplies { get; init; } = [];
    public IReadOnlyList<string> CanWorkReplies { get; init; } = [];
    public IReadOnlyList<string> CanBreatheReplies { get; init; } = [];
    public IReadOnlyList<string> CanGetTiredReplies { get; init; } = [];
    public IReadOnlyList<string> CanHaveEmotionsReplies { get; init; } = [];
    public IReadOnlyList<string> CanWhistleReplies { get; init; } = [];
    public IReadOnlyList<string> CanCookReplies { get; init; } = [];
    public IReadOnlyList<string> CanMakeCoffeeReplies { get; init; } = [];
    public IReadOnlyList<string> CanMakeBreakfastReplies { get; init; } = [];
    public IReadOnlyList<string> CanJumpReplies { get; init; } = [];
    public IReadOnlyList<JiboConditionedReply> BlackHistoryMonthReplies { get; init; } = [];
    public IReadOnlyList<string> BlackHistoryMonthFactReplies { get; init; } = [];
    public IReadOnlyList<string> HowAreYouReplies { get; init; } = [];
    public IReadOnlyList<string> AgeReplies { get; init; } = [];
    public IReadOnlyList<JiboConditionedReply> EmotionReplies { get; init; } = [];
    public IReadOnlyList<string> PersonalityReplies { get; init; } = [];
    public IReadOnlyList<string> PizzaReplies { get; init; } = [];
    public IReadOnlyList<string> SurpriseReplies { get; init; } = [];
    public IReadOnlyList<string> PersonalReportReplies { get; init; } = [];
    public IReadOnlyList<string> PersonalReportKickOffReplies { get; init; } = [];
    public IReadOnlyList<string> PersonalReportOutroReplies { get; init; } = [];
    public IReadOnlyList<string> ReportSkillTemplates { get; init; } = [];
    public IReadOnlyList<string> BackupHowReplies { get; init; } = [];
    public IReadOnlyList<string> RestoreHowReplies { get; init; } = [];
    public IReadOnlyList<string> UpdateNextReplies { get; init; } = [];
    public IReadOnlyList<string> UpdateLastReplies { get; init; } = [];
    public IReadOnlyList<string> RecommendMovieReplies { get; init; } = [];
    public IReadOnlyList<string> SearchWebReplies { get; init; } = [];
    public IReadOnlyList<string> WeatherIntroReplies { get; init; } = [];
    public IReadOnlyList<string> WeatherTomorrowIntroReplies { get; init; } = [];
    public IReadOnlyList<string> WeatherTodayHighLowReplies { get; init; } = [];
    public IReadOnlyList<string> WeatherTomorrowHighLowReplies { get; init; } = [];
    public IReadOnlyList<string> WeatherServiceDownReplies { get; init; } = [];
    public IReadOnlyList<string> CalendarNothingTodayReplies { get; init; } = [];
    public IReadOnlyList<string> CalendarNothingReplies { get; init; } = [];
    public IReadOnlyList<string> CalendarServiceDownReplies { get; init; } = [];
    public IReadOnlyList<string> CalendarOutroReplies { get; init; } = [];
    public IReadOnlyList<string> CommuteAppSetupReplies { get; init; } = [];
    public IReadOnlyList<string> CommuteConfirmSpeakerReplies { get; init; } = [];
    public IReadOnlyList<string> CommuteNowReplies { get; init; } = [];
    public IReadOnlyList<string> CommuteMinutesLeftReplies { get; init; } = [];
    public IReadOnlyList<string> CommuteDepartTimeNormalReplies { get; init; } = [];
    public IReadOnlyList<string> CommuteDepartTimeNotNormalReplies { get; init; } = [];
    public IReadOnlyList<string> CommuteDriveNormalReplies { get; init; } = [];
    public IReadOnlyList<string> CommuteDriveLateReplies { get; init; } = [];
    public IReadOnlyList<string> CommuteDriveHurryReplies { get; init; } = [];
    public IReadOnlyList<string> CommuteDrivePoorReplies { get; init; } = [];
    public IReadOnlyList<string> CommuteDriveTerribleReplies { get; init; } = [];
    public IReadOnlyList<string> CommuteTransportNormalReplies { get; init; } = [];
    public IReadOnlyList<string> CommuteTransportLateReplies { get; init; } = [];
    public IReadOnlyList<string> CommuteTransportHurryReplies { get; init; } = [];
    public IReadOnlyList<string> CommuteServiceDownReplies { get; init; } = [];
    public IReadOnlyList<string> NewsIntroReplies { get; init; } = [];
    public IReadOnlyList<string> NewsCategoryIntroReplies { get; init; } = [];
    public IReadOnlyList<string> NewsOutroReplies { get; init; } = [];
    public IReadOnlyList<string> WeatherReplies { get; init; } = [];
    public IReadOnlyList<string> CalendarReplies { get; init; } = [];
    public IReadOnlyList<string> CommuteReplies { get; init; } = [];
    public IReadOnlyList<string> NewsReplies { get; init; } = [];
    public IReadOnlyList<string> NewsBriefings { get; init; } = [];
    public IReadOnlyList<string> GenericFallbackReplies { get; init; } = [];
    public IReadOnlyList<string> DanceReplies { get; init; } = [];
    public IReadOnlyList<string> DanceQuestionReplies { get; init; } = [];
}