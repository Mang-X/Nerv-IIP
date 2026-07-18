using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using NetCorePal.Extensions.Primitives;
using Npgsql;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Workbench;
using Nerv.IIP.Business.Mes.Web.Application.Commands.WorkOrders;
using Nerv.IIP.Business.Mes.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Business.Mes.Web.Application.MasterData;
using Nerv.IIP.Business.Mes.Web.Application.Planning;
using Nerv.IIP.Business.Mes.Web.Application.Scheduling;
using Nerv.IIP.Contracts.DemandPlanning;
using Nerv.IIP.Contracts.MasterData;
using Nerv.IIP.Messaging.CAP;

namespace Nerv.IIP.Business.Mes.Web.Tests;

public sealed class SkuDisabledConsumerTests
{
    [MesRealPostgresFact]
    public async Task PostgreSQL_consumer_persists_disabled_sku_and_changes_new_work_order_behavior()
    {
        await using var database = await TemporaryDatabase.CreateAsync(
            Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES")!);
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(database.ConnectionString)
            .Options;
        var changedAtUtc = DateTimeOffset.Parse("2026-07-18T08:00:00Z");

        await using (var consumerContext = CreateContext(options))
        {
            await consumerContext.Database.MigrateAsync(CancellationToken.None);
            var handler = new SkuDisabledIntegrationEventHandlerForProjectMesSkuAvailability(
                consumerContext,
                new InMemoryIntegrationEventDeadLetterStore());
            await handler.HandleAsync(DisabledEvent(changedAtUtc), CancellationToken.None);
            await consumerContext.SaveChangesAsync(CancellationToken.None);
        }

        await using var commandContext = CreateContext(options);
        await Assert.ThrowsAsync<DisabledMesSkuException>(() =>
            new ConvertPlanToWorkOrderCommandHandler(commandContext).Handle(
                new ConvertPlanToWorkOrderCommand(
                    "org-001",
                    "env-dev",
                    "PLAN-PG-NEW",
                    "WO-PG-NEW",
                    changedAtUtc.AddMinutes(1),
                    "SKU-DISABLED",
                    "PV-001",
                    5m,
                    "PCS",
                    changedAtUtc.AddDays(2),
                    null),
                CancellationToken.None));

        Assert.Single(await commandContext.MesSkuAvailabilities.ToListAsync(CancellationToken.None));
        Assert.Single(await commandContext.ProcessedIntegrationEvents.ToListAsync(CancellationToken.None));
        Assert.Empty(await commandContext.WorkOrders.ToListAsync(CancellationToken.None));
    }

    [MesRealPostgresFact]
    public async Task PostgreSQL_concurrent_sku_disabled_events_converge_without_retry_poisoning()
    {
        await using var database = await TemporaryDatabase.CreateAsync(
            Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES")!);
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(database.ConnectionString)
            .Options;
        await using (var setup = CreateContext(options))
        {
            await setup.Database.MigrateAsync(CancellationToken.None);
        }

        var olderAtUtc = DateTimeOffset.Parse("2026-07-18T08:00:00Z");
        var newerAtUtc = olderAtUtc.AddMinutes(1);
        await using var olderContext = CreateContext(options);
        await using var newerContext = CreateContext(options);
        var olderHandler = new SkuDisabledIntegrationEventHandlerForProjectMesSkuAvailability(
            olderContext,
            new InMemoryIntegrationEventDeadLetterStore());
        var newerHandler = new SkuDisabledIntegrationEventHandlerForProjectMesSkuAvailability(
            newerContext,
            new InMemoryIntegrationEventDeadLetterStore());

        await Task.WhenAll(
            ConsumeAndSaveAsync(olderHandler, olderContext, DisabledEvent(olderAtUtc)),
            ConsumeAndSaveAsync(
                newerHandler,
                newerContext,
                DisabledEvent(newerAtUtc) with
                {
                    EventId = "evt-sku-disabled-002",
                    IdempotencyKey = "sku-disabled-002",
                    Payload = new MasterDataDisabledPayload("sku", "SKU-DISABLED", "disabled", "quality-hold", newerAtUtc)
                }));

        await using var assertionContext = CreateContext(options);
        var availability = await assertionContext.MesSkuAvailabilities.SingleAsync(CancellationToken.None);
        Assert.Equal(newerAtUtc, availability.ChangedAtUtc);
        Assert.Equal("quality-hold", availability.DisabledReason);
        Assert.Equal(2, await assertionContext.ProcessedIntegrationEvents.CountAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Consuming_sku_disabled_changes_new_work_order_behavior_without_mutating_history()
    {
        var databaseRoot = new InMemoryDatabaseRoot();
        var options = CreateOptions(databaseRoot);
        var changedAtUtc = DateTimeOffset.Parse("2026-07-18T08:00:00Z");

        await using (var setup = CreateContext(options))
        {
            setup.WorkOrders.Add(WorkOrder.Create(
                "org-001", "env-dev", "WO-HISTORICAL", "SKU-DISABLED", "PV-001", 10m, 100, changedAtUtc.AddDays(1), "PCS"));
            await setup.SaveChangesAsync(CancellationToken.None);
        }

        await using (var consumerContext = CreateContext(options))
        {
            var handler = new SkuDisabledIntegrationEventHandlerForProjectMesSkuAvailability(
                consumerContext,
                new InMemoryIntegrationEventDeadLetterStore());
            var integrationEvent = DisabledEvent(changedAtUtc);

            await handler.HandleAsync(integrationEvent, CancellationToken.None);
            await handler.HandleAsync(integrationEvent, CancellationToken.None);
            await consumerContext.SaveChangesAsync(CancellationToken.None);
        }

        await using var commandContext = CreateContext(options);
        var exception = await Assert.ThrowsAsync<DisabledMesSkuException>(() =>
            new ConvertPlanToWorkOrderCommandHandler(commandContext).Handle(
                new ConvertPlanToWorkOrderCommand(
                    "org-001",
                    "env-dev",
                    "PLAN-NEW",
                    "WO-NEW",
                    changedAtUtc.AddMinutes(1),
                    "SKU-DISABLED",
                    "PV-001",
                    5m,
                    "PCS",
                    changedAtUtc.AddDays(2),
                    null),
                CancellationToken.None));

        Assert.Contains("disabled", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Single(await commandContext.WorkOrders.ToListAsync(CancellationToken.None));
        Assert.Equal("WO-HISTORICAL", (await commandContext.WorkOrders.SingleAsync(CancellationToken.None)).WorkOrderIdValue);
        Assert.Single(await commandContext.MesSkuAvailabilities.ToListAsync(CancellationToken.None));
        Assert.Single(await commandContext.ProcessedIntegrationEvents.ToListAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Disabled_sku_also_blocks_rush_work_orders_but_only_in_the_matching_scope()
    {
        var options = CreateOptions(new InMemoryDatabaseRoot());
        var changedAtUtc = DateTimeOffset.Parse("2026-07-18T08:00:00Z");
        await using var dbContext = CreateContext(options);
        var handler = new SkuDisabledIntegrationEventHandlerForProjectMesSkuAvailability(
            dbContext,
            new InMemoryIntegrationEventDeadLetterStore());
        await handler.HandleAsync(DisabledEvent(changedAtUtc), CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var store = new InMemoryMesPlanningStore();
        var rushHandler = new CreateRushWorkOrderCommandHandler(store, new RuleScheduler(), null, dbContext);
        await Assert.ThrowsAsync<DisabledMesSkuException>(() => rushHandler.Handle(
            RushCommand("org-001", "env-dev", "WO-BLOCKED", changedAtUtc),
            CancellationToken.None));

        await rushHandler.Handle(
            RushCommand("org-002", "env-dev", "WO-ALLOWED", changedAtUtc),
            CancellationToken.None);

        Assert.DoesNotContain(store.WorkOrders, x => x.WorkOrderId == "WO-BLOCKED");
        Assert.Contains(store.WorkOrders, x => x.WorkOrderId == "WO-ALLOWED");
    }

    [Fact]
    public async Task Idempotent_work_order_replays_remain_accepted_after_sku_is_disabled()
    {
        var options = CreateOptions(new InMemoryDatabaseRoot());
        var changedAtUtc = DateTimeOffset.Parse("2026-07-18T08:00:00Z");
        await using var dbContext = CreateContext(options);
        var codingService = new MesCodingService();
        var planHandler = new ConvertPlanToWorkOrderCommandHandler(dbContext, codingService);
        var planCommand = new ConvertPlanToWorkOrderCommand(
            "org-001", "env-dev", "PLAN-REPLAY", null, changedAtUtc.AddMinutes(-1),
            "SKU-DISABLED", "PV-001", 1m, "PCS", changedAtUtc.AddDays(1), null,
            IdempotencyKey: "plan-replay-001");
        var firstPlan = await planHandler.Handle(planCommand, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var rushStore = new InMemoryMesPlanningStore();
        var rushHandler = new CreateRushWorkOrderCommandHandler(rushStore, new RuleScheduler(), codingService, dbContext);
        var rushCommand = RushCommand("org-001", "env-dev", "WO-RUSH-REPLAY", changedAtUtc) with
        {
            IdempotencyKey = "rush-replay-001"
        };
        var firstRush = await rushHandler.Handle(rushCommand, CancellationToken.None);

        var consumer = new SkuDisabledIntegrationEventHandlerForProjectMesSkuAvailability(
            dbContext,
            new InMemoryIntegrationEventDeadLetterStore());
        await consumer.HandleAsync(DisabledEvent(changedAtUtc), CancellationToken.None);

        var replayedPlan = await planHandler.Handle(planCommand, CancellationToken.None);
        var replayedRush = await rushHandler.Handle(rushCommand, CancellationToken.None);

        Assert.Equal(firstPlan.ReferenceId, replayedPlan.ReferenceId);
        Assert.Equal(firstRush.WorkOrderId, replayedRush.WorkOrderId);
        Assert.Single(await dbContext.WorkOrders.ToListAsync(CancellationToken.None));
        Assert.Single(rushStore.WorkOrders);
    }

    [MesRealPostgresFact]
    public async Task PostgreSQL_disable_commit_serializes_before_new_work_order_creation()
    {
        await using var database = await TemporaryDatabase.CreateAsync(
            Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES")!);
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(database.ConnectionString)
            .Options;
        await using (var setup = CreateContext(options))
        {
            await setup.Database.MigrateAsync(CancellationToken.None);
        }

        await using var consumerContext = CreateContext(options);
        await using var commandContext = CreateContext(options);
        var projectionReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowConsumerCommit = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var blockingCoordinator = new BlockingSkuAvailabilityCoordinator(
            new PostgreSqlMesSkuAvailabilityScopeCoordinator(consumerContext),
            projectionReady,
            allowConsumerCommit);
        var consumer = new SkuDisabledIntegrationEventHandlerForProjectMesSkuAvailability(
            consumerContext,
            new InMemoryIntegrationEventDeadLetterStore(),
            blockingCoordinator);
        var consumeTask = consumer.HandleAsync(
            DisabledEvent(DateTimeOffset.Parse("2026-07-18T08:00:00Z")),
            CancellationToken.None);
        await projectionReady.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        var suggestionHandler = new PlanningSuggestionAcceptedIntegrationEventHandlerForCreateMesWorkOrder(
            commandContext,
            deadLetters,
            null,
            new PostgreSqlMesSkuAvailabilityScopeCoordinator(commandContext));
        var createTask = suggestionHandler.HandleAsync(
            PlanningSuggestionEvent(DateTimeOffset.Parse("2026-07-18T08:01:00Z")),
            CancellationToken.None);

        await Task.Delay(250);
        Assert.False(createTask.IsCompleted, "Creation must wait for the in-flight disable transaction for the same SKU scope.");
        allowConsumerCommit.SetResult();
        await consumeTask;
        await createTask;

        await using var assertionContext = CreateContext(options);
        Assert.Empty(await assertionContext.WorkOrders.ToListAsync(CancellationToken.None));
        Assert.Single(await assertionContext.MesSkuAvailabilities.ToListAsync(CancellationToken.None));
        Assert.Contains(
            await deadLetters.ListAsync(
                PlanningSuggestionAcceptedIntegrationEventHandlerForCreateMesWorkOrder.ConsumerName,
                IntegrationEventDeadLetterStatus.Pending,
                CancellationToken.None),
            x => x.FailureCode == "mes.planningSuggestionAccepted.skuDisabled");
    }

    [Fact]
    public async Task Invalid_sku_disabled_payload_is_dead_lettered_without_poisoning_the_consumer()
    {
        var options = CreateOptions(new InMemoryDatabaseRoot());
        await using var dbContext = CreateContext(options);
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        var handler = new SkuDisabledIntegrationEventHandlerForProjectMesSkuAvailability(dbContext, deadLetters);
        var changedAtUtc = DateTimeOffset.Parse("2026-07-18T08:00:00Z");
        var invalidEvent = DisabledEvent(changedAtUtc) with
        {
            Payload = new MasterDataDisabledPayload("business-partner", "SKU-DISABLED", "disabled", "retired", changedAtUtc)
        };

        await handler.HandleAsync(invalidEvent, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        Assert.Empty(await dbContext.MesSkuAvailabilities.ToListAsync(CancellationToken.None));
        Assert.Empty(await dbContext.ProcessedIntegrationEvents.ToListAsync(CancellationToken.None));
        Assert.Single(await deadLetters.ListAsync(
            SkuDisabledIntegrationEventHandlerForProjectMesSkuAvailability.ConsumerName,
            IntegrationEventDeadLetterStatus.Pending,
            CancellationToken.None));
    }

    private static CreateRushWorkOrderCommand RushCommand(
        string organizationId,
        string environmentId,
        string workOrderId,
        DateTimeOffset now) =>
        new(
            organizationId,
            environmentId,
            workOrderId,
            "SKU-DISABLED",
            "PV-001",
            1m,
            now.AddDays(1),
            "WC-001",
            $"{workOrderId}-OP-10",
            10,
            TimeSpan.FromMinutes(30),
            now);

    private static SkuDisabledIntegrationEvent DisabledEvent(DateTimeOffset changedAtUtc) =>
        new(
            "evt-sku-disabled-001",
            MasterDataIntegrationEventTypes.SkuDisabled,
            MasterDataIntegrationEventVersions.V1,
            changedAtUtc,
            MasterDataIntegrationEventSources.BusinessMasterData,
            "corr-sku-disabled-001",
            "cause-sku-disabled-001",
            "org-001",
            "env-dev",
            "user:masterdata-admin",
            "sku-disabled-001",
            new MasterDataDisabledPayload("sku", "SKU-DISABLED", "disabled", "retired", changedAtUtc));

    private static PlanningSuggestionAcceptedIntegrationEvent PlanningSuggestionEvent(DateTimeOffset occurredAtUtc) =>
        new(
            "evt-demand-sku-race",
            DemandPlanningIntegrationEventTypes.PlanningSuggestionAccepted,
            DemandPlanningIntegrationEventVersions.V1,
            occurredAtUtc,
            DemandPlanningIntegrationEventSources.BusinessDemandPlanning,
            "corr-demand-sku-race",
            "cause-demand-sku-race",
            "org-001",
            "env-dev",
            "user:planner",
            "demand-sku-race",
            new PlanningSuggestionAcceptedPayload(
                "SUG-SKU-RACE",
                "MRP-001",
                "planned-work-order",
                "SKU-DISABLED",
                "PCS",
                "SITE-A",
                1m,
                new DateOnly(2026, 7, 19),
                new DateOnly(2026, 7, 18),
                "DEMAND-001",
                "PV-001",
                "BusinessMes",
                "WorkOrder",
                null));

    private static async Task ConsumeAndSaveAsync(
        SkuDisabledIntegrationEventHandlerForProjectMesSkuAvailability handler,
        ApplicationDbContext dbContext,
        SkuDisabledIntegrationEvent integrationEvent)
    {
        await handler.HandleAsync(integrationEvent, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
    }

    private sealed class BlockingSkuAvailabilityCoordinator(
        IMesSkuAvailabilityScopeCoordinator inner,
        TaskCompletionSource projectionReady,
        TaskCompletionSource allowCommit) : IMesSkuAvailabilityScopeCoordinator
    {
        public Task ExecuteAsync(
            string organizationId,
            string environmentId,
            string skuCode,
            Func<CancellationToken, Task> action,
            CancellationToken cancellationToken) =>
            inner.ExecuteAsync(
                organizationId,
                environmentId,
                skuCode,
                async token =>
                {
                    await action(token);
                    projectionReady.SetResult();
                    await allowCommit.Task.WaitAsync(token);
                },
                cancellationToken);

        public Task<T> ExecuteAsync<T>(
            string organizationId,
            string environmentId,
            string skuCode,
            Func<CancellationToken, Task<T>> action,
            CancellationToken cancellationToken) =>
            inner.ExecuteAsync(organizationId, environmentId, skuCode, action, cancellationToken);
    }

    private static DbContextOptions<ApplicationDbContext> CreateOptions(InMemoryDatabaseRoot databaseRoot) =>
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"mes-sku-disabled-{Guid.CreateVersion7():N}", databaseRoot)
            .Options;

    private static ApplicationDbContext CreateContext(DbContextOptions<ApplicationDbContext> options) =>
        new(options, new NoopMediator());

    private sealed class TemporaryDatabase(
        string adminConnectionString,
        string databaseName,
        string connectionString) : IAsyncDisposable
    {
        public string ConnectionString { get; } = connectionString;

        public static async Task<TemporaryDatabase> CreateAsync(string baseConnectionString)
        {
            var databaseName = $"nerv_mes_sku_disabled_{Guid.CreateVersion7():N}";
            var adminConnectionString = new NpgsqlConnectionStringBuilder(baseConnectionString)
            {
                Database = "postgres"
            }.ConnectionString;
            await using var connection = new NpgsqlConnection(adminConnectionString);
            await connection.OpenAsync(CancellationToken.None);
            await using var command = new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\"", connection);
            await command.ExecuteNonQueryAsync(CancellationToken.None);
            return new TemporaryDatabase(
                adminConnectionString,
                databaseName,
                new NpgsqlConnectionStringBuilder(baseConnectionString)
                {
                    Database = databaseName
                }.ConnectionString);
        }

        public async ValueTask DisposeAsync()
        {
            await using var connection = new NpgsqlConnection(adminConnectionString);
            await connection.OpenAsync(CancellationToken.None);
            await using var command = new NpgsqlCommand(
                $"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE)",
                connection);
            await command.ExecuteNonQueryAsync(CancellationToken.None);
        }
    }
}
