namespace Jibo.Cloud.Api.Hosting;

internal static class PortalStaticFileMapper
{
    internal static void MapPortalStaticFiles(this WebApplication app)
    {
        var portalDirectory = ResolveStaticDirectory(app.Environment.WebRootPath, app.Environment.ContentRootPath, "portal", "index.html");
        var harnessDirectory = ResolveStaticDirectory(app.Environment.WebRootPath, app.Environment.ContentRootPath, "harness", "index.html");

        app.MapGet("/portal", () => Results.Redirect("/portal/index.html"));
        app.MapGet("/portal.html", () => Results.Redirect("/portal/index.html"));
        app.MapGet("/portal/index.html", () => Serve(portalDirectory, "index.html", "text/html; charset=utf-8"));
        app.MapGet("/portal/portal.css", () => Serve(portalDirectory, "portal.css", "text/css; charset=utf-8"));
        app.MapGet("/portal/portal.js",
            () => Serve(portalDirectory, "portal.js", "application/javascript; charset=utf-8"));
        app.MapGet("/portal/status", () => Results.Redirect("/portal/status/index.html"));
        app.MapGet("/portal/status.html", () => Results.Redirect("/portal/status/index.html"));
        app.MapGet("/portal/status/index.html", () => Serve(portalDirectory, "status/index.html", "text/html; charset=utf-8"));
        app.MapGet("/portal/status/status.css", () => Serve(portalDirectory, "status/status.css", "text/css; charset=utf-8"));
        app.MapGet("/portal/status/status.js",
            () => Serve(portalDirectory, "status/status.js", "application/javascript; charset=utf-8"));
        app.MapGet("/portal/admin/onboarding", () => Results.Redirect("/portal/admin/onboarding/index.html"));
        app.MapGet("/portal/admin/onboarding/index.html",
            () => Serve(portalDirectory, "admin/onboarding/index.html", "text/html; charset=utf-8"));
        app.MapGet("/portal/admin/onboarding/onboarding.js",
            () => Serve(portalDirectory, "admin/onboarding/onboarding.js", "application/javascript; charset=utf-8"));
        app.MapGet("/portal/admin/harness", () => Results.Redirect("/portal/admin/harness/index.html"));
        app.MapGet("/portal/admin/harness/index.html", () => Serve(harnessDirectory, "index.html", "text/html; charset=utf-8"));
        app.MapGet("/portal/lrd", () => Results.Redirect("/portal/lrd/index.html"));
        app.MapGet("/portal/lrd/index.html", () => Serve(portalDirectory, "lrd/index.html", "text/html; charset=utf-8"));
        app.MapGet("/portal/lrd/lrd.css", () => Serve(portalDirectory, "lrd/lrd.css", "text/css; charset=utf-8"));
        app.MapGet("/portal/lrd/lrd.js",
            () => Serve(portalDirectory, "lrd/lrd.js", "application/javascript; charset=utf-8"));

        app.MapGet("/harness", () => Results.Redirect("/portal/admin/harness/index.html"));
        app.MapGet("/harness.html", () => Results.Redirect("/portal/admin/harness/index.html"));
        app.MapGet("/harness/index.html", () => Results.Redirect("/portal/admin/harness/index.html"));
        app.MapGet("/harness/harness.css", () => Serve(harnessDirectory, "harness.css", "text/css; charset=utf-8"));
        app.MapGet("/harness/harness.js",
            () => Serve(harnessDirectory, "harness.js", "application/javascript; charset=utf-8"));
    }

    internal static bool IsPortalPath(PathString path)
    {
        return path.StartsWithSegments("/portal", StringComparison.OrdinalIgnoreCase) ||
               path.StartsWithSegments("/api/portal", StringComparison.OrdinalIgnoreCase) ||
               path.StartsWithSegments("/harness", StringComparison.OrdinalIgnoreCase);
    }

    private static IResult Serve(string portalDirectory, string fileName, string contentType)
    {
        var filePath = Path.Combine(portalDirectory, fileName);
        if (!File.Exists(filePath))
            return Results.NotFound($"Portal asset '{fileName}' was not found.");

        return Results.File(filePath, contentType);
    }

    private static string ResolveStaticDirectory(string? webRootPath, string? contentRootPath, string folderName, string requiredFileName)
    {
        var candidates = new[]
        {
            CombineIfAvailable(webRootPath, folderName),
            Path.Combine(AppContext.BaseDirectory, "wwwroot", folderName),
            CombineIfAvailable(contentRootPath, "wwwroot", folderName)
        };

        foreach (var candidate in candidates)
            if (!string.IsNullOrWhiteSpace(candidate) &&
                Directory.Exists(candidate) &&
                File.Exists(Path.Combine(candidate, requiredFileName)))
                return candidate;

        return candidates.FirstOrDefault(candidate => !string.IsNullOrWhiteSpace(candidate))
               ?? Path.Combine(AppContext.BaseDirectory, "wwwroot", folderName);
    }

    private static string ResolvePortalDirectory(string? webRootPath, string? contentRootPath)
    {
        return ResolveStaticDirectory(webRootPath, contentRootPath, "portal", "index.html");
    }

    private static string? CombineIfAvailable(string? first, params string[] parts)
    {
        if (string.IsNullOrWhiteSpace(first))
            return null;

        return Path.Combine(first, Path.Combine(parts));
    }
}
