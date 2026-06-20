using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Jibo.Cloud.Application.Abstractions;

namespace Jibo.Cloud.Application.Services;

public sealed class JiboVerificationService
{
    private static readonly TimeSpan VerificationLifetime = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan TokenLifetime = TimeSpan.FromMinutes(5);

    private readonly ConcurrentDictionary<string, PendingJiboVerification> _sessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string> _lookupKeyToSession = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, IssuedJiboVerificationToken> _tokens =
        new(StringComparer.OrdinalIgnoreCase);

    public JiboVerificationStartResult StartVerification(ICloudStateStore stateStore, string friendlyId)
    {
        var device = stateStore.FindDeviceByFriendlyId(friendlyId);
        if (device is null)
            return JiboVerificationStartResult.NotFound;

        PurgeExpired();

        if (_lookupKeyToSession.TryGetValue(device.RobotId, out var existingSessionId) &&
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
            device.RobotId,
            code,
            DateTimeOffset.UtcNow.Add(VerificationLifetime));

        _sessions[sessionId] = pending;
        RegisterLookupKeys(pending);

        return JiboVerificationStartResult.Success(sessionId, pending.ExpiresAtUtc);
    }

    public string? GetPendingCodeForDevice(string? deviceOrFriendlyId)
    {
        if (string.IsNullOrWhiteSpace(deviceOrFriendlyId)) return null;

        PurgeExpired();

        if (_lookupKeyToSession.TryGetValue(deviceOrFriendlyId, out var sessionId) &&
            _sessions.TryGetValue(sessionId, out var pending) &&
            pending.ExpiresAtUtc > DateTimeOffset.UtcNow)
            return pending.Code;

        return null;
    }

    public JiboVerificationConfirmResult TryConfirm(string sessionId, string code)
    {
        PurgeExpired();

        if (!_sessions.TryGetValue(sessionId, out var pending) || pending.ExpiresAtUtc <= DateTimeOffset.UtcNow)
            return JiboVerificationConfirmResult.NotFound;

        if (!string.Equals(NormalizeCode(code), pending.Code, StringComparison.Ordinal))
            return JiboVerificationConfirmResult.InvalidCode;

        _sessions.TryRemove(sessionId, out _);
        UnregisterLookupKeys(pending);

        var token = Guid.NewGuid().ToString("N");
        _tokens[token] = new IssuedJiboVerificationToken(
            token,
            pending.DeviceId,
            pending.FriendlyId,
            DateTimeOffset.UtcNow.Add(TokenLifetime));

        return JiboVerificationConfirmResult.Success(token, pending.FriendlyId);
    }

    public IssuedJiboVerificationToken? TryConsumeToken(string token)
    {
        PurgeExpired();

        if (!_tokens.TryGetValue(token, out var issued) || issued.ExpiresAtUtc <= DateTimeOffset.UtcNow)
            return null;

        _tokens.TryRemove(token, out _);
        return issued;
    }

    private void RegisterLookupKeys(PendingJiboVerification pending)
    {
        _lookupKeyToSession[pending.FriendlyId] = pending.SessionId;
        if (!pending.FriendlyId.Equals(pending.DeviceId, StringComparison.OrdinalIgnoreCase))
            _lookupKeyToSession[pending.DeviceId] = pending.SessionId;
    }

    private void UnregisterLookupKeys(PendingJiboVerification pending)
    {
        _lookupKeyToSession.TryRemove(pending.FriendlyId, out _);
        if (!pending.FriendlyId.Equals(pending.DeviceId, StringComparison.OrdinalIgnoreCase))
            _lookupKeyToSession.TryRemove(pending.DeviceId, out _);
    }

    private void PurgeExpired()
    {
        var now = DateTimeOffset.UtcNow;

        foreach (var pair in _sessions)
        {
            if (pair.Value.ExpiresAtUtc > now) continue;
            _sessions.TryRemove(pair.Key, out var expired);
            if (expired is not null) UnregisterLookupKeys(expired);
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
        string FriendlyId,
        string Code,
        DateTimeOffset ExpiresAtUtc);

    public sealed record IssuedJiboVerificationToken(
        string Token,
        string DeviceId,
        string FriendlyId,
        DateTimeOffset ExpiresAtUtc);

    public sealed record JiboVerificationStartResult(
        bool Ok,
        string? SessionId,
        DateTimeOffset? ExpiresAtUtc,
        string? Error)
    {
        public static JiboVerificationStartResult NotFound =>
            new(false, null, null, "No Jibo was found with that friendly ID.");

        public static JiboVerificationStartResult Success(string sessionId, DateTimeOffset expiresAtUtc) =>
            new(true, sessionId, expiresAtUtc, null);
    }

    public sealed record JiboVerificationConfirmResult(
        bool Ok,
        string? Token,
        string? FriendlyId,
        string? Error)
    {
        public static JiboVerificationConfirmResult NotFound =>
            new(false, null, null, "Verification session was not found or has expired.");

        public static JiboVerificationConfirmResult InvalidCode =>
            new(false, null, null, "That verification code is incorrect.");

        public static JiboVerificationConfirmResult Success(string token, string friendlyId) =>
            new(true, token, friendlyId, null);
    }
}
