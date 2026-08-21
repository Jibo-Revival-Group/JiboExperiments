using Jibo.Cloud.Domain.Models;
using Npgsql;
using NpgsqlTypes;

namespace Jibo.Cloud.Infrastructure.Persistence;

public sealed class PostgreSqlLoopMemberRepository(PostgreSqlCloudStateDataSource dataSource) : ILoopMemberRepository
{
    private const string Columns = "m.MemberId, m.LoopId, m.AccountId, m.Email, m.FirstName, m.LastName, m.Gender, " +
        "m.Birthday, m.IsChild, m.PhoneNumber, m.Status, m.MemberType, m.Nickname, m.PhoneticName, " +
        "m.FaceEnrolled, m.VoiceEnrolled, m.LegalGuardianId, m.AgreementId, m.CreatedUtc, m.PortalEditedUtc";

    public async Task<IReadOnlyList<LoopMemberRecord>> ListAsync(string accountId, string loopId, int limit = 250,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.Value.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT {Columns} FROM LoopMembers m INNER JOIN Loops l ON l.LoopId = m.LoopId
            WHERE l.OwnerAccountId = @account AND m.LoopId = @loop
            ORDER BY m.CreatedUtc, m.MemberId LIMIT @limit
            """;
        AddScope(command, accountId, loopId); command.Parameters.AddWithValue("limit", Math.Clamp(limit, 1, 1000));
        var result = new List<LoopMemberRecord>(); await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) result.Add(Map(reader)); return result;
    }

    public async Task<LoopMemberRecord?> GetAsync(string accountId, string loopId, string memberId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.Value.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT {Columns} FROM LoopMembers m INNER JOIN Loops l ON l.LoopId = m.LoopId
            WHERE l.OwnerAccountId = @account AND m.LoopId = @loop AND m.MemberId = @member
            """;
        AddScope(command, accountId, loopId); command.Parameters.AddWithValue("member", Require(memberId, nameof(memberId)));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Map(reader) : null;
    }

