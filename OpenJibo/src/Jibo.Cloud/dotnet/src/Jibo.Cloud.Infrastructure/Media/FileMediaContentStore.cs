using System.Text.Json;
using Jibo.Cloud.Application.Abstractions;

namespace Jibo.Cloud.Infrastructure.Media;

internal sealed class FileMediaContentStore : IMediaContentStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public FileMediaContentStore(string? directoryPath)
    {
        DirectoryPath = string.IsNullOrWhiteSpace(directoryPath) ? null : directoryPath;
    }

    private string? DirectoryPath { get; }

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
        var payload = new
        {
            path,
            contentType,
            meta
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

        if (File.Exists(metaPath))
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
}