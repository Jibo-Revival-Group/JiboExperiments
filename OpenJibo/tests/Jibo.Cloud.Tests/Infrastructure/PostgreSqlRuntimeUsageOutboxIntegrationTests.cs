using System.Diagnostics;
using Npgsql;

namespace Jibo.Cloud.Tests.Infrastructure;

public sealed class PostgreSqlRuntimeUsageOutboxIntegrationTests
{
    [PostgreSqlIntegrationFact]
    [Trait("Category", "PostgreSqlIntegration")]
    public async Task Migration_IsRepeatableAndEnforcesPrivacyAndImmutabilityBoundaries()
    {
        await using var database = await RuntimeUsageTestDatabase.CreateAsync();
        await database.ApplyMigrationAsync();

        Assert.Equal(6, await database.ExecuteScalarAsync<long>("""
            SELECT COUNT(*)
            FROM information_schema.tables
            WHERE table_schema = current_schema()
              AND table_name LIKE 'runtimeusage%'
            """));

        await database.ExecuteAsync("""
            INSERT INTO RuntimeUsageRobotBindings
                (SourceSubjectHmac, BindingVersion, ManagedRobotId, ActiveFromUtc, ActorCode, ReasonCode)
            VALUES
                (decode(repeat('11', 32), 'hex'), 1, '11111111-1111-1111-1111-111111111111',
                 '2026-09-13T01:00:00Z', 'binding-admin', 'initial-link');
            """);

        await database.ExecuteAsync("""
            SELECT * FROM RecordRuntimeUsageEvent(
                decode(repeat('11', 32), 'hex'),
                '22222222-2222-2222-2222-222222222222', '2026-09-13T01:05:00Z', 'staging',
                1, 0, 1, 100, 200, 5, 6, 300, 400, 500, NULL);
            SELECT * FROM RecordRuntimeUsageEvent(
                decode(repeat('11', 32), 'hex'),
                '22222222-2222-2222-2222-222222222222', '2026-09-13T01:05:00Z', 'staging',
                1, 0, 1, 100, 200, 5, 6, 300, 400, 500, NULL);
            SELECT * FROM ScheduleRuntimeUsageSnapshot(
                decode(repeat('11', 32), 'hex'), '2026-09-13', 'staging',
                '33333333-3333-3333-3333-333333333333',
                'AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA');
            """);

        Assert.Equal(1, await database.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM RuntimeUsageAppliedEvents"));
        Assert.Equal(1, await database.ExecuteScalarAsync<long>("""
            SELECT SuccessfulTurns FROM RuntimeUsageDailyAccumulators
            WHERE ManagedRobotId='11111111-1111-1111-1111-111111111111'
            """));
        Assert.Equal(1, await database.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM RuntimeUsageOutboxMessages"));
        Assert.Equal(1, await database.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM RuntimeUsageOutboxDelivery"));

        var idempotencyConflict = await Assert.ThrowsAsync<PostgresException>(() => database.ExecuteAsync("""
            SELECT * FROM RecordRuntimeUsageEvent(
                decode(repeat('11', 32), 'hex'),
                '22222222-2222-2222-2222-222222222222', '2026-09-13T01:05:00Z', 'staging',
                1, 0, 1, 101, 200, 5, 6, 300, 400, 500, NULL);
            """));
        Assert.Equal(PostgresErrorCodes.InvalidParameterValue, idempotencyConflict.SqlState);

        await database.ExecuteAsync("""
            SELECT * FROM RecordRuntimeUsageEvent(
                decode(repeat('11', 32), 'hex'),
                '44444444-4444-4444-4444-444444444444', '2026-09-13T01:06:00Z', 'staging',
                1, 0, 0, 0, 0, 1, 1, 50, 60, 70, NULL);
            SELECT * FROM ScheduleRuntimeUsageSnapshot(
                decode(repeat('11', 32), 'hex'), '2026-09-13', 'staging',
                '55555555-5555-5555-5555-555555555555',
                'BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB');
            """);
        Assert.Equal("1,2", await database.ExecuteScalarAsync<string>("""
            SELECT string_agg(SourceSequence::TEXT, ',' ORDER BY SourceSequence)
            FROM RuntimeUsageOutboxMessages
            """));
        Assert.Equal(1, await database.ExecuteScalarAsync<long>("""
            SELECT ScheduledSourceSequence
            FROM ScheduleRuntimeUsageSnapshot(
                decode(repeat('11', 32), 'hex'), '2026-09-13', 'staging',
                '33333333-3333-3333-3333-333333333333',
                'AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA')
            """));

        var conflictingScheduleKey = await Assert.ThrowsAsync<PostgresException>(() => database.ExecuteAsync("""
            SELECT * FROM ScheduleRuntimeUsageSnapshot(
                decode(repeat('11', 32), 'hex'), '2026-09-13', 'staging',
                '33333333-3333-3333-3333-333333333333',
                'CCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCC');
            """));
        Assert.Equal(PostgresErrorCodes.InvalidParameterValue, conflictingScheduleKey.SqlState);

        await database.ExecuteAsync("""
            UPDATE RuntimeUsageOutboxDelivery
            SET DeliveryState='leased', AttemptCount=1, LeaseOwner='collector-1',
                LeaseExpiresUtc=NOW() + INTERVAL '1 minute', UpdatedUtc=NOW()
            WHERE MessageId='33333333-3333-3333-3333-333333333333';
            """);
        Assert.Equal("leased", await database.ExecuteScalarAsync<string>("""
            SELECT DeliveryState
            FROM RuntimeUsageOutboxDelivery
            WHERE MessageId='33333333-3333-3333-3333-333333333333'
            """));

        var update = await Assert.ThrowsAsync<PostgresException>(() => database.ExecuteAsync("""
            UPDATE RuntimeUsageOutboxMessages
            SET SuccessfulTurns=4
            WHERE MessageId='33333333-3333-3333-3333-333333333333'
            """));
        Assert.Equal("55000", update.SqlState);

        var delete = await Assert.ThrowsAsync<PostgresException>(() => database.ExecuteAsync("""
            DELETE FROM RuntimeUsageOutboxMessages
            WHERE MessageId='33333333-3333-3333-3333-333333333333'
            """));
        Assert.Equal("55000", delete.SqlState);

        await database.ExecuteAsync("""
            UPDATE RuntimeUsageDailyAccumulators
            SET IsIncomplete=TRUE, FirstIncompleteUtc='2026-09-13T01:06:00Z',
                IncompleteReasonCode='source-write-failed', UpdatedUtc=NOW()
            WHERE ManagedRobotId='11111111-1111-1111-1111-111111111111'
              AND UsageDate='2026-09-13' AND ServiceEnvironment='staging';
            """);
        var clearIncomplete = await Assert.ThrowsAsync<PostgresException>(() => database.ExecuteAsync("""
            UPDATE RuntimeUsageDailyAccumulators
            SET IsIncomplete=FALSE, FirstIncompleteUtc=NULL, IncompleteReasonCode=NULL, UpdatedUtc=NOW()
            WHERE ManagedRobotId='11111111-1111-1111-1111-111111111111'
              AND UsageDate='2026-09-13' AND ServiceEnvironment='staging';
            """));
        Assert.Equal("55000", clearIncomplete.SqlState);

        var mutateEvent = await Assert.ThrowsAsync<PostgresException>(() => database.ExecuteAsync("""
            UPDATE RuntimeUsageAppliedEvents
            SET SuccessfulTurnsDelta=99
            WHERE UsageEventId='22222222-2222-2222-2222-222222222222'
            """));
        Assert.Equal("55000", mutateEvent.SqlState);

        await database.ExecuteAsync("""
            UPDATE RuntimeUsageRobotBindings
            SET RevokedUtc='2026-09-13T02:00:00Z'
            WHERE SourceSubjectHmac=decode(repeat('11', 32), 'hex') AND BindingVersion=1;
            """);
        var rewriteBinding = await Assert.ThrowsAsync<PostgresException>(() => database.ExecuteAsync("""
            UPDATE RuntimeUsageRobotBindings
            SET ManagedRobotId='99999999-9999-9999-9999-999999999999'
            WHERE SourceSubjectHmac=decode(repeat('11', 32), 'hex') AND BindingVersion=1;
            """));
        Assert.Equal("55000", rewriteBinding.SqlState);

        var deleteDelivery = await Assert.ThrowsAsync<PostgresException>(() => database.ExecuteAsync("""
            DELETE FROM RuntimeUsageOutboxDelivery
            WHERE MessageId='33333333-3333-3333-3333-333333333333'
            """));
        Assert.Equal("55000", deleteDelivery.SqlState);

        var deleteAccumulator = await Assert.ThrowsAsync<PostgresException>(() => database.ExecuteAsync("""
            DELETE FROM RuntimeUsageDailyAccumulators
            WHERE ManagedRobotId='11111111-1111-1111-1111-111111111111'
              AND UsageDate='2026-09-13' AND ServiceEnvironment='staging'
            """));
        Assert.Equal("55000", deleteAccumulator.SqlState);
    }

    [PostgreSqlIntegrationFact]
    [Trait("Category", "PostgreSqlIntegration")]
    public async Task Migration_RejectsAmbiguousBindingsAndCompleteZeroSubstitution()
    {
        await using var database = await RuntimeUsageTestDatabase.CreateAsync();

        await database.ExecuteAsync("""
            INSERT INTO RuntimeUsageRobotBindings
                (SourceSubjectHmac, BindingVersion, ManagedRobotId, ActiveFromUtc, ActorCode, ReasonCode)
            VALUES
                (decode(repeat('55', 32), 'hex'), 1, '55555555-5555-5555-5555-555555555555',
                 '2026-09-13T01:00:00Z', 'binding-admin', 'initial-link');
            """);

        var duplicateBinding = await Assert.ThrowsAsync<PostgresException>(() => database.ExecuteAsync("""
            INSERT INTO RuntimeUsageRobotBindings
                (SourceSubjectHmac, BindingVersion, ManagedRobotId, ActiveFromUtc, ActorCode, ReasonCode)
            VALUES
                (decode(repeat('55', 32), 'hex'), 2, '66666666-6666-6666-6666-666666666666',
                 '2026-09-13T02:00:00Z', 'binding-admin', 'replacement');
            """));
        Assert.Equal(PostgresErrorCodes.UniqueViolation, duplicateBinding.SqlState);

        var incompleteWithoutEvidence = await Assert.ThrowsAsync<PostgresException>(() => database.ExecuteAsync("""
            INSERT INTO RuntimeUsageDailyAccumulators
                (ManagedRobotId, UsageDate, ServiceEnvironment, FirstEventUtc, LastEventUtc,
                 Revision, IsIncomplete)
            VALUES
                ('55555555-5555-5555-5555-555555555555', '2026-09-13', 'staging',
                 '2026-09-13T01:00:00Z', '2026-09-13T01:00:00Z', 1, TRUE);
            """));
        Assert.Equal(PostgresErrorCodes.CheckViolation, incompleteWithoutEvidence.SqlState);

        var completeZero = await Assert.ThrowsAsync<PostgresException>(() => database.ExecuteAsync("""
            INSERT INTO RuntimeUsageDailyAccumulators
                (ManagedRobotId, UsageDate, ServiceEnvironment, FirstEventUtc, LastEventUtc, Revision)
            VALUES
                ('55555555-5555-5555-5555-555555555555', '2026-09-13', 'staging',
                 '2026-09-13T01:00:00Z', '2026-09-13T01:00:00Z', 1);
            """));
        Assert.Equal(PostgresErrorCodes.CheckViolation, completeZero.SqlState);

        var wrongUtcDay = await Assert.ThrowsAsync<PostgresException>(() => database.ExecuteAsync("""
            INSERT INTO RuntimeUsageDailyAccumulators
                (ManagedRobotId, UsageDate, ServiceEnvironment, FirstEventUtc, LastEventUtc, Revision)
            VALUES
                ('55555555-5555-5555-5555-555555555555', '2026-09-13', 'staging',
                 '2026-09-14T00:00:00Z', '2026-09-14T00:00:00Z', 1);
            """));
        Assert.Equal(PostgresErrorCodes.CheckViolation, wrongUtcDay.SqlState);

        await database.ExecuteAsync("""
            INSERT INTO RuntimeUsageRobotBindings
                (SourceSubjectHmac, BindingVersion, ManagedRobotId, ActiveFromUtc, RevokedUtc,
                 ActorCode, ReasonCode)
            VALUES
                (decode(repeat('77', 32), 'hex'), 1, '77777777-7777-7777-7777-777777777777',
                 '2026-09-13T00:00:00Z', '2026-09-13T03:00:00Z', 'binding-admin', 'historical'),
                (decode(repeat('77', 32), 'hex'), 2, '88888888-8888-8888-8888-888888888888',
                 '2026-09-13T01:00:00Z', '2026-09-13T04:00:00Z', 'binding-admin', 'historical');
            INSERT INTO RuntimeUsageDailyAccumulators
                (ManagedRobotId, UsageDate, ServiceEnvironment, SuccessfulTurns,
                 FirstEventUtc, LastEventUtc, Revision)
            VALUES
                ('77777777-7777-7777-7777-777777777777', '2026-09-13', 'staging', 1,
                 '2026-09-13T02:00:00Z', '2026-09-13T02:00:00Z', 1);
            """);

        var ambiguousEvent = await Assert.ThrowsAsync<PostgresException>(() => database.ExecuteAsync("""
            SELECT * FROM RecordRuntimeUsageEvent(
                decode(repeat('77', 32), 'hex'),
                '99999999-9999-9999-9999-999999999999', '2026-09-13T02:00:00Z', 'staging',
                1, 0, 0, 0, 0, 0, 0, 0, 0, 0, NULL)
            """));
        Assert.Equal(PostgresErrorCodes.InvalidParameterValue, ambiguousEvent.SqlState);

        var ambiguousSchedule = await Assert.ThrowsAsync<PostgresException>(() => database.ExecuteAsync("""
            SELECT * FROM ScheduleRuntimeUsageSnapshot(
                decode(repeat('77', 32), 'hex'), '2026-09-13', 'staging',
                'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
                'FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF')
            """));
        Assert.Equal(PostgresErrorCodes.InvalidParameterValue, ambiguousSchedule.SqlState);

        var nullEvent = await Assert.ThrowsAsync<PostgresException>(() => database.ExecuteAsync("""
            SELECT * FROM RecordRuntimeUsageEvent(
                NULL, 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb',
                '2026-09-13T02:00:00Z', 'staging',
                1, 0, 0, 0, 0, 0, 0, 0, 0, 0, NULL)
            """));
        Assert.Equal(PostgresErrorCodes.InvalidParameterValue, nullEvent.SqlState);
    }

    [PostgreSqlIntegrationFact]
    [Trait("Category", "PostgreSqlIntegration")]
    public async Task RecordAndSchedule_ConcurrentWritersPreserveTotalsAndOneGapFreeSequence()
    {
        await using var database = await RuntimeUsageTestDatabase.CreateAsync();
        await database.ExecuteAsync("""
            INSERT INTO RuntimeUsageRobotBindings
                (SourceSubjectHmac, BindingVersion, ManagedRobotId, ActiveFromUtc, ActorCode, ReasonCode)
            VALUES
                (decode(repeat('66', 32), 'hex'), 1, '66666666-6666-6666-6666-666666666666',
                 '2026-09-13T01:00:00Z', 'binding-admin', 'initial-link');
            """);

        await Task.WhenAll(
            database.ExecuteAsync("""
                SELECT * FROM RecordRuntimeUsageEvent(
                    decode(repeat('66', 32), 'hex'),
                    '77777777-7777-7777-7777-777777777777', '2026-09-13T02:00:00Z', 'staging',
                    1, 0, 0, 0, 0, 1, 0, 10, 0, 10, NULL)
                """),
            database.ExecuteAsync("""
                SELECT * FROM RecordRuntimeUsageEvent(
                    decode(repeat('66', 32), 'hex'),
                    '88888888-8888-8888-8888-888888888888', '2026-09-13T02:01:00Z', 'staging',
                    1, 0, 0, 0, 0, 1, 0, 10, 0, 10, NULL)
                """));

        Assert.Equal(2, await database.ExecuteScalarAsync<long>("""
            SELECT SuccessfulTurns FROM RuntimeUsageDailyAccumulators
            WHERE ManagedRobotId='66666666-6666-6666-6666-666666666666'
            """));
        Assert.Equal(2, await database.ExecuteScalarAsync<long>("""
            SELECT Revision FROM RuntimeUsageDailyAccumulators
            WHERE ManagedRobotId='66666666-6666-6666-6666-666666666666'
            """));

        var scheduleResults = await Task.WhenAll(
            database.TryExecuteAsync("""
                SELECT * FROM ScheduleRuntimeUsageSnapshot(
                    decode(repeat('66', 32), 'hex'), '2026-09-13', 'staging',
                    '99999999-9999-9999-9999-999999999999',
                    'DDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDD')
                """),
            database.TryExecuteAsync("""
                SELECT * FROM ScheduleRuntimeUsageSnapshot(
                    decode(repeat('66', 32), 'hex'), '2026-09-13', 'staging',
                    'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
                    'EEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEE')
                """));

        Assert.Single(scheduleResults, result => result is null);
        Assert.Single(scheduleResults, result => result == PostgresErrorCodes.InvalidParameterValue);
        Assert.Equal(1, await database.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM RuntimeUsageOutboxMessages"));
        Assert.Equal(1, await database.ExecuteScalarAsync<long>(
            "SELECT SourceSequence FROM RuntimeUsageOutboxMessages"));
    }

    [PostgreSqlIntegrationFact]
    [Trait("Category", "PostgreSqlIntegration")]
    public async Task DeliveryBoundary_ClaimsHeadOfLineAndSupportsLeaseTakeoverAndReplay()
    {
        await using var database = await RuntimeUsageTestDatabase.CreateAsync();
        await database.ExecuteAsync("""
            INSERT INTO RuntimeUsageRobotBindings
                (SourceSubjectHmac, BindingVersion, ManagedRobotId, ActiveFromUtc, ActorCode, ReasonCode)
            VALUES
                (decode(repeat('aa', 32), 'hex'), 1, 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
                 '2026-09-13T00:00:00Z', 'binding-admin', 'delivery-test');
            SELECT * FROM RecordRuntimeUsageEvent(
                decode(repeat('aa', 32), 'hex'),
                'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb', '2026-09-13T01:00:00Z', 'staging',
                1, 0, 0, 0, 0, 1, 0, 10, 0, 20, NULL);
            SELECT * FROM ScheduleRuntimeUsageSnapshot(
                decode(repeat('aa', 32), 'hex'), '2026-09-13', 'staging',
                'cccccccc-cccc-cccc-cccc-cccccccccccc',
                'CCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCC');
            SELECT * FROM RecordRuntimeUsageEvent(
                decode(repeat('aa', 32), 'hex'),
                'dddddddd-dddd-dddd-dddd-dddddddddddd', '2026-09-13T02:00:00Z', 'staging',
                1, 0, 0, 0, 0, 1, 0, 10, 0, 20, NULL);
            SELECT * FROM ScheduleRuntimeUsageSnapshot(
                decode(repeat('aa', 32), 'hex'), '2026-09-13', 'staging',
                'eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee',
                'EEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEE');
            SELECT * FROM RecordRuntimeUsageEvent(
                decode(repeat('aa', 32), 'hex'),
                'ffffffff-ffff-ffff-ffff-ffffffffffff', '2026-09-13T03:00:00Z', 'staging',
                1, 0, 0, 0, 0, 1, 0, 10, 0, 20, NULL);
            SELECT * FROM ScheduleRuntimeUsageSnapshot(
                decode(repeat('aa', 32), 'hex'), '2026-09-13', 'staging',
                '12121212-1212-1212-1212-121212121212',
                'FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF');
            """);

        var first = await database.ExecuteScalarAsync<string>("""
            SELECT MessageId::text
            FROM ClaimRuntimeUsageOutbox('collector-a', 1, 1)
            """);
        Assert.Equal("cccccccc-cccc-cccc-cccc-cccccccccccc", first);

        Assert.Null(await database.ExecuteScalarOrNullAsync("""
            SELECT MessageId::text
            FROM ClaimRuntimeUsageOutbox('collector-b', 300, 1)
            """));
        Assert.Equal("22023", await database.TryExecuteAsync("""
            SELECT * FROM AcknowledgeRuntimeUsageOutbox(
                'cccccccc-cccc-cccc-cccc-cccccccccccc', 'wrong-owner', decode(repeat('11', 32), 'hex'))
            """));

        await database.ExecuteAsync("""
            UPDATE RuntimeUsageOutboxDelivery
            SET LeaseExpiresUtc = NOW() - INTERVAL '1 second'
            WHERE MessageId = 'cccccccc-cccc-cccc-cccc-cccccccccccc'
            """);
        var takeover = await database.ExecuteScalarAsync<string>("""
            SELECT MessageId::text
            FROM ClaimRuntimeUsageOutbox('collector-b', 300, 1)
            """);
        Assert.Equal(first, takeover);
        Assert.Equal("22023", await database.TryExecuteAsync("""
            SELECT * FROM AcknowledgeRuntimeUsageOutbox(
                'cccccccc-cccc-cccc-cccc-cccccccccccc', 'collector-a', decode(repeat('11', 32), 'hex'))
            """));

        Assert.False(await database.ExecuteScalarAsync<bool>("""
            SELECT WasReplay
            FROM AcknowledgeRuntimeUsageOutbox(
                'cccccccc-cccc-cccc-cccc-cccccccccccc', 'collector-b', decode(repeat('22', 32), 'hex'))
            """));
        Assert.True(await database.ExecuteScalarAsync<bool>("""
            SELECT WasReplay
            FROM AcknowledgeRuntimeUsageOutbox(
                'cccccccc-cccc-cccc-cccc-cccccccccccc', 'collector-b', decode(repeat('22', 32), 'hex'))
            """));

        var second = await database.ExecuteScalarAsync<string>("""
            SELECT MessageId::text
            FROM ClaimRuntimeUsageOutbox('collector-c', 300, 1)
            """);
        Assert.Equal("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee", second);
        Assert.False(await database.ExecuteScalarAsync<bool>("""
            SELECT WasReplay
            FROM QuarantineRuntimeUsageOutbox(
                'eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee', 'collector-c', 'invalid-envelope')
            """));
        Assert.True(await database.ExecuteScalarAsync<bool>("""
            SELECT WasReplay
            FROM QuarantineRuntimeUsageOutbox(
                'eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee', 'collector-c', 'invalid-envelope')
            """));
        Assert.Null(await database.ExecuteScalarOrNullAsync("""
            SELECT MessageId::text
            FROM ClaimRuntimeUsageOutbox('collector-d', 300, 1)
            """));
        Assert.Equal("pending", await database.ExecuteScalarAsync<string>("""
            SELECT DeliveryState
            FROM RuntimeUsageOutboxDelivery
            WHERE MessageId='12121212-1212-1212-1212-121212121212'
            """));
    }

    [PostgreSqlIntegrationFact]
    [Trait("Category", "PostgreSqlIntegration")]
    public async Task DeliveryBoundary_ConcurrentClaimsYieldOneLease()
    {
        await using var database = await RuntimeUsageTestDatabase.CreateAsync();
        await database.ExecuteAsync("""
            INSERT INTO RuntimeUsageRobotBindings
                (SourceSubjectHmac, BindingVersion, ManagedRobotId, ActiveFromUtc, ActorCode, ReasonCode)
            VALUES
                (decode(repeat('ab', 32), 'hex'), 1, 'abababab-abab-abab-abab-abababababab',
                 '2026-09-13T00:00:00Z', 'binding-admin', 'concurrent-delivery-test');
            SELECT * FROM RecordRuntimeUsageEvent(
                decode(repeat('ab', 32), 'hex'),
                'acacacac-acac-acac-acac-acacacacacac', '2026-09-13T01:00:00Z', 'staging',
                1, 0, 0, 0, 0, 1, 0, 10, 0, 20, NULL);
            SELECT * FROM ScheduleRuntimeUsageSnapshot(
                decode(repeat('ab', 32), 'hex'), '2026-09-13', 'staging',
                'adadadad-adad-adad-adad-adadadadadad',
                'ADADADADADADADADADADADADADADADADADADADADADA');
            """);

        var claims = await Task.WhenAll(
            database.ExecuteScalarOrNullAsync("""
                SELECT MessageId::text
                FROM ClaimRuntimeUsageOutbox('concurrent-a', 300, 1)
                """),
            database.ExecuteScalarOrNullAsync("""
                SELECT MessageId::text
                FROM ClaimRuntimeUsageOutbox('concurrent-b', 300, 1)
                """));

        Assert.Single(claims, value => value is not null);
        Assert.Equal("adadadad-adad-adad-adad-adadadadadad",
            claims.Single(value => value is not null));
        Assert.Equal(1, await database.ExecuteScalarAsync<long>("""
            SELECT AttemptCount FROM RuntimeUsageOutboxDelivery
            WHERE MessageId='adadadad-adad-adad-adad-adadadadadad'
            """));
    }

    [PostgreSqlIntegrationFact]
    [Trait("Category", "PostgreSqlIntegration")]
    public async Task DeliveryBoundary_DefersWithDurableExactReplayAndPreservesOrdering()
    {
        await using var database = await RuntimeUsageTestDatabase.CreateAsync();
        await database.ExecuteAsync("""
            INSERT INTO RuntimeUsageRobotBindings
                (SourceSubjectHmac, BindingVersion, ManagedRobotId, ActiveFromUtc, ActorCode, ReasonCode)
            VALUES
                (decode(repeat('bc', 32), 'hex'), 1, 'bcbcbcbc-bcbc-bcbc-bcbc-bcbcbcbcbcbc',
                 '2026-09-13T00:00:00Z', 'binding-admin', 'defer-test');
            SELECT * FROM RecordRuntimeUsageEvent(
                decode(repeat('bc', 32), 'hex'),
                'bdbdbdbd-bdbd-bdbd-bdbd-bdbdbdbdbdbd', '2026-09-13T01:00:00Z', 'staging',
                1, 0, 0, 0, 0, 1, 0, 10, 0, 20, NULL);
            SELECT * FROM ScheduleRuntimeUsageSnapshot(
                decode(repeat('bc', 32), 'hex'), '2026-09-13', 'staging',
                'bebebebe-bebe-bebe-bebe-bebebebebebe',
                'BEBEBEBEBEBEBEBEBEBEBEBEBEBEBEBEBEBEBEBEBEB');
            SELECT * FROM RecordRuntimeUsageEvent(
                decode(repeat('bc', 32), 'hex'),
                'bfbfbfbf-bfbf-bfbf-bfbf-bfbfbfbfbfbf', '2026-09-13T02:00:00Z', 'staging',
                1, 0, 0, 0, 0, 1, 0, 10, 0, 20, NULL);
            SELECT * FROM ScheduleRuntimeUsageSnapshot(
                decode(repeat('bc', 32), 'hex'), '2026-09-13', 'staging',
                'cacacaca-caca-caca-caca-cacacacacaca',
                'CACACACACACACACACACACACACACACACACACACACACAC');
            SELECT * FROM ClaimRuntimeUsageOutbox('defer-collector', 300, 1);
            """);

        Assert.False(await database.ExecuteScalarAsync<bool>("""
            SELECT WasReplay FROM DeferRuntimeUsageOutbox(
                'bebebebe-bebe-bebe-bebe-bebebebebebe', 'defer-collector',
                'cdcdcdcd-cdcd-cdcd-cdcd-cdcdcdcdcdcd',
                date_trunc('second', clock_timestamp()) + INTERVAL '1 hour', 'assembly-waiting')
            """));
        Assert.Equal("pending,1,assembly-waiting", await database.ExecuteScalarAsync<string>("""
            SELECT DeliveryState || ',' || AttemptCount::text || ',' || LastFailureCategory
            FROM RuntimeUsageOutboxDelivery
            WHERE MessageId='bebebebe-bebe-bebe-bebe-bebebebebebe'
            """));
        Assert.Equal(1, await database.ExecuteScalarAsync<long>("""
            SELECT COUNT(*) FROM RuntimeUsageOutboxDeferrals
            WHERE OperationId='cdcdcdcd-cdcd-cdcd-cdcd-cdcdcdcdcdcd'
            """));
        Assert.Null(await database.ExecuteScalarOrNullAsync("""
            SELECT MessageId::text FROM ClaimRuntimeUsageOutbox('other-collector', 300, 1)
            """));

        Assert.True(await database.ExecuteScalarAsync<bool>("""
            SELECT WasReplay FROM DeferRuntimeUsageOutbox(
                'bebebebe-bebe-bebe-bebe-bebebebebebe', 'defer-collector',
                'cdcdcdcd-cdcd-cdcd-cdcd-cdcdcdcdcdcd',
                (SELECT NotBeforeUtc FROM RuntimeUsageOutboxDeferrals
                 WHERE OperationId='cdcdcdcd-cdcd-cdcd-cdcd-cdcdcdcdcdcd'),
                'assembly-waiting')
            """));
        Assert.Equal("22023", await database.TryExecuteAsync("""
            SELECT * FROM DeferRuntimeUsageOutbox(
                'bebebebe-bebe-bebe-bebe-bebebebebebe', 'defer-collector',
                'cdcdcdcd-cdcd-cdcd-cdcd-cdcdcdcdcdcd',
                (SELECT NotBeforeUtc FROM RuntimeUsageOutboxDeferrals
                 WHERE OperationId='cdcdcdcd-cdcd-cdcd-cdcd-cdcdcdcdcdcd'),
                'different-reason')
            """));

        await database.ExecuteAsync("""
            UPDATE RuntimeUsageOutboxDelivery
            SET DeliveryState='quarantined', NotBeforeUtc=NOW(), QuarantinedUtc=NOW(),
                QuarantineCategory='operator-review', UpdatedUtc=NOW()
            WHERE MessageId='bebebebe-bebe-bebe-bebe-bebebebebebe';
            """);
        Assert.True(await database.ExecuteScalarAsync<bool>("""
            SELECT WasReplay FROM DeferRuntimeUsageOutbox(
                'bebebebe-bebe-bebe-bebe-bebebebebebe', 'defer-collector',
                'cdcdcdcd-cdcd-cdcd-cdcd-cdcdcdcdcdcd',
                (SELECT NotBeforeUtc FROM RuntimeUsageOutboxDeferrals
                 WHERE OperationId='cdcdcdcd-cdcd-cdcd-cdcd-cdcdcdcdcdcd'),
                'assembly-waiting')
            """));

        var mutateReceipt = await Assert.ThrowsAsync<PostgresException>(() => database.ExecuteAsync("""
            UPDATE RuntimeUsageOutboxDeferrals SET FailureCategory='rewritten'
            WHERE OperationId='cdcdcdcd-cdcd-cdcd-cdcd-cdcdcdcdcdcd'
            """));
        Assert.Equal("55000", mutateReceipt.SqlState);
    }

    [PostgreSqlIntegrationFact]
    [Trait("Category", "PostgreSqlIntegration")]
    public async Task DeliveryBoundary_RejectsNullAndInvalidRequestsWithoutMutation()
    {
        await using var database = await RuntimeUsageTestDatabase.CreateAsync();

        Assert.Equal("22023", await database.TryExecuteAsync(
            "SELECT * FROM ClaimRuntimeUsageOutbox(NULL, 300, 1)"));
        Assert.Equal("22023", await database.TryExecuteAsync(
            "SELECT * FROM ClaimRuntimeUsageOutbox('collector', NULL, 1)"));
        Assert.Equal("22023", await database.TryExecuteAsync(
            "SELECT * FROM ClaimRuntimeUsageOutbox('collector', 300, NULL)"));
        Assert.Equal("22023", await database.TryExecuteAsync("""
            SELECT * FROM AcknowledgeRuntimeUsageOutbox(
                NULL, 'collector', decode(repeat('11', 32), 'hex'))
            """));
        Assert.Equal("22023", await database.TryExecuteAsync("""
            SELECT * FROM AcknowledgeRuntimeUsageOutbox(
                'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa', 'collector', NULL)
            """));
        Assert.Equal("22023", await database.TryExecuteAsync("""
            SELECT * FROM QuarantineRuntimeUsageOutbox(
                'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa', 'collector', NULL)
            """));
        Assert.Equal("22023", await database.TryExecuteAsync("""
            SELECT * FROM DeferRuntimeUsageOutbox(
                'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa', 'collector',
                'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb', clock_timestamp() - INTERVAL '1 minute', 'retry')
            """));
        Assert.Equal("22023", await database.TryExecuteAsync("""
            SELECT * FROM DeferRuntimeUsageOutbox(
                'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa', 'collector',
                'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb', clock_timestamp() + INTERVAL '25 hours', 'retry')
            """));
    }

    private sealed class RuntimeUsageTestDatabase : IAsyncDisposable
    {
        private const string ConnectionVariable = "OPENJIBO_TEST_POSTGRES_CONNECTION_STRING";
        private readonly string _adminConnectionString;
        private readonly string _schemaName;

        private RuntimeUsageTestDatabase(string adminConnectionString, string schemaName)
        {
            _adminConnectionString = adminConnectionString;
            _schemaName = schemaName;
            ConnectionString = new NpgsqlConnectionStringBuilder(adminConnectionString)
            {
                SearchPath = schemaName,
                ApplicationName = "OpenJibo.RuntimeUsage.IntegrationTests",
                MaxPoolSize = 4
            }.ConnectionString;
        }

        private string ConnectionString { get; }

        internal static async Task<RuntimeUsageTestDatabase> CreateAsync()
        {
            var admin = Environment.GetEnvironmentVariable(ConnectionVariable)
                        ?? throw new InvalidOperationException($"Set {ConnectionVariable}.");
            var schema = $"openjibo_runtime_usage_test_{Guid.NewGuid():N}";
            var database = new RuntimeUsageTestDatabase(admin, schema);
            await using (var connection = new NpgsqlConnection(admin))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = $"CREATE SCHEMA {QuoteIdentifier(schema)}";
                await command.ExecuteNonQueryAsync();
            }

            try
            {
                await database.ApplyMigrationAsync();
                return database;
            }
            catch
            {
                await database.DisposeAsync();
                throw;
            }
        }

        internal async Task ApplyMigrationAsync()
        {
            foreach (var fileName in new[]
                     {
                         "011_create_runtime_usage_outbox.state.sql",
                         "012_runtime_usage_delivery_boundary.state.sql",
                         "015_runtime_usage_defer_boundary.state.sql"
                     })
            {
                var path = Path.Combine(AppContext.BaseDirectory, "Migrations", "PostgreSql", fileName);
                await ExecuteAsync(await File.ReadAllTextAsync(path));
            }
        }

        internal async Task ExecuteAsync(string sql)
        {
            await using var connection = new NpgsqlConnection(ConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            await command.ExecuteNonQueryAsync();
        }

        internal async Task<string?> TryExecuteAsync(string sql)
        {
            try
            {
                await ExecuteAsync(sql);
                return null;
            }
            catch (PostgresException exception)
            {
                return exception.SqlState;
            }
        }

        internal async Task<T> ExecuteScalarAsync<T>(string sql)
        {
            await using var connection = new NpgsqlConnection(ConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            var value = await command.ExecuteScalarAsync();
            return (T)Convert.ChangeType(value!, typeof(T), System.Globalization.CultureInfo.InvariantCulture);
        }

        internal async Task<object?> ExecuteScalarOrNullAsync(string sql)
        {
            await using var connection = new NpgsqlConnection(ConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            return await command.ExecuteScalarAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await using var connection = new NpgsqlConnection(_adminConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"DROP SCHEMA {QuoteIdentifier(_schemaName)} CASCADE";
            await command.ExecuteNonQueryAsync();
        }

        private static string QuoteIdentifier(string identifier)
        {
            Debug.Assert(identifier.StartsWith("openjibo_runtime_usage_test_", StringComparison.Ordinal));
            return $"\"{identifier.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
        }
    }
}
