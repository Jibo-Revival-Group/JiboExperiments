using System.Net;
using System.Net.Sockets;

namespace Jibo.Cloud.Api.Hosting;

/// <summary>
/// Bounds the legacy tokenless Hub compatibility path to an explicitly enabled,
/// private-LAN HTTP deployment and one client address at a time.
/// </summary>
internal sealed class SingleRobotHttpHubAccessGuard(IConfiguration configuration)
{
    private const string EnabledConfigurationKey =
        "OpenJibo:SelfHosted:AllowTokenlessSingleRobotHub";

    private const string DeploymentModeConfigurationKey = "OpenJibo:Deployment:Mode";
    private const string IsolatedSelfHostedMode = "self-hosted-isolated";
    private const int MaxLeasesPerClient = 2;

    private readonly object syncRoot = new();
    private readonly Dictionary<string, int> activeClientLeases = new(StringComparer.OrdinalIgnoreCase);

    internal SingleRobotHttpHubAccessGuard(bool enabled)
        : this(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [EnabledConfigurationKey] = enabled.ToString(),
                [DeploymentModeConfigurationKey] = IsolatedSelfHostedMode
            })
            .Build())
    {
    }

    internal HubAccessLeaseResult TryAcquire(HttpContext context, string kind)
    {
        if (!string.Equals(configuration[DeploymentModeConfigurationKey], IsolatedSelfHostedMode,
                StringComparison.OrdinalIgnoreCase))
            return HubAccessLeaseResult.Denied("not-isolated-self-hosted");

        if (!configuration.GetValue<bool>(EnabledConfigurationKey))
            return HubAccessLeaseResult.Denied("compatibility-mode-disabled");


        if (kind is not ("neo-hub-listen" or "neo-hub-proactive") ||
            !IsExactHubPath(context.Request.Path))
            return HubAccessLeaseResult.Denied("not-an-exact-hub-route");

        if (context.Request.IsHttps)
            return HubAccessLeaseResult.Denied("https-requires-token");

        if (HasProxyForwardingHeaders(context.Request))
            return HubAccessLeaseResult.Denied("proxy-forwarding-not-supported");

        if (!IsPrivateHost(context.Request.Host.Host))
            return HubAccessLeaseResult.Denied("host-is-not-private");

        var remoteAddress = NormalizeAddress(context.Connection.RemoteIpAddress);
        if (remoteAddress is null || !IsPrivateAddress(remoteAddress))
            return HubAccessLeaseResult.Denied("client-is-not-private");

        var clientKey = remoteAddress.ToString();
        lock (syncRoot)
        {
            if (activeClientLeases.Count > 0 && !activeClientLeases.ContainsKey(clientKey))
                return HubAccessLeaseResult.Denied("single-robot-client-already-active");

            activeClientLeases.TryGetValue(clientKey, out var leaseCount);
            if (leaseCount >= MaxLeasesPerClient)
                return HubAccessLeaseResult.Denied("single-robot-connection-limit-reached");
            activeClientLeases[clientKey] = leaseCount + 1;
        }

        return HubAccessLeaseResult.Allowed(new ClientLease(this, clientKey));
    }

    private void Release(string clientKey)
    {
        lock (syncRoot)
        {
            if (!activeClientLeases.TryGetValue(clientKey, out var leaseCount))
                return;

            if (leaseCount <= 1)
                activeClientLeases.Remove(clientKey);
            else
                activeClientLeases[clientKey] = leaseCount - 1;
        }
    }

    private static bool HasProxyForwardingHeaders(HttpRequest request) =>
        request.Headers.ContainsKey("Forwarded") ||
        request.Headers.ContainsKey("X-Forwarded-For") ||
        request.Headers.ContainsKey("X-Forwarded-Host") ||
        request.Headers.ContainsKey("X-Forwarded-Proto") ||
        request.Headers.ContainsKey("X-Original-Proto");

    private static bool IsExactHubPath(PathString path)
    {
        var normalized = path.Value?.TrimEnd('/');
        return string.Equals(normalized, "/listen", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(normalized, "/v1/listen", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(normalized, "/proactive", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(normalized, "/v1/proactive", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPrivateHost(string host)
    {
        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".local", StringComparison.OrdinalIgnoreCase))
            return true;

        return IPAddress.TryParse(host.Trim('[', ']'), out var address) &&
               IsPrivateAddress(NormalizeAddress(address)!);
    }

    private static IPAddress? NormalizeAddress(IPAddress? address) =>
        address?.IsIPv4MappedToIPv6 == true ? address.MapToIPv4() : address;

    private static bool IsPrivateAddress(IPAddress address)
    {
        if (IPAddress.IsLoopback(address))
            return true;

        var bytes = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetwork)
            return bytes[0] == 10 ||
                   bytes[0] == 127 ||
                   bytes[0] == 169 && bytes[1] == 254 ||
                   bytes[0] == 172 && bytes[1] is >= 16 and <= 31 ||
                   bytes[0] == 192 && bytes[1] == 168;

        return address.AddressFamily == AddressFamily.InterNetworkV6 &&
               (address.IsIPv6LinkLocal || (bytes[0] & 0xfe) == 0xfc);
    }

    private sealed class ClientLease(SingleRobotHttpHubAccessGuard owner, string clientKey) : IDisposable
    {
        private int disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) == 0)
                owner.Release(clientKey);
        }
    }
}

internal sealed record HubAccessLeaseResult(bool IsAllowed, string Reason, IDisposable? Lease)
{
    internal static HubAccessLeaseResult Allowed(IDisposable lease) => new(true, "allowed", lease);

    internal static HubAccessLeaseResult Denied(string reason) => new(false, reason, null);
}
