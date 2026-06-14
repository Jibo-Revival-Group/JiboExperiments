using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Jibo.Cloud.Application.Services;
using Jibo.Cloud.Domain.Models;
using Jibo.Cloud.Infrastructure.Media;
using Jibo.Cloud.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;

namespace Jibo.Cloud.Tests.Protocol;

public sealed class JiboCloudProtocolServiceTests
{
    private readonly JiboCloudProtocolService _service = new(new InMemoryCloudStateStore());

    [Fact]
    public async Task CreateHubToken_ReturnsTokenAndExpiry()
    {
        var result = await _service.DispatchAsync(new ProtocolEnvelope
        {
            HostName = "api.jibo.com",
            Method = "POST",
            ServicePrefix = "Account_20160715",
            Operation = "CreateHubToken",
            BodyText = "{}"
        });

        using var payload = JsonDocument.Parse(result.BodyText);
        Assert.Equal(200, result.StatusCode);
        Assert.StartsWith("hub-", payload.RootElement.GetProperty("token").GetString());
        Assert.True(payload.RootElement.GetProperty("expires").GetInt64() > 0);
    }

    [Fact]
    public async Task NewRobotToken_UsesBodyDeviceId_WhenHeaderDeviceIdIsEmpty()
    {
        var result = await _service.DispatchAsync(new ProtocolEnvelope
        {
            HostName = "api.jibo.com",
            Method = "POST",
            ServicePrefix = "Notification_20160715",
            Operation = "NewRobotToken",
            DeviceId = string.Empty,
            BodyText = """{"deviceId":"robot-123"}"""
        });

        using var payload = JsonDocument.Parse(result.BodyText);
        Assert.Equal(200, result.StatusCode);
        Assert.Contains("robot-123", payload.RootElement.GetProperty("token").GetString());
    }

    [Fact]
    public async Task GetUpdateFrom_WithoutStagedUpdate_ReturnsUpdateNotFound()
    {
        var result = await _service.DispatchAsync(new ProtocolEnvelope
        {
            HostName = "api.jibo.com",
            Method = "POST",
            ServicePrefix = "Update_20160715",
            Operation = "GetUpdateFrom",
            BodyText = """{"subsystem":"robot","fromVersion":"1.0.0"}"""
        });

        Assert.Equal(404, result.StatusCode);
        using var payload = JsonDocument.Parse(result.BodyText);
        Assert.Equal("UPDATE_NOT_FOUND", payload.RootElement.GetProperty("__type").GetString());
    }

    [Fact]
    public async Task GetUpdateFrom_IgnoresSameVersionUpdates()
    {
        await _service.DispatchAsync(new ProtocolEnvelope
        {
            HostName = "api.jibo.com",
            Method = "POST",
            ServicePrefix = "Update_20160715",
            Operation = "CreateUpdate",
            BodyText = """{"fromVersion":"1.0.0","toVersion":"1.0.1","changes":"Bug fix","subsystem":"robot"}"""
        });

        var result = await _service.DispatchAsync(new ProtocolEnvelope
        {
            HostName = "api.jibo.com",
            Method = "POST",
            ServicePrefix = "Update_20160715",
            Operation = "GetUpdateFrom",
            BodyText = """{"subsystem":"robot","fromVersion":"1.0.1"}"""
        });

        Assert.Equal(404, result.StatusCode);
        using var payload = JsonDocument.Parse(result.BodyText);
        Assert.Equal("UPDATE_NOT_FOUND", payload.RootElement.GetProperty("__type").GetString());
    }

    [Fact]
    public async Task SchedulerGetUpdate_ReturnsWrappedUpdateList()
    {
        var result = await _service.DispatchAsync(new ProtocolEnvelope
        {
            HostName = "127.0.0.1",
            Method = "GET",
            Path = "/update"
        });

        using var payload = JsonDocument.Parse(result.BodyText);
        Assert.Equal(200, result.StatusCode);
        Assert.True(payload.RootElement.TryGetProperty("updates", out var updates));
        Assert.Equal(JsonValueKind.Array, updates.ValueKind);
        Assert.Empty(updates.EnumerateArray());
    }

