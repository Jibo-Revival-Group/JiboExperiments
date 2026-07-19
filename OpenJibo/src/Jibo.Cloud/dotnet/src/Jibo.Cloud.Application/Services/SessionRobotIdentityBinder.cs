using System.Text.Json;
using Jibo.Cloud.Domain.Models;

namespace Jibo.Cloud.Application.Services;

/// <summary>
/// Binds a per-robot identity from CONTEXT onto the WebSocket session so path-token listen
/// sockets are not stuck on the process-wide singleton robot DeviceId.
/// Real Pegasus/BE firmware never sets <c>data.general.robotID</c> (that block only carries
/// <c>release</c>); the actual per-robot signal is <c>data.runtime.loop.jibo.id</c> /
/// <c>data.runtime.loop.loopId</c>. Synthetic tests may still supply <c>general.robotID</c>.
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
            var data = root.TryGetProperty("data", out var nestedData) && nestedData.ValueKind == JsonValueKind.Object
                ? nestedData
                : root;

            if (data.TryGetProperty("general", out var general) && general.ValueKind == JsonValueKind.Object &&
                (TryReadString(general, "robotID", out robotId) || TryReadString(general, "robotId", out robotId)))
            {
                _ = TryReadString(general, "accountID", out accountId) ||
                    TryReadString(general, "accountId", out accountId);
                return true;
            }

            // Real Pegasus/BE firmware never sets general.robotID — its general block only ever
            // carries {"release": "..."}. The actual per-robot signal on every real CONTEXT message
            // is data.runtime.loop.jibo.id (this specific Jibo unit) / data.runtime.loop.loopId
            // (household), confirmed from captured hardware traffic in artifact-output/jibo-test-*.
            if (data.TryGetProperty("runtime", out var runtime) && runtime.ValueKind == JsonValueKind.Object &&
                runtime.TryGetProperty("loop", out var loop) && loop.ValueKind == JsonValueKind.Object)
            {
                if (loop.TryGetProperty("jibo", out var jibo) && jibo.ValueKind == JsonValueKind.Object &&
                    TryReadString(jibo, "id", out robotId))
                    return true;

                if (TryReadString(loop, "loopId", out robotId))
                    return true;
            }

            return false;
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
