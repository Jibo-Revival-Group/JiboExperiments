namespace Jibo.Cloud.Application.Services;

public static class DiceRollSpokenReplyFormatter
{
    private const string InvalidSidesReply =
        "I can roll dice with 2 to 100 sides. Try asking me to roll a dice or roll a 20 sided die.";

    public static string Format(int sides, int result)
    {
        if (sides == 20 && result == 1)
            return "It landed on a 1. Critical failure!";

        return $"It landed on {result}.";
    }

    public static string FormatInvalidSides() => InvalidSidesReply;

    public static string? FormatEsml(int sides, int result, string spokenLine)
    {
        if (sides != 6 || result is < 1 or > 6) return null;

        return $"<speak><anim cat='jiboji' filter='roll-die-{result}' nonBlocking='true'/><break size='0.3'/> {spokenLine}</speak>";
    }
}
