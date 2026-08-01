using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Jibo.Cloud.Application.Services;

public static class HomeAssistantCommandAuthentication
{
    public static readonly TimeSpan MaxTimestampSkew = TimeSpan.FromSeconds(60);

    public static string GenerateCommandSecret()
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public static string GenerateNonce()
    {
        Span<byte> bytes = stackalloc byte[16];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public static string BuildCanonical(IReadOnlyDictionary<string, string> fields)
    {
        var builder = new StringBuilder();
        var first = true;
        foreach (var pair in fields
                     .Where(static pair =>
                         !string.Equals(pair.Key, "signature", StringComparison.Ordinal))
                     .OrderBy(static pair => pair.Key, StringComparer.Ordinal))
        {
            if (!first)
                builder.Append('\n');
            first = false;
            builder.Append(pair.Key);
            builder.Append('=');
            builder.Append(pair.Value);
        }

        return builder.ToString();
    }

    public static string Sign(IReadOnlyDictionary<string, string> fields, string commandSecret)
    {
        var canonical = BuildCanonical(fields);
        var key = Encoding.UTF8.GetBytes(commandSecret);
        var payload = Encoding.UTF8.GetBytes(canonical);
        using var hmac = new HMACSHA256(key);
        return Convert.ToHexString(hmac.ComputeHash(payload)).ToLowerInvariant();
    }

    public static bool Verify(
        IReadOnlyDictionary<string, string> fields,
        string commandSecret,
        string signature,
        DateTimeOffset? nowUtc = null)
    {
        if (string.IsNullOrWhiteSpace(commandSecret) ||
            string.IsNullOrWhiteSpace(signature) ||
            !fields.TryGetValue("timestamp", out var timestampText) ||
            string.IsNullOrWhiteSpace(timestampText) ||
            !long.TryParse(timestampText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var unixSeconds))
            return false;

        var now = nowUtc ?? DateTimeOffset.UtcNow;
        var timestamp = DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
        if ((now - timestamp).Duration() > MaxTimestampSkew)
            return false;

        var expected = Sign(fields, commandSecret);
        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        var signatureBytes = Encoding.UTF8.GetBytes(signature.Trim().ToLowerInvariant());
        return expectedBytes.Length == signatureBytes.Length &&
               CryptographicOperations.FixedTimeEquals(expectedBytes, signatureBytes);
    }
}
