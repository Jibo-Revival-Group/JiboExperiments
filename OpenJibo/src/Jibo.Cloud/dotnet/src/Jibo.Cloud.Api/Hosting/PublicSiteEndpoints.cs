namespace Jibo.Cloud.Api.Hosting;

internal static class PublicSiteEndpoints
{
    private static readonly string[] KnownFiles =
    [
        "index.html",
        "launch-rules.html",
        "launch-rules.js",
        "site.css"
    ];

    public static void Map(WebApplication app, string siteRoot)
    {
        if (!Directory.Exists(siteRoot))
            return;

        foreach (var fileName in KnownFiles)
        {
            var filePath = Path.Combine(siteRoot, fileName);
            if (!File.Exists(filePath))
                continue;

            var contentType = GetContentType(fileName);
            app.MapGet($"/{fileName}", () => Results.File(filePath, contentType));
        }

        var indexPath = Path.Combine(siteRoot, "index.html");
        if (File.Exists(indexPath))
            app.MapGet("/", () => Results.File(indexPath, "text/html"));
    }

    private static string GetContentType(string fileName)
    {
        return Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".html" or ".htm" => "text/html; charset=utf-8",
            ".js" => "text/javascript; charset=utf-8",
            ".css" => "text/css; charset=utf-8",
            _ => "application/octet-stream"
        };
    }
}
