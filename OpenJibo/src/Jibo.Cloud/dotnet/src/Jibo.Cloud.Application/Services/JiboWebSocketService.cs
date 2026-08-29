using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Jibo.Cloud.Application.Abstractions;
using Jibo.Cloud.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jibo.Cloud.Application.Services;

public sealed class JiboWebSocketService(
    ICloudStateStore stateStore,
    IWebSocketTelemetrySink telemetrySink,
    WebSocketTurnFinalizationService turnFinalizationService,
    ProactiveTransactionHandler proactiveTransactionHandler,
    ILogger<JiboWebSocketService> logger)
{
    public JiboWebSocketService(
        ICloudStateStore stateStore,
        IWebSocketTelemetrySink telemetrySink,
        WebSocketTurnFinalizationService turnFinalizationService)
        : this(
            stateStore,
            telemetrySink,
            turnFinalizationService,
            new ProactiveTransactionHandler(turnFinalizationService),
            NullLogger<JiboWebSocketService>.Instance)
    {
    }

    public CloudSession GetOrCreateSession(WebSocketMessageEnvelope envelope)
    {
        var sessionKey = WebSocketSessionKeyResolver.ResolveSessionKey(envelope);
        return stateStore.FindActiveSessionByToken(sessionKey) ??
               stateStore.OpenSession(envelope.Kind, null, sessionKey, envelope.HostName, envelope.Path);
    }

    public async Task<IReadOnlyList<WebSocketReply>> HandleMessageAsync(WebSocketMessageEnvelope envelope,
        CancellationToken cancellationToken = default)
    {
        var session = GetOrCreateSession(envelope);
        session.LastSeenUtc = DateTimeOffset.UtcNow;
        logger.LogDebug(
            "WebSocket message received session={SessionId} kind={Kind} host={Host} path={Path} " +
            "tokenFingerprint={TokenFingerprint} isBinary={IsBinary} textBytes={TextBytes} binaryBytes={BinaryBytes}",
            session.SessionId,
            envelope.Kind,
            envelope.HostName,
            envelope.Path,
            Fingerprint(envelope.Token),
            envelope.IsBinary,
            envelope.Text?.Length ?? 0,
            envelope.Binary?.Length ?? 0);

        if (envelope.IsBinary)
        {
            var replies = await turnFinalizationService.HandleBinaryAudioAsync(session, envelope, cancellationToken);
            logger.LogDebug(
                "WebSocket binary audio handled session={SessionId} replyCount={ReplyCount} glsmPhase={GlsmPhase}",
                session.SessionId,
                replies.Count,
                WebSocketTurnFinalizationService.ResolveGlsmPhase(session));
            await telemetrySink.RecordTurnEventAsync(envelope, session, "binary_audio_received",
                new Dictionary<string, object?>
                {
                    ["bytes"] = envelope.Binary?.Length ?? 0,
                    ["glsmPhase"] = WebSocketTurnFinalizationService.ResolveGlsmPhase(session)
                }, cancellationToken);
            return replies;
        }

        var parsedType = ReadMessageType(envelope.Text);
        session.LastMessageType = parsedType;
        logger.LogDebug("WebSocket parsed message session={SessionId} messageType={MessageType} glsmPhase={GlsmPhase}",
            session.SessionId,
            parsedType,
            WebSocketTurnFinalizationService.ResolveGlsmPhase(session));
        var containsInlineTurnPayload = parsedType == "LISTEN" && ContainsInlineTurnPayload(envelope.Text);
        var staleListenRecovered = false;
        var staleListenAgeMs = 0;
        switch (parsedType)
        {
            case "LISTEN" when
                !containsInlineTurnPayload &&
                WebSocketTurnFinalizationService.ShouldIgnoreLateListenSetup(session, envelope.Text):
            {
                var (lateTransId, lateRules) = ResolveLateListenNoInputPayload(session, envelope.Text);
                var replies = ResponsePlanToSocketMessagesMapper
                    .MapNoInputAndRedirectToSkill(lateTransId, lateRules, "@be/idle")
                    .Select(map => new WebSocketReply
                    {
                        Text = map.Text,
                        DelayMs = map.DelayMs
                    })
                    .ToArray();

                await telemetrySink.RecordTurnEventAsync(envelope, session, "late_listen_ignored",
                    new Dictionary<string, object?>
                    {
                        ["messageType"] = parsedType,
                        ["activeTransID"] = session.TurnState.TransId,
                        ["ignoredTransID"] = lateTransId,
                        ["replyCount"] = replies.Length
                    }, cancellationToken);
                logger.LogDebug(
                    "WebSocket late listen ignored session={SessionId} activeTransID={ActiveTransId} ignoredTransID={IgnoredTransId} replyCount={ReplyCount}",
                    session.SessionId,
                    session.TurnState.TransId,
                    lateTransId,
                    replies.Length);
                return replies;
            }
            case "LISTEN" when
                !containsInlineTurnPayload &&
                WebSocketTurnFinalizationService.TryRecoverStalePendingListen(session, out staleListenAgeMs):
                staleListenRecovered = true;
                await telemetrySink.RecordTurnEventAsync(envelope, session, "glsm_stale_listen_recovered",
                    new Dictionary<string, object?>
                    {
                        ["staleAgeMs"] = staleListenAgeMs,
                        ["transID"] = session.TurnState.TransId,
                        ["glsmPhase"] = WebSocketTurnFinalizationService.ResolveGlsmPhase(session)
                    }, cancellationToken);
                break;
        }

        WebSocketTurnFinalizationService.ObserveIncomingMessage(session, envelope.Text);

        switch (parsedType)
        {
            case "CONTEXT":
            {
                if (string.Equals(envelope.Kind, "neo-hub-proactive", StringComparison.OrdinalIgnoreCase) &&
                    proactiveTransactionHandler.HasPendingTrigger(envelope))
                {
                    var proactiveReplies = await proactiveTransactionHandler.HandleContextAsync(
                        session,
                        envelope,
                        cancellationToken);
                    await telemetrySink.RecordTurnEventAsync(envelope, session, "proactive_context_completed",
                        new Dictionary<string, object?>
                        {
                            ["replyCount"] = proactiveReplies.Count,
                            ["replyTypes"] = proactiveReplies.Select(reply => ReadMessageType(reply.Text)).ToArray(),
                            ["transID"] = session.LastTransId
                        }, cancellationToken);
                    return proactiveReplies;
                }

                var replies = await turnFinalizationService.HandleContextAsync(session, envelope, cancellationToken);
                if (!string.IsNullOrWhiteSpace(session.DeviceId))
                    stateStore.ReinheritDialogMetadata(session);
                await telemetrySink.RecordTurnEventAsync(envelope, session, "context_received",
                    new Dictionary<string, object?>
                    {
                        ["transID"] = session.TurnState.TransId,
                        ["glsmPhase"] = WebSocketTurnFinalizationService.ResolveGlsmPhase(session),
                        ["robotId"] = session.DeviceId
                    }, cancellationToken);
                return replies;
            }
            case "LISTEN":
            {
                var replies = containsInlineTurnPayload
                    ? await turnFinalizationService.HandleTurnAsync(session, envelope, parsedType, cancellationToken)
                    : turnFinalizationService.HandleListenSetup(session, envelope);
                logger.LogDebug(
                    "WebSocket listen handled session={SessionId} inlineTurn={InlineTurn} replyCount={ReplyCount} transId={TransId} intent={Intent}",
                    session.SessionId,
                    containsInlineTurnPayload,
                    replies.Count,
                    session.TurnState.TransId,
                    session.LastIntent);
                await telemetrySink.RecordTurnEventAsync(envelope, session, "turn_processed",
                    new Dictionary<string, object?>
                    {
                        ["messageType"] = parsedType,
                        ["replyCount"] = replies.Count,
                        ["transcript"] = session.LastTranscript,
                        ["intent"] = session.LastIntent,
                        ["glsmPhase"] = WebSocketTurnFinalizationService.ResolveGlsmPhase(session),
                        ["staleListenRecovered"] = staleListenRecovered,
                        ["staleListenAgeMs"] = staleListenAgeMs
                    }, cancellationToken);
                return replies;
            }
            case "TRIGGER" when
                string.Equals(envelope.Kind, "neo-hub-proactive", StringComparison.OrdinalIgnoreCase):
            {
                var replies = proactiveTransactionHandler.HandleTrigger(session, envelope, cancellationToken);
                await telemetrySink.RecordTurnEventAsync(envelope, session, "proactive_trigger_held",
                    new Dictionary<string, object?>
                    {
                        ["transID"] = session.LastTransId
                    }, cancellationToken);
                return replies;
            }
            case "CLIENT_NLU" or "CLIENT_ASR" or "TRIGGER":
            {
                var replies =
                    await turnFinalizationService.HandleTurnAsync(session, envelope, parsedType, cancellationToken);
                logger.LogDebug(
                    "WebSocket turn handled session={SessionId} messageType={MessageType} replyCount={ReplyCount} transId={TransId} intent={Intent}",
                    session.SessionId,
                    parsedType,
                    replies.Count,
                    session.TurnState.TransId,
                    session.LastIntent);
                await telemetrySink.RecordTurnEventAsync(envelope, session, "turn_processed",
                    new Dictionary<string, object?>
                    {
                        ["messageType"] = parsedType,
                        ["replyCount"] = replies.Count,
                        ["transcript"] = session.LastTranscript,
                        ["intent"] = session.LastIntent,
                        ["glsmPhase"] = WebSocketTurnFinalizationService.ResolveGlsmPhase(session)
                    }, cancellationToken);
                return replies;
            }
            default:
                return [];
        }
    }

    public bool MarkPrematureSocketLoopEnded(CloudSession session, string? expectedTransId = null)
    {
        logger.LogDebug(
            "WebSocket premature socket loop ended session={SessionId} transId={TransId} expectedTransId={ExpectedTransId} bufferedBytes={BufferedBytes} awaitingTurnCompletion={AwaitingTurnCompletion}",
            session.SessionId,
            session.TurnState.TransId,
            expectedTransId,
            session.TurnState.BufferedAudioBytes,
            session.TurnState.AwaitingTurnCompletion);
        var marked = WebSocketTurnFinalizationService.MarkPrematureSocketLoopEnded(session, expectedTransId);
        logger.LogDebug(
            "WebSocket premature socket loop mark result session={SessionId} expectedTransId={ExpectedTransId} marked={Marked}",
            session.SessionId,
            expectedTransId,
            marked);
        return marked;
    }

    public async Task<IReadOnlyList<WebSocketReply>> HandleIdleAsync(
        CloudSession session,
        WebSocketMessageEnvelope envelope,
        CancellationToken cancellationToken = default)
    {
        var replies = await turnFinalizationService.HandleIdleAsync(session, envelope, cancellationToken);
        if (replies.Count > 0)
            logger.LogInformation(
                "WebSocket idle watchdog emitted replies session={SessionId} replyCount={ReplyCount} glsmPhase={GlsmPhase}",
                session.SessionId,
                replies.Count,
                WebSocketTurnFinalizationService.ResolveGlsmPhase(session));
        return replies;
    }

    private static string ReadMessageType(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "UNKNOWN";

        try
        {
            using var document = JsonDocument.Parse(text);
            if (document.RootElement.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String)
                return type.GetString() ?? "UNKNOWN";
        }
        catch
        {
            return "TEXT";
        }

        return "UNKNOWN";
    }

    private static string Fingerprint(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "none";

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value.Trim()));
        return Convert.ToHexString(hash)[..12].ToLowerInvariant();
    }

    private static bool ContainsInlineTurnPayload(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;

        try
        {
            using var document = JsonDocument.Parse(text);
            if (!document.RootElement.TryGetProperty("data", out var data) ||
                data.ValueKind != JsonValueKind.Object) return false;

            if (data.TryGetProperty("text", out var transcript) &&
                transcript.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(transcript.GetString()))
                return true;

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

    private static (string TransId, IReadOnlyList<string> Rules) ResolveLateListenNoInputPayload(
        CloudSession session,
        string? text)
    {
        var transId = session.TurnState.TransId ?? session.LastTransId ?? string.Empty;
        var rules = session.TurnState.ListenRules;

        if (string.IsNullOrWhiteSpace(text)) return (transId, rules);

        try
        {
            using var document = JsonDocument.Parse(text);
            var root = document.RootElement;

            if (root.TryGetProperty("transID", out var transIdValue) &&
                transIdValue.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(transIdValue.GetString()))
                transId = transIdValue.GetString()!;

            if (root.TryGetProperty("data", out var data) &&
                data.ValueKind == JsonValueKind.Object &&
                data.TryGetProperty("rules", out var ruleValues) &&
                ruleValues.ValueKind == JsonValueKind.Array)
            {
                var parsedRules = ruleValues.EnumerateArray()
                    .Where(static item => item.ValueKind == JsonValueKind.String)
                    .Select(static item => item.GetString() ?? string.Empty)
                    .Where(static rule => !string.IsNullOrWhiteSpace(rule))
                    .ToArray();

                if (parsedRules.Length > 0) rules = parsedRules;
            }
        }
        catch
        {
            // Best effort parsing for late-listen cleanup.
        }

        return (transId, rules);
    }
}
