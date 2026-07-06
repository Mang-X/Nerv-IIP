using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.EngineeringChangeAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.FinishedGoodsReceiptRequestAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.MaterialSupplyAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.OperationTaskAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Workbench;
using Nerv.IIP.Business.Mes.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Business.Mes.Web.Application.ProductEngineering;
using Nerv.IIP.Contracts.ProductEngineering;
using Nerv.IIP.Messaging.CAP;
using NetCorePal.Extensions.Primitives;
using NetCorePal.Extensions.Repository.EntityFrameworkCore;

namespace Nerv.IIP.Business.Mes.Web.Tests;

public sealed class ProductEngineeringReleaseEventHandlerTests
{
    [Fact]
    public async Task ProductionVersionCreatedHandler_BindsCreatedMesWorkOrdersWithoutProductionVersionForSameSku()
    {
        var databaseRoot = new InMemoryDatabaseRoot();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"mes-product-engineering-release-{Guid.CreateVersion7():N}", databaseRoot)
            .Options;
        await using (var dbContext = CreateDbContext(options))
        {
            dbContext.WorkOrders.Add(WorkOrder.Create(
                "org-001",
                "env-dev",
                "WO-FG-001",
                "SKU-FG-1000",
                null,
                10m,
                10,
                DateTimeOffset.Parse("2026-06-02T16:00:00Z"),
                "PCS",
                null));
            dbContext.WorkOrders.Add(WorkOrder.Create(
                "org-001",
                "env-dev",
                "WO-FG-002",
                "SKU-OTHER",
                null,
                10m,
                10,
                DateTimeOffset.Parse("2026-06-02T16:00:00Z"),
                "PCS",
                null));
            await dbContext.SaveChangesAsync(CancellationToken.None);
        }

        await using (var dbContext = CreateDbContext(options))
        {
            var handler = new ProductionVersionCreatedIntegrationEventHandlerForBindMesWorkOrders(
                dbContext,
                new InMemoryIntegrationEventDeadLetterStore());

            await handler.HandleAsync(CreateProductionVersionCreatedEvent(), CancellationToken.None);
            await dbContext.SaveChangesAsync(CancellationToken.None);
        }

