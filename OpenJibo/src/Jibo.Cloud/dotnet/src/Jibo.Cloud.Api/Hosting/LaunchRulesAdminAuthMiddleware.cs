using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace Jibo.Cloud.Api.Hosting;

internal sealed class LaunchRulesAdminAuthMiddleware(RequestDelegate next, IOptions<LaunchRulesAdminOptions> options)
{
    private readonly LaunchRulesAdminOptions _options = options.Value;

    public async Task InvokeAsync(HttpContext context)
    {
        if (!RequiresAuth(context.Request.Path))
        {
            await next(context);
            return;
        }

        if (!_options.IsConfigured)
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            await context.Response.WriteAsJsonAsync(new
            {
                error = "Launch rules admin UI is disabled. Set OPENJIBO_LAUNCH_RULES_PASSWORD in .env."
            });
            return;
        }

        if (!TryAuthorize(context.Request))
        {
            context.Response.Headers.WWWAuthenticate = "Basic realm=\"OpenJibo Launch Rules\", charset=\"UTF-8\"";
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "Invalid or missing admin password." });
            return;
        }

        await next(context);
    }

    private bool TryAuthorize(HttpRequest request)
    {
        if (!AuthenticationHeaderValue.TryParse(request.Headers.Authorization, out var header))
            return false;

        if (!string.Equals(header.Scheme, "Basic", StringComparison.OrdinalIgnoreCase))
            return false;

        string decoded;
        try
        {
            decoded = Encoding.UTF8.GetString(Convert.FromBase64String(header.Parameter ?? string.Empty));
        }
        catch (FormatException)
        {
            return false;
        }

        var separatorIndex = decoded.IndexOf(':');
        if (separatorIndex < 0) return false;

        var password = decoded[(separatorIndex + 1)..];
        return FixedTimeEquals(password, _options.AdminPassword!);
    }

    private static bool RequiresAuth(PathString path)
    {
        var value = path.Value ?? string.Empty;
        if (value.Equals("/launch-rules.html", StringComparison.OrdinalIgnoreCase)) return true;
        return value.StartsWith("/api/admin/launch-rules", StringComparison.OrdinalIgnoreCase);
    }

    private static bool FixedTimeEquals(string left, string right)
    {
        var leftBytes = Encoding.UTF8.GetBytes(left);
        var rightBytes = Encoding.UTF8.GetBytes(right);
        return leftBytes.Length == rightBytes.Length &&
               CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }
}
