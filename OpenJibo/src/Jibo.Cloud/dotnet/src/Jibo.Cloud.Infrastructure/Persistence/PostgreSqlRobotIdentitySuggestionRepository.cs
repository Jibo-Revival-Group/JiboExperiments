using System.Text.Json;
using Jibo.Cloud.Application.Services;
using Npgsql;
using NpgsqlTypes;

namespace Jibo.Cloud.Infrastructure.Persistence;

public sealed class PostgreSqlRobotIdentitySuggestionRepository(PostgreSqlCloudStateDataSource dataSource)
    : IRobotIdentitySuggestionRepository
{
    public void Observe(string deviceId, string proposedRobotId, RobotIdentitySuggestionEvidence evidence)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(proposedRobotId);
        using var connection = dataSource.Value.OpenConnection();
        using var transaction = connection.BeginTransaction();
        using (var command = new NpgsqlCommand("""
                                                INSERT INTO RobotIdentitySuggestions
                                                    (ObservedDeviceId, ProposedRobotId, Evidence)
                                                VALUES (@deviceId, @proposedRobotId, @evidence)
                                                ON CONFLICT ((LOWER(ObservedDeviceId)), (LOWER(ProposedRobotId)))
                                                DO UPDATE SET
                                                    ObservationCount =
                                                        LEAST(RobotIdentitySuggestions.ObservationCount + 1, 2147483647),
                                                    LastObservedUtc = NOW(),
                                                    DismissedUtc = NULL,
                                                    Evidence = (
                                                        SELECT COALESCE(jsonb_agg(recent.item ORDER BY recent.ordinal),
                                                                        '[]'::jsonb)
                                                        FROM (
                                                            SELECT item, ordinal
                                                            FROM jsonb_array_elements(
                                                                RobotIdentitySuggestions.Evidence ||
                                                                EXCLUDED.Evidence)
                                                                WITH ORDINALITY AS entries(item, ordinal)
                                                            ORDER BY ordinal DESC
                                                            LIMIT 8
                                                        ) AS recent
                                                    )
                                                """, connection, transaction))
        {
            command.Parameters.AddWithValue("deviceId", deviceId.Trim());
            command.Parameters.AddWithValue("proposedRobotId", proposedRobotId.Trim());
            command.Parameters.AddWithValue("evidence", NpgsqlDbType.Jsonb,
                JsonSerializer.Serialize(new[] { evidence }));
            command.ExecuteNonQuery();
        }

        using (var prune = new NpgsqlCommand("""
                                              DELETE FROM RobotIdentitySuggestions
                                              WHERE LastObservedUtc < NOW() - INTERVAL '30 days'
                                                 OR (ObservedDeviceId, ProposedRobotId) IN (
                                                     SELECT ObservedDeviceId, ProposedRobotId
                                                     FROM (
                                                         SELECT ObservedDeviceId, ProposedRobotId,
                                                                ROW_NUMBER() OVER (
                                                                    PARTITION BY LOWER(ObservedDeviceId)
                                                                    ORDER BY ObservationCount DESC,
                                                                             LastObservedUtc DESC) AS rank
                                                         FROM RobotIdentitySuggestions
                                                     ) ranked
                                                     WHERE rank > 4
                                                 )
                                              """, connection, transaction))
            prune.ExecuteNonQuery();

        transaction.Commit();
    }

    public RobotIdentitySuggestionCandidate? GetBest(string deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId)) return null;
        using var connection = dataSource.Value.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
                              SELECT ProposedRobotId, ObservationCount, FirstObservedUtc,
                                     LastObservedUtc, Evidence
                              FROM RobotIdentitySuggestions
                              WHERE LOWER(ObservedDeviceId) = LOWER(@deviceId)
                                AND DismissedUtc IS NULL
                                AND LastObservedUtc >= NOW() - INTERVAL '30 days'
                              ORDER BY ObservationCount DESC, LastObservedUtc DESC
                              LIMIT 1
                              """;
        command.Parameters.AddWithValue("deviceId", deviceId.Trim());
        using var reader = command.ExecuteReader();
        if (!reader.Read()) return null;
        return new RobotIdentitySuggestionCandidate(
            reader.GetString(0),
            reader.GetInt32(1),
            reader.GetFieldValue<DateTimeOffset>(2),
            reader.GetFieldValue<DateTimeOffset>(3),
            DeserializeEvidence(reader.GetString(4)));
    }

    public void Dismiss(string deviceId, string? proposedRobotId = null)
    {
        if (string.IsNullOrWhiteSpace(deviceId)) return;
        using var connection = dataSource.Value.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
                              UPDATE RobotIdentitySuggestions
                              SET DismissedUtc = NOW()
                              WHERE LOWER(ObservedDeviceId) = LOWER(@deviceId)
                                AND (@proposedRobotId IS NULL OR
                                     LOWER(ProposedRobotId) = LOWER(@proposedRobotId))
                              """;
        command.Parameters.AddWithValue("deviceId", deviceId.Trim());
        command.Parameters.AddWithValue("proposedRobotId", NpgsqlDbType.Text,
            string.IsNullOrWhiteSpace(proposedRobotId) ? DBNull.Value : proposedRobotId.Trim());
        command.ExecuteNonQuery();
    }

    private static IReadOnlyList<RobotIdentitySuggestionEvidence> DeserializeEvidence(string json) =>
        JsonSerializer.Deserialize<RobotIdentitySuggestionEvidence[]>(json) ?? [];
}
