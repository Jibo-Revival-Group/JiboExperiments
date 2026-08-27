namespace Jibo.Cloud.Application.Abstractions;

public interface ITransportMetrics
{
    void HttpPayload(string direction, string endpointClass, string method, int statusCode, long bytes);
    void WebSocketConnectionOpened(string socketKind);
    void WebSocketConnectionClosed(string socketKind);
    void WebSocketMessage(string direction, string socketKind, string payloadClass, string? messageClass, long bytes);
    void ActiveSessionsChanged(long delta);
    void BufferedAudioAccepted(long bytes);
    void BufferedAudioLimitRejected(long bytes);
    void ActiveTurnsChanged(long delta);
    void TurnPhaseCompleted(string phase, string outcome, double durationMilliseconds);
    void TurnFinalizationSuppressed(string reason);
    void TurnRepliesEmitted(long count, bool hasEndOfStream);
    void PersistenceCacheAccess(string store, string result);
    void PostgreSqlPoolConfigured(string store, int maximumConnections);
}

public sealed class NullTransportMetrics : ITransportMetrics
{
    public static readonly NullTransportMetrics Instance = new();
    private NullTransportMetrics() { }
    public void HttpPayload(string direction, string endpointClass, string method, int statusCode, long bytes) { }
    public void WebSocketConnectionOpened(string socketKind) { }
    public void WebSocketConnectionClosed(string socketKind) { }
    public void WebSocketMessage(string direction, string socketKind, string payloadClass, string? messageClass,
        long bytes) { }
    public void ActiveSessionsChanged(long delta) { }
    public void BufferedAudioAccepted(long bytes) { }
    public void BufferedAudioLimitRejected(long bytes) { }
    public void ActiveTurnsChanged(long delta) { }
    public void TurnPhaseCompleted(string phase, string outcome, double durationMilliseconds) { }
    public void TurnFinalizationSuppressed(string reason) { }
    public void TurnRepliesEmitted(long count, bool hasEndOfStream) { }
    public void PersistenceCacheAccess(string store, string result) { }
    public void PostgreSqlPoolConfigured(string store, int maximumConnections) { }
}
