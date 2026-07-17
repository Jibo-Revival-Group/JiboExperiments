using Jibo.Cloud.Infrastructure.Persistence;

namespace Jibo.Cloud.Tests.Infrastructure;

public sealed class MemberCalendarFeedStoreTests
{
    [Fact]
    public void UpsertAndClear_MemberCalendarFeed_RoundTripsEncryptedSnapshot()
    {
        var path = Path.Combine(Path.GetTempPath(), $"openjibo-cal-feed-{Guid.NewGuid():N}.json");
        try
        {
            var snapshotStore = new EncryptedUserDataSnapshotStore(path, new UserDataEncryptionService());
            var store = new InMemoryUserIntegrationStore(snapshotStore);

            var saved = store.UpsertMemberCalendarFeed(
                "openjibo-default-loop",
                "mbr-zane",
                "https://calendar.example.com/zane/basic.ics");

            Assert.Equal("mbr-zane", saved.MemberId);
            Assert.Equal("calendar.example.com",
                Jibo.Cloud.Infrastructure.Calendar.IcalUrlValidator.TryGetSafeHost(saved.IcalUrl));

            var reloaded = new InMemoryUserIntegrationStore(
                new EncryptedUserDataSnapshotStore(path, new UserDataEncryptionService()));
            var found = reloaded.FindMemberCalendarFeed("openjibo-default-loop", "mbr-zane");
            Assert.NotNull(found);
            Assert.Equal(saved.IcalUrl, found!.IcalUrl);

            Assert.NotNull(reloaded.ClearMemberCalendarFeed("openjibo-default-loop", "mbr-zane"));
            Assert.Null(reloaded.FindMemberCalendarFeed("openjibo-default-loop", "mbr-zane"));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void Load_MigratesSchemaV1SnapshotsWithoutWipingHomeAssistantLinks()
    {
        var path = Path.Combine(Path.GetTempPath(), $"openjibo-cal-feed-v1-{Guid.NewGuid():N}.json");
        try
        {
            var encryption = new UserDataEncryptionService();
            var snapshotStore = new EncryptedUserDataSnapshotStore(path, encryption);
            snapshotStore.Save(new Jibo.Cloud.Domain.Models.UserIntegrationSnapshot
            {
                SchemaVersion = 1,
                HomeAssistantLinks =
                [
                    new Jibo.Cloud.Domain.Models.HomeAssistantLinkRecord
                    {
                        JiboDeviceId = "device-1",
                        JiboFriendlyName = "Friendly",
                        HaInstanceId = "ha-1"
                    }
                ],
                MemberCalendarFeeds = []
            });

            var store = new InMemoryUserIntegrationStore(
                new EncryptedUserDataSnapshotStore(path, encryption));
            Assert.Single(store.GetHomeAssistantLinks());
            Assert.Empty(store.GetMemberCalendarFeeds());
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
