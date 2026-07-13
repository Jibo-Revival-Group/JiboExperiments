using Jibo.Cloud.Application.Services;

namespace Jibo.Cloud.Tests.Application;

public sealed class DiceRollSpokenReplyFormatterTests
{
    [Fact]
    public void Format_DefaultResult_UsesLandedOn()
    {
        Assert.Equal("It landed on 4.", DiceRollSpokenReplyFormatter.Format(6, 4));
    }

    [Fact]
    public void Format_D20CriticalFailure_UsesSpecialLine()
    {
        Assert.Equal(
            "It landed on a 1. Critical failure!",
            DiceRollSpokenReplyFormatter.Format(20, 1));
    }

    [Fact]
    public void FormatEsml_D6_IncludesRollDieAnimation()
    {
        var esml = DiceRollSpokenReplyFormatter.FormatEsml(6, 4, "It landed on 4.");
        Assert.NotNull(esml);
        Assert.Contains("roll-die-4", esml, StringComparison.Ordinal);
        Assert.Contains("It landed on 4.", esml, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatEsml_D20_ReturnsNull()
    {
        Assert.Null(DiceRollSpokenReplyFormatter.FormatEsml(20, 17, "It landed on 17."));
    }

    [Fact]
    public void FormatInvalidSides_ReturnsClarification()
    {
        Assert.Equal(
            "I can roll dice with 2 to 100 sides. Try asking me to roll a dice or roll a 20 sided die.",
            DiceRollSpokenReplyFormatter.FormatInvalidSides());
    }
}
