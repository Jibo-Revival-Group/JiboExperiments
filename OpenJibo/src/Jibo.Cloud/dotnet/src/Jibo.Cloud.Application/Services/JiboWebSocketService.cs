using System.Text.Json;
using Jibo.Cloud.Application.Abstractions;
using Jibo.Cloud.Application.Logging;
using Jibo.Cloud.Domain.Models;
using Microsoft.Extensions.Logging;

namespace Jibo.Cloud.Application.Services;

public sealed class JiboWebSocketService(
    ICloudStateStore stateStore,
    IWebSocketTelemetrySink telemetrySink,
    WebSocketTurnFinalizationService turnFinalizationService,
    ILogger<JiboWebSocketService> logger)
{
    private readonly DetailedOperationLogger _detailedLogger = new(logger);
    public CloudSession GetOrCreateSession(WebSocketMessageEnvelope envelope)
    {
        _detailedLogger.LogEntry(nameof(GetOrCreateSession),
            ("token", envelope.Token),
            ("kind", envelope.Kind),
            ("host", envelope.HostName));

        var session = stateStore.FindSessionByToken(envelope.Token ?? string.Empty) ??
               stateStore.OpenSession(envelope.Kind, null, envelope.Token, envelope.HostName, envelope.Path);

        _detailedLogger.LogExit(nameof(GetOrCreateSession), $"sessionId={session.SessionId}");
        return session;
    }

    public async Task<IReadOnlyList<WebSocketReply>> HandleMessageAsync(WebSocketMessageEnvelope envelope, CancellationToken cancellationToken = default)
    {
        _detailedLogger.LogEntry(nameof(HandleMessageAsync),
            ("isBinary", envelope.IsBinary),
            ("textLength", envelope.Text?.Length ?? 0),
            ("binaryLength", envelope.Binary?.Length ?? 0));

        var session = GetOrCreateSession(envelope);
        session.LastSeenUtc = DateTimeOffset.UtcNow;

        _detailedLogger.LogState(nameof(HandleMessageAsync), "SessionId", session.SessionId);
        _detailedLogger.LogState(nameof(HandleMessageAsync), "SessionKind", session.Kind);

        if (envelope.IsBinary)
        {
            _detailedLogger.LogStep(nameof(HandleMessageAsync), "ProcessingBinaryAudio", $"bytes={envelope.Binary?.Length ?? 0}");
            var replies = await turnFinalizationService.HandleBinaryAudioAsync(session, envelope, cancellationToken);
            await telemetrySink.RecordTurnEventAsync(envelope, session, "binary_audio_received", new Dictionary<string, object?>
            {
                ["bytes"] = envelope.Binary?.Length ?? 0
            }, cancellationToken);
            _detailedLogger.LogPayload(nameof(HandleMessageAsync), "BinaryAudio", envelope.Binary?.Length ?? 0, null);
            _detailedLogger.LogExit(nameof(HandleMessageAsync), $"replies={replies.Count}");
            return replies;
        }

        var parsedType = ReadMessageType(envelope.Text);
        _detailedLogger.LogDecision(nameof(HandleMessageAsync), "MessageTypeResolved", parsedType);

        session.LastMessageType = parsedType;
        WebSocketTurnFinalizationService.ObserveIncomingMessage(session, envelope.Text);
        _detailedLogger.LogState(nameof(HandleMessageAsync), "LastMessageType", parsedType);

        switch (parsedType)
        {
            case "CONTEXT":
            {
                _detailedLogger.LogStep(nameof(HandleMessageAsync), "ProcessingContext", $"transId={session.TurnState.TransId}");
                var replies = await turnFinalizationService.HandleContextAsync(session, envelope, cancellationToken);
                await telemetrySink.RecordTurnEventAsync(envelope, session, "context_received", new Dictionary<string, object?>
                {
                    ["transID"] = session.TurnState.TransId
                }, cancellationToken);
                _detailedLogger.LogExit(nameof(HandleMessageAsync), $"replies={replies.Count}");
                return replies;
            }
            case "LISTEN":
            {
                var hasInlinePayload = ContainsInlineTurnPayload(envelope.Text);
                _detailedLogger.LogDecision(nameof(HandleMessageAsync), "ListenHandlerSelected", hasInlinePayload ? "inline_turn" : "listen_setup");
                var replies = hasInlinePayload
                    ? await turnFinalizationService.HandleTurnAsync(session, envelope, parsedType, cancellationToken)
                    : WebSocketTurnFinalizationService.HandleListenSetup(session, envelope);
                await telemetrySink.RecordTurnEventAsync(envelope, session, "turn_processed", new Dictionary<string, object?>
                {
                    ["messageType"] = parsedType,
                    ["replyCount"] = replies.Count,
                    ["transcript"] = session.LastTranscript,
                    ["intent"] = session.LastIntent
                }, cancellationToken);
                _detailedLogger.LogExit(nameof(HandleMessageAsync), $"replies={replies.Count}");
                return replies;
            }
            case "CLIENT_NLU" or "CLIENT_ASR":
            {
                _detailedLogger.LogStep(nameof(HandleMessageAsync), "ProcessingTurn", $"type={parsedType}");
                var replies = await turnFinalizationService.HandleTurnAsync(session, envelope, parsedType, cancellationToken);
                await telemetrySink.RecordTurnEventAsync(envelope, session, "turn_processed", new Dictionary<string, object?>
                {
                    ["messageType"] = parsedType,
                    ["replyCount"] = replies.Count,
                    ["transcript"] = session.LastTranscript,
                    ["intent"] = session.LastIntent
                }, cancellationToken);
                _detailedLogger.LogExit(nameof(HandleMessageAsync), $"replies={replies.Count}");
                return replies;
            }
            default:
                _detailedLogger.LogDecision(nameof(HandleMessageAsync), "UnknownMessageType", $"type={parsedType}");
                _detailedLogger.LogExit(nameof(HandleMessageAsync), "empty");
                return [];
        }
    }

    private static string ReadMessageType(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "UNKNOWN";
        }

        try
        {
            using var document = JsonDocument.Parse(text);
            if (document.RootElement.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String)
            {
                return type.GetString() ?? "UNKNOWN";
            }
        }
        catch
        {
            return "TEXT";
        }

        return "UNKNOWN";
    }

    private static bool ContainsInlineTurnPayload(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(text);
            if (!document.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            if (data.TryGetProperty("text", out var transcript) &&
                transcript.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(transcript.GetString()))
            {
                return true;
            }

            return data.TryGetProperty("asr", out var asr) &&
                   asr.ValueKind == JsonValueKind.Object &&
                   asr.TryGetProperty("text", out var asrText) &&
                   asrText.ValueKind == JsonValueKind.String &&
                   !string.IsNullOrWhiteSpace(asrText.GetString());
        }
        catch
        {
            return false;
        }
    }
}
