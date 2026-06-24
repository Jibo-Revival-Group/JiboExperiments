using System.Text.Json;
using Jibo.Cloud.Domain.Models;
using Jibo.Cloud.Infrastructure.Telemetry;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Jibo.Cloud.Tests.WebSockets;

public sealed class FileWebSocketTelemetrySinkTests : IDisposable
{
    private readonly string _appBaseDirectory;
    private readonly string _directoryPath;
    private readonly string _repoRoot;

    public FileWebSocketTelemetrySinkTests()
    {
        _directoryPath = Path.Combine(Path.GetTempPath(), "OpenJibo.Tests", Guid.NewGuid().ToString("N"));
        _repoRoot = Path.Combine(_directoryPath, "OpenJibo");
        _appBaseDirectory = Path.Combine(_repoRoot, "src", "Jibo.Cloud", "dotnet", "src", "Jibo.Cloud.Api", "bin",
            "Debug", "net10.0");
    }

    public void Dispose()
    {
        if (Directory.Exists(_directoryPath)) Directory.Delete(_directoryPath, true);
    }

    [Fact]
    public async Task RecordsFixtureOnConnectionClose()
    {
        var sink = CreateSink();
        var envelope = new WebSocketMessageEnvelope
        {
            ConnectionId = "conn-1",
            HostName = "neo-hub.jibo.com",
            Path = "/listen",
            Kind = "neo-hub-listen",
            Token = "token-1",
            Text = """{"type":"LISTEN","transID":"trans-1","data":{"text":"hello jibo"}}"""
        };
        var session = new CloudSession
        {
            Token = "token-1",
            HostName = "neo-hub.jibo.com",
            Path = "/listen",
            TurnState =
            {
                TransId = "trans-1"
            }
        };

        await sink.RecordConnectionOpenedAsync(envelope, session);
        await sink.RecordInboundAsync(envelope, session, "LISTEN");
        await sink.RecordOutboundAsync(envelope, session,
        [
            new WebSocketReply { Text = """{"type":"LISTEN"}""" },
            new WebSocketReply { Text = """{"type":"EOS"}""" }
        ]);
        await sink.RecordConnectionClosedAsync(envelope, session, "test");

        var fixtureDirectory = Path.Combine(_directoryPath, "fixtures");
        var fixturePath = Directory.GetFiles(fixtureDirectory, "*.flow.json").Single();
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(fixturePath));
        Assert.Equal("neo-hub.jibo.com",
            document.RootElement.GetProperty("session").GetProperty("hostName").GetString());
        Assert.Equal(1, document.RootElement.GetProperty("steps").GetArrayLength());
        Assert.Equal("LISTEN",
            document.RootElement.GetProperty("steps")[0].GetProperty("expectedReplyTypes")[0].GetString());

        var indexPath = Path.Combine(_directoryPath, "capture-index.ndjson");
        var indexEntries = await ReadNdjsonAsync(indexPath);
        Assert.Contains(indexEntries, entry => entry.GetProperty("eventType").GetString() == "connection_opened");
        Assert.Contains(indexEntries, entry => entry.GetProperty("eventType").GetString() == "message_in");
        Assert.Contains(indexEntries, entry => entry.GetProperty("eventType").GetString() == "message_out");
        Assert.Contains(indexEntries, entry => entry.GetProperty("eventType").GetString() == "connection_closed");
        Assert.Contains(indexEntries, entry => entry.GetProperty("eventType").GetString() == "fixture_export");
        Assert.Contains(indexEntries, entry =>
            entry.GetProperty("details").TryGetProperty("eventFilePath", out var eventFilePath) &&
            eventFilePath.GetString()!.EndsWith(".events.ndjson", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RecordTurnEventAsync_AddsTurnDiagnosticsToCaptureIndex()
    {
        var sink = CreateSink();
        var envelope = new WebSocketMessageEnvelope
        {
            ConnectionId = "conn-1",
            HostName = "neo-hub.jibo.com",
            Path = "/listen",
            Kind = "neo-hub-listen",
            Token = "token-1",
            Text = """{"type":"CLIENT_ASR","transID":"trans-1","data":{"text":"yes"}}"""
        };
        var session = new CloudSession
        {
            Token = "token-1",
            HostName = "neo-hub.jibo.com",
            Path = "/listen",
            TurnState =
            {
                TransId = "trans-1"
            }
        };

        await sink.RecordTurnEventAsync(envelope, session, "early_probe_scheduled", new Dictionary<string, object?>
        {
            ["listenRule"] = "shared/yes_no",
            ["attempt"] = 1
        });

        var indexPath = Path.Combine(_directoryPath, "capture-index.ndjson");
        var indexEntries = await ReadNdjsonAsync(indexPath);
        var entry = Assert.Single(indexEntries,
            item => item.GetProperty("eventType").GetString() == "early_probe_scheduled");
        var details = entry.GetProperty("details");

        Assert.Equal("trans-1", details.GetProperty("transId").GetString());
        Assert.Equal("shared/yes_no",
            details.GetProperty("details").GetProperty("listenRule").GetString());
        Assert.True(details.TryGetProperty("eventFilePath", out var eventFilePath));
        Assert.EndsWith(".events.ndjson", eventFilePath.GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RecordsFixtureUsingRepoRootForRelativePaths()
    {
        Directory.CreateDirectory(_repoRoot);
        Directory.CreateDirectory(_appBaseDirectory);
        await File.WriteAllTextAsync(Path.Combine(_repoRoot, "OpenJibo.slnx"), string.Empty);
        var captureDirectory = CapturePathResolver.Resolve("captures/websocket", _repoRoot, _appBaseDirectory);

        var sink = new FileWebSocketTelemetrySink(
            NullLogger<FileWebSocketTelemetrySink>.Instance,
            Options.Create(new WebSocketTelemetryOptions
            {
                Enabled = true,
                ExportFixtures = true,
                DirectoryPath = captureDirectory
            }));

        var envelope = new WebSocketMessageEnvelope
        {
            ConnectionId = "conn-relative",
            HostName = "neo-hub.jibo.com",
            Path = "/listen",
            Kind = "neo-hub-listen",
            Token = "token-relative",
            Text = """{"type":"LISTEN","transID":"trans-relative","data":{"text":"hello"}}"""
        };
        var session = new CloudSession
        {
            Token = "token-relative",
            HostName = "neo-hub.jibo.com",
            Path = "/listen",
            TurnState =
            {
                TransId = "trans-relative"
            }
        };

        await sink.RecordConnectionOpenedAsync(envelope, session);
        await sink.RecordOutboundAsync(envelope, session, [new WebSocketReply { Text = """{"type":"LISTEN"}""" }]);
        await sink.RecordConnectionClosedAsync(envelope, session, "test");

        var fixtureDirectory = Path.Combine(captureDirectory, "fixtures");
        Assert.Single(Directory.GetFiles(fixtureDirectory, "*.flow.json"));
    }

    private FileWebSocketTelemetrySink CreateSink()
    {
        return new FileWebSocketTelemetrySink(
            NullLogger<FileWebSocketTelemetrySink>.Instance,
            Options.Create(new WebSocketTelemetryOptions
            {
                Enabled = true,
                ExportFixtures = true,
                DirectoryPath = _directoryPath
            }));
    }

    private static async Task<List<JsonElement>> ReadNdjsonAsync(string filePath)
    {
        var entries = new List<JsonElement>();
        foreach (var line in await File.ReadAllLinesAsync(filePath))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            using var document = JsonDocument.Parse(line);
            entries.Add(document.RootElement.Clone());
        }

        return entries;
    }
}