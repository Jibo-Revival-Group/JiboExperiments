using Jibo.Cloud.Domain.Models;
using Npgsql;
using NpgsqlTypes;

namespace Jibo.Cloud.Infrastructure.Persistence;

public sealed class PostgreSqlHolidayOverrideRepository(PostgreSqlCloudStateDataSource ds) : IHolidayOverrideRepository
{
    public async Task<IReadOnlyList<HolidayRecord>> ListAsync(string a, string l, int limit = 250, CancellationToken ct = default) { await using var c = await ds.Value.OpenConnectionAsync(ct); await using var q = c.CreateCommand(); q.CommandText = "SELECT h.HolidayId,h.EventId,h.Name,h.Category,h.Subcategory,h.LoopId,h.MemberId,h.IsEnabled,h.EventDate,h.EndDate,h.Source,h.CountryCode,h.CreatedUtc FROM HolidayOverrides h INNER JOIN Loops x ON x.LoopId=h.LoopId WHERE x.OwnerAccountId=@a AND h.LoopId=@l ORDER BY h.EventDate,h.HolidayId LIMIT @limit"; Ops.Scope(q, a, l); q.Parameters.AddWithValue("limit", Math.Clamp(limit, 1, 1000)); var z = new List<HolidayRecord>(); await using var r = await q.ExecuteReaderAsync(ct); while (await r.ReadAsync(ct)) z.Add(new HolidayRecord { Id = r.GetString(0), EventId = r.GetString(1), Name = r.GetString(2), Category = r.GetString(3), Subcategory = Ops.Text(r, 4), LoopId = r.GetString(5), MemberId = Ops.Text(r, 6), IsEnabled = r.GetBoolean(7), Date = r.GetFieldValue<DateOnly>(8), EndDate = Ops.Date(r, 9), Source = r.GetString(10), CountryCode = r.GetString(11), Created = r.GetFieldValue<DateTimeOffset>(12) }); return z; }
    public async Task<HolidayRecord> UpsertAsync(string a, HolidayRecord h, CancellationToken ct = default) { ArgumentNullException.ThrowIfNull(h); await using var c = await ds.Value.OpenConnectionAsync(ct); await using var t = await c.BeginTransactionAsync(ct); await using var q = new NpgsqlCommand("""
 INSERT INTO HolidayOverrides(HolidayId,EventId,Name,Category,Subcategory,LoopId,MemberId,IsEnabled,EventDate,EndDate,Source,CountryCode,CreatedUtc)
 SELECT @id,@event,@name,@category,@sub,@l,@member,@enabled,@date,@end,@source,@country,@created WHERE EXISTS(SELECT 1 FROM Loops WHERE LoopId=@l AND OwnerAccountId=@a)
 ON CONFLICT(HolidayId) DO UPDATE SET EventId=EXCLUDED.EventId,Name=EXCLUDED.Name,Category=EXCLUDED.Category,Subcategory=EXCLUDED.Subcategory,MemberId=EXCLUDED.MemberId,IsEnabled=EXCLUDED.IsEnabled,EventDate=EXCLUDED.EventDate,EndDate=EXCLUDED.EndDate,Source=EXCLUDED.Source,CountryCode=EXCLUDED.CountryCode WHERE HolidayOverrides.LoopId=EXCLUDED.LoopId
 """, c, t); Ops.Scope(q, a, h.LoopId); q.Parameters.AddWithValue("id", Ops.Req(h.Id)); q.Parameters.AddWithValue("event", h.EventId); q.Parameters.AddWithValue("name", h.Name); q.Parameters.AddWithValue("category", h.Category); Ops.Nullable(q, "sub", h.Subcategory); Ops.Nullable(q, "member", h.MemberId); q.Parameters.AddWithValue("enabled", h.IsEnabled); q.Parameters.AddWithValue("date", h.Date); Ops.NullableDate(q, "end", h.EndDate); q.Parameters.AddWithValue("source", h.Source); q.Parameters.AddWithValue("country", h.CountryCode); q.Parameters.AddWithValue("created", h.Created); await Ops.Mutate(q, c, t, ct); return h; }
    public Task<bool> DeleteAsync(string a, string l, string id, CancellationToken ct = default) => Ops.DeleteAsync(ds, "HolidayOverrides", "HolidayId", a, l, id, ct);
}

