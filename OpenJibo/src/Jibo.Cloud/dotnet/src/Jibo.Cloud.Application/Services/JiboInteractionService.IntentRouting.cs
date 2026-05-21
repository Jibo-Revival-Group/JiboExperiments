using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Jibo.Cloud.Application.Abstractions;
using Jibo.Cloud.Domain.Models;
using Jibo.Runtime.Abstractions;

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
            if (yesNoReply == YesNoReply.Affirmative) return ResolveAffirmativeYesNoIntent(yesNoRule);

            if (yesNoReply == YesNoReply.Negative) return ResolveNegativeYesNoIntent(yesNoRule);
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
                "look back over there",
                "look again"))
            return "spin_around";

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
            return "robot_age";

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
                "what do you want",
                "what is it you want",
                "what do you really want"))
            return "robot_desire";

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
                "what is your favorite animal",
                "what's your favorite animal",
                "what s your favorite animal",
                "what is your favourite animal",
                "what's your favourite animal",
                "what s your favourite animal",
                "what animal do you like",
                "what kind of animal do you like",
                "what is your favorite bird",
                "what's your favorite bird",
                "what s your favorite bird",
                "do you like penguins",
                "do you like animals",
                "do you like birds"))
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
                "to do list",
                "todo list",
                "add to my shopping list",
                "add to my to do list",
                "add to my todo list",
                "what's on my shopping list",
                "what is on my shopping list",
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
                "how are things",
                "how's things",
                "how is things",
                "how is your day",
                "how's your day"))
            return "how_are_you";

        if (MatchesAny(
                loweredTranscript,
                "what are you up to",
                "what are you doing",
                "what have you been up to",
                "what are you into"))
            return "robot_what_do_you_like_to_do";

        if (MatchesAny(loweredTranscript, "hello", "hi", "hey")) return "hello";

        if (IsTimeRequest(loweredTranscript)) return "time";

        if (MatchesAny(loweredTranscript, "what day is it", "what day is today")) return "day";

        if (IsDateRequest(loweredTranscript)) return "date";

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
