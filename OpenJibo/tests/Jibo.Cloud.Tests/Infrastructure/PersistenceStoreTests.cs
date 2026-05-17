using Jibo.Cloud.Application.Abstractions;
using Jibo.Cloud.Infrastructure.Persistence;

namespace Jibo.Cloud.Tests.Infrastructure;

public sealed class PersistenceStoreTests
{
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
            if (File.Exists(persistencePath))
            {
                File.Delete(persistencePath);
            }
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
            var media = firstStore.CreateMedia("openjibo-default-loop", "persisted-photo", "image", "photo-ref", false, new Dictionary<string, object?> { ["note"] = "roundtrip" });
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
            Assert.NotNull(secondStore.FindSessionByToken(sessionToken));
            Assert.Equal("3.2.1", secondStore.GetOrCreateDevice(device.DeviceId, null, null).FirmwareVersion);
            Assert.NotEmpty(secondStore.GetPeople());
            Assert.NotEmpty(secondStore.GetLoops());
        }
        finally
        {
            if (File.Exists(persistencePath))
            {
                File.Delete(persistencePath);
            }
        }
    }
}
