using System.Text.Json;

namespace Jibo.Cloud.Tests.Fixtures;

public sealed class FixtureLoaderTests
{
    [Fact]
    public void ProtocolFixtureLoader_NormalizesBackslashSeparators()
    {
        var fixtureDirectory = Path.Combine(AppContext.BaseDirectory, $"fixture-loader-{Guid.NewGuid():N}");
        var fixturePath = Path.Combine(fixtureDirectory, "sample.json");

        Directory.CreateDirectory(fixtureDirectory);

        try
        {
            File.WriteAllText(fixturePath, """
                                           {
                                             "host": "api.jibo.com",
                                             "method": "POST",
                                             "path": "/Account_20160715",
                                             "headers": {
                                               "x-amz-target": "Account_20160715.CheckEmail"
                                             },
                                             "body": {
                                               "email": "owner@openjibo.local"
                                             }
                                           }
                                           """);

            var fixture = ProtocolFixtureLoader.Load(
                $"{Path.GetFileName(fixtureDirectory)}\\sample.json");

            Assert.Equal("sample", fixture.Name);
            Assert.Equal("api.jibo.com", fixture.Request.HostName);
            Assert.Equal("Account_20160715", fixture.Request.ServicePrefix);
            Assert.Equal("CheckEmail", fixture.Request.Operation);
            using var body = JsonDocument.Parse(fixture.Request.BodyText);
            Assert.Equal("owner@openjibo.local", body.RootElement.GetProperty("email").GetString());
        }
        finally
        {
            if (Directory.Exists(fixtureDirectory))
                Directory.Delete(fixtureDirectory, true);
        }
    }
}
