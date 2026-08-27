using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Jibo.Cloud.Application.Abstractions;
using Jibo.Cloud.Application.Services;

namespace Jibo.Cloud.Infrastructure.Telemetry;

/// <summary>Low-cardinality aggregate transport metrics. Payloads and identifiers are never recorded.</summary>
public sealed class TransportMetrics : ITransportMetrics, IDisposable
{
    public const string MeterName = "OpenJibo.Transport";
    private static readonly HashSet<string> SocketKinds =
        ["api-socket", "neo-hub-listen", "neo-hub-proactive", "home-assistant"];
    private static readonly HashSet<string> PayloadClasses = ["text-json", "binary-audio", "control", "other"];
    private static readonly HashSet<string> HttpEndpointClasses = ["protocol", "portal", "static", "health"];
    private static readonly HashSet<string> HttpMethods = ["GET", "POST", "PUT", "DELETE", "PATCH", "HEAD", "OPTIONS"];
    private static readonly HashSet<string> MessageClasses =
    [
        "context", "listen", "client_nlu", "client_asr", "trigger", "loop_updated", "register", "ping",
        "pong", "paired", "unpaired", "verification_code", "command", "command_result", "error", "other"
    ];
    private static readonly HashSet<string> TurnPhases = ["stt", "plan", "finalize"];
    private static readonly HashSet<string> TurnOutcomes =
        ["success", "bypassed", "unavailable", "failure", "canceled"];
    private static readonly HashSet<string> FinalizationSuppressionReasons = ["concurrent"];
    private static readonly HashSet<string> PersistenceStores =
        ["cloud_state", "cloud_device", "personal_memory"];
    private static readonly HashSet<string> CacheResults = ["hit", "miss"];

    private readonly Meter _meter = new(MeterName);
    private readonly Counter<long> _messages;
    private readonly Counter<long> _bytes;
    private readonly Histogram<long> _messageSize;
    private readonly UpDownCounter<long> _activeConnections;
    private readonly Counter<long> _httpPayloads;
    private readonly Counter<long> _httpBytes;
    private readonly Histogram<long> _httpPayloadSize;
    private readonly UpDownCounter<long> _activeSessions;
    private readonly Counter<long> _acceptedBufferedAudioBytes;
    private readonly ObservableGauge<long> _currentBufferedAudioBytes;
    private readonly ObservableGauge<long> _bufferedAudioHighWaterMarkBytes;
    private readonly Histogram<long> _bufferedAudioFrameSize;
    private readonly Counter<long> _bufferedAudioLimitRejections;
    private readonly Counter<long> _rejectedBufferedAudioBytes;
    private readonly UpDownCounter<long> _activeTurns;
    private readonly Histogram<double> _turnPhaseDuration;
    private readonly Counter<long> _turnPhaseOperations;
    private readonly Counter<long> _turnFinalizationSuppressions;
    private readonly Counter<long> _turnReplyBatches;
    private readonly Histogram<long> _turnReplyCount;
    private readonly Counter<long> _persistenceCacheAccesses;
    private readonly ConcurrentDictionary<string, long> _configuredPostgreSqlPoolLimits = new();
    private readonly ObservableGauge<long> _configuredPostgreSqlPoolLimit;