public sealed class PostgreSqlCommuteProfileRepository(PostgreSqlCloudStateDataSource ds) : ICommuteProfileRepository
{
    public async Task<IReadOnlyList<CommuteProfileRecord>> ListAsync(string a, string l, int limit = 250, CancellationToken ct = default) { await using var c = await ds.Value.OpenConnectionAsync(ct); await using var q = c.CreateCommand(); q.CommandText = "SELECT p.CommuteProfileId,p.LoopId,p.MemberId,p.IsEnabled,p.IsComplete,p.Mode,p.WorkHour,p.WorkMinute,p.OriginName,p.DestinationName,p.TypicalDurationMinutes,p.CreatedUtc,p.UpdatedUtc FROM CommuteProfiles p INNER JOIN Loops x ON x.LoopId=p.LoopId WHERE x.OwnerAccountId=@a AND p.LoopId=@l ORDER BY p.UpdatedUtc DESC LIMIT @limit"; Ops.Scope(q, a, l); q.Parameters.AddWithValue("limit", Math.Clamp(limit, 1, 1000)); var z = new List<CommuteProfileRecord>(); await using var r = await q.ExecuteReaderAsync(ct); while (await r.ReadAsync(ct)) z.Add(new CommuteProfileRecord { Id = r.GetString(0), LoopId = r.GetString(1), MemberId = Ops.Text(r, 2), IsEnabled = r.GetBoolean(3), IsComplete = r.GetBoolean(4), Mode = r.GetString(5), WorkHour = r.GetInt32(6), WorkMinute = r.GetInt32(7), OriginName = Ops.Text(r, 8), DestinationName = Ops.Text(r, 9), TypicalDurationMinutes = r.GetInt32(10), Created = r.GetFieldValue<DateTimeOffset>(11), Updated = r.GetFieldValue<DateTimeOffset>(12) }); return z; }
    public async Task<CommuteProfileRecord> UpsertAsync(string a, CommuteProfileRecord p, CancellationToken ct = default) { ArgumentNullException.ThrowIfNull(p); await using var c = await ds.Value.OpenConnectionAsync(ct); await using var t = await c.BeginTransactionAsync(ct); await using var q = new NpgsqlCommand("""
 INSERT INTO CommuteProfiles(CommuteProfileId,LoopId,MemberId,IsEnabled,IsComplete,Mode,WorkHour,WorkMinute,OriginName,DestinationName,TypicalDurationMinutes,CreatedUtc,UpdatedUtc)
 SELECT @id,@l,@member,@enabled,@complete,@mode,@hour,@minute,@origin,@destination,@duration,@created,@updated WHERE EXISTS(SELECT 1 FROM Loops WHERE LoopId=@l AND OwnerAccountId=@a)
 ON CONFLICT(CommuteProfileId) DO UPDATE SET MemberId=EXCLUDED.MemberId,IsEnabled=EXCLUDED.IsEnabled,IsComplete=EXCLUDED.IsComplete,Mode=EXCLUDED.Mode,WorkHour=EXCLUDED.WorkHour,WorkMinute=EXCLUDED.WorkMinute,OriginName=EXCLUDED.OriginName,DestinationName=EXCLUDED.DestinationName,TypicalDurationMinutes=EXCLUDED.TypicalDurationMinutes,UpdatedUtc=EXCLUDED.UpdatedUtc WHERE CommuteProfiles.LoopId=EXCLUDED.LoopId
 """, c, t); Ops.Scope(q, a, p.LoopId); q.Parameters.AddWithValue("id", Ops.Req(p.Id)); Ops.Nullable(q, "member", p.MemberId); q.Parameters.AddWithValue("enabled", p.IsEnabled); q.Parameters.AddWithValue("complete", p.IsComplete); q.Parameters.AddWithValue("mode", p.Mode); q.Parameters.AddWithValue("hour", p.WorkHour); q.Parameters.AddWithValue("minute", p.WorkMinute); Ops.Nullable(q, "origin", p.OriginName); Ops.Nullable(q, "destination", p.DestinationName); q.Parameters.AddWithValue("duration", p.TypicalDurationMinutes); q.Parameters.AddWithValue("created", p.Created); q.Parameters.AddWithValue("updated", p.Updated); await Ops.Mutate(q, c, t, ct); return p; }
    public Task<bool> DeleteAsync(string a, string l, string id, CancellationToken ct = default) => Ops.DeleteAsync(ds, "CommuteProfiles", "CommuteProfileId", a, l, id, ct);
}

