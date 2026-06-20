namespace Jibo.Cloud.Api.Hosting;

internal static class SocketKindResolver
{
    private const string ApiSocketHost = "api-socket.jibo.com";
    private const string NeoHubHost = "neo-hub.jibo.com";

    private static readonly HashSet<string> OpenJiboHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "openjibo.com",
        "openjibo.ai",
        "localhost"
    };

    internal static string Resolve(string host, PathString path)
    {
        if (string.Equals(host, ApiSocketHost, StringComparison.OrdinalIgnoreCase)) return "api-socket";

        if (string.Equals(host, NeoHubHost, StringComparison.OrdinalIgnoreCase))
            return path.StartsWithSegments("/v1/proactive") ? "neo-hub-proactive" : "neo-hub-listen";

        if (path.StartsWithSegments("/v1/homeassistant"))
            return "home-assistant";

        return OpenJiboHosts.Contains(host) ? "openjibo" : "neo-hub-listen";
    }
}