using System.Reflection;
using Jibo.Cloud.Api.Hosting;

namespace Jibo.Cloud.Tests.Api;

public sealed class PortalStaticFileMapperTests
{
    [Fact]
    public void ResolvePortalDirectory_DoesNotThrowWhenWebRootIsNull()
    {
        var method = typeof(PortalStaticFileMapper).GetMethod(
            "ResolvePortalDirectory",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);

        var tempRoot = Path.Combine(Path.GetTempPath(), $"openjibo-portal-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);

        var result = method!.Invoke(null, [null, tempRoot]);

        Assert.NotNull(result);
        Assert.IsType<string>(result);
    }
}
