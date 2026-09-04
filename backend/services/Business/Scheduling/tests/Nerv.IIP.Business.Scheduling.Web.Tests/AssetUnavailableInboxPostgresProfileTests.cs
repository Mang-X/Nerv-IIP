using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.Scheduling.Domain.AggregatesModel.SchedulePlanAggregate;
using Nerv.IIP.Business.Scheduling.Infrastructure;
using Nerv.IIP.Business.Scheduling.Infrastructure.IntegrationEvents;
using Nerv.IIP.Business.Scheduling.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Contracts.Maintenance;
using Nerv.IIP.Testing;
using Npgsql;

namespace Nerv.IIP.Business.Scheduling.Web.Tests;

[Collection(SchedulingPostgresLaneDatabase.CollectionName)]
public sealed class AssetUnavailableInboxPostgresProfileTests
{
    [SchedulingPostgresFact]
    public Task Concurrent_claims_with_same_event_id_and_different_business_keys_commit_one_result() =>
        RunRaceAsync("event-shared", "key-a", "event-shared", "key-b");

    [SchedulingPostgresFact]
    public Task Concurrent_claims_with_different_event_ids_and_same_business_key_commit_one_result() =>
        RunRaceAsync("event-a", "key-shared", "event-b", "key-shared");

    private const string PreIdentityMigration = "20260731210209_PersistSchedulePlanBlockWindows";

