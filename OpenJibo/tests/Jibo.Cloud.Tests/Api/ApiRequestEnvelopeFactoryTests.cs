using System.Text;
using Jibo.Cloud.Api.Hosting;
using Jibo.Cloud.Domain.Models;
using Microsoft.AspNetCore.Http;

namespace Jibo.Cloud.Tests.Api;

public sealed class ApiRequestEnvelopeFactoryTests
{
    [Fact]
    public async Task CreateAsync_ReadsRequestMetadataAndResetsBodyStream()
    {
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Post;
        context.Request.Host = new HostString("api.jibo.com");
        context.Request.Path = "/v1/dispatch";
        context.Request.Headers["X-Amz-Target"] = "Account_20160715.CreateHubToken";
        context.Request.Headers["X-Jibo-RobotId"] = "robot-123";
        context.Request.Headers["X-OpenJibo-Firmware"] = "1.2.3";
        context.Request.Headers["X-OpenJibo-AppVersion"] = "4.5.6";
        context.TraceIdentifier = "trace-abc";
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("""{"hello":"world"}"""));

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
