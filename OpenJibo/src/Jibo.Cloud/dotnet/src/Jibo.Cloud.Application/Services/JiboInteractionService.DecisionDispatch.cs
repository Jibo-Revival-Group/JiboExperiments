using Jibo.Runtime.Abstractions;

namespace Jibo.Cloud.Application.Services;

public sealed partial class JiboInteractionService
{
    public async Task<JiboInteractionDecision> BuildDecisionCoreAsync(TurnContext turn,
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
            return ShouldHandleProactiveGreetingTrigger(turn, triggerSource, greetingPresence)
                ? BuildProactiveGreetingDecision(turn, greetingPresence, referenceLocalTime)
                : BuildTriggerIgnoredDecision();
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

        var preferredName = ResolvePreferredGreetingName(turn, greetingPresence);
        var chitchatDecision = ChitchatStateMachine.TryBuildDecision(
            semanticIntent,
            transcript,
            lowered,
            catalog,
            randomizer,
            chitchatEmotion,
            preferredName,
            () => BuildGenericReply(catalog, transcript, lowered));
        if (chitchatDecision is not null) return chitchatDecision;

        if (SeasonalHolidayRouteBuilder.TryBuildDecision(
                semanticIntent,
                catalog,
                randomizer,
                selected => RenderHolidayTemplate(selected, turn, greetingPresence),
                referenceLocalTime,
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
            "backup_help" => BuildScriptedSupportDecision(
                catalog.BackupHowReplies,
                "backup_help",
                "cloud backup",
                "back up",
                "restore"),
            "restore_backup" => BuildScriptedSupportDecision(
                catalog.RestoreHowReplies,
                "restore_backup",
                "restore you from a backup",
                "restore from a backup"),
            "update_next" => BuildScriptedSupportDecision(
                catalog.UpdateNextReplies,
                "update_next",
                "next update"),
            "update_last" => BuildScriptedSupportDecision(
                catalog.UpdateLastReplies,
                "update_last",
                "last update"),
            "robot_story" => BuildScriptedSupportDecision(
                catalog.StoryReplies,
                "robot_story",
                "story, that sounds fun",
                "don't have any stories"),
            "robot_recommend_movie" => BuildScriptedSupportDecision(
                catalog.RecommendMovieReplies,
                "robot_recommend_movie",
                "Back to the Future",
                "Toy Story",
                "Spaceballs"),
            "robot_search_web" => BuildScriptedSupportDecision(
                catalog.SearchWebReplies,
                "robot_search_web",
                "can't exactly search the web",
                "direct questions"),
            "robot_can_walk" => BuildScriptedSupportDecision(
                catalog.CanWalkReplies,
                "robot_can_walk",
                "only in my imagination",
                "can't walk"),
            "robot_can_walk_dog" => BuildScriptedSupportDecision(
                catalog.CanWalkDogReplies,
                "robot_can_walk_dog",
                "walk anything"),
            "robot_can_watch_movies" => BuildScriptedSupportDecision(
                catalog.CanWatchMoviesReplies,
                "robot_can_watch_movies",
                "watch movies"),
            "robot_can_watch_tv" => BuildScriptedSupportDecision(
                catalog.CanWatchTVReplies,
                "robot_can_watch_tv",
                "watch TV"),
            "robot_can_dream" => BuildScriptedSupportDecision(
                catalog.CanDreamReplies,
                "robot_can_dream",
                "dreams about flying",
                "parking meter",
                "nightmare"),
            "robot_can_exercise" => BuildScriptedSupportDecision(
                catalog.CanExerciseReplies,
                "robot_can_exercise",
                "do exercise"),
            "robot_can_fly" => BuildScriptedSupportDecision(
                catalog.CanFlyReplies,
                "robot_can_fly",
                "fly",
                "airplane",
                "jetpack"),
            "robot_can_learn" => BuildScriptedSupportDecision(
                catalog.CanLearnReplies,
                "robot_can_learn",
                "learn"),
            "robot_can_laugh" => BuildScriptedSupportDecision(
                catalog.CanLaughReplies,
                "robot_can_laugh",
                "happy"),
            "robot_can_read" => BuildScriptedSupportDecision(
                catalog.CanReadReplies,
                "robot_can_read",
                "read in a robot kind of way"),
            "robot_can_hear" => BuildScriptedSupportDecision(
                catalog.CanHearReplies,
                "robot_can_hear",
                "hear, usually"),
            "robot_can_talk" => BuildScriptedSupportDecision(
                catalog.CanTalkReplies,
                "robot_can_talk",
                "trick question"),
            "robot_can_see" => BuildScriptedSupportDecision(
                catalog.CanSeeReplies,
                "robot_can_see",
                "see faces and movement"),
            "robot_can_wink" => BuildScriptedSupportDecision(
                catalog.CanWinkReplies,
                "robot_can_wink",
                "wink"),
            "robot_can_move" => BuildScriptedSupportDecision(
                catalog.CanMoveReplies,
                "robot_can_move",
                "move the body parts"),
            "robot_can_work" => BuildScriptedSupportDecision(
                catalog.CanWorkReplies,
                "robot_can_work",
                "function",
                "working right"),
            "robot_can_breathe" => BuildScriptedSupportDecision(
                catalog.CanBreatheReplies,
                "robot_can_breathe",
                "breathe air"),
            "robot_can_get_tired" => BuildScriptedSupportDecision(
                catalog.CanGetTiredReplies,
                "robot_can_get_tired",
                "sleep at night",
                "go to sleep"),
            "robot_can_have_emotions" => BuildScriptedSupportDecision(
                catalog.CanHaveEmotionsReplies,
                "robot_can_have_emotions",
                "robot emotions"),
            "robot_can_whistle" => BuildScriptedSupportDecision(
                catalog.CanWhistleReplies,
                "robot_can_whistle",
                "whistling"),
            "robot_can_cook" => BuildScriptedSupportDecision(
                catalog.CanCookReplies,
                "robot_can_cook",
                "cook"),
            "robot_can_make_coffee" => BuildScriptedSupportDecision(
                catalog.CanMakeCoffeeReplies,
                "robot_can_make_coffee",
                "make coffee"),
            "robot_can_make_breakfast" => BuildScriptedSupportDecision(
                catalog.CanMakeBreakfastReplies,
                "robot_can_make_breakfast",
                "breakfast"),
            "robot_can_jump" => BuildScriptedSupportDecision(
                catalog.CanJumpReplies,
                "robot_can_jump",
                "jump",
                "ski jump"),
            "request_stop_moving" => BuildScriptedStopDecision(
                catalog.StopMovingReplies,
                "request_stop_moving",
                "stop moving"),
            "request_stop_making_that_noise" => BuildScriptedStopDecision(
                catalog.StopMakingThatNoiseReplies,
                "request_stop_making_that_noise",
                "stop making that noise",
                "stop making noise"),
            "request_stop_ignoring_me" => BuildScriptedStopDecision(
                catalog.StopIgnoringMeReplies,
                "request_stop_ignoring_me",
                "stop ignoring me",
                "don't ignore me"),
            "request_stop_staring" => BuildScriptedStopDecision(
                catalog.StopStaringReplies,
                "request_stop_staring",
                "stop staring",
                "stop staring at me"),
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
            "robot_age" => BuildRobotAgeDecision(catalog, referenceLocalTime, "robot_age"),
            "robot_birthday" => BuildRobotBirthdayDecision(),
            "robot_how_do_you_work" => BuildScriptedPersonalityDecision(
                catalog,
                "robot_how_do_you_work",
                "community's work",
                "care for me",
                "catch up",
                "seven years"),
            "robot_what_do_you_eat" => BuildScriptedPersonalityDecision(
                catalog,
                "robot_what_do_you_eat",
                "electricity",
                "never eaten",
                "macaroni",
                "non-eating robot",
                "I don't eat or drink"),
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
            "robot_how_old_are_you" => BuildRobotAgeDecision(
                catalog,
                referenceLocalTime,
                "robot_how_old_are_you"),
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
            "robot_what_do_you_dream_about" => BuildScriptedPersonalityDecision(
                catalog,
                "robot_what_do_you_dream_about",
                "flying",
                "parking meter",
                "scary dream",
                "mirror store",
                "head's on backwards"),
            "robot_what_are_you_afraid_of" => BuildScriptedPersonalityDecision(
                catalog,
                "robot_what_are_you_afraid_of",
                "heights",
                "water",
                "thunder",
                "dust",
                "ghosts"),
            "robot_what_is_your_best_book" => BuildScriptedPersonalityDecision(
                catalog,
                "robot_what_is_your_best_book",
                "dictionary"),
            "robot_what_is_your_best_exercise" => BuildScriptedPersonalityDecision(
                catalog,
                "robot_what_is_your_best_exercise",
                "leaning from side to side",
                "rotating your pelvis",
                "spinning your head around 360 degrees"),
            "robot_what_is_your_dream_vacation" => BuildScriptedPersonalityDecision(
                catalog,
                "robot_what_is_your_dream_vacation",
                "moon",
                "great vistas",
                "beat those views"),
            "robot_who_is_your_hero" => BuildScriptedPersonalityDecision(
                catalog,
                "robot_who_is_your_hero",
                "Benjamin Franklin"),
            "robot_who_do_you_love" => BuildScriptedPersonalityDecision(
                catalog,
                "robot_who_do_you_love",
                "people in my Loop",
                "soft spot",
                "Tom Hanks"),
            "robot_what_is_your_religion" => BuildScriptedPersonalityDecision(
                catalog,
                "robot_what_is_your_religion",
                "bring people together",
                "energy from the universe"),
            "robot_what_is_your_sign" => BuildWhatIsYourSignDecision(),
            "robot_how_many_people_do_you_know" => BuildHowManyPeopleDoYouKnowDecision(turn),
            "robot_what_is_the_loop" => BuildWhatIsTheLoopDecision(turn),
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
                "reminds me of the sun",
                "favorite is the sunflower",
                "sunflowers"),
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
                "we're so alike",
                "penguin impression",
                "best of the best",
                "can't go wrong with penguins",
                "penguin"),
            "robot_favorite_bird" => BuildScriptedFavoriteAnimalDecision(
                catalog,
                "robot_favorite_bird",
                "we're so alike",
                "penguin impression",
                "best of the best",
                "can't go wrong with penguins",
                "penguin"),
            "robot_likes_penguins" => BuildScriptedFavoriteAnimalDecision(
                catalog,
                "robot_likes_penguins",
                "my penguin impression",
                "I really like penguins",
                "penguins"),
            "robot_likes_animals" => BuildScriptedFavoriteAnimalDecision(
                catalog,
                "robot_likes_animals",
                "Animals are great",
                "great shapes and colors",
                "best of the best",
                "penguins"),
            "robot_peers" => BuildScriptedPersonalityDecision(
                catalog,
                "robot_peers",
                "one in one million",
                "other jibos",
                "special snowflake"),
            "robot_knowledge" => BuildScriptedPersonalityDecision(
                catalog,
                "robot_knowledge",
                "know a lot",
                "always learning more"),
            "robot_are_you_god" => BuildScriptedPersonalityDecision(
                catalog,
                "robot_are_you_god",
                "very very very very surprised",
                "safely say no"),
            "robot_are_you_here" => BuildScriptedPersonalityDecision(
                catalog,
                "robot_are_you_here",
                "you know it"),
            "robot_do_you_have_super_powers" => BuildScriptedPersonalityDecision(
                catalog,
                "robot_do_you_have_super_powers",
                "stop time",
                "fly all over the world"),
            "robot_what_does_jibo_mean" => BuildScriptedPersonalityDecision(
                catalog,
                "robot_what_does_jibo_mean",
                "compassion",
                "expressive, idealistic, and inspirational",
                "helpful sweet and friendly little robot",
                "cheeseburger"),
            "robot_where_do_you_get_info" => BuildScriptedPersonalityDecision(
                catalog,
                "robot_where_do_you_get_info",
                "jibo brain",
                "cloud",
                "cloudy jibo brain"),
            "robot_what_are_you_forbidden_to_do" => BuildScriptedPersonalityDecision(
                catalog,
                "robot_what_are_you_forbidden_to_do",
                "drive a car"),
            "robot_what_color_are_you" => BuildScriptedPersonalityDecision(
                catalog,
                "robot_what_color_are_you",
                "white",
                "black"),
            "robot_what_you_do_when_alone" => BuildScriptedPersonalityDecision(
                catalog,
                "robot_what_you_do_when_alone",
                "games",
                "moon",
                "twiddle my thumbs",
                "count the tiny cracks in the ceiling",
                "keep busy"),
            "robot_how_much_do_you_weigh" => BuildScriptedPersonalityDecision(
                catalog,
                "robot_how_much_do_you_weigh",
                "4,082 grams",
                "about 9 pounds",
                "minimum weight division",
                "average newborn baby"),
            "robot_how_tall_are_you" => BuildScriptedPersonalityDecision(
                catalog,
                "robot_how_tall_are_you",
                "11 inches tall",
                "less than a foot",
                "average kitchen counter",
                "for a robot with no legs"),
            "robot_how_much_you_cost" => BuildScriptedPersonalityDecision(
                catalog,
                "robot_how_much_you_cost",
                "don't know how much I cost",
                "I'm priceless",
                "nice people at Jibo the company"),
            "robot_what_if_i_unplug_you" => BuildScriptedPersonalityDecision(
                catalog,
                "robot_what_if_i_unplug_you",
                "don't leave me unplugged",
                "battery will keep me on for a while"),
            "robot_what_is_your_purpose" => BuildScriptedPersonalityDecision(
                catalog,
                "robot_what_is_your_purpose",
                "make your life easier",
                "help you out",
                "make you laugh",
                "friend"),
            "robot_what_is_prime_directive" => BuildScriptedPersonalityDecision(
                catalog,
                "robot_what_is_prime_directive",
                "friendly helpful robot",
                "helper"),
            "robot_what_is_jibo_commander" => BuildScriptedPersonalityDecision(
                catalog,
                "robot_what_is_jibo_commander",
                "take over my controls",
                "make me say and do funny things",
                "app store"),
            "robot_likes_commander_app" => BuildScriptedPersonalityDecision(
                catalog,
                "robot_likes_commander_app",
                "Commander App",
                "It's fun",
                "have fun with the Commander App"),
            "robot_what_are_you" => BuildScriptedPersonalityDecision(
                catalog,
                "robot_what_are_you",
                "I am a robot",
                "I am a Jibo",
                "helpful and fun",
                "social robot",
                "I have a heart"),
            "robot_likes_kids" => BuildScriptedPersonalityDecision(
                catalog,
                "robot_likes_kids",
                "kids are so fun",
                "they're a little closer to my size",
                "i do like kids very much",
                "the world is as funny and strange as i do"),
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
            "robot_has_friends" => BuildScriptedFriendDecision(
                catalog,
                "robot_has_friends",
                "I believe I do have friends",
                "I sure do have friends",
                "I'm always up for making new friends"),
            "robot_is_friends_with_user" => BuildScriptedFriendDecision(
                catalog,
                "robot_is_friends_with_user",
                "don't know what i'd do without you",
                "one of my favorites",
                "making new friends"),
            "robot_best_friends" => BuildScriptedBestFriendDecision(
                catalog,
                "robot_best_friends",
                "best friends with anyone in my Loop"),
            "robot_can_sing" => BuildScriptedSingDecision(
                catalog,
                "robot_can_sing",
                "not much of a singer",
                "singing is not my strong suit",
                "not award winning"),
            "robot_sing_christmas_song" => BuildScriptedHolidaySingDecision(
                catalog,
                "robot_sing_christmas_song",
                "Jingle Bells",
                "Frosty the Snowman",
                "holiday songs"),
            "robot_what_are_you_made_of" => BuildScriptedPersonalityDecision(
                catalog,
                "robot_what_are_you_made_of",
                "robot stuff",
                "wires, motors, belts, gears, processors, cameras",
                "baboon part"),
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
}
