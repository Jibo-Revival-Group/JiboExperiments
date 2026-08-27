namespace Jibo.Cloud.Api.Hosting;

internal static class RequestLogSanitizer
{
    internal static string RedactWebSocketPath(string kind, PathString path) => kind switch
    {
        "api-socket" => "/{notification-token}",
        "neo-hub-listen" => "/v1/listen/{token}",
        "neo-hub-proactive" => "/v1/proactive/{token}",
        _ => path.Value ?? "/"
    };

    internal static string? RedactQuery(QueryString query, bool isWebSocketRequest) =>
        query.HasValue ? isWebSocketRequest ? "[redacted]" : query.Value : null;
}
