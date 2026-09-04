using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Business.Mes.Infrastructure.IntegrationEvents;
using Nerv.IIP.Business.Mes.Infrastructure.Repositories;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Schedules;
using Nerv.IIP.Business.Mes.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Business.Mes.Web.Application.Planning;
using Nerv.IIP.Business.Mes.Web.Application.Scheduling;
using Nerv.IIP.Contracts.IntegrationEvents;
using Nerv.IIP.Contracts.Maintenance;
using Nerv.IIP.Messaging.CAP;
using Npgsql;

namespace Nerv.IIP.Business.Mes.Web.Tests;

[Collection(WebApplicationFactoryCollection.Name)]
public sealed class MaintenanceEventHandlerTests
{
    private const string MigrationBeforeEventInstanceIdentity = "20260902071600_AddMesShiftHandoverAttachments";

    /// <summary>
    /// P1-2 / #2964：v1 与 v2 在两个独立事务里争夺同一停机事实。第一个竞争者赢得双身份 claim 后被夹具停在
    /// 副作用入口之前；第二个竞争者必须被 PostgreSQL advisory 锁挡在 claim 这一行（用 pg_stat_activity 观察到
    /// 真实的 waiter），根本走不到副作用；释放后只有一条收件箱行、一条停机事实、一次重排。
    /// </summary>
    [PostgreSqlFact]
    public async Task PostgreSQL_v1_v2_concurrent_claims_commit_one_business_effect_across_independent_transactions()
    {
        await MesPostgresLaneDatabase.ResetSchemaAsync();
        var fromUtc = DateTimeOffset.Parse("2026-05-22T08:00:00Z");
        const string idempotencyKey = "maintenance.AssetUnavailable:ASSET-CNC-01:20260522080000";
        var v1 = CreateUnavailableEvent(fromUtc) with { EventId = "evt-race-v1", IdempotencyKey = idempotencyKey };
        var v2 = CreateUnavailableV2Event(fromUtc, idempotencyKey) with { EventId = "evt-race-v2" };

        await RunClaimRaceAsync(
            first: (scope, token) => scope.GetRequiredService<AssetUnavailableIntegrationEventHandlerForReschedule>().HandleAsync(v1, token),
            second: (scope, token) => scope.GetRequiredService<AssetUnavailableV2IntegrationEventHandlerForReschedule>().HandleAsync(v2, token),
            expectedWinnerEventId: v1.EventId,
            expectedIdempotencyKey: idempotencyKey);
    }

    /// <summary>
    /// P1-5：约束完整时「相同 EventId、不同 IdempotencyKey」的正向并发对照——事件实例身份单独就能挡住第二个竞争者。
    /// </summary>
    [PostgreSqlFact]
    public async Task PostgreSQL_same_event_id_with_different_business_keys_commits_one_business_effect_across_independent_transactions()
    {
        await MesPostgresLaneDatabase.ResetSchemaAsync();
        var fromUtc = DateTimeOffset.Parse("2026-05-22T08:00:00Z");
        var first = CreateUnavailableV2Event(fromUtc, "maintenance.AssetUnavailable:ASSET-CNC-01:key-a") with { EventId = "evt-race-shared" };
        var second = CreateUnavailableV2Event(fromUtc, "maintenance.AssetUnavailable:ASSET-CNC-01:key-b") with { EventId = "evt-race-shared" };

        await RunClaimRaceAsync(
            first: (scope, token) => scope.GetRequiredService<AssetUnavailableV2IntegrationEventHandlerForReschedule>().HandleAsync(first, token),
            second: (scope, token) => scope.GetRequiredService<AssetUnavailableV2IntegrationEventHandlerForReschedule>().HandleAsync(second, token),
            expectedWinnerEventId: "evt-race-shared",
            expectedIdempotencyKey: first.IdempotencyKey);
    }

    private static async Task RunClaimRaceAsync(
        Func<IServiceProvider, CancellationToken, Task> first,
        Func<IServiceProvider, CancellationToken, Task> second,
        string expectedWinnerEventId,
        string expectedIdempotencyKey)
    {
        var gate = new ClaimRaceGate();
        await using var factory = CreatePipelineFactory(services =>
        {
            services.RemoveAll<IMesPlanningStore>();
            services.AddScoped<IMesPlanningStore>(provider => new ClaimRaceGatePlanningStore(
                new PersistentMesPlanningStore(
                    provider.GetRequiredService<ApplicationDbContext>(),
                    provider.GetRequiredService<IOperationTaskRepository>()),
                gate));
        });
        using var client = factory.CreateClient();
        await MigrateAsync(factory);
        await AssertExactInboxUniqueIndexesAsync(factory);
        await SeedScheduleFactsAsync(factory);

        using var firstScope = factory.Services.CreateScope();
        using var secondScope = factory.Services.CreateScope();
        using var raceTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var firstTask = first(firstScope.ServiceProvider, raceTimeout.Token);
        await gate.FirstPassedClaim.Task.WaitAsync(TimeSpan.FromSeconds(15));

        var secondTask = second(secondScope.ServiceProvider, raceTimeout.Token);
        await MesPostgresAdvisoryLockProbe.WaitForWaitersAsync(
            MesPostgresLaneDatabase.ConnectionString,
            expectedWaiters: 1,
            scopeDescription: "MES asset-unavailable inbox claim");
        Assert.False(secondTask.IsCompleted, "The second competitor must stay blocked on the claim, not finish while the winner still holds it.");
        Assert.Equal(1, gate.SideEffectEntries);

        gate.ReleaseFirst.SetResult();
        await Task.WhenAll(firstTask, secondTask);

        Assert.Equal(1, gate.SideEffectEntries);
        using var assertionScope = factory.Services.CreateScope();
        var db = assertionScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var inbox = Assert.Single(await db.ProcessedIntegrationEvents.AsNoTracking().ToArrayAsync());
        Assert.Equal(expectedWinnerEventId, inbox.EventId);
        Assert.Equal(expectedIdempotencyKey, inbox.IdempotencyKey);
        Assert.Equal(1, await db.WorkCenterUnavailabilities.AsNoTracking().CountAsync());
        Assert.Equal(1, await db.ScheduleResults.AsNoTracking().CountAsync());
    }

