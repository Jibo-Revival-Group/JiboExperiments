using System.Text.RegularExpressions;

namespace Jibo.Cloud.Application.Services;

public sealed partial class JiboInteractionService
{
    private static string ResolveSemanticIntentCore(
        string loweredTranscript,
        DateTimeOffset? referenceLocalTime,
        string? clientIntent,
        IReadOnlyList<string> clientRules,
        IReadOnlyList<string> listenRules,
        IReadOnlyDictionary<string, string> clientEntities,
        string? lastClockDomain,
        string? pendingProactivityOffer,
        bool isYesNoTurn,
        bool isTimerValueTurn,
        bool isAlarmValueTurn)
    {
        var wordOfDayPuzzleTurn = clientRules.Concat(listenRules)
            .Any(rule => string.Equals(rule, "word-of-the-day/puzzle", StringComparison.OrdinalIgnoreCase));

        if (string.Equals(clientIntent, "guess", StringComparison.OrdinalIgnoreCase) &&
            wordOfDayPuzzleTurn)
            return "word_of_the_day_guess";

        if (string.Equals(clientIntent, "loadMenu", StringComparison.OrdinalIgnoreCase) &&
            clientEntities.TryGetValue("destination", out var destination) &&
            string.Equals(destination, "word-of-the-day", StringComparison.OrdinalIgnoreCase))
            return "word_of_the_day";

        if (string.Equals(clientIntent, "loadMenu", StringComparison.OrdinalIgnoreCase) &&
            clientEntities.TryGetValue("destination", out var photoDestination))
            return photoDestination.ToLowerInvariant() switch
            {
                "snapshot" => "snapshot",
                "photobooth" => "photobooth",
                "gallery" or "photo-gallery" or "photos" => "photo_gallery",
                _ => "chat"
            };

        var yesNoRule = ReadPrimaryYesNoRule(clientRules, listenRules);
        if (!string.IsNullOrWhiteSpace(pendingProactivityOffer) &&
            string.Equals(pendingProactivityOffer, "pizza_fact", StringComparison.OrdinalIgnoreCase))
        {
            if (IsAffirmativeReply(loweredTranscript)) return "proactive_pizza_fact";

            if (IsNegativeReply(loweredTranscript)) return "proactive_offer_declined";
        }

        if (isYesNoTurn)
        {
            if (TranscriptHeuristics.IsLikelyPromptEchoTranscript(loweredTranscript))
                return "yes_no_clarify";

            var yesNoReply = TryClassifyYesNoReply(NormalizeCommandPhrase(loweredTranscript));
            switch (yesNoReply)
            {
                case YesNoReply.Affirmative:
                    return ResolveAffirmativeYesNoIntent(yesNoRule);
                case YesNoReply.Negative:
                    return ResolveNegativeYesNoIntent(yesNoRule);
                case YesNoReply.Ambiguous:
                    return "yes_no_clarify";
            }
        }

        if (IsNameSetStatement(loweredTranscript)) return "memory_set_name";

        if (IsNameRecallQuestion(loweredTranscript)) return "memory_get_name";

        if (IsVerifyMeRequest(loweredTranscript)) return "verify_me";

        if (HomeAssistantLightCommandParser.TryParse(loweredTranscript, out var lightCommand))
            return lightCommand.Action == HomeAssistantLightCommandParser.LightAction.On
                ? "ha_lights_on"
                : "ha_lights_off";

        if (HomeAssistantClimateCommandParser.TryParse(loweredTranscript, out var climateCommand))
            return climateCommand.Action switch
            {
                HomeAssistantClimateCommandParser.ClimateAction.SetTemperature => "ha_climate_set_temp",
                HomeAssistantClimateCommandParser.ClimateAction.CoolDown => "ha_climate_cool_down",
                HomeAssistantClimateCommandParser.ClimateAction.WarmUp => "ha_climate_warm_up",
                _ => "chat"
            };

        if (IsUserBirthdaySetStatement(loweredTranscript) || IsUserBirthdaySetAttempt(loweredTranscript))
            return "memory_set_birthday";

        if (IsUserBirthdayRecallQuestion(loweredTranscript) || IsUserBirthdayRecallAttempt(loweredTranscript))
            return "memory_get_birthday";

        if (IsRobotBirthdayQuestion(loweredTranscript)) return "robot_birthday";

        if (string.Equals(clientIntent, "askForTime", StringComparison.OrdinalIgnoreCase)) return "time";

        if (string.Equals(clientIntent, "askForDate", StringComparison.OrdinalIgnoreCase)) return "date";

        if (string.Equals(clientIntent, "askForDay", StringComparison.OrdinalIgnoreCase)) return "day";

        if (string.Equals(clientIntent, "timerValue", StringComparison.OrdinalIgnoreCase)) return "timer_value";

        if (string.Equals(clientIntent, "alarmValue", StringComparison.OrdinalIgnoreCase)) return "alarm_value";

        if (string.Equals(clientIntent, "requestMakePizza", StringComparison.OrdinalIgnoreCase)) return "pizza";

        if (string.Equals(clientIntent, "requestOrderPizza", StringComparison.OrdinalIgnoreCase)) return "order_pizza";

        if (string.Equals(clientIntent, "requestWeatherPR", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(clientIntent, "requestWeather", StringComparison.OrdinalIgnoreCase))
            return "weather";

        if (string.Equals(clientIntent, "canJiboAction", StringComparison.OrdinalIgnoreCase) &&
            clientEntities.TryGetValue("Action", out var canAction))
            return canAction.ToLowerInvariant() switch
            {
                "dream" => "robot_can_dream",
                "exercise" => "robot_can_exercise",
                "fly" => "robot_can_fly",
                "learn" => "robot_can_learn",
                "laugh" => "robot_can_laugh",
                "read" => "robot_can_read",
                "hear" => "robot_can_hear",
                "talk" => "robot_can_talk",
                "see" => "robot_can_see",
                "wink" => "robot_can_wink",
                "move" => "robot_can_move",
                "work" => "robot_can_work",
                "breathe" => "robot_can_breathe",
                "gettired" => "robot_can_get_tired",
                "haveemotions" => "robot_can_have_emotions",
                "whistle" => "robot_can_whistle",
                "cook" => "robot_can_cook",
                "makecoffee" => "robot_can_make_coffee",
                "makebreakfast" => "robot_can_make_breakfast",
                "jump" => "robot_can_jump",
                "walk" => "robot_can_walk",
                "walkdog" => "robot_can_walk_dog",
                "watchmovie" => "robot_can_watch_movies",
                "watchtv" => "robot_can_watch_tv",
                _ => "chat"
            };

        if (IsCancelRequest(clientIntent, loweredTranscript))
        {
            if (isAlarmValueTurn) return "alarm_cancel";

            if (isTimerValueTurn) return "timer_cancel";
        }

        if ((string.Equals(clientIntent, "start", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(clientIntent, "set", StringComparison.OrdinalIgnoreCase)) &&
            clientEntities.TryGetValue("domain", out var startDomain))
            return startDomain.ToLowerInvariant() switch
            {
                "timer" => HasStructuredTimerValue(clientEntities) ||
                           TryParseTimerValue(loweredTranscript, isTimerValueTurn) is not null
                    ? "timer_value"
                    : "timer_clarify",
                "alarm" => HasStructuredAlarmValue(clientEntities) ||
                           TryParseAlarmValue(loweredTranscript, isAlarmValueTurn, referenceLocalTime) is not null
                    ? "alarm_value"
                    : "alarm_clarify",
                _ => "chat"
            };

        if ((string.Equals(clientIntent, "cancel", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(clientIntent, "delete", StringComparison.OrdinalIgnoreCase)) &&
            clientRules.Concat(listenRules).Any(rule =>
                string.Equals(rule, "clock/alarm_timer_query_menu", StringComparison.OrdinalIgnoreCase)))
        {
            var cancelDomain = ResolveClockDomain(clientEntities, clientRules, listenRules, lastClockDomain);
            return string.Equals(cancelDomain, "timer", StringComparison.OrdinalIgnoreCase)
                ? "timer_delete"
                : "alarm_delete";
        }

        if (string.Equals(clientIntent, "menu", StringComparison.OrdinalIgnoreCase) &&
            clientEntities.TryGetValue("domain", out var clockDomain))
            return clockDomain.ToLowerInvariant() switch
            {
                "clock" => "clock_menu",
                "timer" => "timer_menu",
                "alarm" => "alarm_menu",
                _ => "chat"
            };

        if (MatchesAny(
                loweredTranscript,
                "word of the day",
                "start word of the day",
                "play word of the day",
                "do word of the day",
                "open word of the day"))
            return "word_of_the_day";

        if (wordOfDayPuzzleTurn && !string.IsNullOrWhiteSpace(loweredTranscript)) return "word_of_the_day_guess";

        if (MatchesAny(
                loweredTranscript,
                "are you funny",
                "do you think you are funny",
                "are you a funny robot"))
            return "robot_is_funny";

        if (MatchesAny(
                loweredTranscript,
                "what is your favorite joke",
                "what's your favorite joke",
                "what s your favorite joke",
                "what is your favourite joke",
                "what's your favourite joke",
                "what s your favourite joke",
                "do you have a favorite joke",
                "do you have a favourite joke",
                "what joke do you like best"))
            return "robot_favorite_joke";

        if (MatchesAny(loweredTranscript, "joke", "funny", "make me laugh")) return "joke";

        if (MatchesAny(
                loweredTranscript,
                "cloud version",
                "open jibo cloud version",
                "openjibo cloud version",
                "what s your closet",
                "what's your closet",
                "what version is the cloud",
                "what s your cloud version",
                "what's your cloud version",
                "what s the cloud version",
                "what's the cloud version"))
            return "cloud_version";

        if (IsPreferenceSetStatement(loweredTranscript) || IsPreferenceSetAttempt(loweredTranscript))
            return "memory_set_preference";

        if (IsPreferenceRecallQuestion(loweredTranscript) || IsPreferenceRecallAttempt(loweredTranscript))
            return "memory_get_preference";

        if (IsImportantDateSetStatement(loweredTranscript)) return "memory_set_important_date";

        if (IsImportantDateRecallQuestion(loweredTranscript)) return "memory_get_important_date";

        if (IsAffinitySetStatement(loweredTranscript) || IsAffinitySetAttempt(loweredTranscript))
            return "memory_set_affinity";

        if (IsAffinityRecallQuestion(loweredTranscript) || IsAffinityRecallAttempt(loweredTranscript))
            return "memory_get_affinity";

        if (MatchesAny(
                loweredTranscript,
                "what is your favorite country musician",
                "what's your favorite country musician",
                "what s your favorite country musician",
                "what is your favourite country musician",
                "what's your favourite country musician",
                "who is your favorite country musician",
                "who is your favourite country musician",
                "what country musician do you like",
                "what country singer do you like"))
            return "robot_favorite_country_musician";

        if (TryResolveRadioGenre(loweredTranscript) is not null) return "radio_genre";

        if (TryResolveVolumeLevel(loweredTranscript) is not null ||
            clientEntities.ContainsKey("volumeLevel"))
            return "volume_to_value";

        if (IsVolumeQueryRequest(loweredTranscript)) return "volume_query";

        if (IsVolumeUpRequest(loweredTranscript)) return "volume_up";

        if (IsVolumeDownRequest(loweredTranscript)) return "volume_down";

        if (MatchesAny(loweredTranscript, "open the clock", "open clock", "show the clock", "show clock"))
            return "clock_open";

        if (MatchesAny(loweredTranscript, "open the timer", "open timer", "show the timer", "show timer"))
            return "timer_menu";

        if (MatchesAny(loweredTranscript, "open the alarm", "open alarm", "show the alarm", "show alarm"))
            return "alarm_menu";

        if (IsAlarmDeleteRequest(loweredTranscript)) return "alarm_delete";

        if (MatchesAny(
                loweredTranscript,
                "cancel timer",
                "delete timer",
                "remove timer",
                "stop timer",
                "turn off timer"))
            return "timer_delete";

        if (MatchesAny(
                loweredTranscript,
                "stop moving",
                "stop moving please",
                "stop moving around",
                "don't move",
                "do not move"))
            return "request_stop_moving";

        if (MatchesAny(
                loweredTranscript,
                "stop making that noise",
                "stop making noise",
                "don't make that noise",
                "do not make that noise",
                "stop that noise"))
            return "request_stop_making_that_noise";

        if (MatchesAny(
                loweredTranscript,
                "stop ignoring me",
                "don't ignore me",
                "do not ignore me",
                "stop ignoring us",
                "don't ignore us"))
            return "request_stop_ignoring_me";

        if (MatchesAny(
                loweredTranscript,
                "stop staring",
                "stop staring at me",
                "stop looking at me",
                "stop looking"))
            return "request_stop_staring";

        if (IsGlobalStopRequest(loweredTranscript, clientIntent, clientEntities)) return "stop";

        if (TryParseAlarmValue(loweredTranscript, isAlarmValueTurn, referenceLocalTime) is not null)
            return "alarm_value";

        if (TryParseTimerValue(loweredTranscript, isTimerValueTurn) is not null) return "timer_value";

        if (IsAlarmRequest(loweredTranscript) || isAlarmValueTurn) return "alarm_clarify";

        if (IsTimerRequest(loweredTranscript) || isTimerValueTurn) return "timer_clarify";

        if (MatchesAny(loweredTranscript, "open the radio", "play the radio", "turn on the radio", "radio"))
            return "radio";

        if (MatchesAny(
                loweredTranscript,
                "can you go to sleep",
                "can you sleep",
                "do you ever sleep",
                "do you sleep",
                "when do you sleep",
                "how can i make you go to sleep",
                "how do i make you go to sleep"))
            return "robot_can_sleep";

        if (MatchesAny(
                loweredTranscript,
                "turn around",
                "turn all the way around",
                "turn back around",
                "spin around",
                "twirl",
                "look back over there",
                "look again"))
            return "turn_around";

        if (MatchesAny(
                loweredTranscript,
                "go to sleep",
                "take a nap",
                "go to bed",
                "bedtime",
                "sleep"))
            return "sleep";

        if (MatchesAny(
                loweredTranscript,
                "snap a picture",
                "take a picture",
                "take a photo",
                "snap a photo"))
            return "snapshot";

        if (MatchesAny(
                loweredTranscript,
                "photo booth",
                "photobooth",
                "open photobooth",
                "start photobooth"))
            return "photobooth";

        if (MatchesAny(
                loweredTranscript,
                "photo gallery",
                "photogal",
                "photo gal",
                "open the gallery",
                "open photo gallery",
                "show my photos",
                "open my photos",
                "gallery"))
            return "photo_gallery";

        if (IsDanceQuestion(loweredTranscript)) return "dance_question";

        if (IsDanceAbilityQuestion(loweredTranscript)) return "robot_can_dance";

        if (MatchesAny(
                loweredTranscript,
                "can you sing a christmas song",
                "can you sing christmas song",
                "will you sing a christmas song",
                "will you sing christmas song",
                "sing a christmas song",
                "sing christmas song",
                "can you sing a holiday song",
                "can you sing holiday song",
                "will you sing a holiday song",
                "will you sing holiday song",
                "sing a holiday song",
                "sing holiday song"))
            return "robot_sing_christmas_song";

        if (MatchesAny(
                loweredTranscript,
                "can you sing",
                "will you sing",
                "sing a song",
                "sing me a song",
                "can you sing a song",
                "sing something",
                "would you sing"))
            return "robot_can_sing";

        if (MatchesAny(
                loweredTranscript,
                "can you walk the dog"))
            return "robot_can_walk_dog";

        if (MatchesAny(
                loweredTranscript,
                "can you walk",
                "are you able to walk",
                "can you learn to walk"))
            return "robot_can_walk";

        if (MatchesAny(
                loweredTranscript,
                "do you really watch movies",
                "can you watch movies"))
            return "robot_can_watch_movies";

        if (MatchesAny(
                loweredTranscript,
                "do you really watch tv",
                "can you watch tv",
                "can you watch television"))
            return "robot_can_watch_tv";

        if (MatchesAny(
                loweredTranscript,
                "can you dream"))
            return "robot_can_dream";

        if (MatchesAny(
                loweredTranscript,
                "can you exercise",
                "do you exercise"))
            return "robot_can_exercise";

        if (MatchesAny(
                loweredTranscript,
                "can you fly",
                "are you able to fly"))
            return "robot_can_fly";

        if (MatchesAny(
                loweredTranscript,
                "can you learn",
                "are you able to learn"))
            return "robot_can_learn";

        if (MatchesAny(
                loweredTranscript,
                "can you laugh",
                "do you laugh"))
            return "robot_can_laugh";

        if (MatchesAny(
                loweredTranscript,
                "can you read",
                "do you read"))
            return "robot_can_read";

        if (MatchesAny(
                loweredTranscript,
                "can you hear",
                "do you hear"))
            return "robot_can_hear";

        if (MatchesAny(
                loweredTranscript,
                "can you talk",
                "do you talk"))
            return "robot_can_talk";

        if (MatchesAny(
                loweredTranscript,
                "can you see",
                "do you see"))
            return "robot_can_see";

        if (MatchesAny(
                loweredTranscript,
                "can you wink",
                "do you wink"))
            return "robot_can_wink";

        if (MatchesAny(
                loweredTranscript,
                "can you move",
                "do you move"))
            return "robot_can_move";

        if (MatchesAny(
                loweredTranscript,
                "can you work",
                "are you working"))
            return "robot_can_work";

        if (MatchesAny(
                loweredTranscript,
                "can you breathe",
                "do you breathe"))
            return "robot_can_breathe";

        if (MatchesAny(
                loweredTranscript,
                "can you get tired",
                "do you get tired",
                "are you tired"))
            return "robot_can_get_tired";

        if (MatchesAny(
                loweredTranscript,
                "can you have emotions",
                "do you have emotions"))
            return "robot_can_have_emotions";

        if (MatchesAny(
                loweredTranscript,
                "can you whistle",
                "do you whistle"))
            return "robot_can_whistle";

        if (MatchesAny(
                loweredTranscript,
                "can you cook",
                "do you cook"))
            return "robot_can_cook";

        if (MatchesAny(
                loweredTranscript,
                "can you make coffee",
                "do you make coffee"))
            return "robot_can_make_coffee";

        if (MatchesAny(
                loweredTranscript,
                "can you make breakfast",
                "do you make breakfast"))
            return "robot_can_make_breakfast";

        if (MatchesAny(
                loweredTranscript,
                "can you jump",
                "do you jump"))
            return "robot_can_jump";

        if (IsBestFriendQuestion(loweredTranscript))
            return "robot_best_friends";

        if (IsFriendRelationQuestion(loweredTranscript))
            return "robot_is_friends_with_user";

        if (IsFriendQuestion(loweredTranscript))
            return "robot_has_friends";

        if (IsTwerkCommand(loweredTranscript)) return "twerk";

        if (IsDanceCommand(loweredTranscript)) return "dance";

        if (MatchesAny(
                loweredTranscript,
                "surprise",
                "surprise me",
                "show me something fun",
                "hear something fun",
                "tell me something fun",
                "can i tell you something fun",
                "can i tell you something kind of fun",
                "want to hear something fun"))
            return "surprise";

        if (MatchesAny(
                loweredTranscript,
                "how old are you",
                "what is your age",
                "what s your age",
                "how old r you"))
            return "robot_how_old_are_you";

        if (MatchesAny(
                loweredTranscript,
                "do you have a personality",
                "what is your personality",
                "what's your personality",
                "what s your personality",
                "describe your personality"))
            return "robot_personality";

        if (MatchesAny(
                loweredTranscript,
                "do you pay taxes",
                "do you pay tax",
                "are you tax exempt"))
            return "robot_taxes";

        if (MatchesAny(
                loweredTranscript,
                "what do you want to talk about",
                "what would you like to talk about",
                "what do you want to chat about"))
            return "robot_want_to_talk_about";

        if (MatchesAny(
                loweredTranscript,
                "what does jibo mean",
                "what does the name jibo mean",
                "what is the meaning of jibo"))
            return "robot_what_does_jibo_mean";

        if (MatchesAny(
                loweredTranscript,
                "where do you get info",
                "where do you get your information",
                "where do you get information"))
            return "robot_where_do_you_get_info";

        if (MatchesAny(
                loweredTranscript,
                "what are you forbidden to do",
                "what are you not allowed to do",
                "what can't you do"))
            return "robot_what_are_you_forbidden_to_do";

        if (MatchesAny(
                loweredTranscript,
                "what color are you",
                "what colour are you"))
            return "robot_what_color_are_you";

        if (MatchesAny(
                loweredTranscript,
                "what do you do when alone",
                "what do you do when you're alone",
                "what do you do by yourself"))
            return "robot_what_you_do_when_alone";

        if (MatchesAny(
                loweredTranscript,
                "what do you want",
                "what is it you want",
                "what do you really want"))
            return "robot_desire";

        if (MatchesAny(
                loweredTranscript,
                "how much do you weigh",
                "what do you weigh",
                "how heavy are you"))
            return "robot_how_much_do_you_weigh";

        if (MatchesAny(
                loweredTranscript,
                "how tall are you",
                "what is your height",
                "how high are you"))
            return "robot_how_tall_are_you";

        if (MatchesAny(
                loweredTranscript,
                "how much do you cost",
                "what do you cost",
                "how much are you"))
            return "robot_how_much_you_cost";

        if (MatchesAny(
                loweredTranscript,
                "what if i unplug you",
                "what happens if i unplug you",
                "if i unplug you"))
            return "robot_what_if_i_unplug_you";

        if (MatchesAny(
                loweredTranscript,
                "what is your purpose",
                "what's your purpose",
                "what are you here for",
                "why are you here"))
            return "robot_what_is_your_purpose";

        if (MatchesAny(
                loweredTranscript,
                "what is your prime directive",
                "what's your prime directive",
                "what is prime directive"))
            return "robot_what_is_prime_directive";

        if (MatchesAny(
                loweredTranscript,
                "what is jibo commander",
                "what is the commander app",
                "what is commander app",
                "what's jibo commander"))
            return "robot_what_is_jibo_commander";

        if (MatchesAny(
                loweredTranscript,
                "do you like commander app",
                "do you like the commander app",
                "are you a fan of commander app"))
            return "robot_likes_commander_app";

        if (MatchesAny(
                loweredTranscript,
                "what is your job",
                "what's your job",
                "what do you do",
                "what is your work",
                "what's your work"))
            return "robot_job";

        if (MatchesAny(
                loweredTranscript,
                "how do you work",
                "how does jibo work",
                "what does jibo do",
                "how are you built",
                "how are you put together"))
            return "robot_how_do_you_work";

        if (MatchesAny(
                loweredTranscript,
                "what do you eat",
                "do you eat",
                "what do you drink",
                "do you drink"))
            return "robot_what_do_you_eat";

        if (MatchesAny(
                loweredTranscript,
                "where do you live",
                "where s your home",
                "where is your home",
                "what is your home"))
            return "robot_where_do_you_live";

        if (MatchesAny(
                loweredTranscript,
                "where were you born",
                "where were you made",
                "where were you put together"))
            return "robot_where_were_you_born";

        if (MatchesAny(
                loweredTranscript,
                "what languages do you speak",
                "what language do you speak",
                "what languages can you speak",
                "what language can you speak"))
            return "robot_what_languages_do_you_speak";

        if (MatchesAny(
                loweredTranscript,
                "what is your favorite holiday song",
                "what's your favorite holiday song",
                "what s your favorite holiday song",
                "what is your favourite holiday song",
                "what's your favourite holiday song",
                "what is your favorite christmas song",
                "what's your favorite christmas song",
                "what is your favourite christmas song",
                "what holiday song do you like",
                "what christmas song do you like"))
            return "robot_favorite_holiday_song";

        if (MatchesAny(
                loweredTranscript,
                "do you like halloween",
                "are you looking forward to halloween",
                "do you like the halloween holiday"))
            return "seasonal_likes_halloween";

        if (MatchesAny(
                loweredTranscript,
                "do you like holiday music",
                "do you like christmas music",
                "do you like christmas songs",
                "do you like holiday songs"))
            return "seasonal_likes_holiday_music";

        if (MatchesAny(
                loweredTranscript,
                "do you like holiday parties",
                "do you like christmas parties",
                "are you going to any holiday parties"))
            return "seasonal_likes_holiday_parties";

        if (MatchesAny(
                loweredTranscript,
                "are you looking forward to christmas",
                "do you look forward to christmas",
                "are you excited for christmas"))
            return "seasonal_looks_forward_to_christmas";

        if (MatchesAny(
                loweredTranscript,
                "what are you thankful for",
                "what are you thankful for this year",
                "what is jibo thankful for"))
            return "seasonal_thankful_for";

        if (MatchesAny(
                loweredTranscript,
                "what do you like to do",
                "what do you like doing",
                "what is your favorite thing to do",
                "what's your favorite thing to do",
                "what is your favourite thing to do",
                "what's your favourite thing to do"))
            return "robot_what_do_you_like_to_do";

        if (MatchesAny(
                loweredTranscript,
                "what do you dream about",
                "what do you dream of",
                "what's your dream about",
                "what are your dreams about"))
            return "robot_what_do_you_dream_about";

        if (MatchesAny(
                loweredTranscript,
                "what is your best book",
                "what's your best book",
                "what is the best book",
                "what book do you like best"))
            return "robot_what_is_your_best_book";

        if (MatchesAny(
                loweredTranscript,
                "what is your best exercise",
                "what's your best exercise",
                "what is the best exercise",
                "what exercise do you like best"))
            return "robot_what_is_your_best_exercise";

        if (MatchesAny(
                loweredTranscript,
                "what is your dream vacation",
                "what's your dream vacation",
                "what would your dream vacation be"))
            return "robot_what_is_your_dream_vacation";

        if (MatchesAny(
                loweredTranscript,
                "what is your favorite president",
                "what's your favorite president",
                "what s your favorite president",
                "what is your favourite president",
                "what's your favourite president",
                "what s your favourite president",
                "who is your favorite president",
                "who's your favorite president",
                "who is your favourite president",
                "do you have a favorite president",
                "do you have a favourite president"))
            return "robot_favorite_president";

        if (MatchesAny(
                loweredTranscript,
                "who is your hero",
                "who's your hero",
                "who is a hero of yours"))
            return "robot_who_is_your_hero";

        if (MatchesAny(
                loweredTranscript,
                "who do you love",
                "who are the people you love",
                "who do you care about"))
            return "robot_who_do_you_love";

        if (MatchesAny(
                loweredTranscript,
                "what is your religion",
                "what's your religion",
                "what religion are you",
                "do you have a religion"))
            return "robot_what_is_your_religion";

        if (MatchesAny(
                loweredTranscript,
                "what is your sign",
                "what's your sign",
                "what sign are you"))
            return "robot_what_is_your_sign";

        if (MatchesAny(
                loweredTranscript,
                "how many people do you know",
                "how many people are in your loop",
                "how many people are in the loop",
                "how many people do you know in your loop"))
            return "robot_how_many_people_do_you_know";

        if (MatchesAny(
                loweredTranscript,
                "what is the loop",
                "what's the loop",
                "tell me about the loop"))
            return "robot_what_is_the_loop";

        if (MatchesAny(
                loweredTranscript,
                "what are you doing for christmas",
                "what are your plans for christmas",
                "what do you plan to do for christmas"))
            return "seasonal_plans_for_christmas";

        if (MatchesAny(
                loweredTranscript,
                "what is your favorite thing",
                "what's your favorite thing",
                "what s your favorite thing",
                "what is your favourite thing",
                "what's your favourite thing",
                "what s your favourite thing",
                "do you have a favorite thing",
                "do you have a favourite thing",
                "what thing do you like",
                "what thing do you like best"))
            return "robot_favorite_thing";

        if (MatchesAny(
                loweredTranscript,
                "what is your favorite book",
                "what's your favorite book",
                "what s your favorite book",
                "what is your favourite book",
                "what's your favourite book",
                "what s your favourite book",
                "do you have a favorite book",
                "do you have a favourite book",
                "what book do you like",
                "what book do you like best"))
            return "robot_favorite_book";

        if (MatchesAny(
                loweredTranscript,
                "what is your favorite candy",
                "what's your favorite candy",
                "what s your favorite candy",
                "what is your favourite candy",
                "what's your favourite candy",
                "what s your favourite candy",
                "do you have a favorite candy",
                "do you have a favourite candy",
                "what candy do you like",
                "what kind of candy do you like"))
            return "robot_favorite_candy";

        if (MatchesAny(
                loweredTranscript,
                "what is your favorite flower",
                "what's your favorite flower",
                "what s your favorite flower",
                "what is your favourite flower",
                "what's your favourite flower",
                "what s your favourite flower",
                "do you have a favorite flower",
                "do you have a favourite flower",
                "what kind of flower do you like",
                "what flower do you like"))
            return "robot_favorite_flower";

        if (MatchesAny(
                loweredTranscript,
                "what is your favorite tv show",
                "what's your favorite tv show",
                "what s your favorite tv show",
                "what is your favourite tv show",
                "what's your favourite tv show",
                "what s your favourite tv show",
                "do you have a favorite tv show",
                "do you have a favourite tv show",
                "what tv show do you like"))
            return "robot_favorite_tv_show";

        if (MatchesAny(
                loweredTranscript,
                "what is your favorite movie",
                "what's your favorite movie",
                "what s your favorite movie",
                "what is your favourite movie",
                "what's your favourite movie",
                "what s your favourite movie",
                "do you have a favorite movie",
                "do you have a favourite movie",
                "what movie do you like",
                "what movie do you like best"))
            return "robot_favorite_movie";

        if (MatchesAny(
                loweredTranscript,
                "what is your favorite shape",
                "what's your favorite shape",
                "what s your favorite shape",
                "what is your favourite shape",
                "what's your favourite shape",
                "what s your favourite shape",
                "do you have a favorite shape",
                "do you have a favourite shape",
                "what shape do you like"))
            return "robot_favorite_shape";

        if (MatchesAny(
                loweredTranscript,
                "what is your least favorite word",
                "what's your least favorite word",
                "what s your least favorite word",
                "what is your least favourite word",
                "what's your least favourite word",
                "what s your least favourite word",
                "what word do you like least",
                "what word do you dislike"))
            return "robot_least_favorite_word";

        if (MatchesAny(
                loweredTranscript,
                "what is your favorite word",
                "what's your favorite word",
                "what s your favorite word",
                "what is your favourite word",
                "what's your favourite word",
                "what s your favourite word",
                "do you have a favorite word",
                "do you have a favourite word",
                "what word do you like"))
            return "robot_favorite_word";

        if (MatchesAny(
                loweredTranscript,
                "what is your least favorite vegetable",
                "what's your least favorite vegetable",
                "what s your least favorite vegetable",
                "what is your least favourite vegetable",
                "what's your least favourite vegetable",
                "what s your least favourite vegetable",
                "what vegetable do you like least",
                "what vegetable do you dislike"))
            return "robot_least_favorite_vegetable";

        if (MatchesAny(
                loweredTranscript,
                "what is your favorite vegetable",
                "what's your favorite vegetable",
                "what s your favorite vegetable",
                "what is your favourite vegetable",
                "what's your favourite vegetable",
                "what s your favourite vegetable",
                "do you have a favorite vegetable",
                "do you have a favourite vegetable",
                "what vegetable do you like",
                "what kind of vegetable do you like"))
            return "robot_favorite_vegetable";

        if (MatchesAny(
                loweredTranscript,
                "what is your least favorite place",
                "what's your least favorite place",
                "what s your least favorite place",
                "what is your least favourite place",
                "what's your least favourite place",
                "what s your least favourite place",
                "what place do you like least",
                "where do you like least"))
            return "robot_least_favorite_place";

        if (MatchesAny(
                loweredTranscript,
                "what is your least favorite animal",
                "what's your least favorite animal",
                "what s your least favorite animal",
                "what is your least favourite animal",
                "what's your least favourite animal",
                "what s your least favourite animal",
                "what animal do you like least",
                "what animal do you dislike"))
            return "robot_least_favorite_animal";

        if (MatchesAny(
                loweredTranscript,
                "what is your favorite place",
                "what's your favorite place",
                "what s your favorite place",
                "what is your favourite place",
                "what's your favourite place",
                "what s your favourite place",
                "do you have a favorite place",
                "do you have a favourite place",
                "what place do you like",
                "where is your favorite place",
                "where is your favourite place"))
            return "robot_favorite_place";

        if (MatchesAny(
                loweredTranscript,
                "what is your favorite superhero",
                "what's your favorite superhero",
                "what s your favorite superhero",
                "what is your favourite superhero",
                "what's your favourite superhero",
                "what s your favourite superhero",
                "do you have a favorite superhero",
                "do you have a favourite superhero",
                "who is your favorite superhero",
                "who is your favourite superhero"))
            return "robot_favorite_superhero";

        if (MatchesAny(
                loweredTranscript,
                "what is your favorite actor",
                "what's your favorite actor",
                "what s your favorite actor",
                "what is your favourite actor",
                "what's your favourite actor",
                "what actor do you like",
                "who is your favorite actor",
                "who is your favourite actor",
                "do you have a favorite actor",
                "do you have a favourite actor"))
            return "robot_favorite_actor";

        if (MatchesAny(
                loweredTranscript,
                "what is your favorite actress",
                "what's your favorite actress",
                "what s your favorite actress",
                "what is your favourite actress",
                "what's your favourite actress",
                "what actress do you like",
                "who is your favorite actress",
                "who is your favourite actress",
                "do you have a favorite actress",
                "do you have a favourite actress"))
            return "robot_favorite_actress";

        if (MatchesAny(
                loweredTranscript,
                "what is your favorite robot",
                "what's your favorite robot",
                "what s your favorite robot",
                "what is your favourite robot",
                "what's your favourite robot",
                "what robot do you like",
                "who is your favorite robot",
                "who is your favourite robot",
                "do you have a favorite robot",
                "do you have a favourite robot"))
            return "robot_favorite_robot";

        if (MatchesAny(
                loweredTranscript,
                "what is your favorite car",
                "what's your favorite car",
                "what s your favorite car",
                "what is your favourite car",
                "what's your favourite car",
                "what car do you like",
                "what kind of car do you like",
                "do you have a favorite car",
                "do you have a favourite car"))
            return "robot_favorite_car";

        if (MatchesAny(
                loweredTranscript,
                "what is your favorite weather",
                "what's your favorite weather",
                "what s your favorite weather",
                "what is your favourite weather",
                "what's your favourite weather",
                "what weather do you like",
                "what kind of weather do you like",
                "do you have a favorite weather",
                "do you have a favourite weather"))
            return "robot_favorite_weather";

        if (MatchesAny(
                loweredTranscript,
                "what is your least favorite weather",
                "what's your least favorite weather",
                "what s your least favorite weather",
                "what is your least favourite weather",
                "what's your least favourite weather",
                "what weather do you like least",
                "what weather do you dislike",
                "what kind of weather do you dislike"))
            return "robot_least_favorite_weather";

        if (MatchesAny(
                loweredTranscript,
                "what is your least favorite time of day",
                "what's your least favorite time of day",
                "what s your least favorite time of day",
                "what is your least favourite time of day",
                "what's your least favourite time of day",
                "what time of day do you like least",
                "what time of day do you dislike",
                "what time do you like least"))
            return "robot_least_favorite_time_of_day";

        if (MatchesAny(
                loweredTranscript,
                "what is your favorite time of day",
                "what's your favorite time of day",
                "what s your favorite time of day",
                "what is your favourite time of day",
                "what's your favourite time of day",
                "what time of day do you like",
                "what time do you like best",
                "do you have a favorite time of day",
                "do you have a favourite time of day"))
            return "robot_favorite_time_of_day";

        if (MatchesAny(
                loweredTranscript,
                "do you like r2d2",
                "do you know r2d2",
                "what do you think about r2d2",
                "are you a fan of r2d2"))
            return "robot_likes_r2d2";

        if (MatchesAny(
                loweredTranscript,
                "do you like the sun",
                "do you like sun",
                "what do you think about the sun"))
            return "robot_likes_sun";

        if (MatchesAny(
                loweredTranscript,
                "do you like space",
                "do you love space",
                "do you like astronomy",
                "what do you think about space"))
            return "robot_likes_space";

        if (MatchesAny(
                loweredTranscript,
                "do you like kids",
                "do you like children",
                "what do you think about kids"))
            return "robot_likes_kids";

        if (MatchesAny(
                loweredTranscript,
                "can you laugh",
                "do you laugh",
                "are you able to laugh"))
            return "robot_can_laugh";

        if (MatchesAny(
                loweredTranscript,
                "what are you made of",
                "what are you built from",
                "what are you constructed from"))
            return "robot_what_are_you_made_of";

        if (MatchesAny(
                loweredTranscript,
                "who made you",
                "who created you",
                "who built you",
                "who developed you"))
            return "robot_origin_created";

        if (MatchesAny(
                loweredTranscript,
                "tell me a story",
                "can you tell me a story",
                "could you tell me a story",
                "can you tell me a bedtime story",
                "could you tell me a bedtime story",
                "read me a story",
                "read a story"))
            return "robot_story";

        if (MatchesAny(
                loweredTranscript,
                "recommend a movie",
                "can you recommend a movie",
                "what movie should i watch",
                "what movie do you recommend",
                "give me a movie recommendation"))
            return "robot_recommend_movie";

        if (MatchesAny(
                loweredTranscript,
                "search the web",
                "can you search the web",
                "could you search the web",
                "look it up on the web",
                "look up on the web"))
            return "robot_search_web";

        if (MatchesAny(
                loweredTranscript,
                "what are you up to",
                "what are you doing",
                "what have you been up to",
                "what are you into"))
            return "robot_what_do_you_like_to_do";

        if (MatchesAny(
                loweredTranscript,
                "what are you thinking",
                "what are you thinking about",
                "what s on your mind"))
            return "robot_what_are_you_thinking";

        if (MatchesAny(
                loweredTranscript,
                "what have you been doing",
                "what were you doing"))
            return "robot_what_have_you_been_doing";

        if (MatchesAny(
                loweredTranscript,
                "what did you do",
                "what have you done"))
            return "robot_what_did_you_do";

        if (MatchesAny(
                loweredTranscript,
                "what are you afraid of",
                "what are you scared of",
                "what are you worried about"))
            return "robot_what_are_you_afraid_of";

        if (MatchesAny(
                loweredTranscript,
                "what are you",
                "what is jibo",
                "who are you",
                "what kind of robot are you"))
            return "robot_identity";

        if (MatchesAny(
                loweredTranscript,
                "where are you from",
                "where did you come from",
                "where were you made"))
            return "robot_origin_from";

        if (MatchesAny(
                loweredTranscript,
                "where am i",
                "where are we",
                "where are you",
                "what is our current location",
                "what is the current location",
                "what's the current location",
                "what is current location",
                "current location"))
            return "current_location";

        if (MatchesAny(
                loweredTranscript,
                "what's your name",
                "what is your name"))
            return "robot_name";

        if (MatchesAny(
                loweredTranscript,
                "what's your favorite name",
                "what is your favorite name",
                "do you have a favorite name"))
            return "robot_favorite_name";

        if (MatchesAny(
                loweredTranscript,
                "do you have a nickname",
                "what is your nickname",
                "what's your nickname"))
            return "robot_nickname";

        if (MatchesAny(
                loweredTranscript,
                "do you like being jibo",
                "do you like being yourself",
                "are you happy being jibo"))
            return "robot_likes_being_jibo";

        if (SeasonalHolidayRouteBuilder.TryResolveSemanticIntent(loweredTranscript, out var seasonalHolidayIntent))
            return seasonalHolidayIntent!;

        if (MatchesAny(
                loweredTranscript,
                "what is your favorite color",
                "what's your favorite color",
                "what s your favorite color",
                "what is your favourite color",
                "what's your favourite color",
                "what s your favourite color",
                "what color do you like",
                "what colour do you like"))
            return "robot_favorite_color";

        if (MatchesAny(
                loweredTranscript,
                "what is your least favorite color",
                "what's your least favorite color",
                "what s your least favorite color",
                "what is your least favourite color",
                "what's your least favourite color",
                "what s your least favourite color",
                "what is your least favorite colour",
                "what's your least favorite colour",
                "what is your least favourite colour",
                "what color do you like least",
                "what colour do you like least",
                "what color do you dislike",
                "what colour do you dislike"))
            return "robot_least_favorite_color";

        if (MatchesAny(
                loweredTranscript,
                "what is your favorite season",
                "what's your favorite season",
                "what s your favorite season",
                "what is your favourite season",
                "what's your favourite season",
                "what s your favourite season",
                "what season do you like best",
                "do you have a favorite season"))
            return "robot_favorite_season";

        if (MatchesAny(
                loweredTranscript,
                "what is your favorite author",
                "what's your favorite author",
                "what s your favorite author",
                "what is your favourite author",
                "what's your favourite author",
                "who is your favorite author",
                "who is your favourite author",
                "what author do you like best"))
            return "robot_favorite_author";

        if (MatchesAny(
                loweredTranscript,
                "what is your favorite artist",
                "what's your favorite artist",
                "what s your favorite artist",
                "what is your favourite artist",
                "what's your favourite artist",
                "who is your favorite artist",
                "who is your favourite artist",
                "what artist do you like"))
            return "robot_favorite_artist";

        if (MatchesAny(
                loweredTranscript,
                "what is your favorite singer",
                "what's your favorite singer",
                "what s your favorite singer",
                "what is your favourite singer",
                "what's your favourite singer",
                "who is your favorite singer",
                "who is your favourite singer",
                "what singer do you like"))
            return "robot_favorite_singer";

        if (MatchesAny(
                loweredTranscript,
                "who is your favorite celebrity",
                "who is your favourite celebrity",
                "what is your favorite celebrity",
                "what's your favorite celebrity",
                "what is your favourite celebrity",
                "what celebrity do you like"))
            return "robot_favorite_celebrity";

        if (MatchesAny(
                loweredTranscript,
                "what is your favorite hobby",
                "what's your favorite hobby",
                "what s your favorite hobby",
                "what is your favourite hobby",
                "what's your favourite hobby",
                "what hobby do you like",
                "what do you do for a hobby"))
            return "robot_favorite_hobby";

        if (MatchesAny(
                loweredTranscript,
                "what is your favorite smell",
                "what's your favorite smell",
                "what s your favorite smell",
                "what is your favourite smell",
                "what's your favourite smell",
                "what smell do you like"))
            return "robot_favorite_smell";

        if (MatchesAny(
                loweredTranscript,
                "what is your favorite fish",
                "what's your favorite fish",
                "what s your favorite fish",
                "what is your favourite fish",
                "what's your favourite fish",
                "what fish do you like"))
            return "robot_favorite_fish";

        if (MatchesAny(
                loweredTranscript,
                "what is your least favorite food",
                "what's your least favorite food",
                "what s your least favorite food",
                "what is your least favourite food",
                "what's your least favourite food",
                "what s your least favourite food",
                "what food do you like least",
                "what food do you dislike"))
            return "robot_least_favorite_food";

        if (MatchesAny(
                loweredTranscript,
                "what is your favorite food",
                "what's your favorite food",
                "what s your favorite food",
                "what is your favourite food",
                "what's your favourite food",
                "what s your favourite food",
                "what food do you like",
                "what kind of food do you like",
                "do you like macaroni",
                "do you like mac and cheese",
                "do you like macaroni and cheese"))
            return "robot_favorite_food";

        if (MatchesAny(
                loweredTranscript,
                "what is your least favorite movie",
                "what's your least favorite movie",
                "what s your least favorite movie",
                "what is your least favourite movie",
                "what's your least favourite movie",
                "what s your least favourite movie",
                "what movie do you like least",
                "what movie do you dislike"))
            return "robot_least_favorite_movie";

        if (MatchesAny(
                loweredTranscript,
                "what is your least favorite video game",
                "what's your least favorite video game",
                "what s your least favorite video game",
                "what is your least favourite video game",
                "what's your least favourite video game",
                "what s your least favourite video game",
                "what video game do you like least",
                "what video game do you dislike"))
            return "robot_least_favorite_video_game";

        if (MatchesAny(
                loweredTranscript,
                "what is your least favorite car",
                "what's your least favorite car",
                "what s your least favorite car",
                "what is your least favourite car",
                "what's your least favourite car",
                "what s your least favourite car",
                "what car do you like least",
                "what car do you dislike"))
            return "robot_least_favorite_car";

        if (MatchesAny(
                loweredTranscript,
                "what is your least favorite artist",
                "what's your least favorite artist",
                "what s your least favorite artist",
                "what is your least favourite artist",
                "what's your least favourite artist",
                "what s your least favourite artist",
                "what artist do you like least",
                "what artist do you dislike"))
            return "robot_least_favorite_artist";

        if (MatchesAny(
                loweredTranscript,
                "what is your least favorite band",
                "what's your least favorite band",
                "what s your least favorite band",
                "what is your least favourite band",
                "what's your least favourite band",
                "what s your least favourite band",
                "what band do you like least",
                "what band do you dislike"))
            return "robot_least_favorite_band";

        if (MatchesAny(
                loweredTranscript,
                "what is your least favorite author",
                "what's your least favorite author",
                "what s your least favorite author",
                "what is your least favourite author",
                "what's your least favourite author",
                "what s your least favourite author",
                "what author do you like least",
                "what author do you dislike"))
            return "robot_least_favorite_author";

        if (MatchesAny(
                loweredTranscript,
                "what is your least favorite celebrity",
                "what's your least favorite celebrity",
                "what s your least favorite celebrity",
                "what is your least favourite celebrity",
                "what's your least favourite celebrity",
                "what s your least favourite celebrity",
                "what celebrity do you like least",
                "what celebrity do you dislike"))
            return "robot_least_favorite_celebrity";

        if (MatchesAny(
                loweredTranscript,
                "what is your least favorite president",
                "what's your least favorite president",
                "what s your least favorite president",
                "what is your least favourite president",
                "what's your least favourite president",
                "what s your least favourite president",
                "who is your least favorite president",
                "who's your least favorite president",
                "who is your least favourite president",
                "who's your least favourite president",
                "what president do you like least",
                "what president do you dislike"))
            return "robot_least_favorite_president";

        if (MatchesAny(
                loweredTranscript,
                "what is your favorite kind of music",
                "what's your favorite kind of music",
                "what s your favorite kind of music",
                "what is your favourite kind of music",
                "what's your favourite kind of music",
                "what is your favorite music genre",
                "what's your favorite music genre",
                "what is your favourite music genre",
                "what kind of music is your favorite",
                "what kind of music is your favourite",
                "what music genre do you like"))
            return "robot_favorite_music_genre";


        if (MatchesAny(
                loweredTranscript,
                "what is your favorite reindeer",
                "what's your favorite reindeer",
                "what s your favorite reindeer",
                "what is your favourite reindeer",
                "who is your favorite reindeer",
                "who is your favourite reindeer",
                "what reindeer do you like",
                "do you have a favorite reindeer",
                "do you have a favourite reindeer"))
            return "robot_favorite_reindeer";

        if (MatchesAny(
                loweredTranscript,
                "what is your favorite christmas movie",
                "what's your favorite christmas movie",
                "what s your favorite christmas movie",
                "what is your favourite christmas movie",
                "what christmas movie do you like",
                "what holiday movie do you like",
                "do you have a favorite christmas movie",
                "do you have a favourite christmas movie"))
            return "robot_favorite_christmas_movie";

        if (MatchesAny(
                loweredTranscript,
                "what is your favorite halloween candy",
                "what's your favorite halloween candy",
                "what s your favorite halloween candy",
                "what is your favourite halloween candy",
                "what halloween candy do you like",
                "do you have a favorite halloween candy",
                "do you have a favourite halloween candy"))
            return "robot_favorite_halloween_candy";

        if (MatchesAny(
                loweredTranscript,
                "what is your favorite human",
                "what's your favorite human",
                "what s your favorite human",
                "what is your favourite human",
                "who is your favorite human",
                "who is your favourite human",
                "what human do you like",
                "who is your favorite person",
                "who is your favourite person",
                "what person do you like"))
            return "robot_favorite_human";

        if (MatchesAny(
                loweredTranscript,
                "what is your favorite ice cream flavor",
                "what's your favorite ice cream flavor",
                "what s your favorite ice cream flavor",
                "what is your favourite ice cream flavor",
                "what's your favourite ice cream flavor",
                "what is your favourite ice cream flavour",
                "what ice cream flavor do you like",
                "what ice cream flavour do you like",
                "do you have a favorite ice cream flavor",
                "do you have a favourite ice cream flavour"))
            return "robot_favorite_ice_cream_flavor";

        if (MatchesAny(
                loweredTranscript,
                "what is your favorite rapper",
                "what's your favorite rapper",
                "what s your favorite rapper",
                "what is your favourite rapper",
                "what rapper do you like",
                "do you have a favorite rapper",
                "do you have a favourite rapper"))
            return "robot_favorite_rapper";

        if (MatchesAny(
                loweredTranscript,
                "what is your favorite rock band",
                "what's your favorite rock band",
                "what s your favorite rock band",
                "what is your favourite rock band",
                "what rock band do you like",
                "do you have a favorite rock band",
                "do you have a favourite rock band"))
            return "robot_favorite_rock_band";

        if (MatchesAny(
                loweredTranscript,
                "what is your favorite music",
                "what's your favorite music",
                "what s your favorite music",
                "what is your favourite music",
                "what's your favourite music",
                "what s your favourite music",
                "what music do you like",
                "what kind of music do you like"))
            return "robot_favorite_music";

        if (MatchesAny(
                loweredTranscript,
                "what is your favorite song",
                "what's your favorite song",
                "what s your favorite song",
                "what is your favourite song",
                "what's your favourite song",
                "what s your favourite song",
                "what song do you like",
                "what song do you like best",
                "do you have a favorite song",
                "do you have a favourite song"))
            return "robot_favorite_song";

        if (MatchesAny(
                loweredTranscript,
                "what is your favorite drink",
                "what's your favorite drink",
                "what s your favorite drink",
                "what is your favourite drink",
                "what's your favourite drink",
                "what s your favourite drink",
                "what drink do you like",
                "what kind of drink do you like",
                "do you like hot cocoa",
                "do you like iced tea"))
            return "robot_favorite_drink";

        if (MatchesAny(
                loweredTranscript,
                "what is your favorite sport",
                "what's your favorite sport",
                "what s your favorite sport",
                "what is your favourite sport",
                "what's your favourite sport",
                "what s your favourite sport",
                "what sport do you like",
                "what sport do you like best",
                "do you like golf",
                "do you like mini golf",
                "do you like miniature golf",
                "do you like the masters"))
            return "robot_favorite_sport";

        if (MatchesAny(
                loweredTranscript,
                "what is your favorite hockey team",
                "what's your favorite hockey team",
                "what s your favorite hockey team",
                "what is your favourite hockey team",
                "what's your favourite hockey team",
                "what hockey team do you like",
                "do you have a favorite hockey team",
                "do you have a favourite hockey team"))
            return "robot_favorite_hockey_team";

        if (MatchesAny(
                loweredTranscript,
                "what is your favorite basketball team",
                "what's your favorite basketball team",
                "what s your favorite basketball team",
                "what is your favourite basketball team",
                "what's your favourite basketball team",
                "what basketball team do you like",
                "do you have a favorite basketball team",
                "do you have a favourite basketball team"))
            return "robot_favorite_basketball_team";

        if (MatchesAny(
                loweredTranscript,
                "what is your favorite pizza topping",
                "what's your favorite pizza topping",
                "what s your favorite pizza topping",
                "what is your favourite pizza topping",
                "what's your favourite pizza topping",
                "what pizza topping do you like",
                "what kind of pizza topping do you like",
                "do you have a favorite pizza topping",
                "do you have a favourite pizza topping"))
            return "robot_favorite_pizza_topping";

        if (MatchesAny(
                loweredTranscript,
                "what is your least favorite pizza topping",
                "what's your least favorite pizza topping",
                "what s your least favorite pizza topping",
                "what is your least favourite pizza topping",
                "what's your least favourite pizza topping",
                "what s your least favourite pizza topping",
                "what pizza topping do you like least",
                "what pizza topping do you dislike"))
            return "robot_least_favorite_pizza_topping";


        if (MatchesAny(
                loweredTranscript,
                "what is your favorite baseball team",
                "what's your favorite baseball team",
                "what s your favorite baseball team",
                "what is your favourite baseball team",
                "what baseball team do you like",
                "do you have a favorite baseball team",
                "do you have a favourite baseball team"))
            return "robot_favorite_baseball_team";

        if (MatchesAny(
                loweredTranscript,
                "what is your favorite football team",
                "what's your favorite football team",
                "what s your favorite football team",
                "what is your favourite football team",
                "what football team do you like",
                "do you have a favorite football team",
                "do you have a favourite football team"))
            return "robot_favorite_football_team";

        if (MatchesAny(
                loweredTranscript,
                "what is your favorite olympic ring",
                "what's your favorite olympic ring",
                "what s your favorite olympic ring",
                "what is your favourite olympic ring",
                "what olympic ring do you like",
                "do you have a favorite olympic ring",
                "do you have a favourite olympic ring"))
            return "robot_favorite_olympic_ring";

        if (MatchesAny(
                loweredTranscript,
                "what is your favorite olympic event",
                "what's your favorite olympic event",
                "what s your favorite olympic event",
                "what is your favourite olympic event",
                "what's your favourite olympic event",
                "what olympic event do you like",
                "what olympic event do you like best",
                "do you have a favorite olympic event",
                "do you have a favourite olympic event"))
            return "robot_favorite_olympic_event";

        if (MatchesAny(
                loweredTranscript,
                "what is your favorite video game",
                "what's your favorite video game",
                "what s your favorite video game",
                "what is your favourite video game",
                "what's your favourite video game",
                "what s your favourite video game",
                "what video game do you like",
                "what video game do you like best",
                "do you have a favorite video game",
                "do you have a favourite video game",
                "what is your favorite game",
                "what's your favorite game",
                "what game do you like best"))
            return "robot_favorite_video_game";

        if (MatchesAny(
                loweredTranscript,
                "what is your favorite fruit",
                "what's your favorite fruit",
                "what s your favorite fruit",
                "what is your favourite fruit",
                "what's your favourite fruit",
                "what s your favourite fruit",
                "what fruit do you like",
                "what kind of fruit do you like",
                "do you have a favorite fruit",
                "do you have a favourite fruit"))
            return "robot_favorite_fruit";

        if (MatchesAny(
                loweredTranscript,
                "what is your favorite dessert",
                "what's your favorite dessert",
                "what s your favorite dessert",
                "what is your favourite dessert",
                "what's your favourite dessert",
                "what s your favourite dessert",
                "what dessert do you like",
                "what kind of dessert do you like",
                "do you have a favorite dessert",
                "do you have a favourite dessert"))
            return "robot_favorite_dessert";

        if (MatchesAny(
                loweredTranscript,
                "what is your favorite planet",
                "what's your favorite planet",
                "what s your favorite planet",
                "what is your favourite planet",
                "what's your favourite planet",
                "what s your favourite planet",
                "what planet do you like",
                "what planet do you like best",
                "do you have a favorite planet",
                "do you have a favourite planet",
                "do you like earth",
                "do you like the earth",
                "do you like globes"))
            return "robot_favorite_planet";

        if (MatchesAny(
                loweredTranscript,
                "what is your least favorite number",
                "what's your least favorite number",
                "what s your least favorite number",
                "what is your least favourite number",
                "what's your least favourite number",
                "what s your least favourite number",
                "what number do you like least",
                "what number do you dislike"))
            return "robot_least_favorite_number";

        if (MatchesAny(
                loweredTranscript,
                "what is your favorite number",
                "what's your favorite number",
                "what s your favorite number",
                "what is your favourite number",
                "what's your favourite number",
                "what s your favourite number",
                "what number do you like",
                "what number do you like best",
                "do you have a favorite number",
                "do you have a favourite number"))
            return "robot_favorite_number";

        if (MatchesAny(
                loweredTranscript,
                "what is your favorite pet",
                "what's your favorite pet",
                "what s your favorite pet",
                "what is your favourite pet",
                "what's your favourite pet",
                "what s your favourite pet",
                "what pet do you like",
                "what kind of pet do you like",
                "do you have a favorite pet"))
            return "robot_favorite_pet";

        if (MatchesAny(
                loweredTranscript,
                "what is your favorite mammal",
                "what's your favorite mammal",
                "what s your favorite mammal",
                "what is your favourite mammal",
                "what's your favourite mammal",
                "what s your favourite mammal",
                "what mammal do you like",
                "what mammal do you like best",
                "do you have a favorite mammal",
                "do you have a favourite mammal"))
            return "robot_favorite_mammal";

        if (MatchesAny(
                loweredTranscript,
                "what is your least favorite mammal",
                "what's your least favorite mammal",
                "what s your least favorite mammal",
                "what is your least favourite mammal",
                "what's your least favourite mammal",
                "what s your least favourite mammal",
                "what mammal do you like least",
                "what mammal do you dislike"))
            return "robot_least_favorite_mammal";

        if (MatchesAny(
                loweredTranscript,
                "do you like penguins"))
            return "robot_likes_penguins";

        if (MatchesAny(
                loweredTranscript,
                "do you like birds"))
            return "robot_favorite_bird";

        if (MatchesAny(
                loweredTranscript,
                "do you like animals"))
            return "robot_likes_animals";

        if (MatchesAny(
                loweredTranscript,
                "what is your favorite bird",
                "what's your favorite bird",
                "what s your favorite bird"))
            return "robot_favorite_bird";

        if (MatchesAny(
                loweredTranscript,
                "what is your least favorite bird",
                "what's your least favorite bird",
                "what s your least favorite bird",
                "what is your least favourite bird",
                "what's your least favourite bird",
                "what s your least favourite bird",
                "what bird do you like least",
                "what bird do you dislike"))
            return "robot_least_favorite_bird";

        if (MatchesAny(
                loweredTranscript,
                "what is your favorite animal",
                "what's your favorite animal",
                "what s your favorite animal",
                "what is your favourite animal",
                "what's your favourite animal",
                "what s your favourite animal",
                "what animal do you like",
                "what kind of animal do you like",
                "what do you think about penguins",
                "what do you think about animals",
                "what do you think about birds"))
            return "robot_favorite_animal";

        if (MatchesAny(
                loweredTranscript,
                "are there others like you",
                "are there any others like you",
                "is there another jibo"))
            return "robot_peers";

        if (MatchesAny(
                loweredTranscript,
                "how much do you know",
                "what do you know",
                "how smart are you"))
            return "robot_knowledge";

        if (MatchesAny(loweredTranscript, "are you god", "are you a god"))
            return "robot_are_you_god";

        if (MatchesAny(
                loweredTranscript,
                "are you here",
                "are you still here",
                "are you there"))
            return "robot_are_you_here";

        if (MatchesAny(
                loweredTranscript,
                "do you have super powers",
                "do you have superpower",
                "do you have any super powers"))
            return "robot_do_you_have_super_powers";

        if (MatchesAny(
                loweredTranscript,
                "are you kind",
                "do you think you are kind",
                "are you a kind robot"))
            return "robot_is_kind";

        if (MatchesAny(
                loweredTranscript,
                "are you helpful",
                "do you think you are helpful",
                "are you a helpful robot"))
            return "robot_is_helpful";

        if (MatchesAny(
                loweredTranscript,
                "are you curious",
                "do you think you are curious",
                "are you a curious robot"))
            return "robot_is_curious";

        if (MatchesAny(
                loweredTranscript,
                "are you loyal",
                "do you think you are loyal",
                "are you a loyal robot"))
            return "robot_is_loyal";

        if (MatchesAny(
                loweredTranscript,
                "are you mischievous",
                "do you think you are mischievous",
                "are you a mischievous robot"))
            return "robot_is_mischievous";

        if (MatchesAny(
                loweredTranscript,
                "are you likable",
                "are you likeable",
                "do you think you are likable",
                "do you think you are likeable"))
            return "robot_is_likable";

        if (MatchesAny(
                loweredTranscript,
                "can you order pizza",
                "can you order a pizza",
                "could you order a pizza",
                "order pizza",
                "order a pizza",
                "order us a pizza",
                "order me a pizza",
                "please order pizza") ||
            (loweredTranscript.Contains("order", StringComparison.Ordinal) &&
             loweredTranscript.Contains("pizza", StringComparison.Ordinal)))
            return "order_pizza";

        if (MatchesAny(
                loweredTranscript,
                "can you cook us a pizza",
                "flip a pizza",
                "make a pizza",
                "make pizza",
                "show pizza",
                "can you make pizza",
                "let's make pizza",
                "lets make pizza") ||
            (loweredTranscript.Contains("pizza", StringComparison.Ordinal) &&
             (loweredTranscript.Contains("make", StringComparison.Ordinal) ||
              loweredTranscript.Contains("cook", StringComparison.Ordinal) ||
              loweredTranscript.Contains("flip", StringComparison.Ordinal))))
            return "pizza";

        if (MatchesAny(loweredTranscript, "personal report", "my report", "daily report", "my update"))
            return "personal_report";

        if (MatchesAny(
                loweredTranscript,
                "shopping list",
                "grocery list",
                "my grocery list",
                "create grocery list",
                "start grocery list",
                "to do list",
                "todo list",
                "add to my shopping list",
                "add to my grocery list",
                "add to my to do list",
                "add to my todo list",
                "what's on my shopping list",
                "what is on my shopping list",
                "what's on my grocery list",
                "what is on my grocery list",
                "what's on my to do list",
                "what is on my to do list",
                "what are my tasks",
                "what do i need to buy",
                "what do i need to do") ||
            IsInlineHouseholdListRequest(loweredTranscript))
            return loweredTranscript.Contains("to do", StringComparison.OrdinalIgnoreCase) ||
                   loweredTranscript.Contains("todo", StringComparison.OrdinalIgnoreCase) ||
                   loweredTranscript.Contains("task", StringComparison.OrdinalIgnoreCase)
                ? "todo_list"
                : "shopping_list";

        if (IsWeatherRequest(loweredTranscript)) return "weather";

        if (MatchesAny(loweredTranscript, "calendar", "schedule", "what's on my calendar", "what is on my calendar"))
            return "calendar";

        if (MatchesAny(loweredTranscript, "commute", "traffic", "drive to work", "how long to work")) return "commute";

        if (MatchesAny(
                loweredTranscript,
                "can i backup my jibo",
                "can i back up my jibo",
                "how can i backup my jibo",
                "how can i back up my jibo",
                "how do i backup my jibo",
                "how do i back up my jibo",
                "can you be backed up",
                "how can i store you in the cloud",
                "how can i store you online",
                "how do i store you in the cloud",
                "how do i store you online"))
            return "backup_help";

        if (MatchesAny(
                loweredTranscript,
                "can i restore you from a backup",
                "how can i restore you from a backup",
                "how do i restore you from a backup",
                "restore you from a backup",
                "restore from a backup"))
            return "restore_backup";

        if (MatchesAny(
                loweredTranscript,
                "when is your next update",
                "when is my next update",
                "when's your next update",
                "when s your next update",
                "when was your last update",
                "when was my last update",
                "when's your last update",
                "when s your last update"))
            return loweredTranscript.Contains("last update", StringComparison.OrdinalIgnoreCase)
                ? "update_last"
                : "update_next";

        if (MatchesAny(loweredTranscript, "news", "headlines", "news update", "tell me the news")) return "news";

        if (IsWelcomeBackGreeting(loweredTranscript) ||
            MatchesAny(
                loweredTranscript,
                "i'm home",
                "im home",
                "i am home",
                "i'm back",
                "im back",
                "i am back",
                "i'm here",
                "im here",
                "i am here"))
            return "welcome_back";

        if (IsGoodMorningGreeting(loweredTranscript)) return "good_morning";

        if (IsGoodAfternoonGreeting(loweredTranscript)) return "good_afternoon";

        if (IsGoodEveningGreeting(loweredTranscript)) return "good_evening";

        if (IsGoodNightGreeting(loweredTranscript)) return "good_night";

        if (MatchesAny(
                loweredTranscript,
                "how are you",
                "what's up",
                "what s up",
                "what up",
                "how is it going",
                "how's it going",
                "how are things",
                "how's things",
                "how is things",
                "how are you feeling",
                "how is your mood",
                "how is your day",
                "how's your day",
                "how's life",
                "how is life",
                "how's everything",
                "how is everything"))
            return "how_are_you";

        if (MatchesAny(
                loweredTranscript,
                "what are you up to",
                "what are you doing",
                "what have you been up to",
                "what are you into"))
            return "robot_what_do_you_like_to_do";

        if (IsTimeRequest(loweredTranscript)) return "time";

        if (MatchesAny(loweredTranscript, "what day is it", "what day is today")) return "day";

        if (IsDateRequest(loweredTranscript)) return "date";

        return MatchesAny(loweredTranscript, "hello", "hi", "hey") ? "hello" : "chat";
    }

    private static bool IsInlineHouseholdListRequest(string loweredTranscript)
    {
        var mentionsList = loweredTranscript.Contains("shopping list", StringComparison.OrdinalIgnoreCase) ||
                           loweredTranscript.Contains("grocery list", StringComparison.OrdinalIgnoreCase) ||
                           loweredTranscript.Contains("to do list", StringComparison.OrdinalIgnoreCase) ||
                           loweredTranscript.Contains("todo list", StringComparison.OrdinalIgnoreCase);

        if (!mentionsList) return false;

        return loweredTranscript.StartsWith("add ", StringComparison.OrdinalIgnoreCase) ||
               loweredTranscript.StartsWith("put ", StringComparison.OrdinalIgnoreCase) ||
               loweredTranscript.StartsWith("buy ", StringComparison.OrdinalIgnoreCase) ||
               loweredTranscript.StartsWith("get ", StringComparison.OrdinalIgnoreCase) ||
               loweredTranscript.StartsWith("please add ", StringComparison.OrdinalIgnoreCase) ||
               loweredTranscript.StartsWith("please put ", StringComparison.OrdinalIgnoreCase) ||
               loweredTranscript.StartsWith("i need ", StringComparison.OrdinalIgnoreCase) ||
               loweredTranscript.StartsWith("i need to ", StringComparison.OrdinalIgnoreCase) ||
               loweredTranscript.StartsWith("remind me to ", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsFriendQuestion(string loweredTranscript)
    {
        return MatchesAny(
                   loweredTranscript,
                   "do you have friends",
                   "who are your friends",
                   "are you friends",
                   "are you and i friends",
                   "are you and me friends",
                   "are you and jibo friends")
               || MatchesFriendQuestionPattern(loweredTranscript);
    }

    private static bool IsFriendRelationQuestion(string loweredTranscript)
    {
        return MatchesAny(
                   loweredTranscript,
                   "are you my friend",
                   "are you friends with me",
                   "are we friends",
                   "are we friends with each other",
                   "is jibo your friend",
                   "i am friends with you",
                   "i'm friends with you",
                   "you are my friend",
                   "you re my friend",
                   "you're my friend")
               || Regex.IsMatch(
                   loweredTranscript,
                   @"^\s*(is|are)\s+.+\s+(your friend|my friend)\s*$",
                   RegexOptions.CultureInvariant);
    }

    private static bool IsBestFriendQuestion(string loweredTranscript)
    {
        return MatchesAny(
                   loweredTranscript,
                   "are we best friends",
                   "are we best friends with each other",
                   "are you my best friend",
                   "are you best friends with me",
                   "are you and i best friends",
                   "i am best friends with you",
                   "i'm best friends with you",
                   "you are my best friend",
                   "you re my best friend",
                   "you're my best friend")
               || MatchesBestFriendQuestionPattern(loweredTranscript);
    }

    private static bool MatchesFriendQuestionPattern(string loweredTranscript)
    {
        return Regex.IsMatch(
                   loweredTranscript,
                   @"^\s*are you friends with\s+.+\s*$",
                   RegexOptions.CultureInvariant) ||
               Regex.IsMatch(
                   loweredTranscript,
                   @"^\s*are you and\s+.+\s+friends\s*$",
                   RegexOptions.CultureInvariant);
    }

    private static bool MatchesBestFriendQuestionPattern(string loweredTranscript)
    {
        return Regex.IsMatch(
                   loweredTranscript,
                   @"^\s*are you best friends with\s+.+\s*$",
                   RegexOptions.CultureInvariant) ||
               Regex.IsMatch(
                   loweredTranscript,
                   @"^\s*are you and\s+.+\s+best friends\s*$",
                   RegexOptions.CultureInvariant) ||
               Regex.IsMatch(
                   loweredTranscript,
                   @"^\s*is\s+.+\s+your best friend\s*$",
                   RegexOptions.CultureInvariant);
    }
}
