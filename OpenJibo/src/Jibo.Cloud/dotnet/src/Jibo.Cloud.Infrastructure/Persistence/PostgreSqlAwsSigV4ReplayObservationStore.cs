using Jibo.Cloud.Application.Abstractions;
using Npgsql;
using NpgsqlTypes;

namespace Jibo.Cloud.Infrastructure.Persistence;

public sealed class PostgreSqlAwsSigV4ReplayObservationStore(PostgreSqlAwsSigV4ReplayDataSource dataSource)
    : IAwsSigV4ReplayObservationStore
{
    public async Task<AwsSigV4ReplayObservation> ObserveAsync(
        ReadOnlyMemory<byte> replayDigest,
        short keyVersion,
        string operation,
        CancellationToken cancellationToken = default)
    {
        if (replayDigest.Length != 32)
            throw new ArgumentException("Replay digest must contain exactly 32 bytes.", nameof(replayDigest));
        if (keyVersion < 1)
            throw new ArgumentOutOfRangeException(nameof(keyVersion));
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);

        await using var connection = await dataSource.Value.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            "SELECT WasReplay, ObservationCount, FirstSeenUtc, LastSeenUtc, ExpiresUtc " +
            "FROM public.ObserveAwsSigV4Replay(@digest, @keyVersion, @operation)", connection);
        command.Parameters.AddWithValue("digest", NpgsqlDbType.Bytea, replayDigest.ToArray());
        command.Parameters.AddWithValue("keyVersion", NpgsqlDbType.Smallint, keyVersion);
        command.Parameters.AddWithValue("operation", NpgsqlDbType.Text, operation.Trim());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw new InvalidOperationException("Replay observation did not return a result.");

        return new AwsSigV4ReplayObservation(
            reader.GetBoolean(0)
                ? AwsSigV4ReplayObservationStatus.Repeat
                : AwsSigV4ReplayObservationStatus.FirstSeen,
            reader.GetInt64(1),
            reader.GetFieldValue<DateTimeOffset>(2),
            reader.GetFieldValue<DateTimeOffset>(3),
            reader.GetFieldValue<DateTimeOffset>(4));
    }
}
