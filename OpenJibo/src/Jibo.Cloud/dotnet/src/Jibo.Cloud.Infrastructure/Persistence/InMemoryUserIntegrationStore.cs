using Jibo.Cloud.Application.Abstractions;
using Jibo.Cloud.Domain.Models;

namespace Jibo.Cloud.Infrastructure.Persistence;

public sealed class InMemoryUserIntegrationStore : IUserIntegrationStore
{
    private readonly EncryptedUserDataSnapshotStore _snapshotStore;
    private readonly Lock _syncRoot = new();
    private List<HomeAssistantLinkRecord> _links;
    private List<MemberCalendarFeedRecord> _calendarFeeds;

    public InMemoryUserIntegrationStore(EncryptedUserDataSnapshotStore snapshotStore)
    {
        _snapshotStore = snapshotStore;
        var snapshot = snapshotStore.LoadOrReset();
        _links = snapshot.HomeAssistantLinks.ToList();
        _calendarFeeds = snapshot.MemberCalendarFeeds.ToList();
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

    public IReadOnlyList<MemberCalendarFeedRecord> GetMemberCalendarFeeds(string? loopId = null)
    {
        lock (_syncRoot)
        {
            if (string.IsNullOrWhiteSpace(loopId))
                return _calendarFeeds.ToArray();

            return _calendarFeeds
                .Where(feed => feed.LoopId.Equals(loopId, StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }
    }

    public MemberCalendarFeedRecord? FindMemberCalendarFeed(string loopId, string memberId)
    {
        lock (_syncRoot)
        {
            return FindFeedLocked(loopId, memberId);
        }
    }

    public MemberCalendarFeedRecord UpsertMemberCalendarFeed(
        string loopId,
        string memberId,
        string icalUrl,
        bool isEnabled = true)
    {
        if (string.IsNullOrWhiteSpace(loopId))
            throw new ArgumentException("Loop id is required.", nameof(loopId));
        if (string.IsNullOrWhiteSpace(memberId))
            throw new ArgumentException("Member id is required.", nameof(memberId));
        if (string.IsNullOrWhiteSpace(icalUrl))
            throw new ArgumentException("iCal URL is required.", nameof(icalUrl));

        lock (_syncRoot)
        {
            var existing = FindFeedLocked(loopId, memberId);
            var now = DateTimeOffset.UtcNow;
            var record = new MemberCalendarFeedRecord
            {
                FeedId = existing?.FeedId ?? Guid.NewGuid().ToString("N"),
                LoopId = loopId.Trim(),
                MemberId = memberId.Trim(),
                IcalUrl = icalUrl.Trim(),
                IsEnabled = isEnabled,
                CreatedUtc = existing?.CreatedUtc ?? now,
                UpdatedUtc = now,
                LastSuccessUtc = existing?.LastSuccessUtc,
                LastError = existing?.LastError
            };

            if (existing is null)
                _calendarFeeds.Add(record);
            else
                _calendarFeeds = _calendarFeeds
                    .Select(feed => feed.FeedId == existing.FeedId ? record : feed)
                    .ToList();

            PersistLocked();
            return record;
        }
    }

    public MemberCalendarFeedRecord? ClearMemberCalendarFeed(string loopId, string memberId)
    {
        lock (_syncRoot)
        {
            var existing = FindFeedLocked(loopId, memberId);
            if (existing is null) return null;

            _calendarFeeds.RemoveAll(feed => feed.FeedId == existing.FeedId);
            PersistLocked();
            return existing;
        }
    }

    public MemberCalendarFeedRecord? UpdateMemberCalendarFeedSyncStatus(
        string loopId,
        string memberId,
        DateTimeOffset? lastSuccessUtc,
        string? lastError)
    {
        lock (_syncRoot)
        {
            var existing = FindFeedLocked(loopId, memberId);
            if (existing is null) return null;

            var updated = new MemberCalendarFeedRecord
            {
                FeedId = existing.FeedId,
                LoopId = existing.LoopId,
                MemberId = existing.MemberId,
                IcalUrl = existing.IcalUrl,
                IsEnabled = existing.IsEnabled,
                CreatedUtc = existing.CreatedUtc,
                UpdatedUtc = existing.UpdatedUtc,
                LastSuccessUtc = lastSuccessUtc ?? existing.LastSuccessUtc,
                LastError = lastError
            };

            _calendarFeeds = _calendarFeeds
                .Select(feed => feed.FeedId == existing.FeedId ? updated : feed)
                .ToList();
            PersistLocked();
            return updated;
        }
    }

    private MemberCalendarFeedRecord? FindFeedLocked(string loopId, string memberId)
    {
        return _calendarFeeds.FirstOrDefault(feed =>
            feed.LoopId.Equals(loopId, StringComparison.OrdinalIgnoreCase) &&
            feed.MemberId.Equals(memberId, StringComparison.OrdinalIgnoreCase));
    }

    private void PersistLocked()
    {
        _snapshotStore.Save(new UserIntegrationSnapshot
        {
            SchemaVersion = UserIntegrationSnapshot.CurrentSchemaVersion,
            HomeAssistantLinks = _links.ToArray(),
            MemberCalendarFeeds = _calendarFeeds.ToArray()
        });
    }

    private static bool MatchesJiboIdentity(
        HomeAssistantLinkRecord link,
        string? jiboDeviceId,
        string? jiboFriendlyId)
    {
        foreach (var candidate in new[] { jiboDeviceId, jiboFriendlyId }.Where(static value =>
                     !string.IsNullOrWhiteSpace(value)))
            if (link.JiboDeviceId.Equals(candidate, StringComparison.OrdinalIgnoreCase) ||
                link.JiboFriendlyName.Equals(candidate, StringComparison.OrdinalIgnoreCase))
                return true;

        return false;
    }
}