    public TransportMetrics()
    {
        _messages = _meter.CreateCounter<long>("openjibo.transport.websocket.messages", unit: "{message}");
        _bytes = _meter.CreateCounter<long>("openjibo.transport.websocket.payload_bytes", unit: "By");
        _messageSize = _meter.CreateHistogram<long>("openjibo.transport.websocket.message_size", unit: "By");
        _activeConnections = _meter.CreateUpDownCounter<long>("openjibo.transport.websocket.active_connections",
            unit: "{connection}");
        _httpPayloads = _meter.CreateCounter<long>("openjibo.transport.http.payloads", unit: "{payload}");
        _httpBytes = _meter.CreateCounter<long>("openjibo.transport.http.payload_bytes", unit: "By");
        _httpPayloadSize = _meter.CreateHistogram<long>("openjibo.transport.http.payload_size", unit: "By");
        _activeSessions = _meter.CreateUpDownCounter<long>("openjibo.runtime.active_sessions", unit: "{session}");
        _acceptedBufferedAudioBytes = _meter.CreateCounter<long>("openjibo.audio.accepted_bytes", unit: "By");
        _currentBufferedAudioBytes = _meter.CreateObservableGauge("openjibo.audio.current_buffered_bytes",
            () => WebSocketTurnFinalizationService.CurrentBufferedAudioBytes, unit: "By");
        _bufferedAudioHighWaterMarkBytes = _meter.CreateObservableGauge("openjibo.audio.buffered_high_water_bytes",
            () => WebSocketTurnFinalizationService.BufferedAudioHighWaterMarkBytes, unit: "By");
        _bufferedAudioFrameSize = _meter.CreateHistogram<long>("openjibo.audio.buffered_frame_size", unit: "By");
        _bufferedAudioLimitRejections = _meter.CreateCounter<long>("openjibo.audio.buffer_limit_rejections",
            unit: "{rejection}");
        _rejectedBufferedAudioBytes = _meter.CreateCounter<long>("openjibo.audio.rejected_bytes", unit: "By");
        _activeTurns = _meter.CreateUpDownCounter<long>("openjibo.turn.active", unit: "{turn}");
        _turnPhaseDuration = _meter.CreateHistogram<double>("openjibo.turn.phase.duration", unit: "ms");
        _turnPhaseOperations = _meter.CreateCounter<long>("openjibo.turn.phase.operations", unit: "{operation}");
        _turnFinalizationSuppressions = _meter.CreateCounter<long>("openjibo.turn.finalization_suppressions",
            unit: "{suppression}");
        _turnReplyBatches = _meter.CreateCounter<long>("openjibo.turn.reply_batches", unit: "{batch}");
        _turnReplyCount = _meter.CreateHistogram<long>("openjibo.turn.reply_count", unit: "{reply}");
        _persistenceCacheAccesses = _meter.CreateCounter<long>("openjibo.persistence.cache.accesses",
            unit: "{access}");
        _configuredPostgreSqlPoolLimit = _meter.CreateObservableGauge(
            "openjibo.persistence.postgresql.configured_max_connections",
            ObserveConfiguredPostgreSqlPoolLimits,
            unit: "{connection}");
    }

    public void HttpPayload(string direction, string endpointClass, string method, int statusCode, long bytes)
    {
        var tags = new TagList
        {
            { "direction", direction.Equals("out", StringComparison.OrdinalIgnoreCase) ? "out" : "in" },
            { "endpoint_class", HttpEndpointClasses.Contains(endpointClass) ? endpointClass : "other" },
            { "method", NormalizeHttpMethod(method) },
            { "status_class", NormalizeStatusClass(statusCode) }
        };
        var safeBytes = Math.Max(0, bytes);
        _httpPayloads.Add(1, tags);
        _httpBytes.Add(safeBytes, tags);
        _httpPayloadSize.Record(safeBytes, tags);
    }

    public void WebSocketConnectionOpened(string socketKind) =>
        _activeConnections.Add(1, new KeyValuePair<string, object?>("socket_kind", NormalizeSocketKind(socketKind)));

    public void WebSocketConnectionClosed(string socketKind) =>
        _activeConnections.Add(-1, new KeyValuePair<string, object?>("socket_kind", NormalizeSocketKind(socketKind)));

    public void WebSocketMessage(string direction, string socketKind, string payloadClass, string? messageClass,
        long bytes)
    {
        var tags = new TagList
        {
            { "direction", direction.Equals("out", StringComparison.OrdinalIgnoreCase) ? "out" : "in" },
            { "socket_kind", NormalizeSocketKind(socketKind) },
            { "payload_class", PayloadClasses.Contains(payloadClass) ? payloadClass : "other" },
            { "message_class", NormalizeMessageClass(messageClass) }
        };
        var safeBytes = Math.Max(0, bytes);
        _messages.Add(1, tags);
        _bytes.Add(safeBytes, tags);
        _messageSize.Record(safeBytes, tags);
    }

