using Jibo.Cloud.Domain.Models;

namespace Jibo.Cloud.Application.Abstractions;

public interface IUserIntegrationStore
{
    IReadOnlyList<HomeAssistantLinkRecord> GetHomeAssistantLinks();
    HomeAssistantLinkRecord? FindLinkByHaInstanceId(string haInstanceId);
    HomeAssistantLinkRecord? FindLinkByLinkId(string linkId);
    HomeAssistantLinkRecord? FindLinkForJibo(string? jiboDeviceId, string? jiboFriendlyId);

    HomeAssistantLinkRecord AddHomeAssistantLink(
        string jiboDeviceId,
        string jiboFriendlyName,
        string haInstanceId);

    HomeAssistantLinkRecord? RemoveHomeAssistantLink(string linkId);
    void UpdateHomeAssistantLastSeen(string linkId, DateTimeOffset lastSeenUtc);

    IReadOnlyList<MemberCalendarFeedRecord> GetMemberCalendarFeeds(string? loopId = null);
    MemberCalendarFeedRecord? FindMemberCalendarFeed(string loopId, string memberId);

    MemberCalendarFeedRecord UpsertMemberCalendarFeed(
        string loopId,
        string memberId,
        string icalUrl,
        bool isEnabled = true);

    MemberCalendarFeedRecord? ClearMemberCalendarFeed(string loopId, string memberId);

    MemberCalendarFeedRecord? UpdateMemberCalendarFeedSyncStatus(
        string loopId,
        string memberId,
        DateTimeOffset? lastSuccessUtc,
        string? lastError);
}
