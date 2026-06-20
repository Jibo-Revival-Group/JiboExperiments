using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace Jibo.Cloud.Application.Services;

public sealed class JiboVerificationService
{
    private static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan TokenLifetime = TimeSpan.FromMinutes(5);

    private readonly ConcurrentDictionary<string, PendingJiboCode> _codesByCode =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly ConcurrentDictionary<string, string> _latestCodeByLookupId =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly ConcurrentDictionary<string, IssuedJiboVerificationToken> _tokens =
        new(StringComparer.OrdinalIgnoreCase);

    public string IssueCodeForDevice(string? friendlyId, string? deviceId)
    {
        if (string.IsNullOrWhiteSpace(friendlyId) && string.IsNullOrWhiteSpace(deviceId))
            throw new InvalidOperationException("Cannot issue Jibo verification code without device identity.");

        PurgeExpired();

        var resolvedFriendlyId = !string.IsNullOrWhiteSpace(friendlyId)
            ? friendlyId.Trim()
            : deviceId!.Trim();
        var resolvedDeviceId = !string.IsNullOrWhiteSpace(deviceId)
            ? deviceId.Trim()
            : resolvedFriendlyId;

        RemoveExistingCodeForLookup(resolvedFriendlyId);
        if (!resolvedFriendlyId.Equals(resolvedDeviceId, StringComparison.OrdinalIgnoreCase))
            RemoveExistingCodeForLookup(resolvedDeviceId);

        var code = GenerateCode();
        var pending = new PendingJiboCode(
            code,
            resolvedDeviceId,
            resolvedFriendlyId,
            DateTimeOffset.UtcNow.Add(CodeLifetime));

        _codesByCode[code] = pending;
        _latestCodeByLookupId[resolvedFriendlyId] = code;
        if (!resolvedFriendlyId.Equals(resolvedDeviceId, StringComparison.OrdinalIgnoreCase))
            _latestCodeByLookupId[resolvedDeviceId] = code;

        return code;
    }

    public JiboVerificationConfirmResult TryConfirmByCode(string code)
    {
        PurgeExpired();

        var normalized = NormalizeCode(code);
        if (!_codesByCode.TryGetValue(normalized, out var pending) || pending.ExpiresAtUtc <= DateTimeOffset.UtcNow)
            return JiboVerificationConfirmResult.InvalidCode;

        _codesByCode.TryRemove(normalized, out _);
        _latestCodeByLookupId.TryRemove(pending.FriendlyId, out _);
        if (!pending.FriendlyId.Equals(pending.DeviceId, StringComparison.OrdinalIgnoreCase))
            _latestCodeByLookupId.TryRemove(pending.DeviceId, out _);

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

    private void RemoveExistingCodeForLookup(string lookupId)
    {
        if (!_latestCodeByLookupId.TryGetValue(lookupId, out var existingCode)) return;
        _codesByCode.TryRemove(existingCode, out _);
        _latestCodeByLookupId.TryRemove(lookupId, out _);
    }

    private void PurgeExpired()
    {
        var now = DateTimeOffset.UtcNow;

        foreach (var pair in _codesByCode)
        {
            if (pair.Value.ExpiresAtUtc > now) continue;
            _codesByCode.TryRemove(pair.Key, out var expired);
            if (expired is null) continue;

            _latestCodeByLookupId.TryRemove(expired.FriendlyId, out _);
            if (!expired.FriendlyId.Equals(expired.DeviceId, StringComparison.OrdinalIgnoreCase))
                _latestCodeByLookupId.TryRemove(expired.DeviceId, out _);
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
        Span<byte> bytes = stackalloc byte[4];
        RandomNumberGenerator.Fill(bytes);

        var builder = new StringBuilder(4);
        foreach (var value in bytes)
            builder.Append(alphabet[value % alphabet.Length]);

        return builder.ToString();
    }

    private static string NormalizeCode(string code)
    {
        return new string(code.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
    }

    private sealed record PendingJiboCode(
        string Code,
        string DeviceId,
        string FriendlyId,
        DateTimeOffset ExpiresAtUtc);

    public sealed record IssuedJiboVerificationToken(
        string Token,
        string DeviceId,
        string FriendlyId,
        DateTimeOffset ExpiresAtUtc);

    public sealed record JiboVerificationConfirmResult(
        bool Ok,
        string? Token,
        string? FriendlyId,
        string? Error)
    {
        public static JiboVerificationConfirmResult InvalidCode =>
            new(false, null, null, "That verification code is invalid or has expired.");

        public static JiboVerificationConfirmResult Success(string token, string friendlyId) =>
            new(true, token, friendlyId, null);
    }
}
