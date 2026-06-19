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
            var yesNoReply = TryClassifyYesNoReply(NormalizeCommandPhrase(loweredTranscript));
            switch (yesNoReply)
            {
                case YesNoReply.Affirmative:
                    return ResolveAffirmativeYesNoIntent(yesNoRule, listenRules);
                case YesNoReply.Negative:
                    return ResolveNegativeYesNoIntent(yesNoRule);
                case YesNoReply.Ambiguous:
                    return "yes_no_clarify";
            }
        }

        if (IsNameSetStatement(loweredTranscript)) return "memory_set_name";

        if (IsNameRecallQuestion(loweredTranscript)) return "memory_get_name";

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

        if (MatchesAny(loweredTranscript, "joke", "funny", "make me laugh")) return "joke";

        if (MatchesAny(
                loweredTranscript,
                "cloud version",
                "open jibo cloud version",
                "openjibo cloud version",
                "what version is the cloud",
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

        if (MatchesAny(loweredTranscript, "can you dance", "do you dance", "are you able to dance"))
            return "robot_can_dance";

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

        if (MatchesAny(loweredTranscript, "twerk")) return "twerk";

        if (MatchesAny(loweredTranscript, "dance", "boogie")) return "dance";

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
                "what is your favorite flower",
                "what's your favorite flower",
                "what s your favorite flower",
                "what is your favourite flower",
                "what's your favourite flower",
                "what s your favourite flower"))
            return "robot_favorite_flower";

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
                "what is your favorite season",
                "what's your favorite season",
                "what s your favorite season",
                "what season do you like best",
                "do you have a favorite season"))
            return "robot_favorite_season";

        if (MatchesAny(
                loweredTranscript,
                "what is your favorite food",
                "what's your favorite food",
                "what s your favorite food",
                "what is your favourite food",
                "what's your favourite food",
                "what s your favourite food",
                "what food do you like",
                "what kind of food do you like"))
            return "robot_favorite_food";

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
                "what do i need to do"))
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

        if (MatchesAny(loweredTranscript, "hello", "hi", "hey")) return "hello";

        return "chat";
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