        await using var assertionDbContext = CreateDbContext(options);
        var matchingWorkOrder = await assertionDbContext.WorkOrders.SingleAsync(x => x.WorkOrderIdValue == "WO-FG-001");
        var otherWorkOrder = await assertionDbContext.WorkOrders.SingleAsync(x => x.WorkOrderIdValue == "WO-FG-002");
        Assert.Equal("PV-FG-1000", matchingWorkOrder.ProductionVersionId);
        Assert.Null(otherWorkOrder.ProductionVersionId);
        Assert.Equal(1, await assertionDbContext.ProcessedIntegrationEvents.CountAsync());
    }

    [Fact]
    public async Task EngineeringChangeReleasedHandler_RebindsNotStartedOrdersAndMarksStartedWipForDecision()
    {
        var databaseRoot = new InMemoryDatabaseRoot();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"mes-product-engineering-change-{Guid.CreateVersion7():N}", databaseRoot)
            .Options;
        await using (var dbContext = CreateDbContext(options))
        {
            dbContext.WorkOrders.Add(WorkOrder.Create("org-001", "env-dev", "WO-CREATED", "SKU-FG-1000", "PV-OLD", 10m, 10, DateTimeOffset.Parse("2026-07-06T16:00:00Z"), "PCS"));
            var released = WorkOrder.Create("org-001", "env-dev", "WO-RELEASED", "SKU-FG-1000", "PV-OLD", 10m, 10, DateTimeOffset.Parse("2026-07-06T16:00:00Z"), "PCS");
            released.MarkReleased();
            dbContext.WorkOrders.Add(released);
            var started = WorkOrder.Create("org-001", "env-dev", "WO-STARTED", "SKU-FG-1000", "PV-OLD", 10m, 10, DateTimeOffset.Parse("2026-07-06T16:00:00Z"), "PCS");
            started.MarkReleased();
            started.Start(DateTimeOffset.Parse("2026-07-06T08:00:00Z"));
            dbContext.WorkOrders.Add(started);
            var completed = WorkOrder.Create("org-001", "env-dev", "WO-COMPLETE", "SKU-FG-1000", "PV-OLD", 10m, 10, DateTimeOffset.Parse("2026-07-06T16:00:00Z"), "PCS");
            completed.MarkReleased();
            completed.Start(DateTimeOffset.Parse("2026-07-06T08:00:00Z"));
            completed.RecordProductionProgress(10m, 0m, DateTimeOffset.Parse("2026-07-06T09:00:00Z"));
            dbContext.WorkOrders.Add(completed);
            await dbContext.SaveChangesAsync(CancellationToken.None);
        }

        await using (var dbContext = CreateDbContext(options))
        {
            var handler = new EngineeringChangeReleasedIntegrationEventHandlerForMesWip(
                dbContext,
                new InMemoryIntegrationEventDeadLetterStore(),
                new MesEngineeringChangeOptions { NotStartedPolicy = MesEngineeringChangeNotStartedPolicy.AutoRebind });

            await handler.HandleAsync(CreateEngineeringChangeReleasedEvent(), CancellationToken.None);
            await dbContext.SaveChangesAsync(CancellationToken.None);
        }

        await using var assertionDbContext = CreateDbContext(options);
        Assert.Equal("PV-NEW", (await assertionDbContext.WorkOrders.SingleAsync(x => x.WorkOrderIdValue == "WO-CREATED")).ProductionVersionId);
        Assert.Equal("PV-NEW", (await assertionDbContext.WorkOrders.SingleAsync(x => x.WorkOrderIdValue == "WO-RELEASED")).ProductionVersionId);
        Assert.Equal("PV-OLD", (await assertionDbContext.WorkOrders.SingleAsync(x => x.WorkOrderIdValue == "WO-STARTED")).ProductionVersionId);
        Assert.Equal("PV-OLD", (await assertionDbContext.WorkOrders.SingleAsync(x => x.WorkOrderIdValue == "WO-COMPLETE")).ProductionVersionId);

        var impacts = await assertionDbContext.EngineeringChangeWorkOrderImpacts
            .Where(x => x.Status != MesEngineeringChangeImpactStatuses.ArchivedProductionVersion)
            .OrderBy(x => x.WorkOrderId)
            .ToListAsync();
        Assert.Equal(["WO-CREATED", "WO-RELEASED", "WO-STARTED"], impacts.Select(x => x.WorkOrderId));
        Assert.Equal(MesEngineeringChangeImpactStatuses.AutoRebound, impacts.Single(x => x.WorkOrderId == "WO-CREATED").Status);
        Assert.Equal(MesEngineeringChangeImpactStatuses.AutoRebound, impacts.Single(x => x.WorkOrderId == "WO-RELEASED").Status);
        Assert.Equal(MesEngineeringChangeImpactStatuses.PendingDecision, impacts.Single(x => x.WorkOrderId == "WO-STARTED").Status);
        Assert.Equal(WorkOrder.CreatedStatus, impacts.Single(x => x.WorkOrderId == "WO-CREATED").WorkOrderStatusAtDetection);
        Assert.Equal(WorkOrder.ReleasedStatus, impacts.Single(x => x.WorkOrderId == "WO-RELEASED").WorkOrderStatusAtDetection);
        Assert.Equal(WorkOrder.StartedStatus, impacts.Single(x => x.WorkOrderId == "WO-STARTED").WorkOrderStatusAtDetection);
        Assert.Equal(1, await assertionDbContext.ProcessedIntegrationEvents.CountAsync(x => x.ConsumerName == EngineeringChangeReleasedIntegrationEventHandlerForMesWip.ConsumerName));
    }

    [Fact]
    public async Task EngineeringChangeReleasedHandler_PersistsImpactsAndPublishesInsideTransaction()
    {
        var databaseRoot = new InMemoryDatabaseRoot();
        var databaseName = $"mes-product-engineering-change-transaction-{Guid.CreateVersion7():N}";
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName, databaseRoot)
            .Options;
        var services = new ServiceCollection();
        services.AddDbContext<ApplicationDbContext>(builder => builder.UseInMemoryDatabase(databaseName, databaseRoot));
        services.AddScoped<RecordingTransactionUnitOfWork>();
        services.AddScoped<ITransactionUnitOfWork>(provider => provider.GetRequiredService<RecordingTransactionUnitOfWork>());
        services.AddScoped<IMediator>(provider => new TransactionAssertingMediator(provider.GetRequiredService<RecordingTransactionUnitOfWork>()));
        services.AddScoped<IIntegrationEventDeadLetterStore, InMemoryIntegrationEventDeadLetterStore>();
        services.AddSingleton(Options.Create(new MesEngineeringChangeOptions { NotStartedPolicy = MesEngineeringChangeNotStartedPolicy.AutoRebind }));
        services.AddScoped<EngineeringChangeReleasedIntegrationEventHandlerForMesWip>();
        await using var provider = services.BuildServiceProvider();

        await using (var scope = provider.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var started = WorkOrder.Create("org-001", "env-dev", "WO-STARTED", "SKU-FG-1000", "PV-OLD", 10m, 10, DateTimeOffset.Parse("2026-07-06T16:00:00Z"), "PCS");
            started.MarkReleased();
            started.Start(DateTimeOffset.Parse("2026-07-06T08:00:00Z"));
            dbContext.WorkOrders.Add(started);
            await dbContext.SaveChangesAsync(CancellationToken.None);
        }

        await using (var scope = provider.CreateAsyncScope())
        {
            var unitOfWork = scope.ServiceProvider.GetRequiredService<RecordingTransactionUnitOfWork>();
            var mediator = (TransactionAssertingMediator)scope.ServiceProvider.GetRequiredService<IMediator>();
            var handler = scope.ServiceProvider.GetRequiredService<EngineeringChangeReleasedIntegrationEventHandlerForMesWip>();

            await handler.HandleAsync(CreateEngineeringChangeReleasedEvent(), CancellationToken.None);

            Assert.Equal(1, mediator.PublishCount);
            Assert.True(unitOfWork.BeginCalled);
            Assert.True(unitOfWork.CommitCalled);
            Assert.False(unitOfWork.RollbackCalled);
        }

        await using var assertionDbContext = CreateDbContext(options);
        var impact = await assertionDbContext.EngineeringChangeWorkOrderImpacts
            .SingleAsync(x => x.Status == MesEngineeringChangeImpactStatuses.PendingDecision);
        Assert.Equal("WO-STARTED", impact.WorkOrderId);
        Assert.Equal(1, await assertionDbContext.ProcessedIntegrationEvents.CountAsync(x => x.ConsumerName == EngineeringChangeReleasedIntegrationEventHandlerForMesWip.ConsumerName));
    }

    [Fact]
    public async Task EngineeringChangeReleasedHandler_BlocksNotStartedOrdersWhenManualConfirmationPolicyIsConfigured()
    {
        var databaseRoot = new InMemoryDatabaseRoot();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"mes-product-engineering-change-block-{Guid.CreateVersion7():N}", databaseRoot)
            .Options;
        await using (var dbContext = CreateDbContext(options))
        {
            var released = WorkOrder.Create("org-001", "env-dev", "WO-RELEASED", "SKU-FG-1000", "PV-OLD", 10m, 10, DateTimeOffset.Parse("2026-07-06T16:00:00Z"), "PCS");
            released.MarkReleased();
            dbContext.WorkOrders.Add(released);
            await dbContext.SaveChangesAsync(CancellationToken.None);
        }

        await using (var dbContext = CreateDbContext(options))
        {
            var handler = new EngineeringChangeReleasedIntegrationEventHandlerForMesWip(
                dbContext,
                new InMemoryIntegrationEventDeadLetterStore(),
                new MesEngineeringChangeOptions { NotStartedPolicy = MesEngineeringChangeNotStartedPolicy.BlockForManualConfirmation });

            await handler.HandleAsync(CreateEngineeringChangeReleasedEvent(), CancellationToken.None);
            await dbContext.SaveChangesAsync(CancellationToken.None);
        }

        await using var assertionDbContext = CreateDbContext(options);
        var workOrder = await assertionDbContext.WorkOrders.SingleAsync(x => x.WorkOrderIdValue == "WO-RELEASED");
        Assert.Equal(WorkOrder.HoldStatus, workOrder.Status);
        Assert.Equal("PV-OLD", workOrder.ProductionVersionId);
        var impact = await assertionDbContext.EngineeringChangeWorkOrderImpacts
            .SingleAsync(x => x.Status != MesEngineeringChangeImpactStatuses.ArchivedProductionVersion);
        Assert.Equal(MesEngineeringChangeImpactStatuses.BlockedForManualConfirmation, impact.Status);
        Assert.Equal("PV-NEW", impact.SupersededByProductionVersionId);
        Assert.Equal(WorkOrder.ReleasedStatus, impact.WorkOrderStatusAtDetection);
    }

    [Fact]
    public async Task EngineeringChangeReleasedHandler_UsesExactProductionVersionIdMatching()
    {
        var databaseRoot = new InMemoryDatabaseRoot();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"mes-product-engineering-change-case-{Guid.CreateVersion7():N}", databaseRoot)
            .Options;
        await using (var dbContext = CreateDbContext(options))
        {
            dbContext.WorkOrders.Add(WorkOrder.Create("org-001", "env-dev", "WO-LOWERCASE-PV", "SKU-FG-1000", "pv-old", 10m, 10, DateTimeOffset.Parse("2026-07-06T16:00:00Z"), "PCS"));
            await dbContext.SaveChangesAsync(CancellationToken.None);
        }

        await using (var dbContext = CreateDbContext(options))
        {
            var handler = new EngineeringChangeReleasedIntegrationEventHandlerForMesWip(
                dbContext,
                new InMemoryIntegrationEventDeadLetterStore(),
                new MesEngineeringChangeOptions { NotStartedPolicy = MesEngineeringChangeNotStartedPolicy.AutoRebind });

            await handler.HandleAsync(CreateEngineeringChangeReleasedEvent(), CancellationToken.None);
            await dbContext.SaveChangesAsync(CancellationToken.None);
        }

        await using var assertionDbContext = CreateDbContext(options);
        var workOrder = await assertionDbContext.WorkOrders.SingleAsync();
        Assert.Equal("pv-old", workOrder.ProductionVersionId);
        Assert.Equal(WorkOrder.CreatedStatus, workOrder.Status);
        Assert.Empty(await assertionDbContext.EngineeringChangeWorkOrderImpacts
            .Where(x => x.Status != MesEngineeringChangeImpactStatuses.ArchivedProductionVersion)
            .ToListAsync());
    }

    [Fact]
    public async Task ConvertPlanToWorkOrderCommand_RejectsArchivedProductionVersionAfterEcoRelease()
    {
        var databaseRoot = new InMemoryDatabaseRoot();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"mes-product-engineering-change-guard-{Guid.CreateVersion7():N}", databaseRoot)
            .Options;
        await using (var dbContext = CreateDbContext(options))
        {
            dbContext.EngineeringChangeWorkOrderImpacts.Add(MesEngineeringChangeWorkOrderImpact.ArchivedProductionVersion(
                "org-001",
                "env-dev",
                "ECO-721",
                "PV-OLD",
                "PV-NEW",
                new DateOnly(2026, 7, 6),
                DateTimeOffset.Parse("2026-07-06T08:00:00Z")));
            await dbContext.SaveChangesAsync(CancellationToken.None);
        }

        await using (var dbContext = CreateDbContext(options))
        {
            var handler = new ConvertPlanToWorkOrderCommandHandler(dbContext);

            var exception = await Assert.ThrowsAsync<KnownException>(() => handler.Handle(
                new ConvertPlanToWorkOrderCommand(
                    "org-001",
                    "env-dev",
                    "PLAN-721",
                    "WO-NEW",
                    DateTimeOffset.Parse("2026-07-06T09:00:00Z"),
                    "SKU-FG-1000",
                    "PV-OLD",
                    10m,
                    "PCS",
                    DateTimeOffset.Parse("2026-07-07T09:00:00Z"),
                    null),
                CancellationToken.None));
            Assert.Contains("archived production version", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task RecordEngineeringChangeDecisionCommand_StoresActorDecisionReasonAndEcoNumber()
    {
        var databaseRoot = new InMemoryDatabaseRoot();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"mes-product-engineering-change-decision-{Guid.CreateVersion7():N}", databaseRoot)
            .Options;
        await using (var dbContext = CreateDbContext(options))
        {
            dbContext.EngineeringChangeWorkOrderImpacts.Add(MesEngineeringChangeWorkOrderImpact.PendingDecision(
                "org-001",
                "env-dev",
                "WO-STARTED",
                "SKU-FG-1000",
                WorkOrder.StartedStatus,
                "ECO-721",
                "PV-OLD",
                "PV-NEW",
                new DateOnly(2026, 7, 6),
                DateTimeOffset.Parse("2026-07-06T08:00:00Z")));
            var workOrder = WorkOrder.Create("org-001", "env-dev", "WO-STARTED", "SKU-FG-1000", "PV-OLD", 10m, 10, DateTimeOffset.Parse("2026-07-06T16:00:00Z"), "PCS");
            workOrder.MarkReleased();
            workOrder.Start(DateTimeOffset.Parse("2026-07-06T08:00:00Z"));
            dbContext.WorkOrders.Add(workOrder);
            await dbContext.SaveChangesAsync(CancellationToken.None);
        }

        await using (var dbContext = CreateDbContext(options))
        {
            var handler = new RecordEngineeringChangeDecisionCommandHandler(dbContext);
            await handler.Handle(
                new RecordEngineeringChangeDecisionCommand(
                    "org-001",
                    "env-dev",
                    "WO-STARTED",
                    "ECO-721",
                    MesEngineeringChangeDecisions.ContinueWithArchivedVersion,
                    "planner-001",
                    "Customer deviation approved"),
                CancellationToken.None);
            await dbContext.SaveChangesAsync(CancellationToken.None);
        }

        await using var assertionDbContext = CreateDbContext(options);
        var impact = await assertionDbContext.EngineeringChangeWorkOrderImpacts.SingleAsync();
        Assert.Equal(MesEngineeringChangeImpactStatuses.Decided, impact.Status);
        Assert.Equal(MesEngineeringChangeDecisions.ContinueWithArchivedVersion, impact.Decision);
        Assert.Equal("planner-001", impact.DecidedBy);
        Assert.Equal("Customer deviation approved", impact.DecisionReason);
        Assert.Equal("ECO-721", impact.ChangeNumber);
        Assert.NotNull(impact.DecidedAtUtc);
        Assert.Equal(WorkOrder.StartedStatus, (await assertionDbContext.WorkOrders.SingleAsync()).Status);
    }

    [Fact]
    public async Task RecordEngineeringChangeDecisionCommand_KeepsFirstDecisionAndRejectsConflictingDuplicate()
    {
        var databaseRoot = new InMemoryDatabaseRoot();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"mes-product-engineering-change-decision-idempotency-{Guid.CreateVersion7():N}", databaseRoot)
            .Options;
        await using (var dbContext = CreateDbContext(options))
        {
            var blocked = WorkOrder.Create("org-001", "env-dev", "WO-DUP", "SKU-FG-1000", "PV-OLD", 10m, 10, DateTimeOffset.Parse("2026-07-06T16:00:00Z"), "PCS");
            blocked.MarkReleased();
            blocked.Hold("Engineering change ECO-721 requires production version confirmation.");
            dbContext.WorkOrders.Add(blocked);
            dbContext.EngineeringChangeWorkOrderImpacts.Add(MesEngineeringChangeWorkOrderImpact.BlockedForManualConfirmation(
                "org-001",
                "env-dev",
                "WO-DUP",
                "SKU-FG-1000",
                WorkOrder.ReleasedStatus,
                "ECO-721",
                "PV-OLD",
                "PV-NEW",
                new DateOnly(2026, 7, 6),
                DateTimeOffset.Parse("2026-07-06T08:00:00Z")));
            await dbContext.SaveChangesAsync(CancellationToken.None);
        }

        await using (var dbContext = CreateDbContext(options))
        {
            var handler = new RecordEngineeringChangeDecisionCommandHandler(
                dbContext,
                new FixedTimeProvider(DateTimeOffset.Parse("2026-07-06T09:00:00Z")));
            await handler.Handle(
                new RecordEngineeringChangeDecisionCommand(
                    "org-001",
                    "env-dev",
                    "WO-DUP",
                    "ECO-721",
                    MesEngineeringChangeDecisions.ContinueWithArchivedVersion,
                    "planner-001",
                    "Deviation approved"),
                CancellationToken.None);
            await dbContext.SaveChangesAsync(CancellationToken.None);
        }

        await using (var dbContext = CreateDbContext(options))
        {
            var handler = new RecordEngineeringChangeDecisionCommandHandler(
                dbContext,
                new FixedTimeProvider(DateTimeOffset.Parse("2026-07-06T10:00:00Z")));
            await handler.Handle(
                new RecordEngineeringChangeDecisionCommand(
                    "org-001",
                    "env-dev",
                    "WO-DUP",
                    "ECO-721",
                    MesEngineeringChangeDecisions.ContinueWithArchivedVersion,
                    "planner-002",
                    "Duplicate submission"),
                CancellationToken.None);
            await dbContext.SaveChangesAsync(CancellationToken.None);
        }

        await using (var assertionDbContext = CreateDbContext(options))
        {
            var impact = await assertionDbContext.EngineeringChangeWorkOrderImpacts.SingleAsync();
            Assert.Equal(MesEngineeringChangeDecisions.ContinueWithArchivedVersion, impact.Decision);
            Assert.Equal("planner-001", impact.DecidedBy);
            Assert.Equal("Deviation approved", impact.DecisionReason);
            Assert.Equal(DateTimeOffset.Parse("2026-07-06T09:00:00Z"), impact.DecidedAtUtc);
            Assert.Equal(WorkOrder.ReleasedStatus, (await assertionDbContext.WorkOrders.SingleAsync()).Status);
        }

        await using (var dbContext = CreateDbContext(options))
        {
            var handler = new RecordEngineeringChangeDecisionCommandHandler(
                dbContext,
                new FixedTimeProvider(DateTimeOffset.Parse("2026-07-06T11:00:00Z")));
            var exception = await Assert.ThrowsAsync<KnownException>(() => handler.Handle(
                new RecordEngineeringChangeDecisionCommand(
                    "org-001",
                    "env-dev",
                    "WO-DUP",
                    "ECO-721",
                    MesEngineeringChangeDecisions.AbortWorkOrder,
                    "planner-003",
                    "Conflicting duplicate"),
                CancellationToken.None));
            Assert.Contains("already recorded", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task RecordEngineeringChangeDecisionCommand_ContinuesBlockedOrderOrAbortsImpactedOrder()
    {
        var databaseRoot = new InMemoryDatabaseRoot();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"mes-product-engineering-change-decision-actions-{Guid.CreateVersion7():N}", databaseRoot)
            .Options;
        await using (var dbContext = CreateDbContext(options))
        {
            var blocked = WorkOrder.Create("org-001", "env-dev", "WO-BLOCKED", "SKU-FG-1000", "PV-OLD", 10m, 10, DateTimeOffset.Parse("2026-07-06T16:00:00Z"), "PCS");
            blocked.MarkReleased();
            blocked.Hold("Engineering change ECO-721 requires production version confirmation.");
            dbContext.WorkOrders.Add(blocked);
            var started = WorkOrder.Create("org-001", "env-dev", "WO-ABORT", "SKU-FG-1000", "PV-OLD", 10m, 10, DateTimeOffset.Parse("2026-07-06T16:00:00Z"), "PCS");
            started.MarkReleased();
            started.Start(DateTimeOffset.Parse("2026-07-06T08:00:00Z"));
            dbContext.WorkOrders.Add(started);
            dbContext.MaterialIssueRequests.Add(MaterialIssueRequest.Create(
                "org-001",
                "env-dev",
                "MIR-ECO-ABORT",
                "WO-ABORT",
                "OP-ECO-10",
                "MAT-001",
                "PCS",
                2m,
                DateTimeOffset.Parse("2026-07-06T07:00:00Z")));
            dbContext.FinishedGoodsReceiptRequests.Add(FinishedGoodsReceiptRequest.Create(
                "org-001",
                "env-dev",
                "FGR-ECO-ABORT",
                "WO-ABORT",
                "SKU-FG-1000",
                10m,
                "PCS",
                DateTimeOffset.Parse("2026-07-06T16:00:00Z")));
            dbContext.OperationTasks.Add(OperationTask.Queue(
                "org-001",
                "env-dev",
                "WO-ABORT",
                "OP-ECO-10",
                10,
                "WC-10",
                [],
                DateTimeOffset.Parse("2026-07-06T08:00:00Z"),
                TimeSpan.FromMinutes(30)));
            dbContext.EngineeringChangeWorkOrderImpacts.Add(MesEngineeringChangeWorkOrderImpact.BlockedForManualConfirmation(
                "org-001",
                "env-dev",
                "WO-BLOCKED",
                "SKU-FG-1000",
                WorkOrder.ReleasedStatus,
                "ECO-721",
                "PV-OLD",
                "PV-NEW",
                new DateOnly(2026, 7, 6),
                DateTimeOffset.Parse("2026-07-06T08:00:00Z")));
            dbContext.EngineeringChangeWorkOrderImpacts.Add(MesEngineeringChangeWorkOrderImpact.PendingDecision(
                "org-001",
                "env-dev",
                "WO-ABORT",
                "SKU-FG-1000",
                WorkOrder.StartedStatus,
                "ECO-721",
                "PV-OLD",
                "PV-NEW",
                new DateOnly(2026, 7, 6),
                DateTimeOffset.Parse("2026-07-06T08:00:00Z")));
            await dbContext.SaveChangesAsync(CancellationToken.None);
        }

        await using (var dbContext = CreateDbContext(options))
        {
            var handler = new RecordEngineeringChangeDecisionCommandHandler(dbContext);
            await handler.Handle(
                new RecordEngineeringChangeDecisionCommand(
                    "org-001",
                    "env-dev",
                    "WO-BLOCKED",
                    "ECO-721",
                    MesEngineeringChangeDecisions.ContinueWithArchivedVersion,
                    "planner-001",
                    "Deviation approved"),
                CancellationToken.None);
            await handler.Handle(
                new RecordEngineeringChangeDecisionCommand(
                    "org-001",
                    "env-dev",
                    "WO-ABORT",
                    "ECO-721",
                    MesEngineeringChangeDecisions.AbortWorkOrder,
                    "planner-001",
                    "Deviation rejected"),
                CancellationToken.None);
            await dbContext.SaveChangesAsync(CancellationToken.None);
        }

        await using var assertionDbContext = CreateDbContext(options);
        var continued = await assertionDbContext.WorkOrders.SingleAsync(x => x.WorkOrderIdValue == "WO-BLOCKED");
        var aborted = await assertionDbContext.WorkOrders.SingleAsync(x => x.WorkOrderIdValue == "WO-ABORT");
        var cancelledMaterialIssue = await assertionDbContext.MaterialIssueRequests.SingleAsync(x => x.RequestNo == "MIR-ECO-ABORT");
        var cancelledReceipt = await assertionDbContext.FinishedGoodsReceiptRequests.SingleAsync(x => x.RequestNo == "FGR-ECO-ABORT");
        var cancelledOperationTask = await assertionDbContext.OperationTasks.SingleAsync(x => x.OperationTaskIdValue == "OP-ECO-10");
        Assert.Equal(WorkOrder.ReleasedStatus, continued.Status);
        Assert.Null(continued.HoldReason);
        Assert.Equal(WorkOrder.CancelledStatus, aborted.Status);
        Assert.Contains("ECO-721", aborted.CancelReason, StringComparison.Ordinal);
        Assert.Equal(MaterialIssueRequest.CancelledStatus, cancelledMaterialIssue.Status);
        Assert.Equal(FinishedGoodsReceiptRequest.CancelledStatus, cancelledReceipt.Status);
        Assert.Equal(OperationTaskLifecycleStatus.Cancelled, cancelledOperationTask.Status);
    }

    private static ProductionVersionCreatedIntegrationEvent CreateProductionVersionCreatedEvent()
    {
        return new ProductionVersionCreatedIntegrationEvent(
            "evt-product-engineering-pv-created-001",
            ProductEngineeringIntegrationEventTypes.ProductionVersionCreated,
            ProductEngineeringIntegrationEventVersions.V1,
            DateTimeOffset.Parse("2026-06-01T07:30:00Z"),
            ProductEngineeringIntegrationEventSources.BusinessProductEngineering,
            "corr-001",
            "cause-001",
            "org-001",
            "env-dev",
            "product-engineering",
            "product-engineering:production-version-created:org-001:env-dev:PV-FG-1000",
            new ProductionVersionCreatedPayload(
                "PV-FG-1000",
                "SKU-FG-1000",
                "MBOM-FG:A",
                "ROUTE-FG:A",
                new DateOnly(2026, 6, 1),
                null));
    }

    private static EngineeringChangeReleasedIntegrationEvent CreateEngineeringChangeReleasedEvent()
    {
        return new EngineeringChangeReleasedIntegrationEvent(
            "evt-product-engineering-eco-721",
            ProductEngineeringIntegrationEventTypes.EngineeringChangeReleased,
            ProductEngineeringIntegrationEventVersions.V1,
            DateTimeOffset.Parse("2026-07-06T08:00:00Z"),
            ProductEngineeringIntegrationEventSources.BusinessProductEngineering,
            "corr-eco-721",
            "cause-eco-721",
            "org-001",
            "env-dev",
            "product-engineering",
            "product-engineering:engineering-change-released:org-001:env-dev:ECO-721",
            new EngineeringChangeReleasedPayload(
                "change-721",
                "ECO-721",
                ["PV-OLD"],
                new DateOnly(2026, 7, 6),
                [
                    new EngineeringChangeAffectedVersionPayload(
                        "production-version",
                        "PV-OLD",
                        "PV-NEW")
                ]));
    }

    private static ApplicationDbContext CreateDbContext(DbContextOptions<ApplicationDbContext> options)
    {
        return new ApplicationDbContext(options, new NoopMediator());
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class NoopMediator : IMediator
    {
        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification => Task.CompletedTask;

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("Noop mediator cannot send requests.");
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest
        {
            throw new NotSupportedException("Noop mediator cannot send requests.");
        }

        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("Noop mediator cannot send requests.");
        }

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("Noop mediator cannot stream requests.");
        }

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("Noop mediator cannot stream requests.");
        }
    }

    private sealed class TransactionAssertingMediator(RecordingTransactionUnitOfWork unitOfWork) : IMediator
    {
        public int PublishCount { get; private set; }

        public Task Publish(object notification, CancellationToken cancellationToken = default)
        {
            PublishCount++;
            Assert.NotNull(unitOfWork.CurrentTransaction);
            return Task.CompletedTask;
        }

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification
        {
            PublishCount++;
            Assert.NotNull(unitOfWork.CurrentTransaction);
            return Task.CompletedTask;
        }

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("Transaction asserting mediator cannot send requests.");
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest
        {
            throw new NotSupportedException("Transaction asserting mediator cannot send requests.");
        }

        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("Transaction asserting mediator cannot send requests.");
        }

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("Transaction asserting mediator cannot stream requests.");
        }

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("Transaction asserting mediator cannot stream requests.");
        }
    }

    private sealed class RecordingTransactionUnitOfWork : ITransactionUnitOfWork
    {
        public IDbContextTransaction? CurrentTransaction { get; set; }

        public bool BeginCalled { get; private set; }

        public bool CommitCalled { get; private set; }

        public bool RollbackCalled { get; private set; }

        public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
        {
            BeginCalled = true;
            return Task.FromResult<IDbContextTransaction>(new RecordingDbContextTransaction());
        }

        public Task CommitAsync(CancellationToken cancellationToken = default)
        {
            CommitCalled = true;
            CurrentTransaction = null;
            return Task.CompletedTask;
        }

        public Task RollbackAsync(CancellationToken cancellationToken = default)
        {
            RollbackCalled = true;
            CurrentTransaction = null;
            return Task.CompletedTask;
        }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(0);
        }

        public Task<bool> SaveEntitiesAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(true);
        }

        public void Dispose()
        {
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingDbContextTransaction : IDbContextTransaction
    {
        public Guid TransactionId { get; } = Guid.CreateVersion7();

        public void Dispose()
        {
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }

        public void Commit()
        {
        }

        public Task CommitAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public void Rollback()
        {
        }

        public Task RollbackAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }
}
