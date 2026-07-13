namespace Jibo.Cloud.Application.Services;

public static class CountdownSpokenReplyFormatter
{
    public static string Format(string label, int daysUntil)
    {
        if (string.IsNullOrWhiteSpace(label))
            return "I didn't catch what you're asking about. Try asking how long until Christmas.";

        return daysUntil switch
        {
            0 => $"{label} is today.",
            1 => $"{label} is in 1 day.",
            _ => $"{label} is in {daysUntil} days."
        };
    }

    public static string FormatUnresolvedTarget()
        => "I didn't catch what you're asking about. Try asking how long until Christmas.";
}
