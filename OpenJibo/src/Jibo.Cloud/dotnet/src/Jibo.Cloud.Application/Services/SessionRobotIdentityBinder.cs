using System.Text.Json;
using Jibo.Cloud.Domain.Models;

namespace Jibo.Cloud.Application.Services;

/// <summary>
/// Binds Pegasus CONTEXT <c>data.general.robotID</c> (friendlyId) onto the WebSocket session
/// so path-token listen sockets are not stuck on the process-wide singleton robot DeviceId.
/// </summary>
public static class SessionRobotIdentityBinder
{
    public static bool TryBindFromContextPayload(CloudSession session, string? contextPayloadOrEnvelopeText)
    {
        if (!TryReadGeneralRobotIdentity(contextPayloadOrEnvelopeText, out var robotId, out var accountId))
            return false;

        session.DeviceId = robotId;
        session.Metadata["robotID"] = robotId;
        session.Metadata["robotId"] = robotId;
        session.Metadata["friendlyId"] = robotId;
        session.Metadata["deviceId"] = robotId;
        if (!string.IsNullOrWhiteSpace(accountId))
        {
            session.Metadata["accountId"] = accountId;
            session.Metadata["accountID"] = accountId;
        }

        return true;
    }

    public static string? ResolveRobotFriendlyId(CloudSession session, string? contextPayload = null)
    {
        if (TryReadGeneralRobotIdentity(contextPayload ?? ReadMetadataString(session, "context"), out var robotId, out _))
            return robotId;

        foreach (var key in new[] { "robotID", "robotId", "friendlyId", "robotFriendlyId" })
        {
            var value = ReadMetadataString(session, key);
            if (!string.IsNullOrWhiteSpace(value))
                return value.Trim();
        }

        return string.IsNullOrWhiteSpace(session.DeviceId) ? null : session.DeviceId.Trim();
    }

    public static bool TryReadGeneralRobotIdentity(string? json, out string robotId, out string? accountId)
    {
        robotId = string.Empty;
        accountId = null;
        if (string.IsNullOrWhiteSpace(json)) return false;

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var general = root;
            if (root.TryGetProperty("data", out var data) &&
                data.ValueKind == JsonValueKind.Object &&
                data.TryGetProperty("general", out var nestedGeneral) &&
                nestedGeneral.ValueKind == JsonValueKind.Object)
                general = nestedGeneral;
            else if (root.TryGetProperty("general", out var topGeneral) &&
                     topGeneral.ValueKind == JsonValueKind.Object)
                general = topGeneral;
            else
                return false;

            if (!TryReadString(general, "robotID", out robotId) &&
                !TryReadString(general, "robotId", out robotId))
                return false;

            _ = TryReadString(general, "accountID", out accountId) ||
                TryReadString(general, "accountId", out accountId);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string? ReadMetadataString(CloudSession session, string key)
    {
        return session.Metadata.TryGetValue(key, out var value) ? value?.ToString() : null;
    }

    private static bool TryReadString(JsonElement element, string propertyName, out string value)
    {
        value = string.Empty;
        if (!element.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.String)
            return false;

        var text = property.GetString();
        if (string.IsNullOrWhiteSpace(text)) return false;
        value = text.Trim();
        return true;
    }
}
