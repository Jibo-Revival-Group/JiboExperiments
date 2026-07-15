using Jibo.Cloud.Application.Abstractions;
using Jibo.Cloud.Application.Services;

namespace Jibo.Cloud.Tests.Application;

public sealed class MeasurementConversionSpokenReplyFormatterTests
{
    [Fact]
    public void Format_PluralCount_UsesThereAre()
    {
        var entry = new MeasurementConversionEntry(
            new MeasurementUnit("foot", "feet", ["foot", "feet"]),
            new MeasurementUnit("mile", "miles", ["mile", "miles"]),
            5280);

        Assert.Equal(
            "There are 5280 feet in one mile.",
            MeasurementConversionSpokenReplyFormatter.Format(entry));
    }

    [Fact]
    public void Format_SingularCount_UsesThereIs()
    {
        Assert.Equal(
            "There is 1 teaspoon in one tablespoon.",
            MeasurementConversionSpokenReplyFormatter.Format(1, "teaspoon", "teaspoons", "tablespoon"));
    }

    [Fact]
    public void Format_DecimalCount_FormatsDecimalValue()
    {
        Assert.Equal(
            "There are 2.54 inches in one centimeter.",
            MeasurementConversionSpokenReplyFormatter.Format(2.54, "inch", "inches", "centimeter"));
    }
}
