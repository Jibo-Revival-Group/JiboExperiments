using Jibo.Cloud.Application.Abstractions;
using Jibo.Runtime.Abstractions;

namespace Jibo.Cloud.Application.Services;

public sealed partial class JiboInteractionService
{
    private static JiboInteractionDecision BuildCurrentLocationDecision(TurnContext turn)
    {
        var locationName = TryResolveCurrentLocationName(turn);
        if (string.IsNullOrWhiteSpace(locationName))
            return new JiboInteractionDecision(
                "current_location",
                "I'm not sure where we are right now.");

        return new JiboInteractionDecision(
            "current_location",
            $"We're at {NormalizeLocationForSpeech(locationName)} if I'm not mistaken.",
            ContextUpdates: ScriptedResponseDecisionBuilder.BuildScriptedResponseContextUpdates());
    }

    private static JiboInteractionDecision BuildOrderPizzaDecision()
    {
        return new JiboInteractionDecision(
            "order_pizza",
            "I can't do that yet, but I bet I'll be able to do that sometime in the near future.",
            "chitchat-skill",
            new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["esml"] =
                    "<speak>I can't do that yet, but I bet I'll be able to do that sometime in the near future.</speak>",
                ["mim_id"] = "RA_JBO_OrderPizza",
                ["mim_type"] = "announcement",
                ["prompt_id"] = "RA_JBO_OrderPizza_AN_01",
                ["prompt_sub_category"] = "AN"
            });
    }

    private JiboInteractionDecision BuildJokeDecision(JiboExperienceCatalog catalog)
    {
        var joke = randomizer.Choose(catalog.Jokes);
        return new JiboInteractionDecision(
            "joke",
            joke,
            "@be/joke",
            new Dictionary<string, object?>
            {
                ["replyType"] = "joke"
            });
    }

    private JiboInteractionDecision BuildRandomDanceDecision(JiboExperienceCatalog catalog)
    {
        var dance = randomizer.Choose(catalog.DanceAnimations);
        var replyText = randomizer.Choose(catalog.DanceReplies);
        return BuildDanceDecision("dance", dance, replyText);
    }

    private JiboInteractionDecision BuildDanceQuestionDecision(JiboExperienceCatalog catalog)
    {
        return new JiboInteractionDecision("dance_question", randomizer.Choose(catalog.DanceQuestionReplies));
    }

    private static JiboInteractionDecision BuildDanceDecision(string intentName, string dance, string replyText)
    {
        return new JiboInteractionDecision(
            intentName,
            replyText,
            "chitchat-skill",
            new Dictionary<string, object?>
            {
                ["esml"] =
                    $"<speak>Okay.<break size='0.2'/> Watch this.<anim cat='dance' filter='music, {dance}' /></speak>",
                ["mim_id"] = "runtime-chat",
                ["mim_type"] = "announcement"
            });
    }

    private static JiboInteractionDecision BuildVolumeControlDecision(string intentName, string globalIntent,
        string globalValue)
    {
        return new JiboInteractionDecision(
            intentName,
            "Opening volume controls.",
            "global_commands",
            new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["skillId"] = "@be/settings",
                ["globalIntent"] = globalIntent,
                ["volumeLevel"] = globalValue
            });
    }

    private static JiboInteractionDecision BuildSettingsVolumeDecision()
    {
        return new JiboInteractionDecision(
            "volume_query",
            "Opening volume controls.",
            "@be/settings",
            new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["skillId"] = "@be/settings",
                ["localIntent"] = "volumeQuery"
            });
    }

    private static JiboInteractionDecision BuildClockLaunchDecision(string intentName, string domain,
        string clockIntent, string replyText)
    {
        return new JiboInteractionDecision(
            intentName,
            replyText,
            "@be/clock",
            new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["skillId"] = "@be/clock",
                ["domain"] = domain,
                ["clockIntent"] = clockIntent
            });
    }

    private static JiboInteractionDecision BuildClockLaunchDecision(string domain, string replyText)
    {
        return BuildClockLaunchDecision($"{domain}_menu", domain, "menu", replyText);
    }

    private static JiboInteractionDecision BuildClockClarifyDecision(string intentName, string domain, string replyText)
    {
        return new JiboInteractionDecision(
            intentName,
            replyText,
            "@be/clock",
            new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["skillId"] = "@be/clock",
                ["domain"] = domain,
                ["clockIntent"] = "set"
            });
    }

    private static JiboInteractionDecision BuildYesNoClarifyDecision()
    {
        return new JiboInteractionDecision(
            "yes_no_clarify",
            "I heard both yes and no. Could you say that again?");
    }

    private static JiboInteractionDecision BuildTimerValueDecision(
        string loweredTranscript,
        bool allowImplicit,
        IReadOnlyDictionary<string, string> clientEntities)
    {
        var timer = TryReadStructuredTimerValue(clientEntities) ??
                    TryParseTimerValue(loweredTranscript, allowImplicit) ??
                    new ClockTimerValue("0", "1", "null");

        return new JiboInteractionDecision(
            "timer_value",
            "Setting your timer.",
            "@be/clock",
            new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["skillId"] = "@be/clock",
                ["domain"] = "timer",
                ["clockIntent"] = "start",
                ["hours"] = timer.Hours,
                ["minutes"] = timer.Minutes,
                ["seconds"] = timer.Seconds
            });
    }

    private static JiboInteractionDecision BuildAlarmValueDecision(
        string loweredTranscript,
        bool allowImplicit,
        DateTimeOffset? referenceLocalTime,
        IReadOnlyDictionary<string, string> clientEntities)
    {
        var alarm = TryReadStructuredAlarmValue(clientEntities) ??
                    TryParseAlarmValue(loweredTranscript, allowImplicit, referenceLocalTime) ??
                    new ClockAlarmValue("7:00", "am");

        return new JiboInteractionDecision(
            "alarm_value",
            "Setting your alarm.",
            "@be/clock",
            new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["skillId"] = "@be/clock",
                ["domain"] = "alarm",
                ["clockIntent"] = "start",
                ["time"] = alarm.Time,
                ["ampm"] = alarm.AmPm
            });
    }

    private static JiboInteractionDecision BuildRadioGenreLaunchDecision(string loweredTranscript)
    {
        var station = TryResolveRadioGenre(loweredTranscript) ?? "Country";

        return new JiboInteractionDecision(
            "radio_genre",
            $"Playing {FormatRadioGenreForSpeech(station)} on the radio.",
            "@be/radio",
            new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["skillId"] = "@be/radio",
                ["station"] = station
            });
    }

    private static JiboInteractionDecision BuildWordOfTheDayGuessDecision(
        IReadOnlyDictionary<string, string> clientEntities,
        string transcript,
        IReadOnlyList<string> listenAsrHints)
    {
        var guess = ResolveWordOfTheDayGuess(clientEntities, transcript, listenAsrHints);

        var reply = string.IsNullOrWhiteSpace(guess)
            ? "I heard your word of the day guess."
            : $"I heard {guess}.";

        return new JiboInteractionDecision(
            "word_of_the_day_guess",
            reply,
            "@be/word-of-the-day",
            new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["guess"] = guess,
                ["skillId"] = "@be/word-of-the-day",
                ["cloudResponseMode"] = "completion_only"
            });
    }

    private static string ResolveWordOfTheDayGuess(
        IReadOnlyDictionary<string, string> clientEntities,
        string transcript,
        IReadOnlyList<string> listenAsrHints)
    {
        if (clientEntities.TryGetValue("guess", out var guessValue) &&
            !string.IsNullOrWhiteSpace(guessValue))
            return guessValue;

        return WordOfDayGuessResolver.Resolve(transcript, listenAsrHints);
    }
}