public sealed class PostgreSqlCalendarEventRepository(PostgreSqlCloudStateDataSource ds) : ICalendarEventRepository
{
    public async Task<IReadOnlyList<CalendarEventRecord>> ListAsync(string a, string l, DateOnly? from = null, DateOnly? to = null, int limit = 500, CancellationToken ct = default) { await using var c = await ds.Value.OpenConnectionAsync(ct); await using var q = c.CreateCommand(); q.CommandText = "SELECT e.CalendarEventId,e.LoopId,e.Summary,e.TimeLabel,e.EventDate,e.EndDate,e.IsAllDay,e.IsEnabled,e.Source,e.MemberId,e.CreatedUtc FROM CalendarEvents e INNER JOIN Loops x ON x.LoopId=e.LoopId WHERE x.OwnerAccountId=@a AND e.LoopId=@l AND (@from IS NULL OR e.EventDate>=@from) AND (@to IS NULL OR e.EventDate<=@to) ORDER BY e.EventDate,e.CalendarEventId LIMIT @limit"; Ops.Scope(q, a, l); Ops.NullableDate(q, "from", from); Ops.NullableDate(q, "to", to); q.Parameters.AddWithValue("limit", Math.Clamp(limit, 1, 2000)); var z = new List<CalendarEventRecord>(); await using var r = await q.ExecuteReaderAsync(ct); while (await r.ReadAsync(ct)) z.Add(new CalendarEventRecord { Id = r.GetString(0), LoopId = r.GetString(1), Summary = r.GetString(2), TimeLabel = Ops.Text(r, 3), Date = r.GetFieldValue<DateOnly>(4), EndDate = Ops.Date(r, 5), IsAllDay = r.GetBoolean(6), IsEnabled = r.GetBoolean(7), Source = r.GetString(8), MemberId = Ops.Text(r, 9), Created = r.GetFieldValue<DateTimeOffset>(10) }); return z; }
    public async Task<CalendarEventRecord> UpsertAsync(string a, CalendarEventRecord e, CancellationToken ct = default) { ArgumentNullException.ThrowIfNull(e); await using var c = await ds.Value.OpenConnectionAsync(ct); await using var t = await c.BeginTransactionAsync(ct); await using var q = new NpgsqlCommand("""
 INSERT INTO CalendarEvents(CalendarEventId,LoopId,Summary,TimeLabel,EventDate,EndDate,IsAllDay,IsEnabled,Source,MemberId,CreatedUtc,UpdatedUtc)
 SELECT @id,@l,@summary,@label,@date,@end,@allDay,@enabled,@source,@member,@created,NOW() WHERE EXISTS(SELECT 1 FROM Loops WHERE LoopId=@l AND OwnerAccountId=@a)
 ON CONFLICT(CalendarEventId) DO UPDATE SET Summary=EXCLUDED.Summary,TimeLabel=EXCLUDED.TimeLabel,EventDate=EXCLUDED.EventDate,EndDate=EXCLUDED.EndDate,IsAllDay=EXCLUDED.IsAllDay,IsEnabled=EXCLUDED.IsEnabled,Source=EXCLUDED.Source,MemberId=EXCLUDED.MemberId,UpdatedUtc=NOW() WHERE CalendarEvents.LoopId=EXCLUDED.LoopId
 """, c, t); Ops.Scope(q, a, e.LoopId); q.Parameters.AddWithValue("id", Ops.Req(e.Id)); q.Parameters.AddWithValue("summary", e.Summary); Ops.Nullable(q, "label", e.TimeLabel); q.Parameters.AddWithValue("date", e.Date); Ops.NullableDate(q, "end", e.EndDate); q.Parameters.AddWithValue("allDay", e.IsAllDay); q.Parameters.AddWithValue("enabled", e.IsEnabled); q.Parameters.AddWithValue("source", e.Source); Ops.Nullable(q, "member", e.MemberId); q.Parameters.AddWithValue("created", e.Created); await Ops.Mutate(q, c, t, ct); return e; }
    public Task<bool> DeleteAsync(string a, string l, string id, CancellationToken ct = default) => Ops.DeleteAsync(ds, "CalendarEvents", "CalendarEventId", a, l, id, ct);
}

