using System.Collections.Concurrent;
using System.Text.Json;
using Jibo.Cloud.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jibo.Cloud.Application.Services;

/// <summary>
/// Coordinates the stock proactive flow. A robot sends TRIGGER first and CONTEXT
/// second; no listen-turn frames are valid on this socket.
/// </summary>
public sealed class ProactiveTransactionHandler(
    WebSocketTurnFinalizationService turnFinalizationService,
    ILogger<ProactiveTransactionHandler>? logger = null)
{
    private static readonly TimeSpan PendingLifetime = TimeSpan.FromSeconds(30);
    private readonly ConcurrentDictionary<string, PendingTrigger> _pending = new();
    private readonly ILogger<ProactiveTransactionHandler> _logger =
        logger ?? NullLogger<ProactiveTransactionHandler>.Instance;

    public bool HasPendingTrigger(WebSocketMessageEnvelope envelope)
    {
        var key = ResolveConnectionKey(envelope);
        if (!_pending.TryGetValue(key, out var pending)) return false;
        if (pending.CreatedUtc >= DateTimeOffset.UtcNow - PendingLifetime) return true;
        _pending.TryRemove(key, out _);
        return false;
    }

    public IReadOnlyList<WebSocketReply> HandleTrigger(
        CloudSession session,
        WebSocketMessageEnvelope envelope,
        CancellationToken cancellationToken = default)
    {
        RemoveExpired();
        var connectionKey = ResolveConnectionKey(envelope);
        var pending = new PendingTrigger(
            ReadStringProperty(envelope.Text, "transID"),
            envelope.Text ?? "{}",
            DateTimeOffset.UtcNow);
        _pending[connectionKey] = pending;
        if (cancellationToken.CanBeCanceled)
            cancellationToken.Register(() => _pending.TryRemove(connectionKey, out _));
        _logger.LogDebug(
            "Proactive trigger held for context session={SessionId} connection={ConnectionId} transId={TransId}",
            session.SessionId,
            envelope.ConnectionId,
            pending.TransId);
        return [];
    }

    public async Task<IReadOnlyList<WebSocketReply>> HandleContextAsync(
        CloudSession session,
        WebSocketMessageEnvelope envelope,
        CancellationToken cancellationToken = default)
    {
        if (!_pending.TryRemove(ResolveConnectionKey(envelope), out var pending))
            return [];

        var contextPayload = ExtractDataPayload(envelope.Text) ?? envelope.Text ?? "{}";
        session.TurnState.SawContext = true;
        session.TurnState.ContextPayload = contextPayload;
        if (!string.IsNullOrWhiteSpace(pending.TransId))
        {
            session.TurnState.TransId = pending.TransId;
            session.LastTransId = pending.TransId;
        }

        session.Metadata["context"] = contextPayload;
        SessionRobotIdentityBinder.TryBindFromContextPayload(session, contextPayload);

        var triggerEnvelope = new WebSocketMessageEnvelope
        {
            ConnectionId = envelope.ConnectionId,
            HostName = envelope.HostName,
            Path = envelope.Path,
            Kind = envelope.Kind,
            Token = envelope.Token,
            Text = pending.Payload
        };

        _logger.LogDebug(
            "Proactive context completing held trigger session={SessionId} connection={ConnectionId} transId={TransId}",
            session.SessionId,
            envelope.ConnectionId,
            pending.TransId);
        return await turnFinalizationService.HandleProactiveAsync(
            session,
            triggerEnvelope,
            cancellationToken);
    }

    private void RemoveExpired()
    {
        var cutoff = DateTimeOffset.UtcNow - PendingLifetime;
        foreach (var item in _pending)
            if (item.Value.CreatedUtc < cutoff)
                _pending.TryRemove(item.Key, out _);
    }

    private static string ResolveConnectionKey(WebSocketMessageEnvelope envelope) =>
        !string.IsNullOrWhiteSpace(envelope.ConnectionId)
            ? envelope.ConnectionId
            : $"{envelope.Kind}:{envelope.Token}:{envelope.Path}";

    private static string? ReadStringProperty(string? text, string propertyName)
    {
        try
        {
            using var document = JsonDocument.Parse(text ?? "{}");
            return document.RootElement.TryGetProperty(propertyName, out var value) &&
                   value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? ExtractDataPayload(string? text)
    {
        try
        {
            using var document = JsonDocument.Parse(text ?? "{}");
            return document.RootElement.TryGetProperty("data", out var data)
                ? data.GetRawText()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record PendingTrigger(string? TransId, string Payload, DateTimeOffset CreatedUtc);
}