    [Fact]
    public async Task SchedulerCheckUpdates_ReturnsWrappedUpdateList()
    {
        var result = await _service.DispatchAsync(new ProtocolEnvelope
        {
            HostName = "localhost",
            Method = "POST",
            Path = "/check-updates",
            BodyText = """{"filter":"robot"}"""
        });

        using var payload = JsonDocument.Parse(result.BodyText);
        Assert.Equal(200, result.StatusCode);
        Assert.Equal("OK", payload.RootElement.GetProperty("status").GetString());
        Assert.True(payload.RootElement.TryGetProperty("data", out var data));
        Assert.Equal(JsonValueKind.Array, data.ValueKind);
        Assert.Empty(data.EnumerateArray());
    }

    [Fact]
    public async Task DispatchAsync_WithoutConfiguredAcceptedHosts_AllowsUnknownHosts()
    {
        var service = new JiboCloudProtocolService(new InMemoryCloudStateStore(), null,
            new ConfigurationBuilder().Build());

        var result = await service.DispatchAsync(new ProtocolEnvelope
        {
            HostName = "example.invalid",
            Method = "POST",
            ServicePrefix = "Account_20160715",
            Operation = "CreateHubToken",
            BodyText = "{}"
        });

        Assert.Equal(200, result.StatusCode);
        using var payload = JsonDocument.Parse(result.BodyText);
        Assert.StartsWith("hub-", payload.RootElement.GetProperty("token").GetString());
    }

