using Microsoft.AspNetCore.Http;

namespace Jibo.Cloud.Api.Hosting;

internal static class TokenResolver
{
    internal static string? Resolve(HttpRequest request)
    {
        var auth = request.Headers.Authorization.ToString();
        if (auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return auth["Bearer ".Length..].Trim();

        var path = request.Path.Value;
        if (!string.IsNullOrWhiteSpace(path) && path.Length > 1) return path.Trim('/');

        return null;
    }
}