    /// <summary>
    /// P1-1 第一支的真实形态：「同 consumer / 同 EventId / 同 IdempotencyKey」的真重复在 MES 的任何合法 schema 版本上都
    /// 不可表示——移除 EventId 唯一索引的那条历史 migration 同时建立了 (ConsumerName, IdempotencyKey) 唯一索引。
    /// 因此 migration 没有「保留最早行」的自动清理分支可走；这里用迁移前 schema 直接证明该前置不可能成立，
    /// 再证明只含合法历史的库能顺利前滚并恰好得到两条唯一索引。
    /// </summary>
    [PostgreSqlFact]
    public async Task PostgreSQL_true_duplicate_event_instances_are_unrepresentable_before_the_migration()
    {
        await MesPostgresLaneDatabase.ResetSchemaAsync();
        var options = MesPostgresLaneDatabase.CreateOptions();
        await using var db = CreateDbContext(options);
        MesPostgresLaneDatabase.AssertUsesGovernedDatabase(db);
        var migrator = db.Database.GetService<IMigrator>();
        await migrator.MigrateAsync(MigrationBeforeEventInstanceIdentity);
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO mes.processed_integration_events
                ("Id", "ConsumerName", "EventId", "EventType", "EventVersion", "SourceService", "IdempotencyKey", "ProcessedAtUtc")
            VALUES
                ('00000000-0000-0000-0000-000000000001', 'business-mes.asset-unavailable', 'evt-historical',
                 'maintenance.AssetUnavailable', 1, 'maintenance', 'key-same', '2026-06-01T09:00:00Z');
            """);

        var rejected = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync("""
            INSERT INTO mes.processed_integration_events
                ("Id", "ConsumerName", "EventId", "EventType", "EventVersion", "SourceService", "IdempotencyKey", "ProcessedAtUtc")
            VALUES
                ('00000000-0000-0000-0000-000000000002', 'business-mes.asset-unavailable', 'evt-historical',
                 'maintenance.AssetUnavailable', 1, 'maintenance', 'key-same', '2026-06-01T10:00:00Z');
            """));
        Assert.Equal(PostgresErrorCodes.UniqueViolation, rejected.SqlState);
        Assert.Equal("ux_processed_integration_events_consumer_idempotency_key", rejected.ConstraintName);

        await migrator.MigrateAsync();

        var survivor = Assert.Single(await db.ProcessedIntegrationEvents.AsNoTracking().ToArrayAsync());
        Assert.Equal("00000000-0000-0000-0000-000000000001", survivor.Id.ToString());
        await AssertExactInboxUniqueIndexesAsync(db);
    }

    /// <summary>
    /// P1-1 第二支：同 consumer / 同 EventId 但 IdempotencyKey 不同是语义歧义，migration 必须 fail-closed 中止，
    /// 诊断列出冲突行标识，且既不删行也不建索引、不写迁移历史。
    /// </summary>
    [PostgreSqlFact]
    public async Task PostgreSQL_migration_fails_closed_on_ambiguous_event_instance_history()
    {
        await MesPostgresLaneDatabase.ResetSchemaAsync();
        var options = MesPostgresLaneDatabase.CreateOptions();
        await using var db = CreateDbContext(options);
        MesPostgresLaneDatabase.AssertUsesGovernedDatabase(db);
        var migrator = db.Database.GetService<IMigrator>();
        await migrator.MigrateAsync(MigrationBeforeEventInstanceIdentity);
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO mes.processed_integration_events
                ("Id", "ConsumerName", "EventId", "EventType", "EventVersion", "SourceService", "IdempotencyKey", "ProcessedAtUtc")
            VALUES
                ('00000000-0000-0000-0000-000000000011', 'business-mes.asset-unavailable', 'evt-ambiguous',
                 'maintenance.AssetUnavailable', 1, 'maintenance', 'key-earlier', '2026-06-01T09:00:00Z'),
                ('00000000-0000-0000-0000-000000000012', 'business-mes.asset-unavailable', 'evt-ambiguous',
                 'maintenance.AssetUnavailable', 2, 'business-maintenance', 'key-later', '2026-06-01T10:00:00Z'),
                ('00000000-0000-0000-0000-000000000013', 'business-mes.asset-unavailable', 'evt-clean',
                 'maintenance.AssetUnavailable', 1, 'maintenance', 'key-clean', '2026-06-01T08:00:00Z'),
                ('00000000-0000-0000-0000-000000000014', 'business-mes.asset-restored', 'evt-ambiguous',
                 'maintenance.AssetRestored', 1, 'maintenance', 'key-other-consumer', '2026-06-01T08:30:00Z');
            """);

        var failure = await Assert.ThrowsAsync<PostgresException>(() => migrator.MigrateAsync());

