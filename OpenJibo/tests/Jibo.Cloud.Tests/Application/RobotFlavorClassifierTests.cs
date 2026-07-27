using Jibo.Cloud.Application.Services;

namespace Jibo.Cloud.Tests.Application;

public sealed class RobotFlavorClassifierTests
{
    [Theory]
    [InlineData("0.9.0", RobotFlavorClassifier.BetaStock)]
    [InlineData("0.1.2", RobotFlavorClassifier.BetaStock)]
    [InlineData("1.9.2", RobotFlavorClassifier.Stock)]
    [InlineData("1.0.0", RobotFlavorClassifier.Stock)]
    [InlineData("2.0.0", RobotFlavorClassifier.Stock)]
    [InlineData("BEam.1.1.0", RobotFlavorClassifier.Beam)]
    [InlineData("beam.2.0.0", RobotFlavorClassifier.Beam)]
    [InlineData("2.0.1", RobotFlavorClassifier.OldBeam)]
    [InlineData(null, RobotFlavorClassifier.UnsureReply)]
    [InlineData("", RobotFlavorClassifier.UnsureReply)]
    [InlineData("   ", RobotFlavorClassifier.UnsureReply)]
    [InlineData("2.0.2", RobotFlavorClassifier.UnsureReply)]
    [InlineData("12.10.0", RobotFlavorClassifier.UnsureReply)]
    [InlineData("not-a-version", RobotFlavorClassifier.UnsureReply)]
    public void ClassifySpokenReply_MapsReleaseToFlavor(string? release, string expected)
    {
        Assert.Equal(expected, RobotFlavorClassifier.ClassifySpokenReply(release));
    }

    [Theory]
    [InlineData("BEam.1.1.0", true)]
    [InlineData("beam.0.1.0", true)]
    [InlineData("2.0.1", false)]
    [InlineData("1.9.2", false)]
    [InlineData("2.0.0", false)]
    [InlineData(null, false)]
    public void IsBeam_RequiresBeamPrefix(string? release, bool expected)
    {
        Assert.Equal(expected, RobotFlavorClassifier.IsBeam(release));
    }
}
