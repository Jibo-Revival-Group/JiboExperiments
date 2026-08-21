using System.Collections.Concurrent;
using System.Globalization;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Jibo.Cloud.Application.Abstractions;

namespace Jibo.Cloud.Application.Services;

public sealed class HomeAssistantConnectionRegistry(ITransportMetrics? transportMetrics = null)
{
    private static readonly TimeSpan VerificationLifetime = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan CommandResultTimeout = TimeSpan.FromSeconds(3);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ITransportMetrics _transportMetrics = transportMetrics ?? NullTransportMetrics.Instance;

    private readonly ConcurrentDictionary<string, string> _codeByInstanceId =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly ConcurrentDictionary<string, HomeAssistantConnection> _connections =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly ConcurrentDictionary<string, PendingHomeAssistantVerification> _pendingByCode =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly ConcurrentDictionary<string, TaskCompletionSource<HomeAssistantCommandResult>>
        _pendingCommandResults = new(StringComparer.OrdinalIgnoreCase);

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

    public void RegisterPairedConnection(string instanceId, WebSocket socket)
    {
        _connections[instanceId] = new HomeAssistantConnection(instanceId, socket);

        if (_codeByInstanceId.TryRemove(instanceId, out var code))
            _pendingByCode.TryRemove(code, out _);
    }

    public bool IsInstanceConnected(string instanceId)
    {
        return _connections.TryGetValue(instanceId, out var connection) &&
               connection.Socket.State == WebSocketState.Open;
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

    public async Task<bool> SendPairedAsync(
        string instanceId,
        string jiboFriendlyName,
        string linkId,
        string commandSecret,
        CancellationToken cancellationToken = default)
    {
        if (!_connections.TryGetValue(instanceId, out var connection)) return false;

        if (_codeByInstanceId.TryRemove(instanceId, out var code))
            _pendingByCode.TryRemove(code, out _);

        await SendJsonAsync(connection.Socket, new
        {
            type = "paired",
            jiboFriendlyName,
            linkId,
            commandSecret
        }, "paired", cancellationToken);

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
        }, "verification_code", cancellationToken);
    }

    public async Task SendErrorAsync(WebSocket socket, string message, CancellationToken cancellationToken = default)
    {
        await SendJsonAsync(socket, new
        {
            type = "error",
            message
        }, "error", cancellationToken);
    }

    public async Task SendPongAsync(WebSocket socket, CancellationToken cancellationToken = default)
    {
        await SendJsonAsync(socket, new { type = "pong" }, "pong", cancellationToken);
    }

    public async Task<bool> SendCommandAsync(
        string instanceId,
        string linkId,
        string commandSecret,
        string command,
        IReadOnlyDictionary<string, string>? parameters = null,
        CancellationToken cancellationToken = default)
    {
        if (!_connections.TryGetValue(instanceId, out var connection)) return false;
        if (string.IsNullOrWhiteSpace(commandSecret)) return false;

        var payload = BuildCommandPayload(command, parameters, requestId: null, linkId, commandSecret);
        await SendJsonAsync(connection.Socket, payload, "command", cancellationToken);
        return true;
    }

    public async Task<HomeAssistantCommandResult?> SendCommandAndWaitAsync(
        string instanceId,
        string linkId,
        string commandSecret,
        string command,
        IReadOnlyDictionary<string, string>? parameters = null,
        CancellationToken cancellationToken = default)
    {
        if (!_connections.TryGetValue(instanceId, out var connection)) return null;
        if (string.IsNullOrWhiteSpace(commandSecret)) return null;

        var requestId = Guid.NewGuid().ToString("N");
        var completion = new TaskCompletionSource<HomeAssistantCommandResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingCommandResults[requestId] = completion;

        try
        {
            var payload = BuildCommandPayload(command, parameters, requestId, linkId, commandSecret);
            await SendJsonAsync(connection.Socket, payload, "command", cancellationToken);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(CommandResultTimeout);

            try
            {
                return await completion.Task.WaitAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return HomeAssistantCommandResult.Timeout(requestId);
            }
        }
        finally
        {
            _pendingCommandResults.TryRemove(requestId, out _);
        }
    }

    public bool TryCompleteCommandResult(JsonElement root)
    {
        var result = HomeAssistantCommandResult.FromJson(root);
        if (string.IsNullOrWhiteSpace(result.RequestId)) return false;

        if (!_pendingCommandResults.TryRemove(result.RequestId, out var completion))
            return false;

        return completion.TrySetResult(result);
    }

    public async Task SendUnpairedAsync(
        string instanceId,
        string linkId,
        CancellationToken cancellationToken = default)
    {
        if (!_connections.TryGetValue(instanceId, out var connection)) return;

        if (_codeByInstanceId.TryRemove(instanceId, out var code))
            _pendingByCode.TryRemove(code, out _);

        await SendJsonAsync(connection.Socket, new
        {
            type = "unpaired",
            linkId
        }, "unpaired", cancellationToken);
    }

    private static Dictionary<string, object?> BuildCommandPayload(
        string command,
        IReadOnlyDictionary<string, string>? parameters,
        string? requestId,
        string linkId,
        string commandSecret)
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["type"] = "command",
            ["command"] = command,
            ["linkId"] = linkId,
            ["timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                .ToString(CultureInfo.InvariantCulture),
            ["nonce"] = HomeAssistantCommandAuthentication.GenerateNonce()
        };

        if (!string.IsNullOrWhiteSpace(requestId))
            fields["requestId"] = requestId;

        if (parameters is not null)
            foreach (var pair in parameters)
                if (!string.IsNullOrWhiteSpace(pair.Value))
                    fields[pair.Key] = pair.Value;

        fields["signature"] = HomeAssistantCommandAuthentication.Sign(fields, commandSecret);

        var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in fields)
            payload[pair.Key] = pair.Value;

        return payload;
    }

    private async Task SendJsonAsync(WebSocket socket, object payload, string messageClass,
        CancellationToken cancellationToken)
    {
        if (socket.State != WebSocketState.Open) return;

        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);
        await socket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken);
        _transportMetrics.WebSocketMessage("out", "home-assistant", "text-json", messageClass, bytes.Length);
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

        var builder = new StringBuilder(6);
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
