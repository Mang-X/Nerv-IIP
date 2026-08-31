using Microsoft.AspNetCore.Mvc.Testing;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Business.Mes.Infrastructure.Repositories;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Schedules;
using Nerv.IIP.Business.Mes.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Business.Mes.Web.Application.Planning;
using Nerv.IIP.Business.Mes.Web.Application.Scheduling;
using Nerv.IIP.Contracts.Maintenance;
using Nerv.IIP.Messaging.CAP;

namespace Nerv.IIP.Business.Mes.Web.Tests;

[Collection(WebApplicationFactoryCollection.Name)]
public sealed class MaintenanceEventHandlerTests
{
    [PostgreSqlFact]
    public async Task PostgreSQL_v1_v2_concurrent_claims_commit_one_business_effect_across_independent_transactions()
    {
        await MesPostgresLaneDatabase.ResetSchemaAsync();
        var options = MesPostgresLaneDatabase.CreateOptions();
        await using (var migrationContext = CreateDbContext(options))
        {
            MesPostgresLaneDatabase.AssertUsesGovernedDatabase(migrationContext);
            await migrationContext.Database.MigrateAsync();
        }

        var fromUtc = DateTimeOffset.Parse("2026-05-22T08:00:00Z");
        const string idempotencyKey = "maintenance.AssetUnavailable:ASSET-CNC-01:20260522080000";
        var v1 = CreateUnavailableEvent(fromUtc) with
        {
            EventId = "evt-concurrent-v1",
            IdempotencyKey = idempotencyKey,
        };
        var v2 = CreateUnavailableV2Event(fromUtc, idempotencyKey) with
        {
            EventId = "evt-concurrent-v2",
        };
        var claimBarrier = new AsyncArrivalBarrier(2);

        await using var v1Context = CreateDbContext(options);
        await using var v2Context = CreateDbContext(options);
        MesPostgresLaneDatabase.AssertUsesGovernedDatabase(v1Context);
        MesPostgresLaneDatabase.AssertUsesGovernedDatabase(v2Context);
        var v1Processor = CreateUnavailableProcessor(
            new ClaimBarrierPlanningStore(CreatePersistentStore(v1Context), claimBarrier),
            v1Context);
        var v2Processor = CreateUnavailableProcessor(
            new ClaimBarrierPlanningStore(CreatePersistentStore(v2Context), claimBarrier),
            v2Context);

        await Task.WhenAll(
            v1Processor.ProcessAsync(v1, v1.Payload.DeviceAssetId, v1.Payload.Reason, v1.Payload.FromUtc, CancellationToken.None),
            v2Processor.ProcessAsync(v2, v2.Payload.DeviceAssetId, v2.Payload.ReasonCode, v2.Payload.FromUtc, CancellationToken.None));

        await using var assertionContext = CreateDbContext(options);
        Assert.Equal(1, await assertionContext.ProcessedIntegrationEvents.AsNoTracking().CountAsync());
        Assert.Equal(1, await assertionContext.WorkCenterUnavailabilities.AsNoTracking().CountAsync());
        Assert.Equal(1, await assertionContext.ScheduleResults.AsNoTracking().CountAsync());
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
            var mutationSql = removedIndex switch
            {
                "ux_processed_integration_events_consumer_idempotency_key" =>
                    "DROP INDEX mes.\"ux_processed_integration_events_consumer_idempotency_key\"",
                "ux_processed_integration_events_consumer_event_id" =>
                    "DROP INDEX mes.\"ux_processed_integration_events_consumer_event_id\"",
                _ => throw new ArgumentOutOfRangeException(nameof(removedIndex), removedIndex, null),
            };
            await setup.Database.ExecuteSqlRawAsync(mutationSql);
        }

