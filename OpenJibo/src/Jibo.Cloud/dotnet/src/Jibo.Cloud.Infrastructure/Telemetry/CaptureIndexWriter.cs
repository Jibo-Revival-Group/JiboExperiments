using System.Collections.Concurrent;
using System.Text.Json;

namespace Jibo.Cloud.Infrastructure.Telemetry;

internal static class CaptureIndexWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> DirectoryLocks =
        new(StringComparer.OrdinalIgnoreCase);

    public static async Task AppendAsync(
        string directoryPath,
        string sinkName,
        string eventType,
        IReadOnlyDictionary<string, object?> details,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(directoryPath)) return;

        var directory = Path.GetFullPath(directoryPath);
        Directory.CreateDirectory(directory);
        var indexPath = Path.Combine(directory, "capture-index.ndjson");
        var payload = new
        {
            capturedUtc = DateTimeOffset.UtcNow,
            sink = sinkName,
            eventType,
            details
        };

        var line = JsonSerializer.Serialize(payload, JsonOptions) + Environment.NewLine;
        var gate = DirectoryLocks.GetOrAdd(directory, static _ => new SemaphoreSlim(1, 1));

        await gate.WaitAsync(cancellationToken);
        try
        {
            await File.AppendAllTextAsync(indexPath, line, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }
}