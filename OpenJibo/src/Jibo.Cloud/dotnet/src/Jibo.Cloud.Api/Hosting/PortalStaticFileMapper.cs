namespace Jibo.Cloud.Api.Hosting;

internal static class PortalStaticFileMapper
{
    internal static void MapPortalStaticFiles(this WebApplication app)
    {
        var portalDirectory = ResolvePortalDirectory(app.Environment.WebRootPath, app.Environment.ContentRootPath);

        app.MapGet("/portal", () => Results.Redirect("/portal/index.html"));
        app.MapGet("/portal.html", () => Results.Redirect("/portal/index.html"));
        app.MapGet("/portal/index.html", () => Serve(portalDirectory, "index.html", "text/html; charset=utf-8"));
        app.MapGet("/portal/portal.css", () => Serve(portalDirectory, "portal.css", "text/css; charset=utf-8"));
        app.MapGet("/portal/portal.js",
            () => Serve(portalDirectory, "portal.js", "application/javascript; charset=utf-8"));
    }

    internal static bool IsPortalPath(PathString path)
    {
        return path.StartsWithSegments("/portal", StringComparison.OrdinalIgnoreCase) ||
               path.StartsWithSegments("/api/portal", StringComparison.OrdinalIgnoreCase);
    }

    private static IResult Serve(string portalDirectory, string fileName, string contentType)
    {
        var filePath = Path.Combine(portalDirectory, fileName);
        if (!File.Exists(filePath))
            return Results.NotFound($"Portal asset '{fileName}' was not found.");

        return Results.File(filePath, contentType);
    }

    private static string ResolvePortalDirectory(string? webRootPath, string? contentRootPath)
    {
        var candidates = new[]
        {
            CombineIfAvailable(webRootPath, "portal"),
            Path.Combine(AppContext.BaseDirectory, "wwwroot", "portal"),
            CombineIfAvailable(contentRootPath, "wwwroot", "portal")
        };

        foreach (var candidate in candidates)
            if (!string.IsNullOrWhiteSpace(candidate) &&
                Directory.Exists(candidate) &&
                File.Exists(Path.Combine(candidate, "index.html")))
                return candidate;

        return candidates.FirstOrDefault(candidate => !string.IsNullOrWhiteSpace(candidate))
               ?? Path.Combine(AppContext.BaseDirectory, "wwwroot", "portal");
    }

    private static string? CombineIfAvailable(string? first, params string[] parts)
    {
        if (string.IsNullOrWhiteSpace(first))
            return null;

        return Path.Combine(first, Path.Combine(parts));
    }
}