using System.Globalization;
using System.Text.RegularExpressions;

namespace Jibo.Cloud.Application.Services;

public enum MathOperation
{
    Add,
    Subtract,
    Multiply,
    Divide,
    SquareRoot,
    Power
}

public readonly record struct MathQuery(
    MathOperation Operation,
    double Left,
    double? Right,
    string LeftSpoken,
    string? RightSpoken,
    string OperatorSpoken);

public readonly record struct MathEvaluationResult(
    bool IsSuccess,
    double Value,
    string? ErrorMessage = null);

public static class MathCommandParser
{
    private static readonly string[] CommandLeadPhrases =
    [
        "hey jibo",
        "hello jibo",
        "hi jibo",
        "jibo",
        "o",
        "oh",
        "so",
        "well",
        "um",
        "uh",
        "hmm",
        "erm",
        "ah",
        "please",
        "ok jibo",
        "okay jibo"
    ];

    private static readonly string[] QuestionPrefixes =
    [
        "what's",
        "whats",
        "what is",
        "what s",
        "calculate",
        "compute"
    ];

    private const string NumberPattern = @"(?<num>\d+(?:\.\d+)?|[a-z]+(?:[\s-]+[a-z]+)*)";

    private static readonly Regex SquareRootPattern = new(
        $@"^(?:the\s+)?square\s+root\s+of\s+{NumberPattern}\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex PowerPattern = new(
        $@"^{NumberPattern}\s+to\s+the\s+power\s+of\s+{NumberPattern}\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex SquaredPattern = new(
        $@"^{NumberPattern}\s+squared\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex CubedPattern = new(
        $@"^{NumberPattern}\s+cubed\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex BinaryPattern = new(
        $@"^{NumberPattern}\s+(?<op>plus|\+|minus|subtract|subtracted\s+by|-|\*|x|times|multiplied\s+by|divided\s+by|over|/)\s+{NumberPattern}\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static bool TryParse(string? transcript, out MathQuery query)
    {
        query = default;
        var normalized = NormalizeCommandPhrase(transcript);
        if (string.IsNullOrWhiteSpace(normalized)) return false;

        normalized = StripQuestionPrefix(normalized);
        if (string.IsNullOrWhiteSpace(normalized)) return false;

        if (TryParseSquareRoot(normalized, out query)) return true;
        if (TryParsePower(normalized, out query)) return true;
        if (TryParseSquared(normalized, out query)) return true;
        if (TryParseCubed(normalized, out query)) return true;
        return TryParseBinary(normalized, out query);
    }

    public static MathEvaluationResult Evaluate(MathQuery query)
    {
        return query.Operation switch
        {
            MathOperation.Add => Success(query.Left + query.Right!.Value),
            MathOperation.Subtract => Success(query.Left - query.Right!.Value),
            MathOperation.Multiply => Success(query.Left * query.Right!.Value),
            MathOperation.Divide when query.Right!.Value == 0 =>
                new MathEvaluationResult(false, 0, "I can't divide by zero."),
            MathOperation.Divide => Success(query.Left / query.Right!.Value),
            MathOperation.SquareRoot when query.Left < 0 =>
                new MathEvaluationResult(false, 0, "I can't take the square root of a negative number."),
            MathOperation.SquareRoot => Success(Math.Sqrt(query.Left)),
            MathOperation.Power => Success(Math.Pow(query.Left, query.Right!.Value)),
            _ => new MathEvaluationResult(false, 0, "I couldn't figure that one out.")
        };
    }

    public static bool IsNinePlusTenEasterEgg(MathQuery query)
    {
        if (query.Operation != MathOperation.Add || query.Right is null) return false;

        return (Math.Abs(query.Left - 9) < 0.001 && Math.Abs(query.Right.Value - 10) < 0.001) ||
               (Math.Abs(query.Left - 10) < 0.001 && Math.Abs(query.Right.Value - 9) < 0.001);
    }

    private static bool TryParseSquareRoot(string normalized, out MathQuery query)
    {
        query = default;
        var match = SquareRootPattern.Match(normalized);
        if (!match.Success) return false;

        var spoken = match.Groups["num"].Value.Trim();
        if (!TryParseNumber(spoken, out var value)) return false;

        query = new MathQuery(
            MathOperation.SquareRoot,
            value,
            null,
            spoken,
            null,
            "square root of");
        return true;
    }

    private static bool TryParsePower(string normalized, out MathQuery query)
    {
        query = default;
        var match = PowerPattern.Match(normalized);
        if (!match.Success) return false;

        var leftSpoken = match.Groups["num"].Captures[0].Value.Trim();
        var rightSpoken = match.Groups["num"].Captures[1].Value.Trim();
        if (!TryParseNumber(leftSpoken, out var left) || !TryParseNumber(rightSpoken, out var right))
            return false;

        query = new MathQuery(
            MathOperation.Power,
            left,
            right,
            leftSpoken,
            rightSpoken,
            "to the power of");
        return true;
    }

    private static bool TryParseSquared(string normalized, out MathQuery query)
    {
        query = default;
        var match = SquaredPattern.Match(normalized);
        if (!match.Success) return false;

        var spoken = match.Groups["num"].Value.Trim();
        if (!TryParseNumber(spoken, out var value)) return false;

        query = new MathQuery(
            MathOperation.Power,
            value,
            2,
            spoken,
            "2",
            "squared");
        return true;
    }

    private static bool TryParseCubed(string normalized, out MathQuery query)
    {
        query = default;
        var match = CubedPattern.Match(normalized);
        if (!match.Success) return false;

        var spoken = match.Groups["num"].Value.Trim();
        if (!TryParseNumber(spoken, out var value)) return false;

        query = new MathQuery(
            MathOperation.Power,
            value,
            3,
            spoken,
            "3",
            "cubed");
        return true;
    }

    private static bool TryParseBinary(string normalized, out MathQuery query)
    {
        query = default;
        var match = BinaryPattern.Match(normalized);
        if (!match.Success) return false;

        var leftSpoken = match.Groups["num"].Captures[0].Value.Trim();
        var rightSpoken = match.Groups["num"].Captures[1].Value.Trim();
        if (!TryParseNumber(leftSpoken, out var left) || !TryParseNumber(rightSpoken, out var right))
            return false;

        var opToken = match.Groups["op"].Value.Trim().ToLowerInvariant();
        var (operation, operatorSpoken) = opToken switch
        {
            "plus" or "+" => (MathOperation.Add, "plus"),
            "minus" or "subtract" or "subtracted by" or "-" => (MathOperation.Subtract, "minus"),
            "*" or "x" or "times" or "multiplied by" => (MathOperation.Multiply, "times"),
            "divided by" or "over" or "/" => (MathOperation.Divide, "divided by"),
            _ => ((MathOperation)(-1), string.Empty)
        };

        if ((int)operation < 0) return false;

        query = new MathQuery(operation, left, right, leftSpoken, rightSpoken, operatorSpoken);
        return true;
    }

    private static MathEvaluationResult Success(double value) => new(true, value);

    private static string NormalizeCommandPhrase(string? value)
    {
        var withMathWords = ExpandMathSymbolOperators(value);
        var normalized = TranscriptTextNormalizer.NormalizeLooseText(withMathWords);
        if (string.Equals(normalized, "uh huh", StringComparison.Ordinal) ||
            normalized.StartsWith("uh huh ", StringComparison.Ordinal))
            return normalized;

        return TranscriptTextNormalizer.StripLeadingPhrases(normalized, CommandLeadPhrases);
    }

    private static string ExpandMathSymbolOperators(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;

        return value
            .Replace("÷", " divided by ", StringComparison.Ordinal)
            .Replace("×", " times ", StringComparison.Ordinal)
            .Replace("+", " plus ", StringComparison.Ordinal)
            .Replace("*", " times ", StringComparison.Ordinal)
            .Replace("/", " divided by ", StringComparison.Ordinal)
            .Replace(" - ", " minus ", StringComparison.Ordinal)
            .Replace(" x ", " times ", StringComparison.Ordinal);
    }

    private static string StripQuestionPrefix(string normalized)
    {
        foreach (var prefix in QuestionPrefixes)
        {
            if (normalized.Equals(prefix, StringComparison.Ordinal)) return string.Empty;
            if (normalized.StartsWith($"{prefix} ", StringComparison.Ordinal))
                return normalized[(prefix.Length + 1)..].Trim();
        }

        return normalized;
    }

    private static bool TryParseNumber(string token, out double value)
    {
        value = default;
        var normalized = token.Trim().ToLowerInvariant().Replace("-", " ", StringComparison.Ordinal);
        if (double.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out value))
            return true;

        var intValue = ParseSpokenInteger(normalized);
        if (intValue is null) return false;

        value = intValue.Value;
        return true;
    }

    private static int? ParseSpokenInteger(string normalized)
    {
        if (int.TryParse(normalized, out var numeric)) return numeric;

        if (!normalized.Contains(' '))
            return normalized switch
            {
                "a" or "an" => 1,
                "one" => 1,
                "two" => 2,
                "three" => 3,
                "four" => 4,
                "five" => 5,
                "six" => 6,
                "seven" => 7,
                "eight" => 8,
                "nine" => 9,
                "ten" => 10,
                "eleven" => 11,
                "twelve" => 12,
                "thirteen" => 13,
                "fourteen" => 14,
                "fifteen" => 15,
                "sixteen" => 16,
                "seventeen" => 17,
                "eighteen" => 18,
                "nineteen" => 19,
                "twenty" => 20,
                "thirty" => 30,
                "forty" => 40,
                "fifty" => 50,
                _ => null
            };

        var parts = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 2) return null;

        var first = ParseSpokenInteger(parts[0]);
        var second = ParseSpokenInteger(parts[1]);
        if (first is >= 20 && second is >= 0 and < 10) return first + second;

        return null;
    }
}
