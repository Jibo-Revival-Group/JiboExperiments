using Jibo.Cloud.Application.Abstractions;
using Jibo.Cloud.Domain.Models;
using Jibo.Cloud.Infrastructure.DependencyInjection;
using Jibo.Cloud.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Jibo.Cloud.Tests.Infrastructure;

public sealed class PersistenceStoreTests
{
    [Fact]
    public void BindSessionToDevice_PersistsExplicitInventoryIdentityWithoutReplacingRuntimeIdentity()
    {
        var store = new InMemoryCloudStateStore();
        store.UpsertDevice(new DeviceRegistration
        {
            DeviceId = "Royal-Current-Sage-Canvas",
            RobotId = "Royal-Current-Sage-Canvas",
            FriendlyName = "OpenJibo Registered Robot"
        });
        var session = store.OpenSession("hub", "5c0b221fdf9d450019c5e254", "hub-test", "neohub", "/v1/listen");

        var linked = store.BindSessionToDevice(session.SessionId, "Royal-Current-Sage-Canvas");

        Assert.True(linked);
        Assert.Equal("5c0b221fdf9d450019c5e254", session.DeviceId);
        Assert.Equal("Royal-Current-Sage-Canvas", session.Metadata["registeredDeviceId"]?.ToString());
    }

    [Fact]
    public void ClearSessionDeviceBinding_RemovesOnlyTheExplicitInventoryIdentity()
    {
        var store = new InMemoryCloudStateStore();
        store.UpsertDevice(new DeviceRegistration { DeviceId = "Royal", RobotId = "Royal" });
        var session = store.OpenSession("hub", "runtime-id", "hub-test", "neohub", "/v1/listen");
        Assert.True(store.BindSessionToDevice(session.SessionId, "Royal"));

        var cleared = store.ClearSessionDeviceBinding(session.SessionId);

        Assert.True(cleared);
        Assert.Equal("runtime-id", session.DeviceId);
        Assert.False(session.Metadata.ContainsKey("registeredDeviceId"));
    }

    [Fact]
    public void SnapshotStoreFactory_DefaultsToFileBackend()
    {
        var factory = new PersistenceSnapshotStoreFactory();

        var store = factory.Create(Path.Combine(Path.GetTempPath(), $"factory-{Guid.NewGuid():N}.json"),
            PersistenceBackendKind.File, "sample-snapshot");

        Assert.Equal("JsonFileSnapshotStore", store.GetType().Name);
    }

    [Fact]
    public void SnapshotStoreFactory_AzureBackendIsExplicitlyUnavailable()
    {
        var factory = new PersistenceSnapshotStoreFactory();

        Assert.Throws<InvalidOperationException>(() =>
            factory.Create(Path.Combine(Path.GetTempPath(), $"factory-{Guid.NewGuid():N}.json"),
                PersistenceBackendKind.AzureSql, "sample-snapshot"));
    }

    [Fact]
    public void SnapshotStoreFactory_CanCreateSqliteBackend()
    {
        var factory = new PersistenceSnapshotStoreFactory();
        var databasePath = Path.Combine(Path.GetTempPath(), $"factory-{Guid.NewGuid():N}.db");

        var store = factory.Create(
            null,
            PersistenceBackendKind.Sqlite,
            "sample-snapshot",
            $"Data Source={databasePath}");

        Assert.Equal("SqliteSnapshotStore", store.GetType().Name);
    }

    [Fact]
    public void PersonalMemoryStore_CanUseAlternateSnapshotBackend()
    {
        var backend = new RecordingSnapshotStore();
        var store = new InMemoryPersonalMemoryStore(backend);
        var scope = new PersonalMemoryTenantScope("acct-b", "loop-b", "device-b", "person-b");

        store.SetName(scope, "Alt Backend");

        Assert.Single(backend.Saves);
        Assert.Equal("Alt Backend", store.GetName(scope));
        Assert.Equal("1", store.GetPersistenceStateInfo().SchemaVersion);
    }

    [Fact]
    public void CloudStateStore_CanUseAlternateSnapshotBackend()
    {
        var backend = new RecordingSnapshotStore();
        var store = new InMemoryCloudStateStore(backend);

        store.CreateMedia("openjibo-default-loop", "backend-photo", "image", "photo-ref", false, null);

        Assert.Single(backend.Saves);
        Assert.Contains(store.ListMedia(), item => item.Path == "backend-photo");
        Assert.Equal("1", store.GetPersistenceStateInfo().SchemaVersion);
    }

