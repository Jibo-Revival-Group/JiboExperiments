using System.Security.Cryptography;
using System.Text.Json;
using Jibo.Cloud.Application.Abstractions;

namespace Jibo.Cloud.Infrastructure.Media;

internal sealed class FileMediaContentStore(string? directoryPath) : IMediaContentStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private string? DirectoryPath { get; } = string.IsNullOrWhiteSpace(directoryPath) ? null : directoryPath;

    public async Task StoreAsync(string path, string contentType, byte[] content,
        IReadOnlyDictionary<string, object?>? meta, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(DirectoryPath) || string.IsNullOrWhiteSpace(path)) return;

        var root = Path.GetFullPath(DirectoryPath);
        var relative = MediaPathHelper.GetRelativeStoragePath(path);
        var contentPath = Path.Combine(root, $"{relative}.bin");
        var metaPath = Path.Combine(root, $"{relative}.json");

        Directory.CreateDirectory(Path.GetDirectoryName(contentPath)!);
        await File.WriteAllBytesAsync(contentPath, content, cancellationToken);
        var manifestMeta = meta is null
            ? new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, object?>(meta, StringComparer.OrdinalIgnoreCase);
        manifestMeta["contentLength"] = content.Length;
        manifestMeta["contentSha256"] = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
        manifestMeta["storedUtc"] = DateTimeOffset.UtcNow;
        var payload = new
        {
            path,
            contentType,
            meta = manifestMeta
        };
        await File.WriteAllTextAsync(metaPath, JsonSerializer.Serialize(payload, JsonOptions), cancellationToken);
    }

    public async Task<MediaContentSnapshot?> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(DirectoryPath) || string.IsNullOrWhiteSpace(path)) return null;

        var root = Path.GetFullPath(DirectoryPath);
        var relative = MediaPathHelper.GetRelativeStoragePath(path);
        var contentPath = Path.Combine(root, $"{relative}.bin");
        var metaPath = Path.Combine(root, $"{relative}.json");
        if (!File.Exists(contentPath)) return null;

        var content = await File.ReadAllBytesAsync(contentPath, cancellationToken);
        var contentType = "application/octet-stream";
        IDictionary<string, object?> meta = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        if (!File.Exists(metaPath))
            return new MediaContentSnapshot
            {
                ContentType = contentType,
                Content = content,
                Meta = meta as IReadOnlyDictionary<string, object?> ?? new Dictionary<string, object?>(meta)
            };

        try
        {
            using var document = JsonDocument.Parse(await File.ReadAllTextAsync(metaPath, cancellationToken));
            var rootElement = document.RootElement;
            if (rootElement.TryGetProperty("contentType", out var type) &&
                type.ValueKind == JsonValueKind.String)
                contentType = type.GetString() ?? contentType;

            if (rootElement.TryGetProperty("meta", out var metaElement) &&
                metaElement.ValueKind == JsonValueKind.Object)
                meta = JsonSerializer.Deserialize<Dictionary<string, object?>>(metaElement.GetRawText(),
                           JsonOptions) ??
                       new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            // Keep binary content available even if the manifest is malformed.
        }

        return new MediaContentSnapshot
        {
            ContentType = contentType,
            Content = content,
            Meta = meta as IReadOnlyDictionary<string, object?> ?? new Dictionary<string, object?>(meta)
        };
    }

    public async Task<IReadOnlyList<MediaContentItem>> ListAsync(string prefix, int maxCount = 100,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(DirectoryPath) || !Directory.Exists(DirectoryPath)) return [];

        var normalizedPrefix = string.IsNullOrWhiteSpace(prefix)
            ? string.Empty
            : MediaPathHelper.GetRelativeStoragePath(prefix).Replace(Path.DirectorySeparatorChar, '/');
        var items = new List<MediaContentItem>();
        foreach (var metaPath in Directory.EnumerateFiles(DirectoryPath, "*.json", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (items.Count >= Math.Max(1, maxCount)) break;

            try
            {
                using var document = JsonDocument.Parse(await File.ReadAllTextAsync(metaPath, cancellationToken));
                var root = document.RootElement;
                var path = root.TryGetProperty("path", out var pathElement) ? pathElement.GetString() : null;
                if (string.IsNullOrWhiteSpace(path) ||
                    !MediaPathHelper.GetRelativeStoragePath(path).Replace(Path.DirectorySeparatorChar, '/')
                        .StartsWith(normalizedPrefix, StringComparison.OrdinalIgnoreCase))
                    continue;

                var contentType = root.TryGetProperty("contentType", out var typeElement)
                    ? typeElement.GetString() ?? "application/octet-stream"
                    : "application/octet-stream";
                var meta = root.TryGetProperty("meta", out var metaElement) && metaElement.ValueKind == JsonValueKind.Object
                    ? JsonSerializer.Deserialize<Dictionary<string, object?>>(metaElement.GetRawText(), JsonOptions) ?? []
                    : new Dictionary<string, object?>();
                items.Add(new MediaContentItem { Path = path, ContentType = contentType, Meta = meta });
            }
            catch (JsonException)
            {
                // Skip malformed manifests while keeping the remaining diagnostics visible.
            }
        }

        return items;
    }
}