        var fromUtc = DateTimeOffset.Parse("2026-05-22T08:00:00Z");
        var sharedEventId = reuseEventId ? "evt-mutated-shared" : "evt-mutated-v1";
        var v1 = CreateUnavailableEvent(fromUtc) with
        {
            EventId = sharedEventId,
            IdempotencyKey = "mutation-key-v1",
        };
        var v2 = CreateUnavailableV2Event(
            fromUtc,
            reuseEventId ? "mutation-key-v2" : v1.IdempotencyKey) with
        {
            EventId = reuseEventId ? sharedEventId : "evt-mutated-v2",
            Payload = new AssetUnavailableV2Payload("ASSET-CNC-02", "CUSTOM-DOWNTIME-CODE", fromUtc.AddSeconds(1)),
        };
        var claimBarrier = new AsyncArrivalBarrier(2);

        await using var v1Context = CreateDbContext(options);
        await using var v2Context = CreateDbContext(options);
        var v1Processor = CreateUnavailableProcessor(
            new ClaimBarrierPlanningStore(CreatePersistentStore(v1Context), claimBarrier),
            v1Context,
            autoReschedule: false);
        var v2Processor = CreateUnavailableProcessor(
            new ClaimBarrierPlanningStore(CreatePersistentStore(v2Context), claimBarrier),
            v2Context,
            autoReschedule: false);

        await Task.WhenAll(
            v1Processor.ProcessAsync(v1, v1.Payload.DeviceAssetId, v1.Payload.Reason, v1.Payload.FromUtc, CancellationToken.None),
            v2Processor.ProcessAsync(v2, v2.Payload.DeviceAssetId, v2.Payload.ReasonCode, v2.Payload.FromUtc, CancellationToken.None));

        await using var assertionContext = CreateDbContext(options);
        Assert.Equal(2, await assertionContext.ProcessedIntegrationEvents.AsNoTracking().CountAsync());
        Assert.Equal(2, await assertionContext.WorkCenterUnavailabilities.AsNoTracking().CountAsync());
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

    private static MesAssetUnavailableCanonicalProcessor CreateUnavailableProcessor(
        IMesPlanningStore store,
        ApplicationDbContext dbContext,
        bool autoReschedule = true) =>
        new(
            store,
            new RuleScheduler(),
            new MesRescheduleOptions { AutoRescheduleOnAssetUnavailable = autoReschedule },
            dbContext);

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

    private sealed class AsyncArrivalBarrier(int participantCount)
    {
        private readonly TaskCompletionSource allArrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int arrivals;

        public Task SignalAndWaitAsync(CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref arrivals) == participantCount)
            {
                allArrived.TrySetResult();
            }

            return allArrived.Task.WaitAsync(cancellationToken);
        }
    }

    private sealed class ClaimBarrierPlanningStore(IMesPlanningStore inner, AsyncArrivalBarrier barrier) : IMesPlanningStore
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
            await barrier.SignalAndWaitAsync(cancellationToken);
            return await inner.ResolveWorkCenterIdAsync(deviceAssetId, cancellationToken);
        }
        public async Task<string> ResolveWorkCenterIdAsync(string organizationId, string environmentId, string deviceAssetId, CancellationToken cancellationToken = default)
        {
            await barrier.SignalAndWaitAsync(cancellationToken);
            return await inner.ResolveWorkCenterIdAsync(organizationId, environmentId, deviceAssetId, cancellationToken);
        }
        public Task<MesScheduleResult> AddScheduleResultAsync(RescheduleTrigger trigger, DateTimeOffset scheduledAtUtc, RuleSchedulePlan plan, IReadOnlyCollection<ScheduledOperation>? compareAssignments = null, CancellationToken cancellationToken = default) => inner.AddScheduleResultAsync(trigger, scheduledAtUtc, plan, compareAssignments, cancellationToken);
        public Task<IReadOnlyCollection<ScheduleOperation>> GetScheduleOperationsAsync(string organizationId, string environmentId, CancellationToken cancellationToken = default) => inner.GetScheduleOperationsAsync(organizationId, environmentId, cancellationToken);
    }
}