    [Fact]
    public void CloudStateStore_BootstrapRobot_DoesNotUsePlaceholderSerialNumber()
    {
        var store = new InMemoryCloudStateStore();
        var robot = store.GetRobot();

        Assert.False(string.IsNullOrWhiteSpace(robot.DeviceId));
        Assert.NotEqual("my-robot-serial-number", robot.DeviceId);
        Assert.StartsWith("openjibo-bootstrap-", robot.DeviceId, StringComparison.Ordinal);
        Assert.Equal(robot.DeviceId, robot.RobotId);
    }

    [Fact]
    public void PersonalMemoryStore_RoundTripsStateAndRevision()
    {
        var persistencePath = Path.Combine(Path.GetTempPath(), $"openjibo-personal-memory-{Guid.NewGuid():N}.json");

        try
        {
            var scope = new PersonalMemoryTenantScope("acct-a", "loop-a", "device-a", "person-a");

            var firstStore = new InMemoryPersonalMemoryStore(persistencePath);
            firstStore.SetName(scope, "Jibo Friend");
            firstStore.SetBirthday(scope, "May 17");
            firstStore.SetPreference(scope, "color", "blue");
            firstStore.SetImportantDate(scope, "anniversary", "June 1");
            firstStore.SetAffinity(scope, "pizza", PersonalAffinity.Like);
            firstStore.AddListItem(scope, "groceries", "milk");
            firstStore.SavePersistedState();

            var firstInfo = firstStore.GetPersistenceStateInfo();
            Assert.Equal("1", firstInfo.SchemaVersion);
            Assert.True(firstInfo.Revision > 0);
            Assert.NotNull(firstInfo.LastSavedUtc);

            var secondStore = new InMemoryPersonalMemoryStore(persistencePath);
            var secondInfo = secondStore.GetPersistenceStateInfo();
            Assert.Equal(firstInfo.Revision, secondInfo.Revision);
            Assert.Equal("Jibo Friend", secondStore.GetName(scope));
            Assert.Equal("May 17", secondStore.GetBirthday(scope));
            Assert.Equal("blue", secondStore.GetPreference(scope, "color"));
            Assert.Equal("June 1", secondStore.GetImportantDate(scope, "anniversary"));
            Assert.Equal(PersonalAffinity.Like, secondStore.GetAffinity(scope, "pizza"));
            Assert.Contains("milk", secondStore.GetListItems(scope, "groceries"));
        }
        finally
        {
            if (File.Exists(persistencePath)) File.Delete(persistencePath);
        }
    }

