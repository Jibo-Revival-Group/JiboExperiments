namespace Jibo.Cloud.Api.Hosting;

internal static class TokenResolver
{
    private static readonly string[] HubPaths =
    [
        "v1/listen",
        "listen",
        "v1/proactive",
        "proactive"
    ];

    internal static string? Resolve(HttpRequest request)
    {
        var auth = request.Headers.Authorization.ToString();
        if (auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return auth["Bearer ".Length..].Trim();

        var path = request.Path.Value;
        if (!string.IsNullOrWhiteSpace(path) && path.Length > 1)
        {
            var trimmed = path.Trim('/');
            foreach (var hubPath in HubPaths)
            {
                if (string.Equals(trimmed, hubPath, StringComparison.OrdinalIgnoreCase))
                    return null;

                var prefix = hubPath + "/";
                if (trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return trimmed[prefix.Length..];
            }

            return trimmed;
        }

        return null;
    }
}