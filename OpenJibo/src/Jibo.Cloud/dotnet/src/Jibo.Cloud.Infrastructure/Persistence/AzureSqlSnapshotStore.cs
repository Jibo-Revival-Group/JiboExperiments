using System.Data;
using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace Jibo.Cloud.Infrastructure.Persistence;

internal sealed class AzureSqlSnapshotStore(
    string connectionString,
    string snapshotName,
    string tableName = "PersistenceSnapshots")
    : ISnapshotStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _connectionString = string.IsNullOrWhiteSpace(connectionString)
        ? throw new InvalidOperationException("Azure SQL persistence requires a connection string.")
        : connectionString;

    private readonly string _snapshotName = string.IsNullOrWhiteSpace(snapshotName)
        ? throw new ArgumentException("A snapshot name is required for Azure SQL persistence.",
            nameof(snapshotName))
        : snapshotName;

    private readonly string _tableName = string.IsNullOrWhiteSpace(tableName) ? "PersistenceSnapshots" : tableName;

    public TSnapshot? Load<TSnapshot>() where TSnapshot : class
    {
        using var connection = new SqlConnection(_connectionString);
        connection.Open();
        EnsureTable(connection);

        using var command = connection.CreateCommand();
        command.CommandText = $"""
                               SELECT SnapshotJson
                               FROM dbo.[{_tableName}]
                               WHERE SnapshotName = @snapshotName
                               """;
        command.Parameters.Add(new SqlParameter("@snapshotName", SqlDbType.NVarChar, 200) { Value = _snapshotName });

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
        using var connection = new SqlConnection(_connectionString);
        connection.Open();
        EnsureTable(connection);

        var json = JsonSerializer.Serialize(snapshot, JsonOptions);

        using var command = connection.CreateCommand();
        command.CommandText = $"""
                               MERGE dbo.[{_tableName}] AS target
                               USING (SELECT @snapshotName AS SnapshotName) AS source
                               ON target.SnapshotName = source.SnapshotName
                               WHEN MATCHED THEN
                                   UPDATE SET SnapshotJson = @snapshotJson,
                                              UpdatedUtc = SYSUTCDATETIME()
                               WHEN NOT MATCHED THEN
                                   INSERT (SnapshotName, SnapshotJson, CreatedUtc, UpdatedUtc)
                                   VALUES (@snapshotName, @snapshotJson, SYSUTCDATETIME(), SYSUTCDATETIME());
                               """;
        command.Parameters.Add(new SqlParameter("@snapshotName", SqlDbType.NVarChar, 200) { Value = _snapshotName });
        command.Parameters.Add(new SqlParameter("@snapshotJson", SqlDbType.NVarChar, -1) { Value = json });
        command.ExecuteNonQuery();
    }

    private void EnsureTable(SqlConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"""
                               IF OBJECT_ID(N'dbo.[{_tableName}]', N'U') IS NULL
                               BEGIN
                                   CREATE TABLE dbo.[{_tableName}] (
                                       SnapshotName nvarchar(200) NOT NULL CONSTRAINT PK_{_tableName}_SnapshotName PRIMARY KEY,
                                       SnapshotJson nvarchar(max) NOT NULL,
                                       CreatedUtc datetimeoffset NOT NULL,
                                       UpdatedUtc datetimeoffset NOT NULL
                                   );
                               END
                               """;
        command.ExecuteNonQuery();
    }
}