    [Fact]
    public void CloudStateStore_RoundTripsTopologyAndContentState()
    {
        var persistencePath = Path.Combine(Path.GetTempPath(), $"openjibo-cloud-state-{Guid.NewGuid():N}.json");

        try
        {
            var firstStore = new InMemoryCloudStateStore(persistencePath);
            var update = firstStore.CreateUpdate("1.0.0", "1.0.1", "Bug fix", null, 42, "robot", null, null);
            var media = firstStore.CreateMedia("openjibo-default-loop", "persisted-photo", "image", "photo-ref", false,
                new Dictionary<string, object?> { ["note"] = "roundtrip" });
            var commute = firstStore.UpsertCommuteProfile(new CommuteProfileRecord
            {
                LoopId = "openjibo-default-loop",
                Mode = "driving",
                WorkHour = 8,
                WorkMinute = 30,
                TypicalDurationMinutes = 25
            });
            var calendarEvent = firstStore.UpsertCalendarEvent(new CalendarEventRecord
            {
                LoopId = "openjibo-default-loop",
                Summary = "Report review",
                TimeLabel = "at 6:00 p.m.",
                Date = DateOnly.FromDateTime(DateTime.UtcNow)
            });
            var greetingPresence = firstStore.UpsertGreetingPresence(new GreetingPresenceRecord
            {
                LoopId = "openjibo-default-loop",
                PersonId = "person-1",
                SpeakerId = "person-1",
                PreferredName = "Jake",
                LastSeenUtc = DateTimeOffset.UtcNow.AddMinutes(-5),
                LastGreetedUtc = DateTimeOffset.UtcNow.AddMinutes(-4),
                LastGreetingRoute = "ProactiveGreeting",
                LastGreetingIntent = "proactive_greeting"
            });
            var loopMember = firstStore.AddLoopMember("openjibo-default-loop", null, "family@example.com", "Family",
                "Tester", null, null, false, "adult");
            var enrolledMember = firstStore.SetMemberEnrollment("openjibo-default-loop", loopMember.Id, true, true);
            var recognitionObservation = firstStore.RecordRecognitionObservation("openjibo-default-loop",
                enrolledMember.Id, "Face", "Recognized", 0.93, "conversion-video-smoke");
            var sessionToken = firstStore.IssueRobotToken("robot-123");
            var device = firstStore.GetOrCreateDevice("robot-123", "3.2.1", "4.5.6");
            firstStore.SavePersistedState();

            var firstInfo = firstStore.GetPersistenceStateInfo();
            Assert.Equal("1", firstInfo.SchemaVersion);
            Assert.True(firstInfo.Revision > 0);
            Assert.NotNull(firstInfo.LastSavedUtc);

            var secondStore = new InMemoryCloudStateStore(persistencePath);
            var secondInfo = secondStore.GetPersistenceStateInfo();
            Assert.Equal(firstInfo.Revision, secondInfo.Revision);
            Assert.Contains(secondStore.ListUpdates("robot"), item => item.UpdateId == update.UpdateId);
            Assert.Contains(secondStore.ListMedia(), item => item.Path == media.Path);
            Assert.Contains(secondStore.GetCommuteProfiles("openjibo-default-loop"),
                item => item.Id == commute.Id && item.Mode == commute.Mode);
            Assert.Contains(secondStore.GetCalendarEvents("openjibo-default-loop"),
                item => item.Id == calendarEvent.Id && item.Summary == calendarEvent.Summary);
            Assert.Contains(secondStore.GetGreetingPresences("openjibo-default-loop"),
                item => item.PersonId == greetingPresence.PersonId &&
                        item.PreferredName == greetingPresence.PreferredName &&
                        item.LastGreetingRoute == greetingPresence.LastGreetingRoute);
            var persistedMember = Assert.Single(secondStore.GetLoopMembers("openjibo-default-loop"),
                item => item.Id == enrolledMember.Id);
            Assert.True(persistedMember.FaceEnrolled);
            Assert.True(persistedMember.VoiceEnrolled);
            var persistedObservation = Assert.Single(secondStore.GetRecognitionObservations("openjibo-default-loop"),
                item => item.ObservationId == recognitionObservation.ObservationId);
            Assert.Equal(enrolledMember.Id, persistedObservation.MemberId);
            Assert.Equal("face", persistedObservation.Modality);
            Assert.Equal("recognized", persistedObservation.Outcome);
            Assert.Equal("conversion-video-smoke", persistedObservation.Source);
            Assert.NotNull(secondStore.FindSessionByToken(sessionToken));
            Assert.Equal("3.2.1", secondStore.GetOrCreateDevice(device.DeviceId, null, null).FirmwareVersion);
            Assert.NotEmpty(secondStore.GetPeople());
            Assert.NotEmpty(secondStore.GetLoops());
        }
        finally
        {
            if (File.Exists(persistencePath)) File.Delete(persistencePath);
        }
    }

