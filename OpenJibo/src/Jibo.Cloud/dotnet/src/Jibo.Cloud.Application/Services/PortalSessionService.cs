using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace Jibo.Cloud.Application.Services;

public sealed class PortalSessionService
{
    private static readonly TimeSpan SessionLifetime = TimeSpan.FromHours(24);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly byte[] _signingKey;
    private readonly ConcurrentDictionary<string, DateTimeOffset> _revokedTokens =
        new(StringComparer.OrdinalIgnoreCase);

    public PortalSessionService(IConfiguration configuration)
    {
        var configuredSecret = configuration["OpenJibo:Portal:SessionSigningKey"]
            ?? configuration["OpenJibo:Portal:StatusPassword"]
            ?? Environment.GetEnvironmentVariable("OPENJIBO_PORTAL_SESSION_SIGNING_KEY")
            ?? Environment.GetEnvironmentVariable("OPENJIBO_PORTAL_STATUS_PASSWORD")
            ?? "openjibo-portal-session-development-fallback";

        _signingKey = SHA256.HashData(Encoding.UTF8.GetBytes(configuredSecret));
    }

    public PortalSession CreateSession(string deviceId, string friendlyId, string? userId = null)
    {
        PurgeRevocations();

        var now = DateTimeOffset.UtcNow;
        var expiresAt = now.Add(SessionLifetime);
        var payload = new SessionTokenPayload(
            deviceId.Trim(),
            friendlyId.Trim(),
            now.ToUnixTimeSeconds(),
            expiresAt.ToUnixTimeSeconds(),
            Guid.NewGuid().ToString("N"),
            string.IsNullOrWhiteSpace(userId) ? null : userId.Trim());

        var token = BuildToken(payload);
        return new PortalSession(token, payload.DeviceId, payload.FriendlyId, expiresAt, payload.UserId);
    }

    public PortalSession? TryGetSession(string? token)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;

        PurgeRevocations();

        var normalizedToken = token.Trim();
        if (_revokedTokens.ContainsKey(normalizedToken))
            return null;

        if (!TryParseToken(normalizedToken, out var payload))
            return null;

        var now = DateTimeOffset.UtcNow;
        if (payload.ExpiresAtUtc <= now.ToUnixTimeSeconds() || payload.IssuedAtUtc > now.ToUnixTimeSeconds())
            return null;

        return new PortalSession(
            normalizedToken,
            payload.DeviceId,
            payload.FriendlyId,
            DateTimeOffset.FromUnixTimeSeconds(payload.ExpiresAtUtc),
            payload.UserId);
    }

    public void RevokeSession(string? token)
    {
        if (string.IsNullOrWhiteSpace(token)) return;

        var normalizedToken = token.Trim();
        if (!TryParseToken(normalizedToken, out var payload))
        {
            _revokedTokens[normalizedToken] = DateTimeOffset.UtcNow.Add(SessionLifetime);
            return;
        }

        _revokedTokens[normalizedToken] = DateTimeOffset.FromUnixTimeSeconds(payload.ExpiresAtUtc);
        PurgeRevocations();
    }

    private static string BuildTokenString(string payloadJson, string signature)
    {
        return $"{Base64UrlEncode(Encoding.UTF8.GetBytes(payloadJson))}.{signature}";
    }

    private string BuildToken(SessionTokenPayload payload)
    {
        var payloadJson = JsonSerializer.Serialize(payload, JsonOptions);
        var signature = Base64UrlEncode(Hmac(Encoding.UTF8.GetBytes(payloadJson)));
        return BuildTokenString(payloadJson, signature);
    }

    private bool TryParseToken(string token, out SessionTokenPayload payload)
    {
        payload = default!;

        var parts = token.Split('.', 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2)
            return false;

        byte[] payloadBytes;
        byte[] signatureBytes;
        try
        {
            payloadBytes = Base64UrlDecode(parts[0]);
            signatureBytes = Base64UrlDecode(parts[1]);
        }
        catch
        {
            return false;
        }

        var expectedSignature = Hmac(payloadBytes);
        if (!CryptographicOperations.FixedTimeEquals(signatureBytes, expectedSignature))
            return false;

        try
        {
            payload = JsonSerializer.Deserialize<SessionTokenPayload>(payloadBytes, JsonOptions)
                      ?? throw new JsonException();
            return !string.IsNullOrWhiteSpace(payload.DeviceId) &&
                   !string.IsNullOrWhiteSpace(payload.FriendlyId);
        }
        catch
        {
            payload = default!;
            return false;
        }
    }

    private byte[] Hmac(byte[] payloadBytes)
    {
        using var hmac = new HMACSHA256(_signingKey);
        return hmac.ComputeHash(payloadBytes);
    }

    private void PurgeRevocations()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var pair in _revokedTokens)
        {
            if (pair.Value > now) continue;
            _revokedTokens.TryRemove(pair.Key, out _);
        }
    }

    private static string Base64UrlEncode(byte[] data)
    {
        return Convert.ToBase64String(data)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static byte[] Base64UrlDecode(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded = padded.PadRight(padded.Length + ((4 - padded.Length % 4) % 4), '=');
        return Convert.FromBase64String(padded);
    }

    private sealed record SessionTokenPayload(
        string DeviceId,
        string FriendlyId,
        long IssuedAtUtc,
        long ExpiresAtUtc,
        string Nonce,
        string? UserId);

    public sealed record PortalSession(
        string Token,
        string DeviceId,
        string FriendlyId,
        DateTimeOffset ExpiresAtUtc,
        string? UserId = null);
}