public sealed class PostgreSqlGreetingPresenceRepository(PostgreSqlCloudStateDataSource ds) : IGreetingPresenceRepository
{
    public async Task<IReadOnlyList<GreetingPresenceRecord>> ListAsync(
        string accountId, string loopId, int limit = 250, CancellationToken ct = default)
    {
        await using var connection = await ds.Value.OpenConnectionAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT GreetingPresenceId,AccountId,LoopId,PersonId,SpeakerId,PreferredName,LastSeenUtc,
                LastGreetedUtc,LastGreetingRoute,LastGreetingIntent,CreatedUtc,UpdatedUtc,LastBirthdayGreetingDate
            FROM GreetingPresences WHERE AccountId=@a AND LoopId=@l ORDER BY LastSeenUtc DESC LIMIT @limit
            """;
        Ops.Scope(command, accountId, loopId);
        command.Parameters.AddWithValue("limit", Math.Clamp(limit, 1, 1000));
        var greetings = new List<GreetingPresenceRecord>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            greetings.Add(new GreetingPresenceRecord
            {
                Id = reader.GetString(0), AccountId = reader.GetString(1), LoopId = reader.GetString(2),
                PersonId = reader.GetString(3), SpeakerId = Ops.Text(reader, 4), PreferredName = Ops.Text(reader, 5),
                LastSeenUtc = reader.GetFieldValue<DateTimeOffset>(6), LastGreetedUtc = Ops.Time(reader, 7),
                LastGreetingRoute = Ops.Text(reader, 8), LastGreetingIntent = Ops.Text(reader, 9),
                CreatedUtc = reader.GetFieldValue<DateTimeOffset>(10), UpdatedUtc = reader.GetFieldValue<DateTimeOffset>(11),
                LastBirthdayGreetingDate = Ops.Date(reader, 12)
            });
        }

        return greetings;
    }

    public async Task<GreetingPresenceRecord> UpsertAsync(GreetingPresenceRecord presence, CancellationToken ct = default)
    {
        if (!await RecordAsync(presence, null, ct))
            throw new InvalidOperationException("Loop scope was not found.");
        return presence;
    }

    public Task<bool> TryRecordBirthdayAsync(GreetingPresenceRecord presence, CancellationToken ct = default) =>
        RecordAsync(presence, presence.LastBirthdayGreetingDate ??
            throw new ArgumentException("The local birthday greeting date is required.", nameof(presence)), ct);

    private async Task<bool> RecordAsync(GreetingPresenceRecord presence, DateOnly? birthdayDate, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(presence);
        await using var connection = await ds.Value.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await using var command = new NpgsqlCommand("""
            INSERT INTO GreetingPresences(GreetingPresenceId,AccountId,LoopId,PersonId,SpeakerId,PreferredName,
                LastSeenUtc,LastGreetedUtc,LastGreetingRoute,LastGreetingIntent,CreatedUtc,UpdatedUtc,LastBirthdayGreetingDate)
            SELECT @id,@a,@l,@person,@speaker,@preferred,@seen,@greeted,@route,@intent,@created,@updated,@birthday
            WHERE EXISTS(SELECT 1 FROM Loops WHERE LoopId=@l AND OwnerAccountId=@a)
            ON CONFLICT(LoopId,PersonId) DO UPDATE SET SpeakerId=EXCLUDED.SpeakerId,PreferredName=EXCLUDED.PreferredName,
                LastSeenUtc=GREATEST(EXCLUDED.LastSeenUtc,GreetingPresences.LastSeenUtc),
                LastGreetedUtc=EXCLUDED.LastGreetedUtc,LastGreetingRoute=EXCLUDED.LastGreetingRoute,
                LastGreetingIntent=EXCLUDED.LastGreetingIntent,UpdatedUtc=EXCLUDED.UpdatedUtc,
                LastBirthdayGreetingDate=COALESCE(EXCLUDED.LastBirthdayGreetingDate,GreetingPresences.LastBirthdayGreetingDate)
            WHERE GreetingPresences.AccountId=EXCLUDED.AccountId AND
                (@birthday IS NULL OR GreetingPresences.LastBirthdayGreetingDate IS NULL OR GreetingPresences.LastBirthdayGreetingDate < @birthday)
            """, connection, transaction);
        Ops.Scope(command, presence.AccountId, presence.LoopId);
        command.Parameters.AddWithValue("id", Ops.Req(presence.Id));
        command.Parameters.AddWithValue("person", Ops.Req(presence.PersonId));
        Ops.Nullable(command, "speaker", presence.SpeakerId);
        Ops.Nullable(command, "preferred", presence.PreferredName);
        command.Parameters.AddWithValue("seen", presence.LastSeenUtc);
        Ops.NullableTime(command, "greeted", presence.LastGreetedUtc);
        Ops.Nullable(command, "route", presence.LastGreetingRoute);
        Ops.Nullable(command, "intent", presence.LastGreetingIntent);
        command.Parameters.AddWithValue("created", presence.CreatedUtc);
        command.Parameters.AddWithValue("updated", presence.UpdatedUtc);
        Ops.NullableDate(command, "birthday", birthdayDate);
        if (await command.ExecuteNonQueryAsync(ct) == 0) return false;
        await CloudStateRevision.BumpAsync(connection, transaction, ct);
        await transaction.CommitAsync(ct);
        return true;
    }

    public async Task RecordInteractionAsync(GreetingPresenceRecord presence, DateTimeOffset windowStartUtc,
        CancellationToken ct = default)
    {
        await using var connection = await ds.Value.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await using var command = new NpgsqlCommand("""
            INSERT INTO GreetingPresences(GreetingPresenceId,AccountId,LoopId,PersonId,LastSeenUtc)
            SELECT @id,@a,@l,@person,@seen WHERE EXISTS(SELECT 1 FROM Loops WHERE LoopId=@l AND OwnerAccountId=@a)
            ON CONFLICT(LoopId,PersonId) DO UPDATE SET LastSeenUtc=EXCLUDED.LastSeenUtc,UpdatedUtc=NOW()
            WHERE GreetingPresences.AccountId=EXCLUDED.AccountId AND GreetingPresences.LastSeenUtc < @window
                AND GreetingPresences.LastSeenUtc < EXCLUDED.LastSeenUtc
            """, connection, transaction);
        Ops.Scope(command, presence.AccountId, presence.LoopId);
        command.Parameters.AddWithValue("id", Ops.Req(presence.Id));
        command.Parameters.AddWithValue("person", Ops.Req(presence.PersonId));
        command.Parameters.AddWithValue("seen", presence.LastSeenUtc.ToUniversalTime());
        command.Parameters.AddWithValue("window", windowStartUtc.ToUniversalTime());
        if (await command.ExecuteNonQueryAsync(ct) == 0) return;
        await CloudStateRevision.BumpAsync(connection, transaction, ct);
        await transaction.CommitAsync(ct);
    }

    public Task<bool> DeleteAsync(string accountId, string loopId, string id, CancellationToken ct = default) =>
        Ops.DeleteAsync(ds, "GreetingPresences", "GreetingPresenceId", accountId, loopId, id, ct, true);
}

internal static class Ops
{
    internal static void Scope(NpgsqlCommand q, string a, string l) { q.Parameters.AddWithValue("a", Req(a)); q.Parameters.AddWithValue("l", Req(l)); }
    internal static string Req(string v) => !string.IsNullOrWhiteSpace(v) ? v.Trim() : throw new ArgumentException("Value is required.");
    internal static void Nullable(NpgsqlCommand q, string n, string? v) => q.Parameters.Add(n, NpgsqlDbType.Text).Value = (object?)(string.IsNullOrWhiteSpace(v) ? null : v.Trim()) ?? DBNull.Value;
    internal static void NullableDate(NpgsqlCommand q, string n, DateOnly? v) => q.Parameters.Add(n, NpgsqlDbType.Date).Value = (object?)v ?? DBNull.Value;
    internal static void NullableTime(NpgsqlCommand q, string n, DateTimeOffset? v) => q.Parameters.Add(n, NpgsqlDbType.TimestampTz).Value = (object?)v ?? DBNull.Value;
    internal static string? Text(NpgsqlDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i); internal static DateOnly? Date(NpgsqlDataReader r, int i) => r.IsDBNull(i) ? null : r.GetFieldValue<DateOnly>(i); internal static DateTimeOffset? Time(NpgsqlDataReader r, int i) => r.IsDBNull(i) ? null : r.GetFieldValue<DateTimeOffset>(i);
    internal static async Task Mutate(NpgsqlCommand q, NpgsqlConnection c, NpgsqlTransaction t, CancellationToken ct) { if (await q.ExecuteNonQueryAsync(ct) == 0) throw new InvalidOperationException("Loop scope was not found."); await CloudStateRevision.BumpAsync(c, t, ct); await t.CommitAsync(ct); }
    internal static async Task<bool> DeleteAsync(PostgreSqlCloudStateDataSource ds, string table, string idColumn, string a, string l, string id, CancellationToken ct, bool accountColumn = false) { await using var c = await ds.Value.OpenConnectionAsync(ct); await using var t = await c.BeginTransactionAsync(ct); await using var q = new NpgsqlCommand(accountColumn ? $"DELETE FROM {table} WHERE AccountId=@a AND LoopId=@l AND {idColumn}=@id" : $"DELETE FROM {table} x USING Loops o WHERE o.LoopId=x.LoopId AND o.OwnerAccountId=@a AND x.LoopId=@l AND x.{idColumn}=@id", c, t); Scope(q, a, l); q.Parameters.AddWithValue("id", Req(id)); var ok = await q.ExecuteNonQueryAsync(ct) > 0; if (ok) await CloudStateRevision.BumpAsync(c, t, ct); await t.CommitAsync(ct); return ok; }
}