    [Fact]
    public void CloudStateStore_AllowsMultipleRobotsForSingleAccount()
    {
        var persistencePath =
            Path.Combine(Path.GetTempPath(), $"openjibo-cloud-multiple-robots-{Guid.NewGuid():N}.json");

        try
        {
            var firstStore = new InMemoryCloudStateStore(persistencePath);
            var account = firstStore.GetAccount();
            var loop = Assert.Single(firstStore.GetLoops(),
                candidate => candidate.OwnerAccountId == account.AccountId);

            var kitchenRobot = firstStore.GetOrCreateDevice("BOJW-KITCHEN-0001", "1.9.2", "1.0.20");
            var officeRobot = firstStore.GetOrCreateDevice("BOJW-OFFICE-0002", "1.9.2", "1.0.20");

            firstStore.AddLoopMember(loop.LoopId, kitchenRobot.RobotId, null, "Kitchen", "Jibo", null, null,
                false, "robot");
            firstStore.AddLoopMember(loop.LoopId, officeRobot.RobotId, null, "Office", "Jibo", null, null,
                false, "robot");
            firstStore.SavePersistedState();

            var secondStore = new InMemoryCloudStateStore(persistencePath);
            var persistedAccount = secondStore.GetAccount();
            var persistedLoop = Assert.Single(secondStore.GetLoops(),
                candidate => candidate.OwnerAccountId == persistedAccount.AccountId);
            var robotMembers = secondStore.GetLoopMembers(persistedLoop.LoopId)
                .Where(member => member.Type == "robot")
                .ToArray();

            Assert.Equal(account.AccountId, persistedAccount.AccountId);
            Assert.NotNull(secondStore.FindDeviceByFriendlyId(kitchenRobot.DeviceId));
            Assert.NotNull(secondStore.FindDeviceByFriendlyId(officeRobot.DeviceId));
            Assert.Contains(robotMembers, member => member.AccountId == kitchenRobot.RobotId);
            Assert.Contains(robotMembers, member => member.AccountId == officeRobot.RobotId);
            Assert.True(robotMembers.Select(member => member.AccountId).Distinct().Count() >= 2);
        }
        finally
        {
            if (File.Exists(persistencePath)) File.Delete(persistencePath);
        }
    }

    [Fact]
    public void CloudStateStore_RehydratesDefaultLoopWhenSnapshotLoopsAreMissing()
    {
        var persistencePath = Path.Combine(Path.GetTempPath(), $"openjibo-cloud-empty-loops-{Guid.NewGuid():N}.json");

        try
        {
            File.WriteAllText(persistencePath, """
                                               {
                                                 "SchemaVersion": "1",
                                                 "Revision": 7,
                                                 "Loops": []
                                               }
                                               """);

            var store = new InMemoryCloudStateStore(persistencePath);

            Assert.NotEmpty(store.GetLoops());
            Assert.Equal("openjibo-default-loop", store.GetLoops()[0].LoopId);
            Assert.NotEmpty(store.GetPeople());
        }
        finally
        {
            if (File.Exists(persistencePath)) File.Delete(persistencePath);
        }
    }

