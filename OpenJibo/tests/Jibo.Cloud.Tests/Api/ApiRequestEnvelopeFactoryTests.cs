using Jibo.Cloud.Api.Hosting;
using Microsoft.AspNetCore.Http;

namespace Jibo.Cloud.Tests.Api;

public sealed class ApiRequestEnvelopeFactoryTests
{
    [Fact]
    public async Task CreateAsync_ReadsRequestMetadataAndResetsBodyStream()
    {
        var context = new DefaultHttpContext
        {
            Request =
            {
                Method = HttpMethods.Post,
                Host = new HostString("api.jibo.com"),
                Path = "/v1/dispatch",
                Headers =
                {
                    ["X-Amz-Target"] = "Account_20160715.CreateHubToken",
                    ["X-Jibo-RobotId"] = "robot-123",
                    ["X-OpenJibo-Firmware"] = "1.2.3",
                    ["X-OpenJibo-AppVersion"] = "4.5.6"
                },
                Body = new MemoryStream("""{"hello":"world"}"""u8.ToArray())
            },
            TraceIdentifier = "trace-abc"
        };

        var envelope = await ApiRequestEnvelopeFactory.CreateAsync(context, CancellationToken.None);

        Assert.Equal("http", envelope.Transport);
        Assert.Equal(HttpMethods.Post, envelope.Method);
        Assert.Equal("api.jibo.com", envelope.HostName);
        Assert.Equal("/v1/dispatch", envelope.Path);
        Assert.Equal("Account_20160715", envelope.ServicePrefix);
        Assert.Equal("CreateHubToken", envelope.Operation);
        Assert.Equal("robot-123", envelope.DeviceId);
        Assert.Equal("trace-abc", envelope.CorrelationId);
        Assert.Equal("1.2.3", envelope.FirmwareVersion);
        Assert.Equal("4.5.6", envelope.ApplicationVersion);
        Assert.Equal("""{"hello":"world"}""", envelope.BodyText);
        Assert.Equal(0, context.Request.Body.Position);
        Assert.Equal("Account_20160715.CreateHubToken", envelope.Headers["X-Amz-Target"]);
    }
}