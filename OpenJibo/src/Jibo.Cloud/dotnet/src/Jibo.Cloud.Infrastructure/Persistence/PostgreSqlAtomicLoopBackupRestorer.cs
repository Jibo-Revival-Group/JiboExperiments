using Jibo.Cloud.Domain.Models;
using Npgsql;
using NpgsqlTypes;
using System.Text.Json;

namespace Jibo.Cloud.Infrastructure.Persistence;

internal interface IAtomicLoopBackupRestorer
{
    Task RestoreAsync(string accountId, BackupManifestRecord manifest, RelationalLoopBackup backup,
        DateTimeOffset restoredUtc, CancellationToken cancellationToken = default);
}

internal sealed class PostgreSqlAtomicLoopBackupRestorer(PostgreSqlCloudStateDataSource dataSource)
    : IAtomicLoopBackupRestorer
{
    public async Task RestoreAsync(string accountId, BackupManifestRecord manifest, RelationalLoopBackup backup,
        DateTimeOffset restoredUtc, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(backup);
        var account = Required(accountId, nameof(accountId));
        var loopId = Required(backup.Loop.LoopId, nameof(backup.Loop.LoopId));

        await using var connection = await dataSource.Value.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            await LockAndUpdateLoopAsync(connection, transaction, account, backup.Loop, cancellationToken);
            await DeleteScopedChildrenAsync(connection, transaction, account, loopId, cancellationToken);
            await ReplaceLoopDevicesAsync(connection, transaction, account, loopId, backup.Devices,
                cancellationToken);
            foreach (var member in backup.Members)
                await UpsertMemberAsync(connection, transaction, account, member, cancellationToken);
            foreach (var person in backup.People)
                await UpsertPersonAsync(connection, transaction, person, cancellationToken);
            foreach (var holiday in backup.Holidays)
                await UpsertHolidayAsync(connection, transaction, holiday, cancellationToken);
            foreach (var commute in backup.Commutes)
                await UpsertCommuteAsync(connection, transaction, commute, cancellationToken);
            foreach (var calendarEvent in backup.CalendarEvents)
                await UpsertCalendarAsync(connection, transaction, calendarEvent, cancellationToken);
            foreach (var greeting in backup.Greetings)
                await UpsertGreetingAsync(connection, transaction, greeting, cancellationToken);
            foreach (var observation in backup.RecognitionObservations)
                await InsertRecognitionAsync(connection, transaction, observation, cancellationToken);
            if (backup.LoopKey is not null)
                await InsertLoopKeyAsync(connection, transaction, backup.LoopKey, cancellationToken);
            foreach (var request in backup.KeyRequests)
                await InsertKeyRequestAsync(connection, transaction, request, cancellationToken);
            foreach (var media in backup.Media)
                await InsertMediaAsync(connection, transaction, media, cancellationToken);

            await using (var mark = new NpgsqlCommand("""
                UPDATE BackupManifests SET RestoredUtc=@restored, Status='restored'
                WHERE BackupId=@backup AND AccountId=@account AND LoopId=@loop
                """, connection, transaction))
            {
                mark.Parameters.AddWithValue("restored", restoredUtc);
                mark.Parameters.AddWithValue("backup", Required(manifest.BackupId, nameof(manifest.BackupId)));
                mark.Parameters.AddWithValue("account", account);
                mark.Parameters.AddWithValue("loop", loopId);
                if (await mark.ExecuteNonQueryAsync(cancellationToken) != 1)
                    throw new InvalidOperationException("The scoped backup manifest was not found.");
            }

            await CloudStateRevision.BumpAsync(connection, transaction, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private static async Task DeleteScopedChildrenAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        string accountId, string loopId, CancellationToken cancellationToken)
    {
        // Every family represented in RelationalLoopBackup is replaced. Tables not represented by the
        // loop-backup contract are deliberately left untouched.
        foreach (var sql in new[]
                 {
                     "DELETE FROM RecognitionObservations WHERE LoopId=@loop",
                     "DELETE FROM GreetingPresences WHERE AccountId=@account AND LoopId=@loop",
                     "DELETE FROM HolidayOverrides WHERE LoopId=@loop",
                     "DELETE FROM CommuteProfiles WHERE LoopId=@loop",
                     "DELETE FROM CalendarEvents WHERE LoopId=@loop",
                     "DELETE FROM People WHERE AccountId=@account AND LoopId=@loop",
                     "DELETE FROM LoopMembers WHERE LoopId=@loop",
                     "DELETE FROM LoopSymmetricKeys WHERE LoopId=@loop",
                     "DELETE FROM KeyRequests WHERE LoopId=@loop",
                     "DELETE FROM MediaRecords WHERE AccountId=@account AND LoopId=@loop"
                 })
            await ExecuteAsync(connection, transaction, sql, cancellationToken,
                ("account", accountId), ("loop", loopId));
    }

    private static async Task LockAndUpdateLoopAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        string accountId, LoopRecord loop, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            UPDATE Loops SET Name=@name, PrimaryRobotId=@robot, PrimaryRobotFriendlyId=@friendly,
                IsSuspended=@suspended, CreatedUtc=@created, UpdatedUtc=@updated
            WHERE LoopId=@loop AND OwnerAccountId=@account
            """, connection, transaction);
        command.Parameters.AddWithValue("name", loop.Name);
        Text(command, "robot", loop.RobotId);
        Text(command, "friendly", loop.RobotFriendlyId);
        command.Parameters.AddWithValue("suspended", loop.IsSuspended);
        command.Parameters.AddWithValue("created", loop.CreatedUtc);
        command.Parameters.AddWithValue("updated", loop.UpdatedUtc);
        command.Parameters.AddWithValue("loop", loop.LoopId);
        command.Parameters.AddWithValue("account", accountId);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw new InvalidOperationException("The loop scope was not found.");
    }

    private static async Task ReplaceLoopDevicesAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        string accountId, string loopId, IReadOnlyList<LoopDeviceLink> devices,
        CancellationToken cancellationToken)
    {
        await ExecuteAsync(connection, transaction, "DELETE FROM LoopDevices WHERE LoopId=@loop",
            cancellationToken, ("loop", loopId));
        foreach (var device in devices)
        {
            await using var command = new NpgsqlCommand("""
                INSERT INTO LoopDevices(LoopId,DeviceId,IsPrimary,AddedUtc)
                SELECT @loop,@device,@primary,@added
                WHERE EXISTS(SELECT 1 FROM AccountDevices WHERE AccountId=@account AND DeviceId=@device)
                """, connection, transaction);
            command.Parameters.AddWithValue("loop", loopId); command.Parameters.AddWithValue("device", device.DeviceId);
            command.Parameters.AddWithValue("primary", device.IsPrimary); command.Parameters.AddWithValue("added", device.AddedUtc);
            command.Parameters.AddWithValue("account", accountId);
            await RequireMutationAsync(command, cancellationToken);
        }
    }

    private static async Task UpsertMemberAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        string accountId, LoopMemberRecord member, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            INSERT INTO LoopMembers(MemberId,LoopId,AccountId,Email,FirstName,LastName,Gender,Birthday,IsChild,
                PhoneNumber,Status,MemberType,Nickname,PhoneticName,FaceEnrolled,VoiceEnrolled,LegalGuardianId,
                AgreementId,CreatedUtc,UpdatedUtc,PortalEditedUtc)
            SELECT @id,@loop,@memberAccount,@email,@first,@last,@gender,@birthday,@child,@phone,@status,@type,
                @nickname,@phonetic,@face,@voice,@guardian,@agreement,@created,NOW(),@portal
            WHERE EXISTS(SELECT 1 FROM Loops WHERE LoopId=@loop AND OwnerAccountId=@owner)
            ON CONFLICT(MemberId) DO UPDATE SET AccountId=EXCLUDED.AccountId,Email=EXCLUDED.Email,
                FirstName=EXCLUDED.FirstName,LastName=EXCLUDED.LastName,Gender=EXCLUDED.Gender,
                Birthday=EXCLUDED.Birthday,IsChild=EXCLUDED.IsChild,PhoneNumber=EXCLUDED.PhoneNumber,
                Status=EXCLUDED.Status,MemberType=EXCLUDED.MemberType,Nickname=EXCLUDED.Nickname,
                PhoneticName=EXCLUDED.PhoneticName,FaceEnrolled=EXCLUDED.FaceEnrolled,
                VoiceEnrolled=EXCLUDED.VoiceEnrolled,LegalGuardianId=EXCLUDED.LegalGuardianId,
                AgreementId=EXCLUDED.AgreementId,UpdatedUtc=NOW(),PortalEditedUtc=EXCLUDED.PortalEditedUtc
            WHERE LoopMembers.LoopId=EXCLUDED.LoopId
            """, connection, transaction);
        command.Parameters.AddWithValue("id", member.Id); command.Parameters.AddWithValue("loop", member.LoopId);
        command.Parameters.AddWithValue("owner", accountId); Text(command, "memberAccount", member.AccountId);
        Text(command, "email", member.Email); Text(command, "first", member.FirstName);
        Text(command, "last", member.LastName); Text(command, "gender", member.Gender);
        Long(command, "birthday", member.Birthday); command.Parameters.AddWithValue("child", member.IsChild);
        Text(command, "phone", member.PhoneNumber); command.Parameters.AddWithValue("status", member.Status);
        command.Parameters.AddWithValue("type", member.Type); Text(command, "nickname", member.Nickname);
        Text(command, "phonetic", member.PhoneticName); command.Parameters.AddWithValue("face", member.FaceEnrolled);
        command.Parameters.AddWithValue("voice", member.VoiceEnrolled); Text(command, "guardian", member.LegalGuardianId);
        Text(command, "agreement", member.AgreementId); command.Parameters.AddWithValue("created", member.CreatedUtc);
        Time(command, "portal", member.PortalEditedUtc);
        await RequireMutationAsync(command, cancellationToken);
    }

    private static async Task UpsertPersonAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        PersonRecord person, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            INSERT INTO People(PersonId,AccountId,LoopId,RobotId,DisplayName,Alias,IsPrimary,CreatedUtc,UpdatedUtc)
            VALUES(@id,@account,@loop,@robot,@display,@alias,@primary,@created,@updated)
            ON CONFLICT(AccountId,LoopId,PersonId) DO UPDATE SET RobotId=EXCLUDED.RobotId,
                DisplayName=EXCLUDED.DisplayName,Alias=EXCLUDED.Alias,IsPrimary=EXCLUDED.IsPrimary,
                UpdatedUtc=EXCLUDED.UpdatedUtc
            """, connection, transaction);
        command.Parameters.AddWithValue("id", person.PersonId); command.Parameters.AddWithValue("account", person.AccountId);
        command.Parameters.AddWithValue("loop", person.LoopId); command.Parameters.AddWithValue("robot", person.RobotId);
        command.Parameters.AddWithValue("display", person.DisplayName); Text(command, "alias", person.Alias);
        command.Parameters.AddWithValue("primary", person.IsPrimary); command.Parameters.AddWithValue("created", person.CreatedUtc);
        command.Parameters.AddWithValue("updated", person.UpdatedUtc); await RequireMutationAsync(command, cancellationToken);
    }

    private static async Task UpsertHolidayAsync(NpgsqlConnection c, NpgsqlTransaction t, HolidayRecord value,
        CancellationToken ct)
    {
        await using var q = new NpgsqlCommand("""
            INSERT INTO HolidayOverrides(HolidayId,EventId,Name,Category,Subcategory,LoopId,MemberId,IsEnabled,
                EventDate,EndDate,Source,CountryCode,CreatedUtc)
            VALUES(@id,@event,@name,@category,@sub,@loop,@member,@enabled,@date,@end,@source,@country,@created)
            ON CONFLICT(HolidayId) DO UPDATE SET EventId=EXCLUDED.EventId,Name=EXCLUDED.Name,
                Category=EXCLUDED.Category,Subcategory=EXCLUDED.Subcategory,MemberId=EXCLUDED.MemberId,
                IsEnabled=EXCLUDED.IsEnabled,EventDate=EXCLUDED.EventDate,EndDate=EXCLUDED.EndDate,
                Source=EXCLUDED.Source,CountryCode=EXCLUDED.CountryCode WHERE HolidayOverrides.LoopId=EXCLUDED.LoopId
            """, c, t);
        q.Parameters.AddWithValue("id", value.Id); q.Parameters.AddWithValue("event", value.EventId);
        q.Parameters.AddWithValue("name", value.Name); q.Parameters.AddWithValue("category", value.Category);
        Text(q, "sub", value.Subcategory); q.Parameters.AddWithValue("loop", value.LoopId); Text(q, "member", value.MemberId);
        q.Parameters.AddWithValue("enabled", value.IsEnabled); q.Parameters.AddWithValue("date", value.Date);
        Date(q, "end", value.EndDate); q.Parameters.AddWithValue("source", value.Source);
        q.Parameters.AddWithValue("country", value.CountryCode); q.Parameters.AddWithValue("created", value.Created);
        await RequireMutationAsync(q, ct);
    }

    private static async Task UpsertCommuteAsync(NpgsqlConnection c, NpgsqlTransaction t, CommuteProfileRecord value,
        CancellationToken ct)
    {
        await using var q = new NpgsqlCommand("""
            INSERT INTO CommuteProfiles(CommuteProfileId,LoopId,MemberId,IsEnabled,IsComplete,Mode,WorkHour,
                WorkMinute,OriginName,DestinationName,TypicalDurationMinutes,CreatedUtc,UpdatedUtc)
            VALUES(@id,@loop,@member,@enabled,@complete,@mode,@hour,@minute,@origin,@destination,@duration,@created,@updated)
            ON CONFLICT(CommuteProfileId) DO UPDATE SET MemberId=EXCLUDED.MemberId,IsEnabled=EXCLUDED.IsEnabled,
                IsComplete=EXCLUDED.IsComplete,Mode=EXCLUDED.Mode,WorkHour=EXCLUDED.WorkHour,
                WorkMinute=EXCLUDED.WorkMinute,OriginName=EXCLUDED.OriginName,DestinationName=EXCLUDED.DestinationName,
                TypicalDurationMinutes=EXCLUDED.TypicalDurationMinutes,UpdatedUtc=EXCLUDED.UpdatedUtc
            WHERE CommuteProfiles.LoopId=EXCLUDED.LoopId
            """, c, t);
        q.Parameters.AddWithValue("id", value.Id); q.Parameters.AddWithValue("loop", value.LoopId);
        Text(q, "member", value.MemberId); q.Parameters.AddWithValue("enabled", value.IsEnabled);
        q.Parameters.AddWithValue("complete", value.IsComplete); q.Parameters.AddWithValue("mode", value.Mode);
        q.Parameters.AddWithValue("hour", value.WorkHour); q.Parameters.AddWithValue("minute", value.WorkMinute);
        Text(q, "origin", value.OriginName); Text(q, "destination", value.DestinationName);
        q.Parameters.AddWithValue("duration", value.TypicalDurationMinutes); q.Parameters.AddWithValue("created", value.Created);
        q.Parameters.AddWithValue("updated", value.Updated); await RequireMutationAsync(q, ct);
    }

    private static async Task UpsertCalendarAsync(NpgsqlConnection c, NpgsqlTransaction t, CalendarEventRecord value,
        CancellationToken ct)
    {
        await using var q = new NpgsqlCommand("""
            INSERT INTO CalendarEvents(CalendarEventId,LoopId,Summary,TimeLabel,EventDate,EndDate,IsAllDay,
                IsEnabled,Source,MemberId,CreatedUtc,UpdatedUtc)
            VALUES(@id,@loop,@summary,@label,@date,@end,@allDay,@enabled,@source,@member,@created,NOW())
            ON CONFLICT(CalendarEventId) DO UPDATE SET Summary=EXCLUDED.Summary,TimeLabel=EXCLUDED.TimeLabel,
                EventDate=EXCLUDED.EventDate,EndDate=EXCLUDED.EndDate,IsAllDay=EXCLUDED.IsAllDay,
                IsEnabled=EXCLUDED.IsEnabled,Source=EXCLUDED.Source,MemberId=EXCLUDED.MemberId,UpdatedUtc=NOW()
            WHERE CalendarEvents.LoopId=EXCLUDED.LoopId
            """, c, t);
        q.Parameters.AddWithValue("id", value.Id); q.Parameters.AddWithValue("loop", value.LoopId);
        q.Parameters.AddWithValue("summary", value.Summary); Text(q, "label", value.TimeLabel);
        q.Parameters.AddWithValue("date", value.Date); Date(q, "end", value.EndDate);
        q.Parameters.AddWithValue("allDay", value.IsAllDay); q.Parameters.AddWithValue("enabled", value.IsEnabled);
        q.Parameters.AddWithValue("source", value.Source); Text(q, "member", value.MemberId);
        q.Parameters.AddWithValue("created", value.Created); await RequireMutationAsync(q, ct);
    }

    private static async Task UpsertGreetingAsync(NpgsqlConnection c, NpgsqlTransaction t,
        GreetingPresenceRecord value, CancellationToken ct)
    {
        await using var q = new NpgsqlCommand("""
            INSERT INTO GreetingPresences(GreetingPresenceId,AccountId,LoopId,PersonId,SpeakerId,PreferredName,
                LastSeenUtc,LastGreetedUtc,LastGreetingRoute,LastGreetingIntent,CreatedUtc,UpdatedUtc)
            VALUES(@id,@account,@loop,@person,@speaker,@preferred,@seen,@greeted,@route,@intent,@created,@updated)
            ON CONFLICT(LoopId,PersonId) DO UPDATE SET SpeakerId=EXCLUDED.SpeakerId,
                PreferredName=EXCLUDED.PreferredName,LastSeenUtc=EXCLUDED.LastSeenUtc,
                LastGreetedUtc=EXCLUDED.LastGreetedUtc,LastGreetingRoute=EXCLUDED.LastGreetingRoute,
                LastGreetingIntent=EXCLUDED.LastGreetingIntent,UpdatedUtc=EXCLUDED.UpdatedUtc
            WHERE GreetingPresences.AccountId=EXCLUDED.AccountId
            """, c, t);
        q.Parameters.AddWithValue("id", value.Id); q.Parameters.AddWithValue("account", value.AccountId);
        q.Parameters.AddWithValue("loop", value.LoopId); q.Parameters.AddWithValue("person", value.PersonId);
        Text(q, "speaker", value.SpeakerId); Text(q, "preferred", value.PreferredName);
        q.Parameters.AddWithValue("seen", value.LastSeenUtc); Time(q, "greeted", value.LastGreetedUtc);
        Text(q, "route", value.LastGreetingRoute); Text(q, "intent", value.LastGreetingIntent);
        q.Parameters.AddWithValue("created", value.CreatedUtc); q.Parameters.AddWithValue("updated", value.UpdatedUtc);
        await RequireMutationAsync(q, ct);
    }

    private static async Task InsertRecognitionAsync(NpgsqlConnection c, NpgsqlTransaction t,
        RecognitionObservationRecord value, CancellationToken ct)
    {
        await using var q = new NpgsqlCommand("""
            INSERT INTO RecognitionObservations(ObservationId,LoopId,MemberId,RobotId,Modality,Outcome,
                Confidence,Source,ObservedUtc)
            VALUES(@id,@loop,@member,@robot,@modality,@outcome,@confidence,@source,@observed)
            """, c, t);
        q.Parameters.AddWithValue("id", value.ObservationId); q.Parameters.AddWithValue("loop", value.LoopId);
        q.Parameters.AddWithValue("member", value.MemberId); q.Parameters.AddWithValue("robot", value.RobotId);
        q.Parameters.AddWithValue("modality", value.Modality); q.Parameters.AddWithValue("outcome", value.Outcome);
        q.Parameters.Add("confidence", NpgsqlDbType.Double).Value = (object?)value.Confidence ?? DBNull.Value;
        Text(q, "source", value.Source); q.Parameters.AddWithValue("observed", value.ObservedUtc);
        await RequireMutationAsync(q, ct);
    }

    private static async Task InsertLoopKeyAsync(NpgsqlConnection c, NpgsqlTransaction t,
        LoopSymmetricKeyRecord value, CancellationToken ct)
    {
        await using var q = new NpgsqlCommand("""
            INSERT INTO LoopSymmetricKeys(LoopId,EncryptedKey,WrappingKeyId,Algorithm,CreatedUtc,RotatedUtc)
            VALUES(@loop,@key,@wrapping,@algorithm,@created,@rotated)
            """, c, t);
        q.Parameters.AddWithValue("loop", value.LoopId); q.Parameters.AddWithValue("key", value.EncryptedKey);
        q.Parameters.AddWithValue("wrapping", value.WrappingKeyId); q.Parameters.AddWithValue("algorithm", value.Algorithm);
        q.Parameters.AddWithValue("created", value.CreatedUtc); Time(q, "rotated", value.RotatedUtc);
        await RequireMutationAsync(q, ct);
    }

    private static async Task InsertKeyRequestAsync(NpgsqlConnection c, NpgsqlTransaction t,
        StoredKeyRequest value, CancellationToken ct)
    {
        await using var q = new NpgsqlCommand("""
            INSERT INTO KeyRequests(RequestId,LoopId,PublicKey,EncryptedKey,RequestKind,Status,CreatedUtc,CompletedUtc)
            VALUES(@id,@loop,@public,@encrypted,@kind,@status,@created,@completed)
            """, c, t);
        q.Parameters.AddWithValue("id", value.Request.RequestId); q.Parameters.AddWithValue("loop", value.Request.LoopId);
        q.Parameters.AddWithValue("public", value.Request.PublicKey); q.Parameters.AddWithValue("encrypted", value.Request.EncryptedKey);
        q.Parameters.AddWithValue("kind", value.RequestKind); q.Parameters.AddWithValue("status", value.Status);
        q.Parameters.AddWithValue("created", value.Request.CreatedUtc); Time(q, "completed", value.CompletedUtc);
        await RequireMutationAsync(q, ct);
    }

    private static async Task InsertMediaAsync(NpgsqlConnection c, NpgsqlTransaction t, StoredMediaRecord value,
        CancellationToken ct)
    {
        var media = value.Media;
        await using var q = new NpgsqlCommand("""
            INSERT INTO MediaRecords(MediaPath,CreatedUtc,MediaType,Reference,AccountId,LoopId,BlobUri,
                ContentSha256,ContentLength,IsEncrypted,EncryptionKeyId,IsDeleted,DeletedUtc,Meta)
            VALUES(@path,@created,@type,@reference,@account,@loop,@uri,@sha,@length,@encrypted,@keyId,
                @deleted,@deletedUtc,@meta)
            """, c, t);
        q.Parameters.AddWithValue("path", media.Path); q.Parameters.AddWithValue("created", media.CreatedUtc);
        q.Parameters.AddWithValue("type", media.MediaType); q.Parameters.AddWithValue("reference", media.Reference);
        q.Parameters.AddWithValue("account", media.AccountId); q.Parameters.AddWithValue("loop", media.LoopId);
        q.Parameters.AddWithValue("uri", media.Url); Text(q, "sha", value.ContentSha256);
        Long(q, "length", value.ContentLength); q.Parameters.AddWithValue("encrypted", media.IsEncrypted);
        Text(q, "keyId", value.EncryptionKeyId); q.Parameters.AddWithValue("deleted", media.IsDeleted);
        Time(q, "deletedUtc", value.DeletedUtc);
        q.Parameters.Add("meta", NpgsqlDbType.Jsonb).Value = JsonSerializer.Serialize(media.Meta);
        await RequireMutationAsync(q, ct);
    }

    private static async Task ExecuteAsync(NpgsqlConnection c, NpgsqlTransaction t, string sql,
        CancellationToken ct, params (string Name, object Value)[] parameters)
    {
        await using var q = new NpgsqlCommand(sql, c, t);
        foreach (var parameter in parameters) q.Parameters.AddWithValue(parameter.Name, parameter.Value);
        await q.ExecuteNonQueryAsync(ct);
    }

    private static async Task RequireMutationAsync(NpgsqlCommand command, CancellationToken cancellationToken)
    {
        if (await command.ExecuteNonQueryAsync(cancellationToken) == 0)
            throw new InvalidOperationException("The backup record conflicts with another loop scope.");
    }

    private static void Text(NpgsqlCommand command, string name, string? value) =>
        command.Parameters.Add(name, NpgsqlDbType.Text).Value = (object?)value ?? DBNull.Value;
    private static void Long(NpgsqlCommand command, string name, long? value) =>
        command.Parameters.Add(name, NpgsqlDbType.Bigint).Value = (object?)value ?? DBNull.Value;
    private static void Date(NpgsqlCommand command, string name, DateOnly? value) =>
        command.Parameters.Add(name, NpgsqlDbType.Date).Value = (object?)value ?? DBNull.Value;
    private static void Time(NpgsqlCommand command, string name, DateTimeOffset? value) =>
        command.Parameters.Add(name, NpgsqlDbType.TimestampTz).Value = (object?)value ?? DBNull.Value;
    private static string Required(string value, string name) =>
        string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Value is required.", name) : value.Trim();
}
