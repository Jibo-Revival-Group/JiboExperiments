using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text.Json;

namespace Jibo.Cloud.Application.Services;

public sealed class HomeAssistantConnectionRegistry
{
    private static readonly TimeSpan VerificationLifetime = TimeSpan.FromMinutes(10);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ConcurrentDictionary<string, HomeAssistantConnection> _connections =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly ConcurrentDictionary<string, PendingHomeAssistantVerification> _pendingByCode =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly ConcurrentDictionary<string, string> _codeByInstanceId =
        new(StringComparer.OrdinalIgnoreCase);

    public PendingHomeAssistantVerification RegisterConnection(string instanceId, WebSocket socket)
    {
        PurgeExpired();

        var code = GenerateCode();
        var pending = new PendingHomeAssistantVerification(
            instanceId,
            code,
            DateTimeOffset.UtcNow.Add(VerificationLifetime));

        _connections[instanceId] = new HomeAssistantConnection(instanceId, socket);
        _pendingByCode[code] = pending;
        _codeByInstanceId[instanceId] = code;

        return pending;
    }

    public void RemoveConnection(string instanceId)
    {
        _connections.TryRemove(instanceId, out _);

        if (_codeByInstanceId.TryRemove(instanceId, out var code))
            _pendingByCode.TryRemove(code, out _);
    }

    public PendingHomeAssistantVerification? TryGetPendingByCode(string code)
    {
        PurgeExpired();

        var normalized = NormalizeCode(code);
        return _pendingByCode.TryGetValue(normalized, out var pending) &&
               pending.ExpiresAtUtc > DateTimeOffset.UtcNow
            ? pending
            : null;
    }

    public async Task<bool> SendPairedAsync(string instanceId, string jiboFriendlyName, string linkId,
        CancellationToken cancellationToken = default)
    {
        if (!_connections.TryGetValue(instanceId, out var connection)) return false;

        if (_codeByInstanceId.TryRemove(instanceId, out var code))
            _pendingByCode.TryRemove(code, out _);

        await SendJsonAsync(connection.Socket, new
        {
            type = "paired",
            jiboFriendlyName,
            linkId
        }, cancellationToken);

        return true;
    }

    public async Task SendVerificationCodeAsync(
        WebSocket socket,
        PendingHomeAssistantVerification pending,
        CancellationToken cancellationToken = default)
    {
        await SendJsonAsync(socket, new
        {
            type = "verification_code",
            code = pending.Code,
            expiresInSeconds = (int)Math.Max(0, (pending.ExpiresAtUtc - DateTimeOffset.UtcNow).TotalSeconds)
        }, cancellationToken);
    }

    public async Task SendErrorAsync(WebSocket socket, string message, CancellationToken cancellationToken = default)
    {
        await SendJsonAsync(socket, new
        {
            type = "error",
            message
        }, cancellationToken);
    }

    public async Task SendPongAsync(WebSocket socket, CancellationToken cancellationToken = default)
    {
        await SendJsonAsync(socket, new { type = "pong" }, cancellationToken);
    }

    private static async Task SendJsonAsync(WebSocket socket, object payload, CancellationToken cancellationToken)
    {
        if (socket.State != WebSocketState.Open) return;

        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);
        await socket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken);
    }

    private void PurgeExpired()
    {
        var now = DateTimeOffset.UtcNow;

        foreach (var pair in _pendingByCode)
        {
            if (pair.Value.ExpiresAtUtc > now) continue;
            _pendingByCode.TryRemove(pair.Key, out _);
            _codeByInstanceId.TryRemove(pair.Value.InstanceId, out _);
        }
    }

    private static string GenerateCode()
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        Span<byte> bytes = stackalloc byte[6];
        RandomNumberGenerator.Fill(bytes);

        var builder = new System.Text.StringBuilder(6);
        foreach (var value in bytes)
            builder.Append(alphabet[value % alphabet.Length]);

        return builder.ToString();
    }

    private static string NormalizeCode(string code)
    {
        return new string(code.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
    }

    public sealed record PendingHomeAssistantVerification(
        string InstanceId,
        string Code,
        DateTimeOffset ExpiresAtUtc);

    private sealed record HomeAssistantConnection(string InstanceId, WebSocket Socket);
}
