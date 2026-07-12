using Jibo.Cloud.Application.Services;

namespace Jibo.Cloud.Tests.Application;

public sealed class MathCommandParserTests
{
    [Theory]
    [InlineData("what's 12 plus 8", MathOperation.Add, 12, 8)]
    [InlineData("9 + 10", MathOperation.Add, 9, 10)]
    [InlineData("6 x 5", MathOperation.Multiply, 6, 5)]
    [InlineData("6 times 5", MathOperation.Multiply, 6, 5)]
    [InlineData("20 divided by 4", MathOperation.Divide, 20, 4)]
    [InlineData("15 minus 7", MathOperation.Subtract, 15, 7)]
    [InlineData("what is nine plus ten", MathOperation.Add, 9, 10)]
    [InlineData("hey jibo whats nine plus ten", MathOperation.Add, 9, 10)]
    [InlineData("hey jibo what's six times five", MathOperation.Multiply, 6, 5)]
    [InlineData("twelve add eight", MathOperation.Add, 12, 8)]
    [InlineData("twenty one plus twelve", MathOperation.Add, 21, 12)]
    public void TryParse_BinaryOperations_ReturnExpectedValues(
        string transcript,
        MathOperation expectedOperation,
        double expectedLeft,
        double expectedRight)
    {
        var parsed = MathCommandParser.TryParse(transcript, out var query);

        Assert.True(parsed);
        Assert.Equal(expectedOperation, query.Operation);
        Assert.Equal(expectedLeft, query.Left);
        Assert.Equal(expectedRight, query.Right);
    }

    [Theory]
    [InlineData("square root of 9", 9, 3)]
    [InlineData("what's the square root of 16", 16, 4)]
    [InlineData("square root of sixteen", 16, 4)]
    [InlineData("what's the square root of nine", 9, 3)]
    public void TryParse_SquareRoot_ReturnsExpectedResult(string transcript, double input, double expected)
    {
        Assert.True(MathCommandParser.TryParse(transcript, out var query));
        Assert.Equal(MathOperation.SquareRoot, query.Operation);
        Assert.Equal(input, query.Left);

        var evaluation = MathCommandParser.Evaluate(query);
        Assert.True(evaluation.IsSuccess);
        Assert.Equal(expected, evaluation.Value);
    }

    [Theory]
    [InlineData("9 to the power of 3", 9, 3, 729)]
    [InlineData("nine to the power of three", 9, 3, 729)]
    [InlineData("5 squared", 5, 2, 25)]
    [InlineData("five squared", 5, 2, 25)]
    [InlineData("2 cubed", 2, 3, 8)]
    public void TryParse_Power_ReturnsExpectedResult(
        string transcript,
        double expectedLeft,
        double expectedRight,
        double expectedResult)
    {
        Assert.True(MathCommandParser.TryParse(transcript, out var query));
        Assert.Equal(MathOperation.Power, query.Operation);
        Assert.Equal(expectedLeft, query.Left);
        Assert.Equal(expectedRight, query.Right);

        var evaluation = MathCommandParser.Evaluate(query);
        Assert.True(evaluation.IsSuccess);
        Assert.Equal(expectedResult, evaluation.Value);
    }

    [Theory]
    [InlineData("tell me a joke")]
    [InlineData("what time is it")]
    [InlineData("what's your favorite color")]
    public void TryParse_NonMathUtterances_ReturnFalse(string transcript)
    {
        Assert.False(MathCommandParser.TryParse(transcript, out _));
    }

    [Fact]
    public void Evaluate_DivideByZero_ReturnsError()
    {
        Assert.True(MathCommandParser.TryParse("10 divided by 0", out var query));

        var evaluation = MathCommandParser.Evaluate(query);

        Assert.False(evaluation.IsSuccess);
        Assert.Equal("I can't divide by zero.", evaluation.ErrorMessage);
    }

    [Fact]
    public void Evaluate_NegativeSquareRoot_ReturnsError()
    {
        var query = new MathQuery(MathOperation.SquareRoot, -1, null, "-1", null, "square root of");

        var evaluation = MathCommandParser.Evaluate(query);

        Assert.False(evaluation.IsSuccess);
        Assert.Equal("I can't take the square root of a negative number.", evaluation.ErrorMessage);
    }

    [Theory]
    [InlineData(MathOperation.Add, 9, 10, true)]
    [InlineData(MathOperation.Add, 10, 9, true)]
    [InlineData(MathOperation.Add, 8, 8, false)]
    [InlineData(MathOperation.Multiply, 9, 10, false)]
    public void IsNinePlusTenEasterEgg_OnlyMatchesNinePlusTen(
        MathOperation operation,
        double left,
        double right,
        bool expected)
    {
        var query = new MathQuery(operation, left, right, left.ToString(), right.ToString(), "plus");

        Assert.Equal(expected, MathCommandParser.IsNinePlusTenEasterEgg(query));
    }
}
