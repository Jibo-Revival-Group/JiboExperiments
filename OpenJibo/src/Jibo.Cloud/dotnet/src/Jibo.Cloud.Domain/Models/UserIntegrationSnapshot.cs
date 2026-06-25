namespace Jibo.Cloud.Domain.Models;

public sealed class UserIntegrationSnapshot
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public HomeAssistantLinkRecord[] HomeAssistantLinks { get; init; } = [];
}