    [Fact]
    public async Task DispatchAsync_WithConfiguredAcceptedHosts_RejectsUnknownHost()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OpenJibo:AcceptedHosts:0"] = "api.jibo.com"
            })
            .Build();

        var service = new JiboCloudProtocolService(new InMemoryCloudStateStore(), null, configuration);

        var result = await service.DispatchAsync(new ProtocolEnvelope
        {
            HostName = "example.invalid",
            Method = "POST",
            ServicePrefix = "Account_20160715",
            Operation = "CreateHubToken",
            BodyText = "{}"
        });

        Assert.Equal(200, result.StatusCode);
        using var payload = JsonDocument.Parse(result.BodyText);
        Assert.False(payload.RootElement.GetProperty("accepted").GetBoolean());
        Assert.Equal("example.invalid", payload.RootElement.GetProperty("host").GetString());
    }

    [Fact]
    public async Task LegacyLoopSuspendPath_ReturnsOk()
    {
        var result = await _service.DispatchAsync(new ProtocolEnvelope
        {
            HostName = "localhost",
            Method = "POST",
            Path = "/v1/loop/suspend"
        });

        Assert.Equal(200, result.StatusCode);
        using var payload = JsonDocument.Parse(result.BodyText);
        Assert.True(payload.RootElement.GetProperty("ok").GetBoolean());
    }

    [Fact]
    public async Task SchedulerStatusEndpoints_DefaultToNotBackingUpAndNoDownload()
    {
        var backupStatus = await _service.DispatchAsync(new ProtocolEnvelope
        {
            HostName = "localhost",
            Method = "POST",
            Path = "/backup-status"
        });

        using var backupPayload = JsonDocument.Parse(backupStatus.BodyText);
        Assert.Equal(200, backupStatus.StatusCode);
        Assert.Equal("OK", backupPayload.RootElement.GetProperty("status").GetString());
        Assert.False(backupPayload.RootElement.GetProperty("data").GetBoolean());

        var downloadStatus = await _service.DispatchAsync(new ProtocolEnvelope
        {
            HostName = "localhost",
            Method = "POST",
            Path = "/download-status"
        });

        using var downloadPayload = JsonDocument.Parse(downloadStatus.BodyText);
        Assert.Equal(200, downloadStatus.StatusCode);
        Assert.Equal("OK", downloadPayload.RootElement.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, downloadPayload.RootElement.GetProperty("data").ValueKind);
    }

    [Fact]
    public async Task SchedulerBackupRobot_StartsBackupThenClearsIt()
    {
        var start = await _service.DispatchAsync(new ProtocolEnvelope
        {
            HostName = "localhost",
            Method = "POST",
            Path = "/backup-robot"
        });

        using var startPayload = JsonDocument.Parse(start.BodyText);
        Assert.Equal(200, start.StatusCode);
        Assert.Equal("OK", startPayload.RootElement.GetProperty("status").GetString());

        var immediate = await _service.DispatchAsync(new ProtocolEnvelope
        {
            HostName = "localhost",
            Method = "POST",
            Path = "/backup-status"
        });

        using var immediatePayload = JsonDocument.Parse(immediate.BodyText);
        Assert.True(immediatePayload.RootElement.GetProperty("data").GetBoolean());

        await Task.Delay(400);

        var finished = await _service.DispatchAsync(new ProtocolEnvelope
        {
            HostName = "localhost",
            Method = "POST",
            Path = "/backup-status"
        });

        using var finishedPayload = JsonDocument.Parse(finished.BodyText);
        Assert.False(finishedPayload.RootElement.GetProperty("data").GetBoolean());
    }

    [Fact]
    public async Task SchedulerOtaUpdate_ProgressesFromBackupToDownloadAndCompletes()
    {
        await _service.DispatchAsync(new ProtocolEnvelope
        {
            HostName = "api.jibo.com",
            Method = "POST",
            ServicePrefix = "Update_20160715",
            Operation = "CreateUpdate",
            BodyText =
                """{"fromVersion":"12.10.0","toVersion":"12.10.1","changes":"OTA progress","subsystem":"robot","length":1200}"""
        });

        var start = await _service.DispatchAsync(new ProtocolEnvelope
        {
            HostName = "localhost",
            Method = "POST",
            Path = "/ota-update"
        });

        using var startPayload = JsonDocument.Parse(start.BodyText);
        Assert.Equal(200, start.StatusCode);
        Assert.Equal("OK", startPayload.RootElement.GetProperty("status").GetString());

        var backing = await _service.DispatchAsync(new ProtocolEnvelope
        {
            HostName = "localhost",
            Method = "POST",
            Path = "/backup-status"
        });

        using var backingPayload = JsonDocument.Parse(backing.BodyText);
        Assert.True(backingPayload.RootElement.GetProperty("data").GetBoolean());

        await Task.Delay(400);

        var downloading = await _service.DispatchAsync(new ProtocolEnvelope
        {
            HostName = "localhost",
            Method = "POST",
            Path = "/download-status"
        });

        using var downloadingPayload = JsonDocument.Parse(downloading.BodyText);
        Assert.Equal("OK", downloadingPayload.RootElement.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Object, downloadingPayload.RootElement.GetProperty("data").ValueKind);

        await Task.Delay(1200);

        var completedDownload = await _service.DispatchAsync(new ProtocolEnvelope
        {
            HostName = "localhost",
            Method = "POST",
            Path = "/download-status"
        });

        using var completedDownloadPayload = JsonDocument.Parse(completedDownload.BodyText);
        Assert.Equal(JsonValueKind.Null, completedDownloadPayload.RootElement.GetProperty("data").ValueKind);

        var updates = await _service.DispatchAsync(new ProtocolEnvelope
        {
            HostName = "localhost",
            Method = "GET",
            Path = "/update"
        });

        using var updatesPayload = JsonDocument.Parse(updates.BodyText);
        Assert.Contains(updatesPayload.RootElement.GetProperty("updates").EnumerateArray(),
            item => item.GetProperty("subsystem").GetString() == "robot" &&
                    item.GetProperty("changes").GetString() == "OTA progress" &&
                    item.GetProperty("downloaded").GetBoolean());
    }

    [Fact]
    public async Task SchedulerCheckUpdates_IgnoresSameVersionNoopUpdates()
    {
        await _service.DispatchAsync(new ProtocolEnvelope
        {
            HostName = "api.jibo.com",
            Method = "POST",
            ServicePrefix = "Update_20160715",
            Operation = "CreateUpdate",
            BodyText =
                """{"fromVersion":"12.10.0","toVersion":"12.10.0","changes":"No update available","subsystem":"robot","length":0}"""
        });

        var result = await _service.DispatchAsync(new ProtocolEnvelope
        {
            HostName = "localhost",
            Method = "POST",
            Path = "/check-updates",
            BodyText = """{"filter":"robot"}"""
        });

        using var payload = JsonDocument.Parse(result.BodyText);
        Assert.Equal(200, result.StatusCode);
        Assert.Equal("OK", payload.RootElement.GetProperty("status").GetString());
        Assert.True(payload.RootElement.TryGetProperty("data", out var data));
        Assert.Equal(JsonValueKind.Array, data.ValueKind);
        Assert.Empty(data.EnumerateArray());
    }

    [Fact]
    public async Task SchedulerBackupEndpoints_AreDisabledWhenConfigured()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OpenJibo:EnableBackupRestore"] = "false"
            })
            .Build();

        var service = new JiboCloudProtocolService(new InMemoryCloudStateStore(), null, configuration);

        var backupStatus = await service.DispatchAsync(new ProtocolEnvelope
        {
            HostName = "localhost",
            Method = "POST",
            Path = "/backup-status"
        });

        using var backupPayload = JsonDocument.Parse(backupStatus.BodyText);
        Assert.Equal(200, backupStatus.StatusCode);
        Assert.Equal("unknown target default response", backupPayload.RootElement.GetProperty("note").GetString());

        var backupRobot = await service.DispatchAsync(new ProtocolEnvelope
        {
            HostName = "localhost",
            Method = "POST",
            Path = "/backup-robot"
        });

        using var backupRobotPayload = JsonDocument.Parse(backupRobot.BodyText);
        Assert.Equal(200, backupRobot.StatusCode);
        Assert.Equal("unknown target default response", backupRobotPayload.RootElement.GetProperty("note").GetString());
    }

    [Fact]
    public async Task BackupList_WithoutBackups_ReturnsEmptyList()
    {
        var result = await _service.DispatchAsync(new ProtocolEnvelope
        {
            HostName = "api.jibo.com",
            Method = "POST",
            ServicePrefix = "Backup_20170222",
            Operation = "List",
            BodyText = """{"loopId":"loop-123"}"""
        });

        using var payload = JsonDocument.Parse(result.BodyText);
        Assert.Equal(200, result.StatusCode);
        Assert.Equal(JsonValueKind.Array, payload.RootElement.ValueKind);
        Assert.Empty(payload.RootElement.EnumerateArray());
    }

    [Fact]
    public async Task BackupNew_ReturnsUploadUrl_AndListIncludesBackup()
    {
        var create = await _service.DispatchAsync(new ProtocolEnvelope
        {
            HostName = "api.jibo.com",
            Method = "POST",
            ServicePrefix = "Backup_20170222",
            Operation = "New",
            BodyText = """{"loopId":"loop-123"}"""
        });

        using var createPayload = JsonDocument.Parse(create.BodyText);
        Assert.Equal(200, create.StatusCode);
        var uploadUrl = createPayload.RootElement.GetProperty("uploadUrl").GetString();
        Assert.NotNull(uploadUrl);
        Assert.Contains("/upload/backup/", uploadUrl);

        var list = await _service.DispatchAsync(new ProtocolEnvelope
        {
            HostName = "api.jibo.com",
            Method = "POST",
            ServicePrefix = "Backup_20170222",
            Operation = "List",
            BodyText = """{"loopId":"loop-123"}"""
        });

        using var listPayload = JsonDocument.Parse(list.BodyText);
        Assert.Equal(200, list.StatusCode);
        Assert.NotEmpty(listPayload.RootElement.EnumerateArray());
        var item = listPayload.RootElement.EnumerateArray().First();
        Assert.True(item.TryGetProperty("location", out var location));
        Assert.Contains("/backup/", location.GetProperty("url").GetString());
        Assert.False(string.IsNullOrWhiteSpace(location.GetProperty("expires").GetString()));
    }

    [Fact]
    public async Task PutEventsAsync_ReturnsUploadUrl()
    {
        var result = await _service.DispatchAsync(new ProtocolEnvelope
        {
            HostName = "api.jibo.com",
            Method = "POST",
            ServicePrefix = "Log_20160715",
            Operation = "PutEventsAsync",
            BodyText = "{}"
        });

        using var payload = JsonDocument.Parse(result.BodyText);
        Assert.Equal("gzip", payload.RootElement.GetProperty("contentEncoding").GetString());
        Assert.Contains("/upload/log-events", payload.RootElement.GetProperty("uploadUrl").GetString());
    }

    [Fact]
    public async Task PersonListHolidays_DoesNotThrow_WhenLoopStateIsEmpty()
    {
        var persistencePath = Path.Combine(Path.GetTempPath(), $"openjibo-empty-holidays-{Guid.NewGuid():N}.json");
        try
        {
            await File.WriteAllTextAsync(persistencePath, """
                                                          {
                                                            "SchemaVersion": "1",
                                                            "Revision": 0,
                                                            "Loops": [],
                                                            "Holidays": []
                                                          }
                                                          """);

            var service = new JiboCloudProtocolService(new InMemoryCloudStateStore(persistencePath));
            var result = await service.DispatchAsync(new ProtocolEnvelope
            {
                HostName = "api.jibo.com",
                Method = "POST",
                ServicePrefix = "Person_20160715",
                Operation = "ListHolidays",
                BodyText = "{}"
            });

            using var payload = JsonDocument.Parse(result.BodyText);
            Assert.Equal(200, result.StatusCode);
            Assert.Equal(JsonValueKind.Array, payload.RootElement.ValueKind);
            Assert.NotEmpty(payload.RootElement.EnumerateArray());
        }
        finally
        {
            if (File.Exists(persistencePath)) File.Delete(persistencePath);
        }
    }

    [Fact]
    public async Task PersonListHolidays_MergesPersistedLoopHolidayOverrides()
    {
        var persistencePath = Path.Combine(Path.GetTempPath(), $"openjibo-loop-holidays-{Guid.NewGuid():N}.json");
        try
        {
            await File.WriteAllTextAsync(persistencePath, """
                                                          {
                                                            "SchemaVersion": "1",
                                                            "Revision": 0,
                                                            "Loops": [],
                                                            "Holidays": [
                                                              {
                                                                "Id": "birthday-1",
                                                                "EventId": "birthday-1",
                                                                "Name": "Jake's Birthday",
                                                                "Category": "birthday",
                                                                "LoopId": "loop-123",
                                                                "MemberId": "person-123",
                                                                "IsEnabled": true,
                                                                "Date": "2026-05-19",
                                                                "Source": "manual",
                                                                "CountryCode": "US",
                                                                "Created": "2026-05-19T00:00:00Z"
                                                              }
                                                            ]
                                                          }
                                                          """);

            var service = new JiboCloudProtocolService(new InMemoryCloudStateStore(persistencePath));
            var result = await service.DispatchAsync(new ProtocolEnvelope
            {
                HostName = "api.jibo.com",
                Method = "POST",
                ServicePrefix = "Person_20160715",
                Operation = "ListHolidays",
                BodyText = """{"loopId":"loop-123"}"""
            });

            using var payload = JsonDocument.Parse(result.BodyText);
            Assert.Equal(200, result.StatusCode);
            Assert.Contains(payload.RootElement.EnumerateArray(),
                item => item.GetProperty("name").GetString() == "Jake's Birthday");
        }
        finally
        {
            if (File.Exists(persistencePath)) File.Delete(persistencePath);
        }
    }

    [Fact]
    public async Task PersonUpsertCommute_ThenListCommute_ReturnsPersistedLoopProfile()
    {
        var service = new JiboCloudProtocolService(new InMemoryCloudStateStore());

        var upsert = await service.DispatchAsync(new ProtocolEnvelope
        {
            HostName = "api.jibo.com",
            Method = "POST",
            ServicePrefix = "Person_20160715",
            Operation = "UpsertCommute",
            BodyText =
                """{"loopId":"loop-123","mode":"walking","workHour":8,"workMinute":15,"typicalDurationMinutes":22}"""
        });

        using var upsertPayload = JsonDocument.Parse(upsert.BodyText);
        Assert.Equal(200, upsert.StatusCode);
        Assert.Equal("loop-123", upsertPayload.RootElement.GetProperty("loopId").GetString());
        Assert.Equal("walking", upsertPayload.RootElement.GetProperty("mode").GetString());

        var listed = await service.DispatchAsync(new ProtocolEnvelope
        {
            HostName = "api.jibo.com",
            Method = "POST",
            ServicePrefix = "Person_20160715",
            Operation = "ListCommute",
            BodyText = """{"loopId":"loop-123"}"""
        });

        using var listedPayload = JsonDocument.Parse(listed.BodyText);
        Assert.Equal(200, listed.StatusCode);
        Assert.Contains(listedPayload.RootElement.EnumerateArray(),
            item => item.GetProperty("loopId").GetString() == "loop-123" &&
                    item.GetProperty("mode").GetString() == "walking" &&
                    item.GetProperty("workHour").GetInt32() == 8);
    }

    [Fact]
    public async Task MediaCreateAndGet_ReturnsCreatedItem()
    {
        var created = await _service.DispatchAsync(new ProtocolEnvelope
        {
            HostName = "api.jibo.com",
            Method = "POST",
            ServicePrefix = "Media_20160725",
            Operation = "Create",
            BodyText = """{"path":"/media/test-item","type":"image","reference":"demo"}"""
        });

        using var createdPayload = JsonDocument.Parse(created.BodyText);
        Assert.Equal("/media/test-item", createdPayload.RootElement.GetProperty("path").GetString());

        var fetched = await _service.DispatchAsync(new ProtocolEnvelope
        {
            HostName = "api.jibo.com",
            Method = "POST",
            ServicePrefix = "Media_20160725",
            Operation = "Get",
            BodyText = """{"paths":["/media/test-item"]}"""
        });

        using var fetchedPayload = JsonDocument.Parse(fetched.BodyText);
        Assert.Single(fetchedPayload.RootElement.EnumerateArray());
    }

    [Fact]
    public async Task MediaCreate_PersistsAcrossStoreRecreation_WhenPersistencePathIsConfigured()
    {
        var persistencePath = Path.Combine(Path.GetTempPath(), $"openjibo-state-{Guid.NewGuid():N}.json");
        try
        {
            var firstService = new JiboCloudProtocolService(new InMemoryCloudStateStore(persistencePath));
            await firstService.DispatchAsync(new ProtocolEnvelope
            {
                HostName = "api.jibo.com",
                Method = "POST",
                ServicePrefix = "Media_20160725",
                Operation = "Create",
                Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Content-Type"] = "image/jpeg"
                },
                BodyText = """{"path":"persisted-photo","type":"image","reference":"photo"}"""
            });

            var secondService = new JiboCloudProtocolService(new InMemoryCloudStateStore(persistencePath));
            var listed = await secondService.DispatchAsync(new ProtocolEnvelope
            {
                HostName = "api.jibo.com",
                Method = "POST",
                ServicePrefix = "Media_20160725",
                Operation = "List",
                BodyText = "{}"
            });

            using var listedPayload = JsonDocument.Parse(listed.BodyText);
            Assert.Single(listedPayload.RootElement.EnumerateArray());
            Assert.Equal("persisted-photo", listedPayload.RootElement[0].GetProperty("path").GetString());
        }
        finally
        {
            if (File.Exists(persistencePath)) File.Delete(persistencePath);
        }
    }

    [Fact]
    public async Task UpdateAndBackupPersistAcrossStoreRecreation_WhenPersistencePathIsConfigured()
    {
        var persistencePath = Path.Combine(Path.GetTempPath(), $"openjibo-update-backup-{Guid.NewGuid():N}.json");
        try
        {
            var firstService = new JiboCloudProtocolService(new InMemoryCloudStateStore(persistencePath));

            await firstService.DispatchAsync(new ProtocolEnvelope
            {
                HostName = "api.jibo.com",
                Method = "POST",
                ServicePrefix = "Update_20160715",
                Operation = "CreateUpdate",
                BodyText =
                    """{"fromVersion":"12.10.0","toVersion":"12.10.1","changes":"Restore proof","subsystem":"robot"}"""
            });

            await firstService.DispatchAsync(new ProtocolEnvelope
            {
                HostName = "api.jibo.com",
                Method = "POST",
                ServicePrefix = "Backup_20160715",
                Operation = "Create",
                BodyText = """{"name":"manual-backup"}"""
            });

            var firstStore = new InMemoryCloudStateStore(persistencePath);
            var firstInfo = firstStore.GetPersistenceStateInfo();

            var secondService = new JiboCloudProtocolService(new InMemoryCloudStateStore(persistencePath));
            var secondStore = new InMemoryCloudStateStore(persistencePath);
            var secondInfo = secondStore.GetPersistenceStateInfo();

            var updates = await secondService.DispatchAsync(new ProtocolEnvelope
            {
                HostName = "api.jibo.com",
                Method = "POST",
                ServicePrefix = "Update_20160715",
                Operation = "ListUpdates",
                BodyText = """{"subsystem":"robot"}"""
            });

            var backups = await secondService.DispatchAsync(new ProtocolEnvelope
            {
                HostName = "api.jibo.com",
                Method = "POST",
                ServicePrefix = "Backup_20160715",
                Operation = "List",
                BodyText = "{}"
            });

            var schedulerUpdates = await secondService.DispatchAsync(new ProtocolEnvelope
            {
                HostName = "127.0.0.1",
                Method = "GET",
                Path = "/update/robot"
            });

            using var updatesPayload = JsonDocument.Parse(updates.BodyText);
            using var backupsPayload = JsonDocument.Parse(backups.BodyText);
            using var schedulerPayload = JsonDocument.Parse(schedulerUpdates.BodyText);

            Assert.Equal(firstInfo.Revision, secondInfo.Revision);
            Assert.Equal("1", firstInfo.SchemaVersion);
            Assert.Equal("1", secondInfo.SchemaVersion);
            Assert.NotNull(secondInfo.LastLoadedUtc);
            Assert.NotNull(secondInfo.LastSavedUtc);
            Assert.NotEmpty(updatesPayload.RootElement.EnumerateArray());
            Assert.Contains(updatesPayload.RootElement.EnumerateArray(),
                item => item.GetProperty("changes").GetString() == "Restore proof");
            Assert.NotEmpty(backupsPayload.RootElement.EnumerateArray());
            Assert.Contains(backupsPayload.RootElement.EnumerateArray(),
                item => item.GetProperty("location").GetProperty("url").GetString()!.Contains("/backup/", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(schedulerPayload.RootElement.GetProperty("updates").EnumerateArray(),
                item => item.GetProperty("subsystem").GetString() == "robot" &&
                        item.GetProperty("changes").GetString() == "Restore proof");
        }
        finally
        {
            if (File.Exists(persistencePath)) File.Delete(persistencePath);
        }
    }

    [Fact]
    public async Task MediaCreate_StoresBodyAndServesMediaUrl()
    {
        var result = await _service.DispatchAsync(new ProtocolEnvelope
        {
            HostName = "api.jibo.com",
            Method = "POST",
            ServicePrefix = "Media_20160725",
            Operation = "Create",
            Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Content-Type"] = "image/jpeg",
                ["x-path"] = "photo-blob-1",
                ["x-type"] = "image"
            },
            BodyText = "binary-photo-placeholder"
        });

        using var createdPayload = JsonDocument.Parse(result.BodyText);
        Assert.Equal("https://api.jibo.com/media/photo-blob-1",
            createdPayload.RootElement.GetProperty("url").GetString());

        var mediaGet = await _service.DispatchAsync(new ProtocolEnvelope
        {
            HostName = "api.jibo.com",
            Method = "GET",
            Path = "/media/photo-blob-1"
        });

        Assert.Equal(200, mediaGet.StatusCode);
        Assert.Equal("image/jpeg", mediaGet.ContentType);
        Assert.Equal("binary-photo-placeholder", mediaGet.BodyText);
    }

    [Fact]
    public async Task MediaCreate_PersistsBinaryContentThroughFileMediaStore()
    {
        var directoryPath = Path.Combine(Path.GetTempPath(), "OpenJibo.Media.Tests", Guid.NewGuid().ToString("N"));
        var service = new JiboCloudProtocolService(new InMemoryCloudStateStore(),
            new FileMediaContentStore(directoryPath));

        var result = await service.DispatchAsync(new ProtocolEnvelope
        {
            HostName = "api.jibo.com",
            Method = "POST",
            ServicePrefix = "Media_20160725",
            Operation = "Create",
            Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Content-Type"] = "image/jpeg",
                ["x-path"] = "photo-blob-2",
                ["x-type"] = "image"
            },
            BodyText = "binary-photo-placeholder"
        });

        using var createdPayload = JsonDocument.Parse(result.BodyText);
        Assert.Equal("https://api.jibo.com/media/photo-blob-2",
            createdPayload.RootElement.GetProperty("url").GetString());

        var storedFile = Path.Combine(directoryPath, "photo-blob-2.bin");
        Assert.True(File.Exists(storedFile));

        var mediaGet = await service.DispatchAsync(new ProtocolEnvelope
        {
            HostName = "api.jibo.com",
            Method = "GET",
            Path = "/media/photo-blob-2"
        });

        Assert.Equal(200, mediaGet.StatusCode);
        Assert.Equal("image/jpeg", mediaGet.ContentType);
        Assert.Equal("binary-photo-placeholder", mediaGet.BodyText);
    }

    [Fact]
    public async Task MediaCreate_WritesBinaryManifestMetadataForSync()
    {
        var directoryPath = Path.Combine(Path.GetTempPath(), "OpenJibo.Media.Tests", Guid.NewGuid().ToString("N"));
        var service = new JiboCloudProtocolService(new InMemoryCloudStateStore(),
            new FileMediaContentStore(directoryPath));
        const string bodyText = "binary-photo-placeholder";

        var result = await service.DispatchAsync(new ProtocolEnvelope
        {
            HostName = "api.jibo.com",
            Method = "POST",
            ServicePrefix = "Media_20160725",
            Operation = "Create",
            Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Content-Type"] = "image/jpeg",
                ["x-path"] = "photo-blob-manifest",
                ["x-type"] = "image"
            },
            BodyText = bodyText
        });

        using var createdPayload = JsonDocument.Parse(result.BodyText);
        var meta = createdPayload.RootElement.GetProperty("meta");
        Assert.Equal(bodyText.Length, meta.GetProperty("contentLength").GetInt32());
        Assert.Equal(
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(bodyText))).ToLowerInvariant(),
            meta.GetProperty("contentSha256").GetString());

        var metaPath = Path.Combine(directoryPath, "photo-blob-manifest.json");
        using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(metaPath));
        var manifestMeta = manifest.RootElement.GetProperty("meta");
        Assert.Equal(bodyText.Length, manifestMeta.GetProperty("contentLength").GetInt32());
        Assert.Equal(
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(bodyText))).ToLowerInvariant(),
            manifestMeta.GetProperty("contentSha256").GetString());
        Assert.True(manifestMeta.TryGetProperty("storedUtc", out _));
    }

    [Fact]
    public async Task KeyCreateSymmetricKey_ReturnsKeyPayload()
    {
        var result = await _service.DispatchAsync(new ProtocolEnvelope
        {
            HostName = "api.jibo.com",
            Method = "POST",
            ServicePrefix = "Key_20160715",
            Operation = "CreateSymmetricKey",
            BodyText = """{"loopId":"openjibo-default-loop"}"""
        });

        using var payload = JsonDocument.Parse(result.BodyText);
        Assert.Equal("openjibo-default-loop", payload.RootElement.GetProperty("loopId").GetString());
        Assert.False(string.IsNullOrWhiteSpace(payload.RootElement.GetProperty("key").GetString()));
    }

    [Fact]
    public async Task PersonListHolidays_ReturnsHoliday()
    {
        var result = await _service.DispatchAsync(new ProtocolEnvelope
        {
            HostName = "api.jibo.com",
            Method = "POST",
            ServicePrefix = "Person_20160715",
            Operation = "ListHolidays",
            BodyText = "{}"
        });

        using var payload = JsonDocument.Parse(result.BodyText);
        Assert.NotEmpty(payload.RootElement.EnumerateArray());
    }

    [Fact]
    public void InMemoryCloudStateStore_SeedsPeopleForTheDefaultAccountLoop()
    {
        var store = new InMemoryCloudStateStore();

        var people = store.GetPeople();

        Assert.NotEmpty(people);
        Assert.Contains(people, person => person.IsPrimary);
        Assert.Contains(people,
            person => string.Equals(person.AccountId, store.GetAccount().AccountId,
                StringComparison.OrdinalIgnoreCase));
        Assert.Contains(people,
            person => string.Equals(person.LoopId, store.GetLoops()[0].LoopId, StringComparison.OrdinalIgnoreCase));
    }
}
