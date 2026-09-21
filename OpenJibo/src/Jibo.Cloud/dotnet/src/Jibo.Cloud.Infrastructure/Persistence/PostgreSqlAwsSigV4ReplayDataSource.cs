using Npgsql;

namespace Jibo.Cloud.Infrastructure.Persistence;

/// <summary>
/// Owns the dedicated, tightly bounded PostgreSQL pool used only for replay observations.
/// This must not use the broader cloud-state runtime credential.
/// </summary>
public sealed class PostgreSqlAwsSigV4ReplayDataSource : IDisposable, IAsyncDisposable
{
    internal NpgsqlDataSource Value { get; }

    public PostgreSqlAwsSigV4ReplayDataSource(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("A replay-observation PostgreSQL connection string is required.",
                nameof(connectionString));

        var builder = new NpgsqlConnectionStringBuilder(connectionString)
        {
            MaxPoolSize = 1,
            ApplicationName = "OpenJibo.SigV4ReplayObserver"
        };
        var dataSourceBuilder = new NpgsqlDataSourceBuilder(builder.ConnectionString)
        {
            Name = "sigv4_replay_observer"
        };
        Value = dataSourceBuilder.Build();
    }

    public void Dispose() => Value.Dispose();
    public ValueTask DisposeAsync() => Value.DisposeAsync();
}
