using Jibo.Cloud.Domain.Models;
using Jibo.Cloud.Infrastructure.Persistence;

namespace Jibo.Cloud.Tests.Infrastructure;

public sealed class UserIntegrationStoreTests
{
    [Fact]
    public void FindLinkForJibo_MatchesDeviceIdOrFriendlyName()
    {
        var snapshotStore = new EncryptedUserDataSnapshotStore(
            Path.Combine(Path.GetTempPath(), $"openjibo-user-int-{Guid.NewGuid():N}.json"),
            new UserDataEncryptionService());
        var store = new InMemoryUserIntegrationStore(snapshotStore);
        store.AddHomeAssistantLink(
            "BOJW-1000-0017-0820-0020",
            "Ghost-Instance-Onion-Silk",
            "ha-instance-1");

        Assert.NotNull(store.FindLinkForJibo("BOJW-1000-0017-0820-0020", null));
        Assert.NotNull(store.FindLinkForJibo(null, "Ghost-Instance-Onion-Silk"));
        Assert.NotNull(store.FindLinkForJibo("Ghost-Instance-Onion-Silk", "BOJW-1000-0017-0820-0020"));
        Assert.Null(store.FindLinkForJibo("other-device", "other-friendly"));
    }

    [Fact]
    public void FindLinkByLinkId_ReturnsStoredLink()
    {
        var snapshotStore = new EncryptedUserDataSnapshotStore(
            Path.Combine(Path.GetTempPath(), $"openjibo-user-int-{Guid.NewGuid():N}.json"),
            new UserDataEncryptionService());
        var store = new InMemoryUserIntegrationStore(snapshotStore);
        var link = store.AddHomeAssistantLink(
            "BOJW-1000-0017-0820-0020",
            "Ghost-Instance-Onion-Silk",
            "ha-instance-1");

        Assert.NotNull(store.FindLinkByLinkId(link.LinkId));
        Assert.Null(store.FindLinkByLinkId("missing-link"));
    }

    [Fact]
    public void RemoveHomeAssistantLink_RemovesStoredLink()
    {
        var snapshotStore = new EncryptedUserDataSnapshotStore(
            Path.Combine(Path.GetTempPath(), $"openjibo-user-int-{Guid.NewGuid():N}.json"),
            new UserDataEncryptionService());
        var store = new InMemoryUserIntegrationStore(snapshotStore);
        var link = store.AddHomeAssistantLink(
            "BOJW-1000-0017-0820-0020",
            "Ghost-Instance-Onion-Silk",
            "ha-instance-1");

        var removed = store.RemoveHomeAssistantLink(link.LinkId);

        Assert.NotNull(removed);
        Assert.Null(store.FindLinkByLinkId(link.LinkId));
    }

    [Fact]
    public void UpdateHomeAssistantClimateBlacklist_PersistsFlags()
    {
        var snapshotStore = new EncryptedUserDataSnapshotStore(
            Path.Combine(Path.GetTempPath(), $"openjibo-user-int-{Guid.NewGuid():N}.json"),
            new UserDataEncryptionService());
        var store = new InMemoryUserIntegrationStore(snapshotStore);
        var link = store.AddHomeAssistantLink(
            "BOJW-1000-0017-0820-0020",
            "Ghost-Instance-Onion-Silk",
            "ha-instance-1");

        var updated = store.UpdateHomeAssistantClimateBlacklist(link.LinkId, true, false);

        Assert.NotNull(updated);
        Assert.True(updated!.BlacklistHeat);
        Assert.False(updated.BlacklistCool);
        Assert.True(store.FindLinkByLinkId(link.LinkId)!.BlacklistHeat);
        Assert.Equal(link.CommandSecret, updated.CommandSecret);
    }

    [Fact]
    public void AddHomeAssistantLink_GeneratesCommandSecret()
    {
        var snapshotStore = new EncryptedUserDataSnapshotStore(
            Path.Combine(Path.GetTempPath(), $"openjibo-user-int-{Guid.NewGuid():N}.json"),
            new UserDataEncryptionService());
        var store = new InMemoryUserIntegrationStore(snapshotStore);
        var link = store.AddHomeAssistantLink(
            "BOJW-1000-0017-0820-0020",
            "Ghost-Instance-Onion-Silk",
            "ha-instance-1");

        Assert.False(string.IsNullOrWhiteSpace(link.CommandSecret));
        Assert.Equal(64, link.CommandSecret.Length);
    }

    [Fact]
    public void EnsureHomeAssistantCommandSecret_MigratesEmptySecret()
    {
        var path = Path.Combine(Path.GetTempPath(), $"openjibo-user-int-{Guid.NewGuid():N}.json");
        var encryption = new UserDataEncryptionService();
        var snapshotStore = new EncryptedUserDataSnapshotStore(path, encryption);
        snapshotStore.Save(new UserIntegrationSnapshot
        {
            SchemaVersion = UserIntegrationSnapshot.CurrentSchemaVersion,
            HomeAssistantLinks =
            [
                new HomeAssistantLinkRecord
                {
                    LinkId = "legacy-link",
                    JiboDeviceId = "BOJW-1000-0017-0820-0020",
                    JiboFriendlyName = "Ghost-Instance-Onion-Silk",
                    HaInstanceId = "ha-instance-1",
                    CommandSecret = ""
                }
            ]
        });

        var store = new InMemoryUserIntegrationStore(snapshotStore);
        var ensured = store.EnsureHomeAssistantCommandSecret("legacy-link");

        Assert.NotNull(ensured);
        Assert.False(string.IsNullOrWhiteSpace(ensured!.CommandSecret));
        Assert.Equal(64, ensured.CommandSecret.Length);
        Assert.Equal(ensured.CommandSecret, store.FindLinkByLinkId("legacy-link")!.CommandSecret);
    }
}