        // 冲突清单必须在 MessageText 里：Npgsql 默认脱敏 Detail，运维只看得到 message。
        Assert.Contains("AddMesProcessedEventInstanceIdentity aborted", failure.MessageText, StringComparison.Ordinal);
        Assert.Contains("business-mes.asset-unavailable / evt-ambiguous / key-earlier / 2026-06-01T09:00:00.000000Z", failure.MessageText, StringComparison.Ordinal);
        Assert.Contains("business-mes.asset-unavailable / evt-ambiguous / key-later / 2026-06-01T10:00:00.000000Z", failure.MessageText, StringComparison.Ordinal);
        Assert.DoesNotContain("evt-clean", failure.MessageText, StringComparison.Ordinal);
        Assert.DoesNotContain("business-mes.asset-restored", failure.MessageText, StringComparison.Ordinal);
        Assert.Equal("23000", failure.SqlState);
        // fail-closed：数据一行不动，索引不建，迁移历史不写。
        Assert.Equal(4, await db.ProcessedIntegrationEvents.AsNoTracking().CountAsync());
        Assert.DoesNotContain(
            await ReadUniqueIndexDefinitionsAsync(db),
            definition => definition.Contains("ux_processed_integration_events_consumer_event_id", StringComparison.Ordinal));
        var applied = await db.Database.GetAppliedMigrationsAsync();
        Assert.DoesNotContain(applied, id => id.EndsWith("_AddMesProcessedEventInstanceIdentity", StringComparison.Ordinal));
    }

    [PostgreSqlFact]
    public Task PostgreSQL_idempotency_unique_constraint_rejects_its_equivalent_wrong_mutation() =>
        AssertRemovedInboxConstraintAllowsDuplicateAsync(
            "ux_processed_integration_events_consumer_idempotency_key",
            reuseEventId: false);

    [PostgreSqlFact]
    public Task PostgreSQL_event_id_unique_constraint_rejects_its_equivalent_wrong_mutation() =>
        AssertRemovedInboxConstraintAllowsDuplicateAsync(
            "ux_processed_integration_events_consumer_event_id",
            reuseEventId: true);

    /// <summary>
    /// 两条唯一索引各自的反例。advisory 锁 + 事务内读写检查已经把并发竞争者挡在 claim 上，所以走完整
    /// 消费管线不可能再触到索引；这里直接在数据库层证明索引承重：约束完整时第二条同身份行被拒并被
    /// <see cref="ApplicationDbContext"/> 折叠成 0 行写入，删掉对应索引后同一写入就会留下第二行。
    /// </summary>
    private static async Task AssertRemovedInboxConstraintAllowsDuplicateAsync(
        string removedIndex,
        bool reuseEventId)
    {
        await MesPostgresLaneDatabase.ResetSchemaAsync();
        var options = MesPostgresLaneDatabase.CreateOptions();
        await using (var setup = CreateDbContext(options))
        {
            MesPostgresLaneDatabase.AssertUsesGovernedDatabase(setup);
            await setup.Database.MigrateAsync();
            await AssertExactInboxUniqueIndexesAsync(setup);
        }

        ProcessedIntegrationEvent Row(string eventId, string idempotencyKey) => new(
            AssetUnavailableIntegrationEventHandlerForReschedule.ConsumerName,
            eventId,
            MaintenanceIntegrationEventTypes.AssetUnavailable,
            MaintenanceIntegrationEventVersions.V1,
            MaintenanceIntegrationEventSources.Maintenance,
            idempotencyKey,
            DateTimeOffset.UtcNow);
        var first = Row("evt-mutated-first", "mutation-key-first");
        ProcessedIntegrationEvent Second() => reuseEventId
            ? Row("evt-mutated-first", "mutation-key-second")
            : Row("evt-mutated-second", "mutation-key-first");

        await using (var control = CreateDbContext(options))
        {
            control.ProcessedIntegrationEvents.Add(first);
            Assert.Equal(1, await control.SaveChangesAsync());
        }

        // 阳性对照：约束完整，第二条同身份行被唯一索引拒绝并折叠为 0。
        await using (var intact = CreateDbContext(options))
        {
            intact.ProcessedIntegrationEvents.Add(Second());
            Assert.Equal(0, await intact.SaveChangesAsync());
        }

        await using (var assertion = CreateDbContext(options))
        {
            Assert.Equal(1, await assertion.ProcessedIntegrationEvents.AsNoTracking().CountAsync());
            var mutationSql = removedIndex switch
            {
                "ux_processed_integration_events_consumer_idempotency_key" =>
                    "DROP INDEX mes.\"ux_processed_integration_events_consumer_idempotency_key\"",
                "ux_processed_integration_events_consumer_event_id" =>
                    "DROP INDEX mes.\"ux_processed_integration_events_consumer_event_id\"",
                _ => throw new ArgumentOutOfRangeException(nameof(removedIndex), removedIndex, null),
            };
            await assertion.Database.ExecuteSqlRawAsync(mutationSql);
        }

        // 变异：删掉承重索引后，同一写入不再被拒。
        await using (var mutated = CreateDbContext(options))
        {
            mutated.ProcessedIntegrationEvents.Add(Second());
            Assert.Equal(1, await mutated.SaveChangesAsync());
        }

        await using var final = CreateDbContext(options);
        Assert.Equal(2, await final.ProcessedIntegrationEvents.AsNoTracking().CountAsync());
    }

    private static async Task AssertExactInboxUniqueIndexesAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        await AssertExactInboxUniqueIndexesAsync(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
    }

    private static async Task AssertExactInboxUniqueIndexesAsync(ApplicationDbContext db)
    {
        var definitions = await ReadUniqueIndexDefinitionsAsync(db);
        Assert.Contains(definitions, x => x.EndsWith("(\"ConsumerName\", \"EventId\")", StringComparison.Ordinal));
        Assert.Contains(definitions, x => x.EndsWith("(\"ConsumerName\", \"IdempotencyKey\")", StringComparison.Ordinal));
    }

    private static Task<string[]> ReadUniqueIndexDefinitionsAsync(ApplicationDbContext db) =>
        db.Database.SqlQueryRaw<string>("""
            SELECT indexdef AS "Value"
            FROM pg_indexes
            WHERE schemaname = 'mes'
              AND tablename = 'processed_integration_events'
              AND indexdef LIKE 'CREATE UNIQUE INDEX%'
            """).ToArrayAsync();

    private static WebApplicationFactory<Program> CreatePipelineFactory(Action<IServiceCollection>? configureServices = null)
    {
        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:PostgreSQL"] = MesPostgresLaneDatabase.ConnectionString,
            ["Messaging:Provider"] = "InMemory",
            ["Cap:Version"] = "test-mes-2966-claim",
            ["InternalService:BearerToken"] = "test-internal-token",
        };
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            foreach (var (key, value) in settings)
            {
                builder.UseSetting(key, value);
            }

            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(settings));
            if (configureServices is not null)
            {
                builder.ConfigureServices(configureServices);
            }
        });
    }

    private static async Task MigrateAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        MesPostgresLaneDatabase.AssertUsesGovernedDatabase(db);
        await db.Database.MigrateAsync();
    }

    private static async Task SeedScheduleFactsAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var now = DateTimeOffset.Parse("2026-05-22T08:00:00Z");
        var store = scope.ServiceProvider.GetRequiredService<IMesPlanningStore>();
        store.MapDeviceAssetToWorkCenter("ASSET-CNC-01", "WC-A");
        store.AddWorkOrder(new PlannedWorkOrder("org-001", "env-dev", "WO-001", "SKU-1", null, 1m, 10, now.AddDays(1)));
        store.AddOperationTask(new PlannedOperationTask("WO-001", "OP-10", OperationTaskStatus.Queued, 10, "WC-A", [], now, TimeSpan.FromHours(2)));
        await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().SaveChangesAsync();
    }

    [Fact]
    public async Task AssetUnavailableHandler_RecordsOpenUnavailableWindowAndAutoReschedules()
    {
        var store = new InMemoryMesPlanningStore();
        var now = DateTimeOffset.Parse("2026-05-22T08:00:00Z");
        store.MapDeviceAssetToWorkCenter("ASSET-CNC-01", "WC-A");
        store.AddWorkOrder(new PlannedWorkOrder("org-001", "env-dev", "WO-001", "SKU-1", null, 1m, 10, now.AddDays(1)));
        store.AddOperationTask(new PlannedOperationTask("WO-001", "OP-10", OperationTaskStatus.Queued, 10, "WC-A", [], now, TimeSpan.FromHours(2)));
        await using var dbContext = CreateDbContext();

        var handler = new AssetUnavailableIntegrationEventHandlerForReschedule(
            CreateUnavailableProcessor(store, dbContext),
            new InMemoryIntegrationEventDeadLetterStore());

        await handler.HandleAsync(CreateUnavailableEvent(now), CancellationToken.None);

        var window = Assert.Single(store.Unavailabilities);
        Assert.Equal("WC-A", window.WorkCenterId);
        Assert.Null(window.ToUtc);
        Assert.Equal("breakdown", window.Reason);
        Assert.Equal(RescheduleTrigger.AssetUnavailable, Assert.Single(store.ScheduleResults).Trigger);
    }

    [Fact]
    public async Task AssetUnavailableHandler_SkipsDuplicateEventBeforeRecordingWindowOrRescheduling()
    {
        var store = new InMemoryMesPlanningStore();
        var now = DateTimeOffset.Parse("2026-05-22T08:00:00Z");
        store.MapDeviceAssetToWorkCenter("ASSET-CNC-01", "WC-A");
        store.AddWorkOrder(new PlannedWorkOrder("org-001", "env-dev", "WO-001", "SKU-1", null, 1m, 10, now.AddDays(1)));
        store.AddOperationTask(new PlannedOperationTask("WO-001", "OP-10", OperationTaskStatus.Queued, 10, "WC-A", [], now, TimeSpan.FromHours(2)));
        var databaseRoot = new InMemoryDatabaseRoot();
        var options = CreateDbContextOptions($"mes-unavailable-{Guid.CreateVersion7():N}", databaseRoot);
        var integrationEvent = CreateUnavailableEvent(now);

        await using (var dbContext = CreateDbContext(options))
        {
            var handler = new AssetUnavailableIntegrationEventHandlerForReschedule(
                CreateUnavailableProcessor(store, dbContext),
                new InMemoryIntegrationEventDeadLetterStore());
            await handler.HandleAsync(integrationEvent, CancellationToken.None);
            await dbContext.SaveChangesAsync(CancellationToken.None);
        }

        await using (var dbContext = CreateDbContext(options))
        {
            var handler = new AssetUnavailableIntegrationEventHandlerForReschedule(
                CreateUnavailableProcessor(store, dbContext),
                new InMemoryIntegrationEventDeadLetterStore());
            await handler.HandleAsync(integrationEvent, CancellationToken.None);
            await dbContext.SaveChangesAsync(CancellationToken.None);
        }

        Assert.Single(store.Unavailabilities);
        Assert.Single(store.ScheduleResults);
        await using var assertionDbContext = CreateDbContext(options);
        Assert.Equal(1, await assertionDbContext.ProcessedIntegrationEvents.CountAsync());
    }

    [Fact]
    public async Task AssetUnavailableHandler_SkipsReleasedEventWithSameIdempotencyKey()
    {
        var store = new InMemoryMesPlanningStore();
        var now = DateTimeOffset.Parse("2026-05-22T08:00:00Z");
        store.MapDeviceAssetToWorkCenter("ASSET-CNC-01", "WC-A");
        store.AddWorkOrder(new PlannedWorkOrder("org-001", "env-dev", "WO-001", "SKU-1", null, 1m, 10, now.AddDays(1)));
        store.AddOperationTask(new PlannedOperationTask("WO-001", "OP-10", OperationTaskStatus.Queued, 10, "WC-A", [], now, TimeSpan.FromHours(2)));
        var databaseRoot = new InMemoryDatabaseRoot();
        var options = CreateDbContextOptions($"mes-unavailable-idem-{Guid.CreateVersion7():N}", databaseRoot);
        var integrationEvent = CreateUnavailableEvent(now);
        var releasedEvent = integrationEvent with { EventId = "evt-001-released" };

        await using (var dbContext = CreateDbContext(options))
        {
            var handler = new AssetUnavailableIntegrationEventHandlerForReschedule(
                CreateUnavailableProcessor(store, dbContext),
                new InMemoryIntegrationEventDeadLetterStore());
            await handler.HandleAsync(integrationEvent, CancellationToken.None);
            await dbContext.SaveChangesAsync(CancellationToken.None);
        }

        await using (var dbContext = CreateDbContext(options))
        {
            var handler = new AssetUnavailableIntegrationEventHandlerForReschedule(
                CreateUnavailableProcessor(store, dbContext),
                new InMemoryIntegrationEventDeadLetterStore());
            await handler.HandleAsync(releasedEvent, CancellationToken.None);
            await dbContext.SaveChangesAsync(CancellationToken.None);
        }

        Assert.Single(store.Unavailabilities);
        Assert.Single(store.ScheduleResults);
        await using var assertionDbContext = CreateDbContext(options);
        var processed = Assert.Single(await assertionDbContext.ProcessedIntegrationEvents.ToListAsync());
        Assert.Equal(integrationEvent.EventId, processed.EventId);
        Assert.Equal(integrationEvent.IdempotencyKey, processed.IdempotencyKey);
    }

    [Fact]
    public async Task AssetUnavailableV2Handler_UsesReasonCodeAndSharesCrossVersionBusinessIdentity()
    {
        var store = new InMemoryMesPlanningStore();
        var now = DateTimeOffset.Parse("2026-05-22T08:00:00Z");
        store.MapDeviceAssetToWorkCenter("ASSET-CNC-01", "WC-A");
        store.AddWorkOrder(new PlannedWorkOrder("org-001", "env-dev", "WO-001", "SKU-1", null, 1m, 10, now.AddDays(1)));
        store.AddOperationTask(new PlannedOperationTask("WO-001", "OP-10", OperationTaskStatus.Queued, 10, "WC-A", [], now, TimeSpan.FromHours(2)));
        var databaseRoot = new InMemoryDatabaseRoot();
        var options = CreateDbContextOptions($"mes-unavailable-v2-{Guid.CreateVersion7():N}", databaseRoot);
        var v1 = CreateUnavailableEvent(now) with { IdempotencyKey = "asset-unavailable:WO-001:2026-05-22T08:00:00.0000000+00:00" };
        var v2 = CreateUnavailableV2Event(now, v1.IdempotencyKey);

        await using (var dbContext = CreateDbContext(options))
        {
            var handler = new AssetUnavailableV2IntegrationEventHandlerForReschedule(
                CreateUnavailableProcessor(store, dbContext),
                new InMemoryIntegrationEventDeadLetterStore());
            await handler.HandleAsync(v2, CancellationToken.None);
        }

        await using (var dbContext = CreateDbContext(options))
        {
            var handler = new AssetUnavailableIntegrationEventHandlerForReschedule(
                CreateUnavailableProcessor(store, dbContext),
                new InMemoryIntegrationEventDeadLetterStore());
            await handler.HandleAsync(v1, CancellationToken.None);
        }

        var window = Assert.Single(store.Unavailabilities);
        Assert.Equal("CUSTOM-DOWNTIME-CODE", window.Reason);
        Assert.Single(store.ScheduleResults);
        await using var assertionDbContext = CreateDbContext(options);
        var processed = Assert.Single(await assertionDbContext.ProcessedIntegrationEvents.ToListAsync());
        Assert.Equal(v2.EventId, processed.EventId);
        Assert.Equal(v1.IdempotencyKey, processed.IdempotencyKey);
    }

    /// <summary>P1-3：#2965 的 v2 wire contract 把缺失 causationId 归一为合法空串，v2 消费者必须接受它。</summary>
    [Fact]
    public async Task AssetUnavailableV2Handler_AcceptsNormalisedEmptyCausationId()
    {
        var store = new InMemoryMesPlanningStore();
        var now = DateTimeOffset.Parse("2026-05-22T08:00:00Z");
        store.MapDeviceAssetToWorkCenter("ASSET-CNC-01", "WC-A");
        await using var dbContext = CreateDbContext();
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        var handler = new AssetUnavailableV2IntegrationEventHandlerForReschedule(
            CreateUnavailableProcessor(store, dbContext, autoReschedule: false),
            deadLetters);
        var integrationEvent = CreateUnavailableV2Event(now, "asset-unavailable:WO-001:empty-causation") with
        {
            CausationId = string.Empty,
        };

        await handler.HandleAsync(integrationEvent, CancellationToken.None);

        Assert.Single(store.Unavailabilities);
        Assert.Empty(await deadLetters.ListAsync(null, null, CancellationToken.None));
        var processed = Assert.Single(await dbContext.ProcessedIntegrationEvents.ToListAsync());
        Assert.Equal(integrationEvent.EventId, processed.EventId);
    }

    /// <summary>v1 保持原行为：空 causationId 仍是缺失信封字段，进死信、不产生副作用。</summary>
    [Fact]
    public async Task AssetUnavailableHandler_KeepsRejectingEmptyCausationIdForV1()
    {
        var store = new InMemoryMesPlanningStore();
        store.MapDeviceAssetToWorkCenter("ASSET-CNC-01", "WC-A");
        await using var dbContext = CreateDbContext();
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        var handler = new AssetUnavailableIntegrationEventHandlerForReschedule(
            CreateUnavailableProcessor(store, dbContext),
            deadLetters);

        await handler.HandleAsync(
            CreateUnavailableEvent(DateTimeOffset.Parse("2026-05-22T08:00:00Z")) with { CausationId = string.Empty },
            CancellationToken.None);

        Assert.Empty(store.Unavailabilities);
        Assert.Empty(dbContext.ProcessedIntegrationEvents);
        var deadLetter = Assert.Single(await deadLetters.ListAsync(null, null, CancellationToken.None));
        Assert.Equal(IntegrationEventEnvelopeValidator.MissingEnvelopeFieldFailureCode, deadLetter.FailureCode);
    }

    [Fact]
    public async Task AssetUnavailableV2Handler_DeadLettersInvalidV2SourceBeforeBusinessSideEffects()
    {
        var store = new InMemoryMesPlanningStore();
        var dbContext = CreateDbContext();
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        var handler = new AssetUnavailableV2IntegrationEventHandlerForReschedule(
            CreateUnavailableProcessor(store, dbContext),
            deadLetters);
        var integrationEvent = CreateUnavailableV2Event(
            DateTimeOffset.Parse("2026-05-22T08:00:00Z"),
            "asset-unavailable:WO-001:2026-05-22T08:00:00.0000000+00:00") with
        {
            SourceService = MaintenanceIntegrationEventSources.Maintenance
        };

        await handler.HandleAsync(integrationEvent, CancellationToken.None);

        Assert.Empty(store.Unavailabilities);
        Assert.Empty(store.ScheduleResults);
        Assert.Empty(dbContext.ProcessedIntegrationEvents);
        var deadLetter = Assert.Single(await deadLetters.ListAsync(
            AssetUnavailableV2IntegrationEventHandlerForReschedule.ConsumerName,
            IntegrationEventDeadLetterStatus.Pending,
            CancellationToken.None));
        Assert.Equal("invalid-envelope", deadLetter.FailureCode);
        Assert.Equal(integrationEvent.EventId, deadLetter.EventId);
        Assert.Equal(MaintenanceIntegrationEventVersions.V2, deadLetter.EventVersion);
    }

    [Fact]
    public async Task AssetUnavailableV2Handler_SkipsSameEventIdWithDifferentBusinessKey()
    {
        var store = new InMemoryMesPlanningStore();
        var now = DateTimeOffset.Parse("2026-05-22T08:00:00Z");
        var databaseRoot = new InMemoryDatabaseRoot();
        var options = CreateDbContextOptions($"mes-unavailable-event-id-{Guid.CreateVersion7():N}", databaseRoot);
        var first = CreateUnavailableV2Event(now, "asset-unavailable:WO-001:first");
        var conflicting = first with { IdempotencyKey = "asset-unavailable:WO-001:wrong-second-key" };

        foreach (var integrationEvent in new[] { first, conflicting })
        {
            await using var dbContext = CreateDbContext(options);
            var handler = new AssetUnavailableV2IntegrationEventHandlerForReschedule(
                CreateUnavailableProcessor(store, dbContext),
                new InMemoryIntegrationEventDeadLetterStore());
            await handler.HandleAsync(integrationEvent, CancellationToken.None);
        }

        Assert.Single(store.Unavailabilities);
        Assert.Single(store.ScheduleResults);
        await using var assertionDbContext = CreateDbContext(options);
        Assert.Single(await assertionDbContext.ProcessedIntegrationEvents.ToListAsync());
    }

    [Fact]
    public async Task AssetRestoredHandler_ClosesUnavailableWindowAndAutoReschedules()
    {
        var store = new InMemoryMesPlanningStore();
        var now = DateTimeOffset.Parse("2026-05-22T08:00:00Z");
        store.MapDeviceAssetToWorkCenter("ASSET-CNC-01", "WC-A");
        store.AddUnavailability(new WorkCenterUnavailability("WC-A", now, null, "breakdown", "ASSET-CNC-01"));

        var handler = new AssetRestoredIntegrationEventHandlerForReschedule(
            store,
            new RuleScheduler(),
            new MesRescheduleOptions { AutoRescheduleOnAssetRestored = true },
            CreateDbContext(),
            new InMemoryIntegrationEventDeadLetterStore());

        await handler.HandleAsync(CreateRestoredEvent(now.AddHours(2)), CancellationToken.None);

        var window = Assert.Single(store.Unavailabilities);
        Assert.Equal(now.AddHours(2), window.ToUtc);
        Assert.Equal(RescheduleTrigger.AssetRestored, Assert.Single(store.ScheduleResults).Trigger);
    }

    [Fact]
    public async Task AssetRestoredHandler_SkipsDuplicateEventBeforeClosingWindowOrRescheduling()
    {
        var store = new InMemoryMesPlanningStore();
        var now = DateTimeOffset.Parse("2026-05-22T08:00:00Z");
        store.AddUnavailability(new WorkCenterUnavailability("WC-A", now, null, "breakdown", "ASSET-CNC-01"));
        var databaseRoot = new InMemoryDatabaseRoot();
        var options = CreateDbContextOptions($"mes-restored-{Guid.CreateVersion7():N}", databaseRoot);
        var integrationEvent = CreateRestoredEvent(now.AddHours(2));

        await using (var dbContext = CreateDbContext(options))
        {
            var handler = new AssetRestoredIntegrationEventHandlerForReschedule(
                store,
                new RuleScheduler(),
                new MesRescheduleOptions { AutoRescheduleOnAssetRestored = true },
                dbContext,
                new InMemoryIntegrationEventDeadLetterStore());
            await handler.HandleAsync(integrationEvent, CancellationToken.None);
            await dbContext.SaveChangesAsync(CancellationToken.None);
        }

        await using (var dbContext = CreateDbContext(options))
        {
            var handler = new AssetRestoredIntegrationEventHandlerForReschedule(
                store,
                new RuleScheduler(),
                new MesRescheduleOptions { AutoRescheduleOnAssetRestored = true },
                dbContext,
                new InMemoryIntegrationEventDeadLetterStore());
            await handler.HandleAsync(integrationEvent, CancellationToken.None);
            await dbContext.SaveChangesAsync(CancellationToken.None);
        }

        var window = Assert.Single(store.Unavailabilities);
        Assert.Equal(now.AddHours(2), window.ToUtc);
        Assert.Single(store.ScheduleResults);
        await using var assertionDbContext = CreateDbContext(options);
        Assert.Equal(1, await assertionDbContext.ProcessedIntegrationEvents.CountAsync());
    }

    [Fact]
    public async Task AssetUnavailableHandler_DeadLettersUnsupportedEventVersionWithoutRescheduling()
    {
        var store = new InMemoryMesPlanningStore();
        var deadLetterStore = new InMemoryIntegrationEventDeadLetterStore();
        var handler = new AssetUnavailableIntegrationEventHandlerForReschedule(
            CreateUnavailableProcessor(store, CreateDbContext()),
            deadLetterStore);

        await handler.HandleAsync(CreateUnavailableEvent(DateTimeOffset.Parse("2026-05-22T08:00:00Z"), eventVersion: 2), CancellationToken.None);

        Assert.Empty(store.Unavailabilities);
        Assert.Empty(store.ScheduleResults);
        var deadLetter = Assert.Single(await deadLetterStore.ListAsync(
            AssetUnavailableIntegrationEventHandlerForReschedule.ConsumerName,
            IntegrationEventDeadLetterStatus.Pending,
            CancellationToken.None));
        Assert.Equal("unsupported-version", deadLetter.FailureCode);
        Assert.Equal(2, deadLetter.EventVersion);
    }

    [Fact]
    public async Task AssetRestoredHandler_DeadLettersUnsupportedEventVersionWithoutClosingWindow()
    {
        var store = new InMemoryMesPlanningStore();
        var now = DateTimeOffset.Parse("2026-05-22T08:00:00Z");
        store.AddUnavailability(new WorkCenterUnavailability("WC-A", now, null, "breakdown", "ASSET-CNC-01"));
        var deadLetterStore = new InMemoryIntegrationEventDeadLetterStore();
        var handler = new AssetRestoredIntegrationEventHandlerForReschedule(
            store,
            new RuleScheduler(),
            new MesRescheduleOptions { AutoRescheduleOnAssetRestored = true },
            CreateDbContext(),
            deadLetterStore);

        await handler.HandleAsync(CreateRestoredEvent(now.AddHours(2), eventVersion: 2), CancellationToken.None);

        Assert.Null(Assert.Single(store.Unavailabilities).ToUtc);
        Assert.Empty(store.ScheduleResults);
        var deadLetter = Assert.Single(await deadLetterStore.ListAsync(
            AssetRestoredIntegrationEventHandlerForReschedule.ConsumerName,
            IntegrationEventDeadLetterStatus.Pending,
            CancellationToken.None));
        Assert.Equal("unsupported-version", deadLetter.FailureCode);
        Assert.Equal(2, deadLetter.EventVersion);
    }

    [Fact]
    public void PostgreSQL_profile_uses_persistent_dead_letter_store()
    {
        using var factory = new MesPostgreSqlWebApplicationFactory();
        using var scope = factory.Services.CreateScope();

        var store = scope.ServiceProvider.GetRequiredService<IIntegrationEventDeadLetterStore>();

        Assert.IsType<PersistentIntegrationEventDeadLetterStore<ApplicationDbContext>>(store);
    }

    private static AssetUnavailableIntegrationEvent CreateUnavailableEvent(DateTimeOffset fromUtc, int eventVersion = MaintenanceIntegrationEventVersions.V1)
    {
        return new AssetUnavailableIntegrationEvent(
            "evt-001",
            MaintenanceIntegrationEventTypes.AssetUnavailable,
            eventVersion,
            fromUtc,
            MaintenanceIntegrationEventSources.Maintenance,
            "corr-001",
            "cause-001",
            "org-001",
            "env-dev",
            "maintenance",
            "maintenance.AssetUnavailable:ASSET-CNC-01:20260522080000",
            new AssetUnavailablePayload("ASSET-CNC-01", "breakdown", fromUtc));
    }

    private static AssetRestoredIntegrationEvent CreateRestoredEvent(DateTimeOffset restoredAtUtc, int eventVersion = MaintenanceIntegrationEventVersions.V1)
    {
        return new AssetRestoredIntegrationEvent(
            "evt-002",
            MaintenanceIntegrationEventTypes.AssetRestored,
            eventVersion,
            restoredAtUtc,
            MaintenanceIntegrationEventSources.Maintenance,
            "corr-001",
            "evt-001",
            "org-001",
            "env-dev",
            "maintenance",
            "maintenance.AssetRestored:ASSET-CNC-01:20260522100000",
            new AssetRestoredPayload("ASSET-CNC-01", restoredAtUtc));
    }

    private static AssetUnavailableV2IntegrationEvent CreateUnavailableV2Event(DateTimeOffset fromUtc, string idempotencyKey)
    {
        return new AssetUnavailableV2IntegrationEvent(
            "evt-002-v2",
            MaintenanceIntegrationEventTypes.AssetUnavailable,
            MaintenanceIntegrationEventVersions.V2,
            fromUtc,
            MaintenanceIntegrationEventSources.BusinessMaintenance,
            "corr-001",
            "cause-001",
            "org-001",
            "env-dev",
            "maintenance",
            idempotencyKey,
            new AssetUnavailableV2Payload("ASSET-CNC-01", "CUSTOM-DOWNTIME-CODE", fromUtc));
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = CreateDbContextOptions($"mes-{Guid.CreateVersion7():N}", new InMemoryDatabaseRoot());
        return CreateDbContext(options);
    }

    /// <summary>
    /// 单元用例不起 MediatR 管线：用 <see cref="DirectCommandSender"/> 直接调用
    /// <see cref="ProcessAssetUnavailableCommandHandler"/> 并以一次 SaveChanges 模拟 UoW 提交，
    /// 被测对象仍是生产的 handler → processor → command 链。
    /// </summary>
    private static IMesAssetUnavailableCanonicalProcessor CreateUnavailableProcessor(
        IMesPlanningStore store,
        ApplicationDbContext dbContext,
        bool autoReschedule = true) =>
        new MesAssetUnavailableCanonicalProcessor(new DirectCommandSender(
            dbContext,
            store,
            new MesRescheduleOptions { AutoRescheduleOnAssetUnavailable = autoReschedule }));

    private static PersistentMesPlanningStore CreatePersistentStore(ApplicationDbContext dbContext) =>
        new(dbContext, new OperationTaskRepository(dbContext));

    private static ApplicationDbContext CreateDbContext(DbContextOptions<ApplicationDbContext> options)
    {
        return new ApplicationDbContext(options, new NoopMediator());
    }

    private static DbContextOptions<ApplicationDbContext> CreateDbContextOptions(
        string databaseName,
        InMemoryDatabaseRoot databaseRoot)
    {
        return new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName, databaseRoot)
            .Options;
    }

    private sealed class NoopMediator : IMediator
    {
        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification => Task.CompletedTask;

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            _ = request;
            _ = cancellationToken;
            throw new NotSupportedException("Noop mediator cannot send requests.");
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest
        {
            _ = request;
            _ = cancellationToken;
            throw new NotSupportedException("Noop mediator cannot send requests.");
        }

        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
        {
            _ = request;
            _ = cancellationToken;
            throw new NotSupportedException("Noop mediator cannot send requests.");
        }

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            _ = request;
            _ = cancellationToken;
            throw new NotSupportedException("Noop mediator cannot stream requests.");
        }

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
        {
            _ = request;
            _ = cancellationToken;
            throw new NotSupportedException("Noop mediator cannot stream requests.");
        }
    }

    private sealed class MesPostgreSqlWebApplicationFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:PostgreSQL"] = "Host=localhost;Database=nerv_iip_mes_dead_letter_test;Username=nerv;Password=nerv",
                    ["InternalService:BearerToken"] = "test-internal-token",
                });
            });
        }
    }

    private sealed class DirectCommandSender(
        ApplicationDbContext dbContext,
        IMesPlanningStore store,
        MesRescheduleOptions options) : ISender
    {
        public async Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            var command = Assert.IsType<ProcessAssetUnavailableCommand>(request);
            var handler = new ProcessAssetUnavailableCommandHandler(
                new PostgreSqlMesAssetUnavailableInboxClaimCoordinator(dbContext), store, new RuleScheduler(), options);
            var result = await handler.Handle(command, cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
            return (TResponse)(object)result;
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest => throw new NotSupportedException();

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    /// <summary>
    /// 并发夹具：第一个进入副作用的竞争者在这里停住（它已经赢得 claim），直到测试释放；
    /// 同时计数有多少竞争者真的走进了副作用入口——落败者应当一次都进不来。
    /// </summary>
    private sealed class ClaimRaceGate
    {
        private int sideEffectEntries;

        public TaskCompletionSource FirstPassedClaim { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReleaseFirst { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int SideEffectEntries => Volatile.Read(ref sideEffectEntries);

        public async Task EnterSideEffectsAsync(CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref sideEffectEntries) == 1)
            {
                FirstPassedClaim.TrySetResult();
                await ReleaseFirst.Task.WaitAsync(cancellationToken);
            }
        }
    }

    private sealed class ClaimRaceGatePlanningStore(IMesPlanningStore inner, ClaimRaceGate gate) : IMesPlanningStore
    {
        public void AddWorkOrder(PlannedWorkOrder workOrder) => inner.AddWorkOrder(workOrder);
        public void AddOperationTask(PlannedOperationTask operationTask) => inner.AddOperationTask(operationTask);
        public void AddUnavailability(WorkCenterUnavailability unavailability) => inner.AddUnavailability(unavailability);
        public void MapDeviceAssetToWorkCenter(string deviceAssetId, string workCenterId) => inner.MapDeviceAssetToWorkCenter(deviceAssetId, workCenterId);
        public Task<IReadOnlyCollection<PlannedWorkOrder>> GetWorkOrdersAsync(CancellationToken cancellationToken = default) => inner.GetWorkOrdersAsync(cancellationToken);
        public Task<bool> WorkOrderExistsAsync(string organizationId, string environmentId, string workOrderId, CancellationToken cancellationToken = default) => inner.WorkOrderExistsAsync(organizationId, environmentId, workOrderId, cancellationToken);
        public Task<IReadOnlyCollection<PlannedOperationTask>> GetOperationTasksAsync(CancellationToken cancellationToken = default) => inner.GetOperationTasksAsync(cancellationToken);
        public Task<IReadOnlyCollection<WorkCenterUnavailability>> GetUnavailabilitiesAsync(CancellationToken cancellationToken = default) => inner.GetUnavailabilitiesAsync(cancellationToken);
        public Task<IReadOnlyCollection<WorkCenterUnavailability>> GetUnavailabilitiesAsync(string organizationId, string environmentId, CancellationToken cancellationToken = default) => inner.GetUnavailabilitiesAsync(organizationId, environmentId, cancellationToken);
        public Task<IReadOnlyCollection<MesScheduleResult>> GetScheduleResultsAsync(CancellationToken cancellationToken = default) => inner.GetScheduleResultsAsync(cancellationToken);
        public Task CloseUnavailabilityAsync(string deviceAssetId, DateTimeOffset restoredAtUtc, CancellationToken cancellationToken = default) => inner.CloseUnavailabilityAsync(deviceAssetId, restoredAtUtc, cancellationToken);
        public Task CloseUnavailabilityAsync(string organizationId, string environmentId, string deviceAssetId, DateTimeOffset restoredAtUtc, CancellationToken cancellationToken = default) => inner.CloseUnavailabilityAsync(organizationId, environmentId, deviceAssetId, restoredAtUtc, cancellationToken);
        public async Task<string> ResolveWorkCenterIdAsync(string deviceAssetId, CancellationToken cancellationToken = default)
        {
            await gate.EnterSideEffectsAsync(cancellationToken);
            return await inner.ResolveWorkCenterIdAsync(deviceAssetId, cancellationToken);
        }
        public async Task<string> ResolveWorkCenterIdAsync(string organizationId, string environmentId, string deviceAssetId, CancellationToken cancellationToken = default)
        {
            await gate.EnterSideEffectsAsync(cancellationToken);
            return await inner.ResolveWorkCenterIdAsync(organizationId, environmentId, deviceAssetId, cancellationToken);
        }
        public Task<MesScheduleResult> AddScheduleResultAsync(RescheduleTrigger trigger, DateTimeOffset scheduledAtUtc, RuleSchedulePlan plan, IReadOnlyCollection<ScheduledOperation>? compareAssignments = null, CancellationToken cancellationToken = default) => inner.AddScheduleResultAsync(trigger, scheduledAtUtc, plan, compareAssignments, cancellationToken);
        public Task<IReadOnlyCollection<ScheduleOperation>> GetScheduleOperationsAsync(string organizationId, string environmentId, CancellationToken cancellationToken = default) => inner.GetScheduleOperationsAsync(organizationId, environmentId, cancellationToken);
    }
}
