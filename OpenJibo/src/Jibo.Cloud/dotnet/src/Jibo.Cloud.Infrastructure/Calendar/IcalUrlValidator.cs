using System.Net;
using System.Net.Sockets;

namespace Jibo.Cloud.Infrastructure.Calendar;

public static class IcalUrlValidator
{
    /// <param name="requireDnsResolution">
    /// When true (fetch/redirect path), resolve the host and reject private addresses.
    /// When false (portal save), only apply syntactic / literal-IP SSRF guards so configuration
    /// works offline and with hosts that are not yet resolvable.
    /// </param>
    public static bool TryValidateHttpsPublicUrl(
        string? rawUrl,
        out Uri uri,
        out string? error,
        bool requireDnsResolution = false)
    {
        uri = null!;
        error = null;

        if (string.IsNullOrWhiteSpace(rawUrl))
        {
            error = "iCal URL is required.";
            return false;
        }

        if (!Uri.TryCreate(rawUrl.Trim(), UriKind.Absolute, out var parsed) ||
            parsed.Scheme != Uri.UriSchemeHttps)
        {
            error = "iCal URL must be an absolute https URL.";
            return false;
        }

        if (!string.IsNullOrEmpty(parsed.UserInfo))
        {
            error = "iCal URL must not include credentials in the URL.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(parsed.Host))
        {
            error = "iCal URL host is required.";
            return false;
        }

        if (IsBlockedHost(parsed.Host))
        {
            error = "iCal URL host is not allowed.";
            return false;
        }

        if (IPAddress.TryParse(parsed.Host, out var literalAddress))
        {
            if (IsBlockedAddress(literalAddress))
            {
                error = "iCal URL host is not allowed.";
                return false;
            }

            uri = parsed;
            return true;
        }

        if (requireDnsResolution && !TryResolveAndValidateAddresses(parsed.Host, out error))
            return false;

        uri = parsed;
        return true;
    }

    public static string? TryGetSafeHost(string? rawUrl)
    {
        if (!Uri.TryCreate(rawUrl?.Trim() ?? string.Empty, UriKind.Absolute, out var parsed))
            return null;
        return string.IsNullOrWhiteSpace(parsed.Host) ? null : parsed.Host;
    }

    public static bool IsRedirectTargetAllowed(Uri redirectUri, out string? error)
    {
        return TryValidateHttpsPublicUrl(redirectUri.AbsoluteUri, out _, out error, requireDnsResolution: true);
    }

    private static bool TryResolveAndValidateAddresses(string host, out string? error)
    {
        error = null;
        if (IPAddress.TryParse(host, out var literalAddress))
        {
            if (IsBlockedAddress(literalAddress))
            {
                error = "iCal URL host is not allowed.";
                return false;
            }

            return true;
        }

        try
        {
            var addresses = Dns.GetHostAddresses(host);
            if (addresses.Length == 0)
            {
                error = "iCal URL host could not be resolved.";
                return false;
            }

            if (addresses.Any(IsBlockedAddress))
            {
                error = "iCal URL host is not allowed.";
                return false;
            }

            return true;
        }
        catch (SocketException)
        {
            error = "iCal URL host could not be resolved.";
            return false;
        }
    }

    private static bool IsBlockedHost(string host)
    {
        return host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
               host.Equals("metadata.google.internal", StringComparison.OrdinalIgnoreCase) ||
               host.EndsWith(".local", StringComparison.OrdinalIgnoreCase) ||
               host.EndsWith(".internal", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsBlockedAddress(IPAddress address)
    {
        if (IPAddress.IsLoopback(address)) return true;
        if (address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6UniqueLocal)
            return true;

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();
            // 0.0.0.0/8, 10/8, 127/8, 169.254/16, 172.16/12, 192.168/16, 100.64/10 (CGNAT)
            if (bytes[0] == 0) return true;
            if (bytes[0] == 10) return true;
            if (bytes[0] == 127) return true;
            if (bytes[0] == 169 && bytes[1] == 254) return true;
            if (bytes[0] == 172 && bytes[1] is >= 16 and <= 31) return true;
            if (bytes[0] == 192 && bytes[1] == 168) return true;
            if (bytes[0] == 100 && bytes[1] is >= 64 and <= 127) return true;
        }

        return false;
    }
}