    public void ActiveSessionsChanged(long delta) => _activeSessions.Add(delta);

    public void BufferedAudioAccepted(long bytes)
    {
        var safeBytes = Math.Max(0, bytes);
        _acceptedBufferedAudioBytes.Add(safeBytes);
        _bufferedAudioFrameSize.Record(safeBytes);
    }

    public void BufferedAudioLimitRejected(long bytes)
    {
        _bufferedAudioLimitRejections.Add(1);
        _rejectedBufferedAudioBytes.Add(Math.Max(0, bytes));
    }

    public void ActiveTurnsChanged(long delta) => _activeTurns.Add(delta);

    public void TurnPhaseCompleted(string phase, string outcome, double durationMilliseconds)
    {
        var tags = new TagList
        {
            { "phase", NormalizeTurnPhase(phase) },
            { "outcome", NormalizeTurnOutcome(outcome) }
        };
        _turnPhaseOperations.Add(1, tags);
        _turnPhaseDuration.Record(Math.Max(0, durationMilliseconds), tags);
    }

    public void TurnFinalizationSuppressed(string reason) =>
        _turnFinalizationSuppressions.Add(1,
            new KeyValuePair<string, object?>("reason", NormalizeFinalizationSuppressionReason(reason)));

    public void TurnRepliesEmitted(long count, bool hasEndOfStream)
    {
        var tags = new TagList { { "has_eos", hasEndOfStream ? "true" : "false" } };
        _turnReplyBatches.Add(1, tags);
        _turnReplyCount.Record(Math.Max(0, count), tags);
    }

    public void PersistenceCacheAccess(string store, string result)
    {
        var tags = new TagList
        {
            { "store", NormalizePersistenceStore(store) },
            { "result", CacheResults.Contains(result) ? result : "other" }
        };
        _persistenceCacheAccesses.Add(1, tags);
    }

    public void PostgreSqlPoolConfigured(string store, int maximumConnections) =>
        _configuredPostgreSqlPoolLimits[NormalizePersistenceStore(store)] = Math.Max(1, maximumConnections);

    public void Dispose() => _meter.Dispose();

    private static string NormalizeSocketKind(string value) => SocketKinds.Contains(value) ? value : "other";

    private static string NormalizeHttpMethod(string value)
    {
        var normalized = value.Trim().ToUpperInvariant();
        return HttpMethods.Contains(normalized) ? normalized : "OTHER";
    }

    private static string NormalizeStatusClass(int statusCode) => statusCode switch
    {
        >= 100 and <= 199 => "1xx",
        >= 200 and <= 299 => "2xx",
        >= 300 and <= 399 => "3xx",
        >= 400 and <= 499 => "4xx",
        >= 500 and <= 599 => "5xx",
        _ => "other"
    };

    private static string NormalizeMessageClass(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant() ?? "other";
        return MessageClasses.Contains(normalized) ? normalized : "other";
    }

    private IEnumerable<Measurement<long>> ObserveConfiguredPostgreSqlPoolLimits() =>
        _configuredPostgreSqlPoolLimits.Select(pair => new Measurement<long>(pair.Value,
            new KeyValuePair<string, object?>("store", pair.Key)));

    private static string NormalizeTurnPhase(string value) => TurnPhases.Contains(value) ? value : "other";

    private static string NormalizeTurnOutcome(string value) => TurnOutcomes.Contains(value) ? value : "other";

    private static string NormalizeFinalizationSuppressionReason(string value) =>
        FinalizationSuppressionReasons.Contains(value) ? value : "other";

    private static string NormalizePersistenceStore(string value) =>
        PersistenceStores.Contains(value) ? value : "other";
}
