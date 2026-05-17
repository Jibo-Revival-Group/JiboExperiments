using System.Text.Json;

namespace Jibo.Cloud.Infrastructure.Persistence;

internal sealed class JsonFileSnapshotStore : ISnapshotStore
{
    private readonly string? _persistencePath;
    private readonly JsonSerializerOptions _options;

    public JsonFileSnapshotStore(string? persistencePath, JsonSerializerOptions options)
    {
        _persistencePath = persistencePath;
        _options = options;
    }

    public TSnapshot? Load<TSnapshot>() where TSnapshot : class
    {
        if (string.IsNullOrWhiteSpace(_persistencePath) || !File.Exists(_persistencePath))
        {
            return default;
        }

        try
        {
            return JsonSerializer.Deserialize<TSnapshot>(File.ReadAllText(_persistencePath), _options);
        }
        catch
        {
            return default;
        }
    }

    public void Save<TSnapshot>(TSnapshot snapshot) where TSnapshot : class
    {
        if (string.IsNullOrWhiteSpace(_persistencePath))
        {
            return;
        }

        var directory = Path.GetDirectoryName(_persistencePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(_persistencePath, JsonSerializer.Serialize(snapshot, _options));
    }
}
