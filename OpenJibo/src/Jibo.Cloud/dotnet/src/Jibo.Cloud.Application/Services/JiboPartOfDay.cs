namespace Jibo.Cloud.Application.Services;

internal enum JiboPartOfDay
{
    Morning,
    Afternoon,
    Evening,
    Night
}

internal static class JiboPartOfDayExtensions
{
    internal static JiboPartOfDay GetPartOfDay(DateTimeOffset localTime)
    {
        var hours = localTime.Hour;
        if (hours > 5 && hours < 12) return JiboPartOfDay.Morning;
        if (hours < 18) return JiboPartOfDay.Afternoon;
        if (hours < 23) return JiboPartOfDay.Evening;
        return JiboPartOfDay.Night;
    }

    internal static bool TryGetClaimedPartOfDay(string greetingIntent, out JiboPartOfDay claimed)
    {
        claimed = default;
        switch (greetingIntent)
        {
            case "good_morning":
                claimed = JiboPartOfDay.Morning;
                return true;
            case "good_afternoon":
                claimed = JiboPartOfDay.Afternoon;
                return true;
            case "good_evening":
                claimed = JiboPartOfDay.Evening;
                return true;
            default:
                return false;
        }
    }

    internal static bool MatchesClaim(JiboPartOfDay actual, JiboPartOfDay claimed) => actual == claimed;

    internal static string ToClaimToken(this JiboPartOfDay partOfDay) =>
        partOfDay switch
        {
            JiboPartOfDay.Morning => "morning",
            JiboPartOfDay.Afternoon => "afternoon",
            JiboPartOfDay.Evening => "evening",
            _ => string.Empty
        };
}
