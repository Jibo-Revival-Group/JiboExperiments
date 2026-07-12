using Jibo.Cloud.Domain.Models;

namespace Jibo.Cloud.Application.Services;

internal static class WebSocketSessionKeyResolver
{
    private static readonly HashSet<string> AmbiguousPathTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "v1/listen",
        "listen",
        "v1/proactive",
        "proactive"
    };

    internal static string ResolveSessionKey(WebSocketMessageEnvelope envelope)
    {
        var token = envelope.Token?.Trim();
        if (string.IsNullOrWhiteSpace(token))
            return $"conn:{envelope.ConnectionId}";

        if (IsAmbiguousPathToken(token))
            return $"conn:{envelope.ConnectionId}";

        return token;
    }

    internal static bool IsAmbiguousPathToken(string? token)
    {
        return !string.IsNullOrWhiteSpace(token) && AmbiguousPathTokens.Contains(token.Trim());
    }
}
