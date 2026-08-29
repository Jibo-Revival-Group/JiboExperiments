using Npgsql;

namespace Jibo.Cloud.Infrastructure.Persistence;

/// <summary>
/// Owns the one explicitly bounded Npgsql pool shared by the normalized cloud-state repositories.
/// </summary>
public sealed class PostgreSqlCloudStateDataSource : IDisposable, IAsyncDisposable
{
    internal NpgsqlDataSource Value { get; }

    public PostgreSqlCloudStateDataSource(string connectionString, int maxPoolSize = 8)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("A PostgreSQL connection string is required.", nameof(connectionString));

        var builder = new NpgsqlConnectionStringBuilder(connectionString)
        {
            MaxPoolSize = Math.Max(1, maxPoolSize),
            ApplicationName = "OpenJibo.CloudState"
        };
        var dataSourceBuilder = new NpgsqlDataSourceBuilder(builder.ConnectionString)
        {
            Name = "cloud_state"
        };
        Value = dataSourceBuilder.Build();
    }

    public void Dispose() => Value.Dispose();
    public ValueTask DisposeAsync() => Value.DisposeAsync();
}