    [SchedulingPostgresFact]
    public async Task Migration_upgrades_distinct_historical_event_instances_and_old_schema_already_forbids_same_key_duplicates()
    {
        await SchedulingPostgresLaneDatabase.ResetSchemaAsync();
        await using var provider = CreateProvider();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var migrator = db.Database.GetService<IMigrator>();
        await migrator.MigrateAsync(PreIdentityMigration);
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO scheduling.processed_integration_events
                ("Id", "ConsumerName", "EventId", "EventType", "EventVersion", "SourceService", "IdempotencyKey", "ProcessedAtUtc")
            VALUES
                ('00000000-0000-0000-0000-000000000001', 'business-scheduling.asset-unavailable', 'historical-event-a',
                 'maintenance.AssetUnavailable', 1, 'maintenance', 'historical-key-a', '2026-06-01T09:00:00Z'),
                ('00000000-0000-0000-0000-000000000002', 'business-scheduling.asset-unavailable', 'historical-event-b',
                 'maintenance.AssetUnavailable', 1, 'maintenance', 'historical-key-b', '2026-06-01T10:00:00Z');
            """);
        // 旧 schema 已经不允许同 consumer 下重复的 IdempotencyKey：迁移不需要、也不应该带“同 key 自动清理”分支。
        var sameKeyRejection = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync("""
            INSERT INTO scheduling.processed_integration_events
                ("Id", "ConsumerName", "EventId", "EventType", "EventVersion", "SourceService", "IdempotencyKey", "ProcessedAtUtc")
            VALUES
                ('00000000-0000-0000-0000-000000000003', 'business-scheduling.asset-unavailable', 'historical-event-a',
                 'maintenance.AssetUnavailable', 1, 'maintenance', 'historical-key-a', '2026-06-01T11:00:00Z');
            """));
        Assert.Equal(PostgresErrorCodes.UniqueViolation, sameKeyRejection.SqlState);
        Assert.Equal("ux_processed_integration_events_consumer_idempotency_key", sameKeyRejection.ConstraintName);

        await migrator.MigrateAsync();

        var rows = await db.ProcessedIntegrationEvents.AsNoTracking().OrderBy(x => x.IdempotencyKey).ToArrayAsync();
        Assert.Equal(["historical-key-a", "historical-key-b"], rows.Select(x => x.IdempotencyKey));
        await AssertExactUniqueIndexesAsync(db);
    }

    [SchedulingPostgresFact]
    public async Task Migration_fails_closed_on_historical_event_instance_with_ambiguous_business_keys()
    {
        await SchedulingPostgresLaneDatabase.ResetSchemaAsync();
        await using var provider = CreateProvider();
        await using (var setup = provider.CreateAsyncScope())
        {
            var setupDb = setup.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await setupDb.Database.GetService<IMigrator>().MigrateAsync(PreIdentityMigration);
            // 同一事件下出现过两个业务键：删掉任一行都会让该业务键的 inbox 痕迹消失，迁移必须中止。
            await setupDb.Database.ExecuteSqlRawAsync("""
                INSERT INTO scheduling.processed_integration_events
                    ("Id", "ConsumerName", "EventId", "EventType", "EventVersion", "SourceService", "IdempotencyKey", "ProcessedAtUtc")
                VALUES
                    ('00000000-0000-0000-0000-000000000002', 'business-scheduling.asset-unavailable', 'historical-event',
                     'maintenance.AssetUnavailable', 1, 'maintenance', 'historical-key-later', '2026-06-01T10:00:00Z'),
                    ('00000000-0000-0000-0000-000000000001', 'business-scheduling.asset-unavailable', 'historical-event',
                     'maintenance.AssetUnavailable', 1, 'maintenance', 'historical-key-earlier', '2026-06-01T09:00:00Z');
                """);
        }

        PostgresException failure;
        await using (var upgrade = provider.CreateAsyncScope())
        {
            var upgradeDb = upgrade.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            failure = await Assert.ThrowsAsync<PostgresException>(() => upgradeDb.Database.GetService<IMigrator>().MigrateAsync());
        }

        Assert.Equal(PostgresErrorCodes.IntegrityConstraintViolation, failure.SqlState);
        Assert.Contains("AddSchedulingProcessedEventInstanceIdentity aborted", failure.MessageText, StringComparison.Ordinal);
        Assert.Contains(
            "business-scheduling.asset-unavailable/historical-event: historical-key-earlier@2026-06-01T09:00:00.000000Z, historical-key-later@2026-06-01T10:00:00.000000Z",
            failure.MessageText,
            StringComparison.Ordinal);

        await using var verify = provider.CreateAsyncScope();
        var db = verify.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var rows = await db.ProcessedIntegrationEvents.AsNoTracking().OrderBy(x => x.IdempotencyKey).ToArrayAsync();
        Assert.Equal(["historical-key-earlier", "historical-key-later"], rows.Select(x => x.IdempotencyKey));
        Assert.DoesNotContain(
            "20260831142730_AddSchedulingProcessedEventInstanceIdentity",
            await db.Database.GetAppliedMigrationsAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => AssertExactUniqueIndexesAsync(db));
    }

    [SchedulingPostgresFact]
    public async Task Event_id_unique_index_wrong_column_mutation_is_rejected()
    {
        await SchedulingPostgresLaneDatabase.ResetSchemaAsync();
        await using var provider = CreateProvider();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.MigrateAsync();
        await db.Database.ExecuteSqlRawAsync("""
            DROP INDEX scheduling.ux_processed_integration_events_consumer_event_id;
            CREATE UNIQUE INDEX ux_processed_integration_events_consumer_event_id
                ON scheduling.processed_integration_events ("ConsumerName", "EventId", "IdempotencyKey");
            """);
        await Assert.ThrowsAsync<InvalidOperationException>(() => AssertExactUniqueIndexesAsync(db));
    }

    [SchedulingPostgresFact]
    public async Task Idempotency_key_unique_index_wrong_column_mutation_is_rejected()
    {
        await SchedulingPostgresLaneDatabase.ResetSchemaAsync();
        await using var provider = CreateProvider();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.MigrateAsync();
        await db.Database.ExecuteSqlRawAsync("""
            DROP INDEX scheduling.ux_processed_integration_events_consumer_idempotency_key;
            CREATE UNIQUE INDEX ux_processed_integration_events_consumer_idempotency_key
                ON scheduling.processed_integration_events ("ConsumerName", "IdempotencyKey", "EventId");
            """);
        await Assert.ThrowsAsync<InvalidOperationException>(() => AssertExactUniqueIndexesAsync(db));
    }

    /// <summary>
    /// 双身份 claim 必须在工作单元事务内执行：advisory lock 是事务级的，没有事务就没有串行化，claim 的
    /// "先查后写"会退化成竞态。守卫必须在锁执行器里 fail-closed，而不是靠调用约定。
    /// </summary>
    [SchedulingPostgresFact]
    public async Task Inbox_identity_lock_fails_closed_without_an_active_transaction()
    {
        await SchedulingPostgresLaneDatabase.ResetSchemaAsync();
        await using var provider = CreateProvider();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.MigrateAsync();
        var identityLock = scope.ServiceProvider.GetRequiredService<IAssetUnavailableInboxIdentityLock>();

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            SchedulingProcessedIntegrationEventInbox.TryRecordAssetUnavailableAsync(
                db,
                identityLock,
                AssetUnavailableIntegrationEventHandlerForInvalidateSchedulePlans.ConsumerName,
                Event("event-no-tx", "key-no-tx"),
                CancellationToken.None));

        Assert.Contains("active unit-of-work transaction", failure.Message, StringComparison.Ordinal);
        Assert.Empty(await db.ProcessedIntegrationEvents.AsNoTracking().ToArrayAsync());
        await using var transaction = await db.Database.BeginTransactionAsync();
        Assert.True(await SchedulingProcessedIntegrationEventInbox.TryRecordAssetUnavailableAsync(
            db,
            identityLock,
            AssetUnavailableIntegrationEventHandlerForInvalidateSchedulePlans.ConsumerName,
            Event("event-no-tx", "key-no-tx"),
            CancellationToken.None));
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        Assert.Single(await db.ProcessedIntegrationEvents.AsNoTracking().ToArrayAsync());
    }

    /// <summary>
    /// 唯一索引是 claim 的最后一道防线，而防线的另一半是 <c>ApplicationDbContext.SaveChanges</c> 把两条 inbox
    /// 索引的冲突吞成 0 行而不是抛出：否则并发中被吸收的那一方会以异常逃逸成 CAP poison。
    /// 两条索引各验一次，异步与同步 SaveChanges 各验一次。
    /// </summary>
    [SchedulingPostgresFact]
    public Task Save_changes_absorbs_event_id_inbox_conflict_as_zero_rows() =>
        RunSaveChangesConflictAsync("event-shared", "key-a", "event-shared", "key-b");

    [SchedulingPostgresFact]
    public Task Save_changes_absorbs_idempotency_key_inbox_conflict_as_zero_rows() =>
        RunSaveChangesConflictAsync("event-a", "key-shared", "event-b", "key-shared");

    private static async Task RunSaveChangesConflictAsync(string firstEventId, string firstKey, string secondEventId, string secondKey)
    {
        await SchedulingPostgresLaneDatabase.ResetSchemaAsync();
        await using var provider = CreateProvider();
        await using (var setup = provider.CreateAsyncScope())
        {
            var setupDb = setup.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await setupDb.Database.MigrateAsync();
            setupDb.ProcessedIntegrationEvents.Add(ProcessedRecord(firstEventId, firstKey));
            Assert.Equal(1, await setupDb.SaveChangesAsync());
        }

        await using (var conflict = provider.CreateAsyncScope())
        {
            var db = conflict.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.ProcessedIntegrationEvents.Add(ProcessedRecord(secondEventId, secondKey));
            Assert.Equal(0, await db.SaveChangesAsync());
            Assert.Empty(db.ChangeTracker.Entries());
        }

        await using (var conflictSync = provider.CreateAsyncScope())
        {
            var db = conflictSync.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.ProcessedIntegrationEvents.Add(ProcessedRecord(secondEventId, secondKey));
            Assert.Equal(0, db.SaveChanges());
            Assert.Empty(db.ChangeTracker.Entries());
        }

        await using var verify = provider.CreateAsyncScope();
        var verifyDb = verify.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var survivor = Assert.Single(await verifyDb.ProcessedIntegrationEvents.AsNoTracking().ToArrayAsync());
        Assert.Equal((firstEventId, firstKey), (survivor.EventId, survivor.IdempotencyKey));
    }

    private static ProcessedIntegrationEvent ProcessedRecord(string eventId, string idempotencyKey) => new(
        AssetUnavailableIntegrationEventHandlerForInvalidateSchedulePlans.ConsumerName,
        eventId,
        MaintenanceIntegrationEventTypes.AssetUnavailable,
        MaintenanceIntegrationEventVersions.V2,
        MaintenanceIntegrationEventSources.BusinessMaintenance,
        idempotencyKey,
        DateTimeOffset.UtcNow);

    private static async Task RunRaceAsync(string firstEventId, string firstKey, string secondEventId, string secondKey)
    {
        await SchedulingPostgresLaneDatabase.ResetSchemaAsync();
        await using var provider = CreateProvider();
        await using (var setup = provider.CreateAsyncScope())
        {
            var setupDb = setup.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await setupDb.Database.MigrateAsync();
            await AssertExactUniqueIndexesAsync(setupDb);
        }

        var firstClaimed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = CompeteAsync(provider, firstEventId, firstKey, "plan-a", null, firstClaimed, releaseFirst.Task);
        try
        {
            await firstClaimed.Task;
            var second = CompeteAsync(provider, secondEventId, secondKey, "plan-b", secondStarted, null, Task.CompletedTask);
            await secondStarted.Task;
            try
            {
                await WaitForBlockedAdvisoryClaimAsync(provider);
            }
            finally
            {
                // 屏障等待超时也必须放行首事务：否则两个竞争者的事务永久悬挂，后续用例的 DROP SCHEMA … CASCADE
                // 会被阻塞，一次真红被放大成整类红。
                releaseFirst.TrySetResult();
            }
            await Task.WhenAll(first, second);
        }
        finally
        {
            releaseFirst.TrySetResult();
        }

        await using var verify = provider.CreateAsyncScope();
        var db = verify.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Single(await db.ProcessedIntegrationEvents.AsNoTracking().ToArrayAsync());
        Assert.Single(await db.SchedulePlanInvalidations.AsNoTracking().ToArrayAsync());
    }

    /// <remarks>
    /// Each observation owns its own scope and context: <c>Eventually</c> abandons an in-flight observation
    /// when the window closes, so nothing shared may be disposed underneath it.
    /// </remarks>
    private static async Task WaitForBlockedAdvisoryClaimAsync(ServiceProvider provider)
    {
        await Eventually.WaitAsync(
            condition: "the second competitor is blocked on the Scheduling inbox advisory claim",
            observe: async token =>
            {
                await using var scope = provider.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                return await db.Database.SqlQueryRaw<int>("""
                    SELECT COUNT(*)::int AS "Value"
                    FROM pg_locks
                    WHERE locktype = 'advisory'
                      AND database = (SELECT oid FROM pg_database WHERE datname = current_database())
                      AND NOT granted
                    """).SingleAsync(token);
            },
            isSatisfied: waiting => waiting > 0,
            describe: waiting => $"advisoryLockWaiters={waiting}; expected>=1",
            options: new EventuallyOptions(
                Timeout: TimeSpan.FromSeconds(15),
                PollInterval: TimeSpan.FromMilliseconds(50),
                SensitiveValues: [SchedulingPostgresLaneDatabase.ConnectionString]));
    }

    private static async Task AssertExactUniqueIndexesAsync(ApplicationDbContext db)
    {
        var definitions = await db.Database.SqlQueryRaw<string>("""
            SELECT indexdef AS "Value"
            FROM pg_indexes
            WHERE schemaname = 'scheduling'
              AND tablename = 'processed_integration_events'
              AND indexdef LIKE 'CREATE UNIQUE INDEX%'
            """).ToArrayAsync();
        if (!definitions.Any(value => value.EndsWith("(\"ConsumerName\", \"EventId\")", StringComparison.Ordinal)) ||
            !definitions.Any(value => value.EndsWith("(\"ConsumerName\", \"IdempotencyKey\")", StringComparison.Ordinal)))
            throw new InvalidOperationException("Scheduling inbox requires exact, independent EventId and IdempotencyKey unique indexes.");
    }

    private static ServiceProvider CreateProvider()
    {
        var services = new ServiceCollection();
        services.AddMediatR(configuration => configuration.RegisterServicesFromAssembly(typeof(Program).Assembly));
        services.AddSchedulingPostgreSqlPersistence(SchedulingPostgresLaneDatabase.ConnectionString);
        return services.BuildServiceProvider();
    }

    private static async Task CompeteAsync(
        ServiceProvider provider,
        string eventId,
        string idempotencyKey,
        string planId,
        TaskCompletionSource? started,
        TaskCompletionSource? claimed,
        Task release)
    {
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync();
        started?.SetResult();
        var integrationEvent = Event(eventId, idempotencyKey);
        var won = await SchedulingProcessedIntegrationEventInbox.TryRecordAssetUnavailableAsync(
            db,
            scope.ServiceProvider.GetRequiredService<IAssetUnavailableInboxIdentityLock>(),
            AssetUnavailableIntegrationEventHandlerForInvalidateSchedulePlans.ConsumerName,
            integrationEvent,
            CancellationToken.None);
        claimed?.SetResult();
        if (won)
        {
            db.SchedulePlanInvalidations.Add(SchedulePlanInvalidation.Create(
                "org-001", "env-dev", planId, eventId, integrationEvent.EventType,
                integrationEvent.SourceService, "equipmentUnavailable", "ASSET-CNC-01", null, null, null,
                integrationEvent.OccurredAtUtc, DateTimeOffset.UtcNow));
        }
        await release;
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    private static AssetUnavailableV2IntegrationEvent Event(string eventId, string idempotencyKey) => new(
        eventId,
        MaintenanceIntegrationEventTypes.AssetUnavailable,
        MaintenanceIntegrationEventVersions.V2,
        DateTimeOffset.Parse("2026-06-01T09:00:00Z"),
        MaintenanceIntegrationEventSources.BusinessMaintenance,
        $"correlation-{eventId}",
        string.Empty,
        "org-001",
        "env-dev",
        "system:test",
        idempotencyKey,
        new AssetUnavailableV2Payload(
            "ASSET-CNC-01",
            "breakdown",
            DateTimeOffset.Parse("2026-06-01T09:00:00Z")));
}
