using Jibo.Cloud.Domain.Models;
using Jibo.Cloud.Infrastructure.Telemetry;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Jibo.Cloud.Tests.Protocol;

public sealed class FileProtocolTelemetrySinkTests : IDisposable
{
    private readonly string _appBaseDirectory;
    private readonly string _repoRoot;
    private readonly string _workspaceRoot;

    public FileProtocolTelemetrySinkTests()
    {
        _workspaceRoot = Path.Combine(Path.GetTempPath(), "OpenJibo.ProtocolTelemetry.Tests",
            Guid.NewGuid().ToString("N"));
        _repoRoot = Path.Combine(_workspaceRoot, "OpenJibo");
        _appBaseDirectory = Path.Combine(_repoRoot, "src", "Jibo.Cloud", "dotnet", "src", "Jibo.Cloud.Api", "bin",
            "Debug", "net10.0");

        Directory.CreateDirectory(_repoRoot);
        Directory.CreateDirectory(_appBaseDirectory);
        File.WriteAllText(Path.Combine(_repoRoot, "OpenJibo.slnx"), string.Empty);
    }

    public void Dispose()
    {
        if (Directory.Exists(_workspaceRoot)) Directory.Delete(_workspaceRoot, true);
    }

    [Fact]
    public async Task RecordAsync_ResolvesRelativePathAgainstOpenJiboRepoRoot()
    {
        var captureDirectory = CapturePathResolver.Resolve("captures/http", _repoRoot, _appBaseDirectory);
        var sink = new FileProtocolTelemetrySink(
            NullLogger<FileProtocolTelemetrySink>.Instance,
            Options.Create(new ProtocolTelemetryOptions
            {
                Enabled = true,
                DirectoryPath = captureDirectory
            }));

        var envelope = new ProtocolEnvelope
        {
            HostName = "api.jibo.com",
            Method = "POST",
            Path = "/",
            ServicePrefix = "Notification_20150505",
            Operation = "NewRobotToken",
            Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Authorization"] = "AWS4-HMAC-SHA256 reusable-signature",
                ["X-Amz-Security-Token"] = "aws-session-token",
                ["X-Robot-Stream-Token"] = "robot-stream-token",
                ["X-OpenJibo-Release-Smoke-Secret"] = "release-smoke-secret",
                ["X-Amz-Target"] = "Notification_20150505.NewRobotToken"
            },
            BodyText = """{"deviceId":"robot-123","password":"request-password","nested":{"secretAccessKey":"account-secret"}}"""
        };

        var response = ProtocolDispatchResult.Ok(new
        {
            token = "token-robot-123",
            nested = new { refreshToken = "refresh-robot-123" }
        });
        response.Headers["Set-Cookie"] = "session=private-cookie";
        await sink.RecordAsync(envelope, response);
        await sink.RecordAsync(new ProtocolEnvelope
        {
            HostName = "api.jibo.com",
            Method = "POST",
            Path = "/",
            ServicePrefix = "Account_20160715",
            Operation = "Login",
            BodyText = "password=form-secret&access_token=bearer-secret"
        }, ProtocolDispatchResult.Raw(401, "plain-response-token"));

        var captureFile = Directory.GetFiles(captureDirectory, "*.events.ndjson").Single();
        var contents = await File.ReadAllTextAsync(captureFile);
        var indexPath = Path.Combine(captureDirectory, "capture-index.ndjson");
        var indexLines = await File.ReadAllLinesAsync(indexPath);

        Assert.Contains("Notification_20150505", contents);
        Assert.Contains("robot-123", contents);
        Assert.Contains("[REDACTED]", contents);
        Assert.DoesNotContain("reusable-signature", contents);
        Assert.DoesNotContain("release-smoke-secret", contents);
        Assert.DoesNotContain("aws-session-token", contents);
        Assert.DoesNotContain("robot-stream-token", contents);
        Assert.DoesNotContain("request-password", contents);
        Assert.DoesNotContain("account-secret", contents);
        Assert.DoesNotContain("token-robot-123", contents);
        Assert.DoesNotContain("refresh-robot-123", contents);
        Assert.DoesNotContain("private-cookie", contents);
        Assert.DoesNotContain("form-secret", contents);
        Assert.DoesNotContain("bearer-secret", contents);
        Assert.DoesNotContain("plain-response-token", contents);
        Assert.Contains("[NON-JSON BODY OMITTED]", contents);
        Assert.DoesNotContain(Path.Combine("bin", "Debug"), captureFile, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(indexLines,
            line => line.Contains("\"eventType\":\"protocol_record\"", StringComparison.Ordinal));
        Assert.Contains(indexLines, line => line.Contains("\"servicePrefix\":\"Notification_20150505\"",
            StringComparison.Ordinal));
        Assert.Contains(indexLines, line => line.Contains("\"eventFilePath\":", StringComparison.Ordinal) &&
                                            line.Contains(".events.ndjson", StringComparison.Ordinal));
    }
}
