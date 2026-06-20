using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using Jibo.Cloud.Application.Abstractions;

namespace Jibo.Cloud.Application.Services;

public sealed class JiboVerificationService
{
    private static readonly TimeSpan VerificationLifetime = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan TokenLifetime = TimeSpan.FromMinutes(5);

    private readonly ConcurrentDictionary<string, PendingJiboVerification> _sessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string> _deviceToSession = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, IssuedJiboVerificationToken> _tokens =
        new(StringComparer.OrdinalIgnoreCase);

    public JiboVerificationStartResult StartVerification(ICloudStateStore stateStore, string friendlyName)
    {
        var device = stateStore.FindDeviceByFriendlyName(friendlyName);
        if (device is null)
            return JiboVerificationStartResult.NotFound;

        PurgeExpired();

        if (_deviceToSession.TryGetValue(device.DeviceId, out var existingSessionId) &&
            _sessions.TryGetValue(existingSessionId, out var existing) &&
            existing.ExpiresAtUtc > DateTimeOffset.UtcNow)
        {
            return JiboVerificationStartResult.Success(existing.SessionId, existing.ExpiresAtUtc);
        }

        var sessionId = Guid.NewGuid().ToString("N");
        var code = GenerateCode();
        var pending = new PendingJiboVerification(
            sessionId,
            device.DeviceId,
            device.FriendlyName,
            code,
            DateTimeOffset.UtcNow.Add(VerificationLifetime));

        _sessions[sessionId] = pending;
        _deviceToSession[device.DeviceId] = sessionId;

        return JiboVerificationStartResult.Success(sessionId, pending.ExpiresAtUtc);
    }

    public string? GetPendingCodeForDevice(string? deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId)) return null;

        PurgeExpired();

        if (!_deviceToSession.TryGetValue(deviceId, out var sessionId)) return null;
        return _sessions.TryGetValue(sessionId, out var pending) &&
               pending.ExpiresAtUtc > DateTimeOffset.UtcNow
            ? pending.Code
            : null;
    }

    public JiboVerificationConfirmResult TryConfirm(string sessionId, string code)
    {
        PurgeExpired();

        if (!_sessions.TryGetValue(sessionId, out var pending) || pending.ExpiresAtUtc <= DateTimeOffset.UtcNow)
            return JiboVerificationConfirmResult.NotFound;

        if (!string.Equals(NormalizeCode(code), pending.Code, StringComparison.Ordinal))
            return JiboVerificationConfirmResult.InvalidCode;

        _sessions.TryRemove(sessionId, out _);
        _deviceToSession.TryRemove(pending.DeviceId, out _);

        var token = Guid.NewGuid().ToString("N");
        _tokens[token] = new IssuedJiboVerificationToken(
            token,
            pending.DeviceId,
            pending.FriendlyName,
            DateTimeOffset.UtcNow.Add(TokenLifetime));

        return JiboVerificationConfirmResult.Success(token, pending.FriendlyName);
    }

    public IssuedJiboVerificationToken? TryConsumeToken(string token)
    {
        PurgeExpired();

        if (!_tokens.TryGetValue(token, out var issued) || issued.ExpiresAtUtc <= DateTimeOffset.UtcNow)
            return null;

        _tokens.TryRemove(token, out _);
        return issued;
    }

    private void PurgeExpired()
    {
        var now = DateTimeOffset.UtcNow;

        foreach (var pair in _sessions)
        {
            if (pair.Value.ExpiresAtUtc > now) continue;
            _sessions.TryRemove(pair.Key, out _);
            _deviceToSession.TryRemove(pair.Value.DeviceId, out _);
        }

        foreach (var pair in _tokens)
        {
            if (pair.Value.ExpiresAtUtc > now) continue;
            _tokens.TryRemove(pair.Key, out _);
        }
    }

    private static string GenerateCode()
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        Span<byte> bytes = stackalloc byte[6];
        RandomNumberGenerator.Fill(bytes);

        var builder = new StringBuilder(6);
        foreach (var value in bytes)
            builder.Append(alphabet[value % alphabet.Length]);

        return builder.ToString();
    }

    private static string NormalizeCode(string code)
    {
        return new string(code.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
    }

    private sealed record PendingJiboVerification(
        string SessionId,
        string DeviceId,
        string FriendlyName,
        string Code,
        DateTimeOffset ExpiresAtUtc);

    public sealed record IssuedJiboVerificationToken(
        string Token,
        string DeviceId,
        string FriendlyName,
        DateTimeOffset ExpiresAtUtc);

    public sealed record JiboVerificationStartResult(
        bool Ok,
        string? SessionId,
        DateTimeOffset? ExpiresAtUtc,
        string? Error)
    {
        public static JiboVerificationStartResult NotFound =>
            new(false, null, null, "No Jibo was found with that friendly name.");

        public static JiboVerificationStartResult Success(string sessionId, DateTimeOffset expiresAtUtc) =>
            new(true, sessionId, expiresAtUtc, null);
    }

    public sealed record JiboVerificationConfirmResult(
        bool Ok,
        string? Token,
        string? FriendlyName,
        string? Error)
    {
        public static JiboVerificationConfirmResult NotFound =>
            new(false, null, null, "Verification session was not found or has expired.");

        public static JiboVerificationConfirmResult InvalidCode =>
            new(false, null, null, "That verification code is incorrect.");

        public static JiboVerificationConfirmResult Success(string token, string friendlyName) =>
            new(true, token, friendlyName, null);
    }
}
