namespace Jibo.Cloud.Api.Hosting;

internal static class SocketKindResolver
{
    private const string ApiSocketHost = "api-socket.jibo.com";
    private const string OpenJiboSocketHost = "open-jibo-socket.openjibo.com";
    private const string NativeCompatibilitySocketHost = "open-jibo-socket.jibo.pro";
    private const string NeoHubHost = "neo-hub.jibo.com";
    private const string OpenJiboNeoHubHost = "neohub.openjibo.com";

    private static readonly HashSet<string> OpenJiboHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "openjibo.com",
        "openjibo.ai",
        "api.openjibo.com",
        "localhost"
    };

    private static readonly HashSet<string> HubListenPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "/v1/listen",
        "/listen",
        "/v1/proactive",
        "/proactive"
    };

    internal static string Resolve(string host, PathString path)
    {
        if (string.Equals(host, ApiSocketHost, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(host, OpenJiboSocketHost, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(host, NativeCompatibilitySocketHost, StringComparison.OrdinalIgnoreCase))
            return "api-socket";

        if (string.Equals(host, NeoHubHost, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(host, OpenJiboNeoHubHost, StringComparison.OrdinalIgnoreCase))
            return path.StartsWithSegments("/v1/proactive") ? "neo-hub-proactive" : "neo-hub-listen";

        if (path.StartsWithSegments("/v1/homeassistant"))
            return "home-assistant";

        // On self-hosted/container endpoints the Host header is not a canonical Jibo
        // hostname, so the route itself must identify NeoHub traffic.
        if (IsHubPath(path, out var proactive))
            return proactive ? "neo-hub-proactive" : "neo-hub-listen";

        // Self-hosted / LAN: Host is often the machine IP (or localhost) while the robot
        // still opens the stock notification path /{token}. Classify that as api-socket so
        // LoopUpdated can be pushed. Do not reclassify real neo-hub hosts (handled above).
        if (IsNotificationTokenPath(path))
            return "api-socket";

        return OpenJiboHosts.Contains(host) ? "openjibo" : "neo-hub-listen";
    }

    private static bool IsHubPath(PathString path, out bool proactive)
    {
        proactive = path.StartsWithSegments("/v1/proactive") ||
                    path.StartsWithSegments("/proactive");

        return proactive ||
               path.StartsWithSegments("/v1/listen") ||
               path.StartsWithSegments("/listen");
    }

    /// <summary>
    /// Stock notification socket path is <c>/{token}</c> (e.g. <c>/token-Friendly-Id-...</c>),
    /// not the hub listen/proactive routes.
    /// </summary>
    internal static bool IsNotificationTokenPath(PathString path)
    {
        var value = path.Value;
        if (string.IsNullOrWhiteSpace(value) || value == "/")
            return false;

        if (path.StartsWithSegments("/v1/homeassistant"))
            return false;

        foreach (var hubPath in HubListenPaths)
        {
            if (path.StartsWithSegments(hubPath) ||
                string.Equals(value.TrimEnd('/'), hubPath, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        // Single path segment: /token-... or any opaque robot path token.
        var trimmed = value.Trim('/');
        return !string.IsNullOrWhiteSpace(trimmed) && !trimmed.Contains('/');
    }
}
