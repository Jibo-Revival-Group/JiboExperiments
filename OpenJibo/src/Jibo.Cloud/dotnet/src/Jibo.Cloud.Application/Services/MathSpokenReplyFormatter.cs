using System.Globalization;

namespace Jibo.Cloud.Application.Services;

public static class MathSpokenReplyFormatter
{
    private static readonly string[] Connectors = ["equals", "is", "comes to", "is equal to"];

    public static string Format(MathQuery query, MathEvaluationResult evaluation, IJiboRandomizer randomizer)
    {
        if (!evaluation.IsSuccess)
            return evaluation.ErrorMessage ?? "I couldn't figure that one out.";

        var connector = randomizer.Choose(Connectors);
        var answer = FormatAnswer(evaluation.Value);
        var reply = query.Operation switch
        {
            MathOperation.SquareRoot =>
                $"the square root of {query.LeftSpoken} {connector} {answer}",
            MathOperation.Power when string.Equals(query.OperatorSpoken, "squared", StringComparison.OrdinalIgnoreCase) =>
                $"{query.LeftSpoken} squared {connector} {answer}",
            MathOperation.Power when string.Equals(query.OperatorSpoken, "cubed", StringComparison.OrdinalIgnoreCase) =>
                $"{query.LeftSpoken} cubed {connector} {answer}",
            MathOperation.Power =>
                $"{query.LeftSpoken} to the power of {query.RightSpoken} {connector} {answer}",
            _ =>
                $"{query.LeftSpoken} {query.OperatorSpoken} {query.RightSpoken} {connector} {answer}"
        };

        if (MathCommandParser.IsNinePlusTenEasterEgg(query))
            reply += ", but some might say it's 21";

        return reply;
    }

    public static string FormatAnswer(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
            return value.ToString(CultureInfo.InvariantCulture);

        if (Math.Abs(value - Math.Round(value)) < 0.000_000_1)
            return Math.Round(value).ToString(CultureInfo.InvariantCulture);

        return value.ToString("0.##########", CultureInfo.InvariantCulture);
    }
}
