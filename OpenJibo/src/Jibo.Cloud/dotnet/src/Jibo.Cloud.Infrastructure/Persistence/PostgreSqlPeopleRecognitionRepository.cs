using Jibo.Cloud.Domain.Models;
using Npgsql;
using NpgsqlTypes;

namespace Jibo.Cloud.Infrastructure.Persistence;

public sealed class PostgreSqlPersonRepository(PostgreSqlCloudStateDataSource dataSource) : IPersonRepository
{
    private const string Columns = "p.PersonId, p.AccountId, p.LoopId, p.RobotId, p.DisplayName, p.Alias, " +
                                   "p.IsPrimary, p.CreatedUtc, p.UpdatedUtc";
    public async Task<IReadOnlyList<PersonRecord>> ListAsync(string accountId, string loopId, int limit = 250,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.Value.OpenConnectionAsync(cancellationToken); await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {Columns} FROM People p WHERE p.AccountId=@account AND p.LoopId=@loop ORDER BY p.IsPrimary DESC,p.DisplayName,p.PersonId LIMIT @limit";
        Scope(command, accountId, loopId); command.Parameters.AddWithValue("limit", Math.Clamp(limit, 1, 1000)); var result = new List<PersonRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken); while (await reader.ReadAsync(cancellationToken)) result.Add(Map(reader)); return result;
    }
    public async Task<PersonRecord> UpsertAsync(PersonRecord person, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(person); Require(person.PersonId, nameof(person.PersonId)); Require(person.AccountId, nameof(person.AccountId)); Require(person.LoopId, nameof(person.LoopId));
        await using var connection = await dataSource.Value.OpenConnectionAsync(cancellationToken); await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            INSERT INTO People (PersonId,AccountId,LoopId,RobotId,DisplayName,Alias,IsPrimary,CreatedUtc,UpdatedUtc)
            SELECT @id,@account,@loop,@robot,@display,@alias,@primary,@created,@updated
            WHERE EXISTS (SELECT 1 FROM Loops WHERE LoopId=@loop AND OwnerAccountId=@account)
            ON CONFLICT (AccountId,LoopId,PersonId) DO UPDATE SET RobotId=EXCLUDED.RobotId,DisplayName=EXCLUDED.DisplayName,
                Alias=EXCLUDED.Alias,IsPrimary=EXCLUDED.IsPrimary,UpdatedUtc=EXCLUDED.UpdatedUtc
            """, connection, transaction);
        command.Parameters.AddWithValue("id", person.PersonId.Trim()); Scope(command, person.AccountId, person.LoopId);
        command.Parameters.AddWithValue("robot", person.RobotId); command.Parameters.AddWithValue("display", person.DisplayName);
        command.Parameters.Add("alias", NpgsqlDbType.Text).Value = (object?)person.Alias ?? DBNull.Value; command.Parameters.AddWithValue("primary", person.IsPrimary);
        command.Parameters.AddWithValue("created", person.CreatedUtc); command.Parameters.AddWithValue("updated", person.UpdatedUtc);
        if (await command.ExecuteNonQueryAsync(cancellationToken) == 0) throw new InvalidOperationException("Account/loop scope was not found.");
        await CloudStateRevision.BumpAsync(connection, transaction, cancellationToken); await transaction.CommitAsync(cancellationToken); return person;
    }
    public async Task<bool> DeleteAsync(string accountId, string loopId, string personId, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.Value.OpenConnectionAsync(cancellationToken); await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("DELETE FROM People WHERE AccountId=@account AND LoopId=@loop AND PersonId=@id", connection, transaction);
        Scope(command, accountId, loopId); command.Parameters.AddWithValue("id", Require(personId, nameof(personId))); var removed = await command.ExecuteNonQueryAsync(cancellationToken) > 0;
        if (removed) await CloudStateRevision.BumpAsync(connection, transaction, cancellationToken); await transaction.CommitAsync(cancellationToken); return removed;
    }
    private static PersonRecord Map(NpgsqlDataReader r) => new() { PersonId = r.GetString(0), AccountId = r.GetString(1), LoopId = r.GetString(2), RobotId = r.GetString(3), DisplayName = r.GetString(4), Alias = r.IsDBNull(5) ? null : r.GetString(5), IsPrimary = r.GetBoolean(6), CreatedUtc = r.GetFieldValue<DateTimeOffset>(7), UpdatedUtc = r.GetFieldValue<DateTimeOffset>(8) };
    private static void Scope(NpgsqlCommand c, string account, string loop) { c.Parameters.AddWithValue("account", Require(account, nameof(account))); c.Parameters.AddWithValue("loop", Require(loop, nameof(loop))); }
    private static string Require(string value, string name) => !string.IsNullOrWhiteSpace(value) ? value.Trim() : throw new ArgumentException("Value is required.", name);
}

public sealed class PostgreSqlRecognitionObservationRepository(PostgreSqlCloudStateDataSource dataSource)
    : IRecognitionObservationRepository
{
    public async Task<IReadOnlyList<RecognitionObservationRecord>> ListAsync(string accountId, string loopId, int limit = 250, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.Value.OpenConnectionAsync(cancellationToken); await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT o.ObservationId,o.LoopId,o.MemberId,o.RobotId,o.Modality,o.Outcome,o.Confidence,o.Source,o.ObservedUtc
            FROM RecognitionObservations o INNER JOIN Loops l ON l.LoopId=o.LoopId
            WHERE l.OwnerAccountId=@account AND o.LoopId=@loop ORDER BY o.ObservedUtc DESC,o.ObservationId LIMIT @limit
            """;
        Scope(command, accountId, loopId); command.Parameters.AddWithValue("limit", Math.Clamp(limit, 1, 1000)); var result = new List<RecognitionObservationRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken); while (await reader.ReadAsync(cancellationToken)) result.Add(Map(reader)); return result;
    }
    public async Task<IReadOnlyList<RecognitionObservationRecord>> ListAllForBackupAsync(string accountId,
        string loopId, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.Value.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT o.ObservationId,o.LoopId,o.MemberId,o.RobotId,o.Modality,o.Outcome,o.Confidence,o.Source,o.ObservedUtc
            FROM RecognitionObservations o INNER JOIN Loops l ON l.LoopId=o.LoopId
            WHERE l.OwnerAccountId=@account AND o.LoopId=@loop
            ORDER BY o.ObservedUtc,o.ObservationId
            """;
        Scope(command, accountId, loopId);
        var result = new List<RecognitionObservationRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) result.Add(Map(reader));
        return result;
    }
    public async Task<RecognitionObservationRecord> AddAsync(string accountId, RecognitionObservationRecord observation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(observation); Require(observation.ObservationId, nameof(observation.ObservationId)); Require(observation.LoopId, nameof(observation.LoopId)); Require(observation.MemberId, nameof(observation.MemberId));
        if (observation.Confidence is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(observation.Confidence));
        await using var connection = await dataSource.Value.OpenConnectionAsync(cancellationToken); await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            INSERT INTO RecognitionObservations (ObservationId,LoopId,MemberId,RobotId,Modality,Outcome,Confidence,Source,ObservedUtc)
            SELECT @id,@loop,@member,@robot,@modality,@outcome,@confidence,@source,@observed
            WHERE EXISTS (SELECT 1 FROM Loops l INNER JOIN LoopMembers m ON m.LoopId=l.LoopId
                          WHERE l.OwnerAccountId=@account AND l.LoopId=@loop AND m.MemberId=@member)
            ON CONFLICT (ObservationId) DO NOTHING
            """, connection, transaction);
        Scope(command, accountId, observation.LoopId); command.Parameters.AddWithValue("id", observation.ObservationId.Trim()); command.Parameters.AddWithValue("member", observation.MemberId.Trim());
        command.Parameters.AddWithValue("robot", observation.RobotId); command.Parameters.AddWithValue("modality", observation.Modality); command.Parameters.AddWithValue("outcome", observation.Outcome);
        command.Parameters.Add("confidence", NpgsqlDbType.Double).Value = (object?)observation.Confidence ?? DBNull.Value;
        command.Parameters.Add("source", NpgsqlDbType.Text).Value = (object?)observation.Source ?? DBNull.Value; command.Parameters.AddWithValue("observed", observation.ObservedUtc);
        if (await command.ExecuteNonQueryAsync(cancellationToken) == 0) throw new InvalidOperationException("Observation already exists or account/loop/member scope was not found.");
        await CloudStateRevision.BumpAsync(connection, transaction, cancellationToken); await transaction.CommitAsync(cancellationToken); return observation;
    }
    private static RecognitionObservationRecord Map(NpgsqlDataReader r) => new() { ObservationId = r.GetString(0), LoopId = r.GetString(1), MemberId = r.GetString(2), RobotId = r.GetString(3), Modality = r.GetString(4), Outcome = r.GetString(5), Confidence = r.IsDBNull(6) ? null : r.GetDouble(6), Source = r.IsDBNull(7) ? null : r.GetString(7), ObservedUtc = r.GetFieldValue<DateTimeOffset>(8) };
    private static void Scope(NpgsqlCommand c, string account, string loop) { c.Parameters.AddWithValue("account", Require(account, nameof(account))); c.Parameters.AddWithValue("loop", Require(loop, nameof(loop))); }
    private static string Require(string value, string name) => !string.IsNullOrWhiteSpace(value) ? value.Trim() : throw new ArgumentException("Value is required.", name);
}
