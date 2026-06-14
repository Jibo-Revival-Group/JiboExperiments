using System.Text.Json;

namespace Jibo.Cloud.Infrastructure.Persistence;

internal sealed class JsonFileSnapshotStore(string? persistencePath, JsonSerializerOptions options) : ISnapshotStore
{
    public TSnapshot? Load<TSnapshot>() where TSnapshot : class
    {
        if (string.IsNullOrWhiteSpace(persistencePath) || !File.Exists(persistencePath)) return null;

        try
        {
            return JsonSerializer.Deserialize<TSnapshot>(File.ReadAllText(persistencePath), options);
        }
        catch
        {
            return null;
        }
    }

    public void Save<TSnapshot>(TSnapshot snapshot) where TSnapshot : class
    {
        if (string.IsNullOrWhiteSpace(persistencePath)) return;

        var directory = Path.GetDirectoryName(persistencePath);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);

        var tempPath = Path.Combine(
            string.IsNullOrWhiteSpace(directory) ? Directory.GetCurrentDirectory() : directory,
            $".{Path.GetFileName(persistencePath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            File.WriteAllText(tempPath, JsonSerializer.Serialize(snapshot, options));

            if (File.Exists(persistencePath))
                File.Replace(tempPath, persistencePath, null);
            else
                File.Move(tempPath, persistencePath);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }
}