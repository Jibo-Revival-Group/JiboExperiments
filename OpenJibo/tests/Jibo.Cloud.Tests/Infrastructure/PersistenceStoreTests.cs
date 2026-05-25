using Jibo.Cloud.Application.Abstractions;
using Jibo.Cloud.Domain.Models;
using Jibo.Cloud.Infrastructure.Persistence;

namespace Jibo.Cloud.Tests.Infrastructure;

public sealed class PersistenceStoreTests
{
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
            if (Directory.Exists(persistenceDirectory)) Directory.Delete(persistenceDirectory, recursive: true);
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
