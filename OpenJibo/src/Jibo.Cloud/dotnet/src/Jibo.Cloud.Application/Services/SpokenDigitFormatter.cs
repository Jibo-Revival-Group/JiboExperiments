namespace Jibo.Cloud.Application.Services;

internal static class SpokenDigitFormatter
{
    private static readonly string[] DigitWords =
    [
        "zero",
        "one",
        "two",
        "three",
        "four",
        "five",
        "six",
        "seven",
        "eight",
        "nine"
    ];

    public static string Format(string digits)
    {
        if (string.IsNullOrWhiteSpace(digits)) return string.Empty;

        var spoken = new List<string>(digits.Length);
        foreach (var character in digits)
        {
            if (character is >= '0' and <= '9')
                spoken.Add(DigitWords[character - '0']);
        }

        return string.Join(" ", spoken);
    }
}
