using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Jibo.Cloud.Application.Services;
using Jibo.Cloud.Domain.Models;
using Jibo.Cloud.Infrastructure.Media;
using Jibo.Cloud.Infrastructure.Persistence;

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
    public async Task GetUpdateFrom_WithoutStagedUpdate_ReturnsNoopUpdate()
    {
        var result = await _service.DispatchAsync(new ProtocolEnvelope
        {
            HostName = "api.jibo.com",
            Method = "POST",
            ServicePrefix = "Update_20160715",
            Operation = "GetUpdateFrom",
            BodyText = """{"subsystem":"robot","fromVersion":"1.0.0"}"""
        });

        using var payload = JsonDocument.Parse(result.BodyText);
        Assert.Equal(200, result.StatusCode);
        Assert.Equal("application/x-amz-json-1.1", result.ContentType);
        Assert.Equal("noop-update-robot-1.0.0", payload.RootElement.GetProperty("_id").GetString());
        Assert.Equal("1.0.0", payload.RootElement.GetProperty("fromVersion").GetString());
        Assert.Equal("1.0.0", payload.RootElement.GetProperty("toVersion").GetString());
        Assert.Equal("No update available", payload.RootElement.GetProperty("changes").GetString());
        Assert.Equal(0, payload.RootElement.GetProperty("length").GetInt64());
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
                    """{"fromVersion":"1.0.0","toVersion":"1.0.1","changes":"Restore proof","subsystem":"robot"}"""
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

            using var updatesPayload = JsonDocument.Parse(updates.BodyText);
            using var backupsPayload = JsonDocument.Parse(backups.BodyText);

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
                item => item.TryGetProperty("Name", out var name) && name.GetString() == "manual-backup");
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
