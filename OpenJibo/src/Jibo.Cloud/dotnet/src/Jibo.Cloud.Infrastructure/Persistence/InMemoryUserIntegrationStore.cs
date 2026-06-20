using Jibo.Cloud.Application.Abstractions;
using Jibo.Cloud.Domain.Models;

namespace Jibo.Cloud.Infrastructure.Persistence;

public sealed class InMemoryUserIntegrationStore : IUserIntegrationStore
{
    private readonly EncryptedUserDataSnapshotStore _snapshotStore;
    private readonly Lock _syncRoot = new();
    private List<HomeAssistantLinkRecord> _links;

    public InMemoryUserIntegrationStore(EncryptedUserDataSnapshotStore snapshotStore)
    {
        _snapshotStore = snapshotStore;
        var snapshot = snapshotStore.LoadOrReset();
        _links = snapshot.HomeAssistantLinks.ToList();
    }

    public IReadOnlyList<HomeAssistantLinkRecord> GetHomeAssistantLinks()
    {
        lock (_syncRoot)
        {
            return _links.ToArray();
        }
    }

    public HomeAssistantLinkRecord? FindLinkByHaInstanceId(string haInstanceId)
    {
        lock (_syncRoot)
        {
            return _links.FirstOrDefault(link =>
                link.HaInstanceId.Equals(haInstanceId, StringComparison.OrdinalIgnoreCase));
        }
    }

    public HomeAssistantLinkRecord? FindLinkByLinkId(string linkId)
    {
        lock (_syncRoot)
        {
            return _links.FirstOrDefault(link =>
                link.LinkId.Equals(linkId, StringComparison.OrdinalIgnoreCase));
        }
    }

    public HomeAssistantLinkRecord? FindLinkForJibo(string? jiboDeviceId, string? jiboFriendlyId)
    {
        lock (_syncRoot)
        {
            return _links.FirstOrDefault(link => MatchesJiboIdentity(link, jiboDeviceId, jiboFriendlyId));
        }
    }

    public HomeAssistantLinkRecord AddHomeAssistantLink(
        string jiboDeviceId,
        string jiboFriendlyName,
        string haInstanceId)
    {
        lock (_syncRoot)
        {
            var existing = _links.FirstOrDefault(link =>
                link.HaInstanceId.Equals(haInstanceId, StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
            {
                var updated = new HomeAssistantLinkRecord
                {
                    LinkId = existing.LinkId,
                    JiboDeviceId = jiboDeviceId,
                    JiboFriendlyName = jiboFriendlyName,
                    HaInstanceId = haInstanceId,
                    PairedAtUtc = existing.PairedAtUtc,
                    LastSeenUtc = DateTimeOffset.UtcNow
                };

                _links = _links
                    .Select(link => link.LinkId == existing.LinkId ? updated : link)
                    .ToList();
                PersistLocked();
                return updated;
            }

            var record = new HomeAssistantLinkRecord
            {
                JiboDeviceId = jiboDeviceId,
                JiboFriendlyName = jiboFriendlyName,
                HaInstanceId = haInstanceId
            };
            _links.Add(record);
            PersistLocked();
            return record;
        }
    }

    public void UpdateHomeAssistantLastSeen(string linkId, DateTimeOffset lastSeenUtc)
    {
        lock (_syncRoot)
        {
            var index = _links.FindIndex(link => link.LinkId.Equals(linkId, StringComparison.OrdinalIgnoreCase));
            if (index < 0) return;

            var current = _links[index];
            _links[index] = new HomeAssistantLinkRecord
            {
                LinkId = current.LinkId,
                JiboDeviceId = current.JiboDeviceId,
                JiboFriendlyName = current.JiboFriendlyName,
                HaInstanceId = current.HaInstanceId,
                PairedAtUtc = current.PairedAtUtc,
                LastSeenUtc = lastSeenUtc
            };
            PersistLocked();
        }
    }

    public HomeAssistantLinkRecord? RemoveHomeAssistantLink(string linkId)
    {
        lock (_syncRoot)
        {
            var index = _links.FindIndex(link => link.LinkId.Equals(linkId, StringComparison.OrdinalIgnoreCase));
            if (index < 0) return null;

            var removed = _links[index];
            _links.RemoveAt(index);
            PersistLocked();
            return removed;
        }
    }

    private void PersistLocked()
    {
        _snapshotStore.Save(new UserIntegrationSnapshot
        {
            SchemaVersion = UserIntegrationSnapshot.CurrentSchemaVersion,
            HomeAssistantLinks = _links.ToArray()
        });
    }

    private static bool MatchesJiboIdentity(
        HomeAssistantLinkRecord link,
        string? jiboDeviceId,
        string? jiboFriendlyId)
    {
        foreach (var candidate in new[] { jiboDeviceId, jiboFriendlyId }.Where(static value =>
                     !string.IsNullOrWhiteSpace(value)))
        {
            if (link.JiboDeviceId.Equals(candidate, StringComparison.OrdinalIgnoreCase) ||
                link.JiboFriendlyName.Equals(candidate, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
