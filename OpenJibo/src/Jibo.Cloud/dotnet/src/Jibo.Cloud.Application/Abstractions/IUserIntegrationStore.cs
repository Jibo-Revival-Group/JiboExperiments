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
    void UpdateHomeAssistantLastSeen(string linkId, DateTimeOffset lastSeenUtc);
}
