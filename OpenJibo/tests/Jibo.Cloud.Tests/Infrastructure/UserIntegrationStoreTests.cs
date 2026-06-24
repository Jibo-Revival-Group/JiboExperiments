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
}