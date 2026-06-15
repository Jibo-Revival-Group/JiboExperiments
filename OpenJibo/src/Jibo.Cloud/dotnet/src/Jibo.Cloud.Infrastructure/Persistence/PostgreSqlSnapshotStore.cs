using System.Text.Json;
using Npgsql;

namespace Jibo.Cloud.Infrastructure.Persistence;

internal sealed class PostgreSqlSnapshotStore(string connectionString, string snapshotName) : ISnapshotStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public TSnapshot? Load<TSnapshot>() where TSnapshot : class
    {
        using var connection = new NpgsqlConnection(connectionString);
        connection.Open();
        EnsureTable(connection);

        using var command = connection.CreateCommand();
        command.CommandText = """
                              SELECT SnapshotJson
                              FROM PersistenceSnapshots
                              WHERE SnapshotName = @name
                              """;
        command.Parameters.AddWithValue("@name", snapshotName);

        var result = command.ExecuteScalar();
        if (result is not string json || string.IsNullOrWhiteSpace(json)) return null;

        try
        {
            return JsonSerializer.Deserialize<TSnapshot>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    public void Save<TSnapshot>(TSnapshot snapshot) where TSnapshot : class
    {
        using var connection = new NpgsqlConnection(connectionString);
        connection.Open();
        EnsureTable(connection);

        var json = JsonSerializer.Serialize(snapshot, JsonOptions);

        using var command = connection.CreateCommand();
        command.CommandText = """
                              INSERT INTO PersistenceSnapshots (SnapshotName, SnapshotJson, UpdatedUtc)
                              VALUES (@name, @json, @updated)
                              ON CONFLICT (SnapshotName) DO UPDATE SET
                                  SnapshotJson = EXCLUDED.SnapshotJson,
                                  UpdatedUtc = EXCLUDED.UpdatedUtc
                              """;
        command.Parameters.AddWithValue("@name", snapshotName);
        command.Parameters.AddWithValue("@json", json);
        command.Parameters.AddWithValue("@updated", DateTimeOffset.UtcNow);
        command.ExecuteNonQuery();
    }

    private static void EnsureTable(NpgsqlConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
                              CREATE TABLE IF NOT EXISTS PersistenceSnapshots (
                                  SnapshotName TEXT NOT NULL PRIMARY KEY,
                                  SnapshotJson TEXT NOT NULL,
                                  CreatedUtc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                                  UpdatedUtc TIMESTAMPTZ NOT NULL DEFAULT NOW()
                              )
                              """;
        command.ExecuteNonQuery();
    }
}