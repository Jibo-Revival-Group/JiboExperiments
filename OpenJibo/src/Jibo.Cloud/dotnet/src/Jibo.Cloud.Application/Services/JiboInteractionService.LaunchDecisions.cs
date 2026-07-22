namespace Jibo.Cloud.Application.Services;

public sealed partial class JiboInteractionService
{
    private static JiboInteractionDecision BuildWordOfTheDayLaunchDecision()
    {
        return new JiboInteractionDecision(
            "word_of_the_day",
            "Starting word of the day.",
            "@be/word-of-the-day",
            new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["domain"] = "word-of-the-day",
                ["skillId"] = "@be/word-of-the-day"
            });
    }

    private static JiboInteractionDecision BuildRadioLaunchDecision()
    {
        return new JiboInteractionDecision(
            "radio",
            "Opening the radio.",
            "@be/radio",
            new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["skillId"] = "@be/radio"
            });
    }

    private static JiboInteractionDecision BuildPhotoGalleryLaunchDecision()
    {
        return new JiboInteractionDecision(
            "photo_gallery",
            "Opening the photo gallery.",
            "@be/gallery",
            new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["skillId"] = "@be/gallery",
                ["localIntent"] = "menu"
            });
    }

    private static JiboInteractionDecision BuildPhotoCreateDecision(string intentName, string replyText,
        string localIntent)
    {
        return new JiboInteractionDecision(
            intentName,
            replyText,
            "@be/create",
            new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["skillId"] = "@be/create",
                ["localIntent"] = localIntent
            });
    }

    private static JiboInteractionDecision BuildStopDecision()
    {
        return new JiboInteractionDecision(
            "stop",
            "Stopping.",
            "@be/idle",
            new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["skillId"] = "@be/idle",
                ["globalIntent"] = "stop",
                ["nluDomain"] = "global_commands"
            });
    }

    private static JiboInteractionDecision BuildSleepDecision()
    {
        return new JiboInteractionDecision(
            "sleep",
            "Okay. Going to sleep.",
            "@be/idle",
            new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["skillId"] = "@be/idle",
                ["globalIntent"] = "sleep",
                ["nluDomain"] = "global_commands"
            },
            new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["sleepState"] = "sleeping"
            });
    }

    private static JiboInteractionDecision BuildWakeUpDecision()
    {
        // Stock BE's own Hub-disconnect fallback launches @be/greetings and
        // clears SLEEP. Use that same local route for the legacy requestWakeUp
        // command rather than trying to manufacture a cloud-side circadian event.
        return new JiboInteractionDecision(
            "wake_up",
            "Okay, I'm awake.",
            "@be/greetings",
            new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["skillId"] = "@be/greetings",
                ["legacyMimId"] = "RA_JBO_WakeUp"
            },
            new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["sleepState"] = "awake"
            });
    }

    private static JiboInteractionDecision BuildIdleGlobalCommandDecision(
        string intentName,
        string globalIntent,
        string replyText)
    {
        return new JiboInteractionDecision(
            intentName,
            replyText,
            "@be/idle",
            new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["skillId"] = "@be/idle",
                ["globalIntent"] = globalIntent,
                ["nluDomain"] = "global_commands"
            });
    }
}