    public async Task<LoopMemberRecord> UpsertAsync(string accountId, LoopMemberRecord member,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(member); Require(member.Id, nameof(member.Id)); Require(member.LoopId, nameof(member.LoopId));
        await using var connection = await dataSource.Value.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            INSERT INTO LoopMembers (MemberId, LoopId, AccountId, Email, FirstName, LastName, Gender, Birthday,
                IsChild, PhoneNumber, Status, MemberType, Nickname, PhoneticName, FaceEnrolled, VoiceEnrolled,
                LegalGuardianId, AgreementId, CreatedUtc, UpdatedUtc, PortalEditedUtc)
            SELECT @id, @loop, @memberAccount, @email, @first, @last, @gender, @birthday, @child, @phone,
                @status, @type, @nickname, @phonetic, @face, @voice, @guardian, @agreement, @created, NOW(), @portal
            WHERE EXISTS (SELECT 1 FROM Loops WHERE LoopId = @loop AND OwnerAccountId = @owner)
            ON CONFLICT (MemberId) DO UPDATE SET AccountId = EXCLUDED.AccountId, Email = EXCLUDED.Email,
                FirstName = EXCLUDED.FirstName, LastName = EXCLUDED.LastName, Gender = EXCLUDED.Gender,
                Birthday = EXCLUDED.Birthday, IsChild = EXCLUDED.IsChild, PhoneNumber = EXCLUDED.PhoneNumber,
                Status = EXCLUDED.Status, MemberType = EXCLUDED.MemberType, Nickname = EXCLUDED.Nickname,
                PhoneticName = EXCLUDED.PhoneticName, FaceEnrolled = EXCLUDED.FaceEnrolled,
                VoiceEnrolled = EXCLUDED.VoiceEnrolled, LegalGuardianId = EXCLUDED.LegalGuardianId,
                AgreementId = EXCLUDED.AgreementId, UpdatedUtc = NOW(), PortalEditedUtc = EXCLUDED.PortalEditedUtc
            WHERE LoopMembers.LoopId = EXCLUDED.LoopId
            """, connection, transaction);
        command.Parameters.AddWithValue("id", member.Id.Trim()); command.Parameters.AddWithValue("loop", member.LoopId.Trim());
        command.Parameters.AddWithValue("owner", Require(accountId, nameof(accountId)));
        AddNullable(command, "memberAccount", member.AccountId); AddNullable(command, "email", member.Email);
        AddNullable(command, "first", member.FirstName); AddNullable(command, "last", member.LastName);
        AddNullable(command, "gender", member.Gender);
        command.Parameters.Add("birthday", NpgsqlDbType.Bigint).Value = (object?)member.Birthday ?? DBNull.Value;
        command.Parameters.AddWithValue("child", member.IsChild); AddNullable(command, "phone", member.PhoneNumber);
        command.Parameters.AddWithValue("status", member.Status); command.Parameters.AddWithValue("type", member.Type);
        AddNullable(command, "nickname", member.Nickname); AddNullable(command, "phonetic", member.PhoneticName);
        command.Parameters.AddWithValue("face", member.FaceEnrolled); command.Parameters.AddWithValue("voice", member.VoiceEnrolled);
        AddNullable(command, "guardian", member.LegalGuardianId); AddNullable(command, "agreement", member.AgreementId);
        command.Parameters.AddWithValue("created", member.CreatedUtc);
        command.Parameters.Add("portal", NpgsqlDbType.TimestampTz).Value =
            (object?)member.PortalEditedUtc ?? DBNull.Value;
        if (await command.ExecuteNonQueryAsync(cancellationToken) == 0) throw new InvalidOperationException("Loop scope was not found.");
        await CloudStateRevision.BumpAsync(connection, transaction, cancellationToken); await transaction.CommitAsync(cancellationToken);
        return member;
    }

    public async Task<bool> DeleteAsync(string accountId, string loopId, string memberId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.Value.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            DELETE FROM LoopMembers m USING Loops l
            WHERE l.LoopId = m.LoopId AND l.OwnerAccountId = @account AND m.LoopId = @loop AND m.MemberId = @member
            """, connection, transaction);
        AddScope(command, accountId, loopId); command.Parameters.AddWithValue("member", Require(memberId, nameof(memberId)));
        var removed = await command.ExecuteNonQueryAsync(cancellationToken) > 0;
        if (removed) await CloudStateRevision.BumpAsync(connection, transaction, cancellationToken);
        await transaction.CommitAsync(cancellationToken); return removed;
    }

    private static LoopMemberRecord Map(NpgsqlDataReader r) => new()
    {
        Id = r.GetString(0),
        LoopId = r.GetString(1),
        AccountId = Get(r, 2),
        Email = Get(r, 3),
        FirstName = Get(r, 4),
        LastName = Get(r, 5),
        Gender = Get(r, 6),
        Birthday = r.IsDBNull(7) ? null : r.GetInt64(7),
        IsChild = r.GetBoolean(8),
        PhoneNumber = Get(r, 9),
        Status = r.GetString(10),
        Type = r.GetString(11),
        Nickname = Get(r, 12),
        PhoneticName = Get(r, 13),
        FaceEnrolled = r.GetBoolean(14),
        VoiceEnrolled = r.GetBoolean(15),
        LegalGuardianId = Get(r, 16),
        AgreementId = Get(r, 17),
        CreatedUtc = r.GetFieldValue<DateTimeOffset>(18),
        PortalEditedUtc = r.IsDBNull(19) ? null : r.GetFieldValue<DateTimeOffset>(19)
    };
    private static void AddScope(NpgsqlCommand c, string account, string loop) { c.Parameters.AddWithValue("account", Require(account, nameof(account))); c.Parameters.AddWithValue("loop", Require(loop, nameof(loop))); }
    private static void AddNullable(NpgsqlCommand c, string name, string? value) =>
        c.Parameters.Add(name, NpgsqlDbType.Text).Value = (object?)(string.IsNullOrWhiteSpace(value) ? null : value.Trim()) ?? DBNull.Value;
    private static string? Get(NpgsqlDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);
    private static string Require(string value, string name) => !string.IsNullOrWhiteSpace(value) ? value.Trim() : throw new ArgumentException("Value is required.", name);
}
