namespace Jibo.Cloud.Domain.Models;

public sealed class UserIntegrationSnapshot
{
    public const int CurrentSchemaVersion = 2;
    public const int MinimumSupportedSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public HomeAssistantLinkRecord[] HomeAssistantLinks { get; init; } = [];
    public MemberCalendarFeedRecord[] MemberCalendarFeeds { get; init; } = [];
}
