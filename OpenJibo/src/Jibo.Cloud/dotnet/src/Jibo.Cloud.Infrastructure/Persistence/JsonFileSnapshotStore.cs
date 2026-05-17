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

        File.WriteAllText(persistencePath, JsonSerializer.Serialize(snapshot, options));
    }
}