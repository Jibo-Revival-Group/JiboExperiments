using Jibo.Cloud.Infrastructure.Platform;

namespace Jibo.Cloud.Tests.Infrastructure;

public sealed class OperatingSystemPlatformTests
{
    [Fact]
    public void GetCurrent_ReturnsKnownPlatform()
    {
        // Act
        OperatingSystemPlatform? platform = OperatingSystemPlatformResolver.Resolve();
        // Assert
        Assert.NotNull(platform);
        Assert.NotEqual(OperatingSystemPlatform.Unknown, platform);
    }
}