    [Fact]
    public void AddOpenJiboCloud_SeedsConfiguredOwnerNameIntoCloudState()
    {
        var root = Path.Combine(Path.GetTempPath(), $"openjibo-owner-name-{Guid.NewGuid():N}");
        var statePath = Path.Combine(root, "cloud-state.json");
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OpenJibo:State:PersistencePath"] = statePath,
                ["OpenJibo:OwnerFirstName"] = "Jacob",
                ["OpenJibo:OwnerLastName"] = "Dubin"
            })
            .Build();

        Directory.CreateDirectory(root);

        var services = new ServiceCollection();
        services.AddOpenJiboCloud(configuration);

        using var provider = services.BuildServiceProvider();
        var cloudStateStore = provider.GetRequiredService<ICloudStateStore>();

        Assert.Equal("Jacob", cloudStateStore.GetAccount().FirstName);
        Assert.Equal("Dubin", cloudStateStore.GetAccount().LastName);
        Assert.Contains(cloudStateStore.GetPeople(), person =>
            person.IsPrimary &&
            person is { DisplayName: "Jacob Dubin", Alias: "Jacob" });
    }

    [Fact]
    public void AddOpenJiboCloud_FailsFastWhenLocalWhisperDependenciesAreMissing()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OpenJibo:Stt:EnableLocalWhisperCpp"] = "true",
                ["OpenJibo:Stt:FfmpegPath"] = @"Z:\definitely-missing\ffmpeg.exe",
                ["OpenJibo:Stt:WhisperCliPath"] = @"Z:\definitely-missing\whisper-cli.exe",
                ["OpenJibo:Stt:WhisperModelPath"] = @"Z:\definitely-missing\ggml-base.en.bin"
            })
            .Build();

        var services = new ServiceCollection();

        var ex = Assert.Throws<InvalidOperationException>(() => services.AddOpenJiboCloud(configuration));

        Assert.Contains("buffered-audio STT", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("OpenJibo:Stt:FfmpegPath", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("OpenJibo:Stt:WhisperCliPath", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("OpenJibo:Stt:WhisperModelPath", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AddOpenJiboCloud_AutoDerivesSqliteConnectionStringsFromPersistencePaths()
    {
        var root = Path.Combine(Path.GetTempPath(), $"openjibo-sqlite-bootstrap-{Guid.NewGuid():N}");
        var statePath = Path.Combine(root, "cloud-state.json");
        var personalMemoryPath = Path.Combine(root, "personal-memory.json");

        Directory.CreateDirectory(root);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OpenJibo:State:Backend"] = "Sqlite",
                ["OpenJibo:State:PersistencePath"] = statePath,
                ["OpenJibo:PersonalMemory:Backend"] = "Sqlite",
                ["OpenJibo:PersonalMemory:PersistencePath"] = personalMemoryPath
            })
            .Build();

        var services = new ServiceCollection();
        services.AddOpenJiboCloud(configuration);

        using var provider = services.BuildServiceProvider();
        var cloudStateStore = provider.GetRequiredService<ICloudStateStore>();
        var personalMemoryStore = provider.GetRequiredService<IPersonalMemoryStore>();

        cloudStateStore.CreateUpdate("1.0.0", "1.0.1", "Bootstrap smoke", null, 10, "robot", null, null);
        personalMemoryStore.SetName(new PersonalMemoryTenantScope("acct-sqlite", "loop-sqlite", "device-sqlite"),
            "SQLite Bootstrap");

        Assert.True(File.Exists(Path.ChangeExtension(statePath, ".db")));
        Assert.True(File.Exists(Path.ChangeExtension(personalMemoryPath, ".db")));
    }

    [Fact]
    public void AddOpenJiboCloud_DefaultsPersistenceBackendsToSqliteWhenUnspecified()
    {
        var root = Path.Combine(Path.GetTempPath(), $"openjibo-sqlite-default-{Guid.NewGuid():N}");
        var statePath = Path.Combine(root, "cloud-state.json");
        var personalMemoryPath = Path.Combine(root, "personal-memory.json");

        Directory.CreateDirectory(root);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OpenJibo:State:PersistencePath"] = statePath,
                ["OpenJibo:PersonalMemory:PersistencePath"] = personalMemoryPath
            })
            .Build();

        var services = new ServiceCollection();
        services.AddOpenJiboCloud(configuration);

        using var provider = services.BuildServiceProvider();
        var cloudStateStore = provider.GetRequiredService<ICloudStateStore>();
        var personalMemoryStore = provider.GetRequiredService<IPersonalMemoryStore>();

        cloudStateStore.CreateUpdate("1.0.0", "1.0.1", "Bootstrap smoke", null, 10, "robot", null, null);
        personalMemoryStore.SetName(new PersonalMemoryTenantScope("acct-sqlite-default", "loop-sqlite-default",
            "device-sqlite-default"), "SQLite Default");

        Assert.True(File.Exists(Path.ChangeExtension(statePath, ".db")));
        Assert.True(File.Exists(Path.ChangeExtension(personalMemoryPath, ".db")));
    }

    [Fact]
    public void PersonalMemoryStore_IgnoresCorruptSnapshotAndOverwritesWithValidJson()
    {
        var persistenceDirectory = Path.Combine(Path.GetTempPath(), $"openjibo-corrupt-memory-{Guid.NewGuid():N}");
        var persistencePath = Path.Combine(persistenceDirectory, "memory.json");

        try
        {
            Directory.CreateDirectory(persistenceDirectory);
            File.WriteAllText(persistencePath, "{ not valid json");

            var scope = new PersonalMemoryTenantScope("acct-corrupt", "loop-corrupt", "device-corrupt");
            var store = new InMemoryPersonalMemoryStore(persistencePath);
            Assert.Null(store.GetName(scope));

            store.SetName(scope, "Recovered");

            var reloaded = new InMemoryPersonalMemoryStore(persistencePath);
            Assert.Equal("Recovered", reloaded.GetName(scope));
            Assert.DoesNotContain(Directory.GetFiles(persistenceDirectory),
                path => Path.GetFileName(path).Contains(".tmp", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(persistenceDirectory)) Directory.Delete(persistenceDirectory, true);
        }
    }

    private sealed class RecordingSnapshotStore : ISnapshotStore
    {
        public List<object> Saves { get; } = [];

        public TSnapshot2? Load<TSnapshot2>() where TSnapshot2 : class
        {
            return null;
        }

        public void Save<TSnapshot2>(TSnapshot2 snapshot) where TSnapshot2 : class
        {
            Saves.Add(snapshot);
        }
    }
}
