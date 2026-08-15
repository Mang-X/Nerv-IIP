using MediatR;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.FinishedGoodsReceiptRequestAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.QualityAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Business.Mes.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Contracts.DemandPlanning;
using Nerv.IIP.Contracts.Inventory;
using Nerv.IIP.Contracts.ProductEngineering;
using Nerv.IIP.Contracts.Quality;
using Nerv.IIP.Messaging.CAP;

namespace Nerv.IIP.Business.Mes.Web.Tests;

[Collection(MesPostgresLaneDatabase.CollectionName)]
public sealed class MesCapSaveBoundaryPostgresTests
{
    [PostgreSqlFact]
    public async Task Ncr_disposition_persists_business_fact_and_inbox_across_scopes_and_replay()
    {
        await MesPostgresLaneDatabase.ResetSchemaAsync();
        await MigrateDatabaseAsync();
        await using (var seedContext = CreateDbContext())
        {
            seedContext.WorkOrders.Add(WorkOrder.Create(
                "org-001",
                "env-dev",
                "WO-001",
                "SKU-FG",
                "PV-FG-001",
                1m,
                10,
                DateTimeOffset.Parse("2026-08-20T16:00:00Z"),
                "PCS"));
            seedContext.DefectRecords.Add(DefectRecord.Create(
                "org-001",
                "env-dev",
                "DEF-001",
                "WO-001",
                "OP-10",
                "SURFACE",
                1m,
                DateTimeOffset.Parse("2026-08-13T08:00:00Z")));
            await seedContext.SaveChangesAsync();
        }

        var integrationEvent = CreateNcrDispositionEvent();
        await using (var handlerContext = CreateDbContext())
        {
            var handler = new NcrDispositionDecidedIntegrationEventHandlerForUpdateMesDefect(
                handlerContext,
                new InMemoryIntegrationEventDeadLetterStore());
            await handler.HandleAsync(integrationEvent, CancellationToken.None);
        }

        await AssertNcrDispositionPersistedAsync();

        await using (var replayContext = CreateDbContext())
        {
            var replayHandler = new NcrDispositionDecidedIntegrationEventHandlerForUpdateMesDefect(
                replayContext,
                new InMemoryIntegrationEventDeadLetterStore());
            await replayHandler.HandleAsync(integrationEvent, CancellationToken.None);
        }

        await AssertNcrDispositionPersistedAsync();
    }

    [PostgreSqlFact]
    public async Task Production_version_created_persists_binding_and_inbox_across_scopes_and_replay()
    {
        await MesPostgresLaneDatabase.ResetSchemaAsync();
        await MigrateDatabaseAsync();
        await using (var seedContext = CreateDbContext())
        {
            seedContext.WorkOrders.Add(WorkOrder.Create(
                "org-001",
                "env-dev",
                "WO-PV-001",
                "SKU-FG-1000",
                null,
                10m,
                10,
                DateTimeOffset.Parse("2026-08-20T16:00:00Z"),
                "PCS"));
            await seedContext.SaveChangesAsync();
        }

        var integrationEvent = CreateProductionVersionCreatedEvent();
        await using (var handlerContext = CreateDbContext())
        {
            var handler = new ProductionVersionCreatedIntegrationEventHandlerForBindMesWorkOrders(
                handlerContext,
                new InMemoryIntegrationEventDeadLetterStore());
            await handler.HandleAsync(integrationEvent, CancellationToken.None);
        }

        await AssertProductionVersionBindingPersistedAsync();

        await using (var replayContext = CreateDbContext())
        {
            var replayHandler = new ProductionVersionCreatedIntegrationEventHandlerForBindMesWorkOrders(
                replayContext,
                new InMemoryIntegrationEventDeadLetterStore());
            await replayHandler.HandleAsync(integrationEvent, CancellationToken.None);
        }

        await AssertProductionVersionBindingPersistedAsync();
    }

    [PostgreSqlFact]
    public async Task Planning_existing_work_order_early_return_persists_only_inbox()
    {
        await MesPostgresLaneDatabase.ResetSchemaAsync();
        await MigrateDatabaseAsync();
        await using (var seedContext = CreateDbContext())
        {
            seedContext.WorkOrders.Add(WorkOrder.Create(
                "org-001",
                "env-dev",
                "WO-EXISTING-001",
                "SKU-FG-1000",
                "PV-FG-1000",
                12m,
                10,
                DateTimeOffset.Parse("2026-08-20T16:00:00Z"),
                "PCS",
                new SourcePlanReference(
                    DemandPlanningSourceReferences.DemandPlanning,
                    DemandPlanningSourceReferences.PlanningSuggestion,
                    "SUG-EXISTING-001",
                    "SO-001")));
            await seedContext.SaveChangesAsync();
        }

        await using (var handlerContext = CreateDbContext())
        {
            var handler = new PlanningSuggestionAcceptedIntegrationEventHandlerForCreateMesWorkOrder(
                handlerContext,
                new InMemoryIntegrationEventDeadLetterStore());
            await handler.HandleAsync(CreatePlanningSuggestionEvent(), CancellationToken.None);
        }

        await using var assertionContext = CreateDbContext();
        var workOrder = Assert.Single(await assertionContext.WorkOrders.AsNoTracking().ToListAsync());
        Assert.Equal("WO-EXISTING-001", workOrder.WorkOrderIdValue);
        Assert.Equal(WorkOrder.CreatedStatus, workOrder.Status);
        Assert.Equal(1, await CountInboxAsync(
            assertionContext,
            PlanningSuggestionAcceptedIntegrationEventHandlerForCreateMesWorkOrder.ConsumerName));
    }

    [PostgreSqlFact]
    public async Task Stock_movement_posted_no_match_early_return_persists_only_inbox()
    {
        await MesPostgresLaneDatabase.ResetSchemaAsync();
        await MigrateDatabaseAsync();
        await AssertStockMovementPostedEarlyReturnPersistsOnlyInboxAsync(seedMismatchingReceipt: false);
    }

    [PostgreSqlFact]
    public async Task Stock_movement_posted_mismatch_early_return_persists_only_inbox()
    {
        await MesPostgresLaneDatabase.ResetSchemaAsync();
        await MigrateDatabaseAsync();
        await AssertStockMovementPostedEarlyReturnPersistsOnlyInboxAsync(seedMismatchingReceipt: true);
    }

    private static async Task AssertStockMovementPostedEarlyReturnPersistsOnlyInboxAsync(
        bool seedMismatchingReceipt)
    {
        if (seedMismatchingReceipt)
        {
            await using var seedContext = CreateDbContext();
            seedContext.WorkOrders.Add(WorkOrder.Create(
                "org-001",
                "env-dev",
                "WO-001",
                "SKU-FG",
                "PV-FG-001",
                8m,
                10,
                DateTimeOffset.Parse("2026-08-20T16:00:00Z"),
                "PCS"));
            seedContext.FinishedGoodsReceiptRequests.Add(FinishedGoodsReceiptRequest.Create(
                "org-001",
                "env-dev",
                "FGR-001",
                "WO-001",
                "SKU-FG",
                8m,
                "PCS",
                DateTimeOffset.Parse("2026-08-13T09:00:00Z"),
                "LOT-FG-001",
                null));
            await seedContext.SaveChangesAsync();
        }

        await using (var handlerContext = CreateDbContext())
        {
            var handler = new StockMovementPostedIntegrationEventHandlerForMarkMesReceiptPosted(
                handlerContext,
                new InMemoryIntegrationEventDeadLetterStore());
            await handler.HandleAsync(
                CreateStockMovementPostedEvent(
                    seedMismatchingReceipt ? "FGR-001" : "FGR-MISSING",
                    quantity: seedMismatchingReceipt ? 9m : 8m),
                CancellationToken.None);
        }

        await using var assertionContext = CreateDbContext();
        var receipts = await assertionContext.FinishedGoodsReceiptRequests.AsNoTracking().ToListAsync();
        if (seedMismatchingReceipt)
        {
            var receipt = Assert.Single(receipts);
            Assert.Equal(FinishedGoodsReceiptRequest.RequestedStatus, receipt.Status);
            Assert.Null(receipt.PostedInventoryMovementId);
        }
        else
        {
            Assert.Empty(receipts);
        }

        Assert.Equal(1, await CountInboxAsync(
            assertionContext,
            StockMovementPostedIntegrationEventHandlerForMarkMesReceiptPosted.ConsumerName));
    }

    [PostgreSqlFact]
    public async Task Stock_movement_posting_failed_unknown_prefix_early_return_persists_only_inbox()
    {
        await MesPostgresLaneDatabase.ResetSchemaAsync();
        await MigrateDatabaseAsync();

        await using (var handlerContext = CreateDbContext())
        {
            var handler = new StockMovementPostingFailedIntegrationEventHandlerForMarkMesRequestFailed(
                handlerContext,
                new InMemoryIntegrationEventDeadLetterStore());
            await handler.HandleAsync(CreateUnknownStockMovementFailureEvent(), CancellationToken.None);
        }

        await using var assertionContext = CreateDbContext();
        Assert.Empty(await assertionContext.FinishedGoodsReceiptRequests.AsNoTracking().ToListAsync());
        Assert.Empty(await assertionContext.ProductionReportMaterialConsumptions.AsNoTracking().ToListAsync());
        Assert.Empty(await assertionContext.MaterialIssueRequests.AsNoTracking().ToListAsync());
        Assert.Equal(1, await CountInboxAsync(
            assertionContext,
            StockMovementPostingFailedIntegrationEventHandlerForMarkMesRequestFailed.ConsumerName));
    }

    private static async Task MigrateDatabaseAsync()
    {
        await using var dbContext = CreateDbContext();
        MesPostgresLaneDatabase.AssertUsesGovernedDatabase(dbContext);
        await dbContext.Database.MigrateAsync();
    }

    private static ApplicationDbContext CreateDbContext()
    {
        return new ApplicationDbContext(MesPostgresLaneDatabase.CreateOptions(), NoopMediator.Instance);
    }

    private static Task<int> CountInboxAsync(ApplicationDbContext dbContext, string consumerName)
    {
        return dbContext.ProcessedIntegrationEvents.CountAsync(x => x.ConsumerName == consumerName);
    }

    private static async Task AssertNcrDispositionPersistedAsync()
    {
        await using var assertionContext = CreateDbContext();
        var defect = Assert.Single(await assertionContext.DefectRecords.AsNoTracking().ToListAsync());
        Assert.Equal(DefectRecord.ReworkPendingStatus, defect.Status);
        Assert.Equal("NCR-001", defect.NcrId);
        Assert.Equal("RW-WO-001", defect.DispositionReferenceId);
        Assert.Equal(1, await CountInboxAsync(
            assertionContext,
            NcrDispositionDecidedIntegrationEventHandlerForUpdateMesDefect.ConsumerName));
    }

    private static async Task AssertProductionVersionBindingPersistedAsync()
    {
        await using var assertionContext = CreateDbContext();
        var workOrder = Assert.Single(await assertionContext.WorkOrders.AsNoTracking().ToListAsync());
        Assert.Equal("PV-FG-1000", workOrder.ProductionVersionId);
        Assert.Equal(1, await CountInboxAsync(
            assertionContext,
            ProductionVersionCreatedIntegrationEventHandlerForBindMesWorkOrders.ConsumerName));
    }

    private static NcrDispositionDecidedIntegrationEvent CreateNcrDispositionEvent()
    {
        return new NcrDispositionDecidedIntegrationEvent(
            "evt-quality-disposition-001",
            QualityIntegrationEventTypes.DispositionDecided,
            QualityIntegrationEventVersions.V1,
            DateTimeOffset.Parse("2026-08-13T09:00:00Z"),
            QualityIntegrationEventSources.BusinessQuality,
            "corr-quality-disposition-001",
            "quality-command-001",
            "org-001",
            "env-dev",
            "user:quality",
            "quality:disposition:DEF-001",
            new NcrDispositionDecidedPayload(
                "NCR-001",
                "NCR-2026-001",
                "SKU-FG",
                1m,
                QualityNcrDispositionTypes.Rework,
                "approval-001",
                "RW-WO-001",
                null,
                null,
                DateTimeOffset.Parse("2026-08-13T09:00:00Z"))
            {
                SourceDocumentId = "DEF-001"
            });
    }

    private static ProductionVersionCreatedIntegrationEvent CreateProductionVersionCreatedEvent()
    {
        return new ProductionVersionCreatedIntegrationEvent(
            "evt-product-engineering-pv-created-001",
            ProductEngineeringIntegrationEventTypes.ProductionVersionCreated,
            ProductEngineeringIntegrationEventVersions.V1,
            DateTimeOffset.Parse("2026-08-13T09:00:00Z"),
            ProductEngineeringIntegrationEventSources.BusinessProductEngineering,
            "corr-product-engineering-pv-created-001",
            "product-engineering-command-001",
            "org-001",
            "env-dev",
            "user:product-engineering",
            "product-engineering:production-version-created:org-001:env-dev:PV-FG-1000",
            new ProductionVersionCreatedPayload(
                "PV-FG-1000",
                "SKU-FG-1000",
                "MBOM-FG:A",
                "ROUTE-FG:A",
                new DateOnly(2026, 8, 13),
                null));
    }

    private static PlanningSuggestionAcceptedIntegrationEvent CreatePlanningSuggestionEvent()
    {
        return new PlanningSuggestionAcceptedIntegrationEvent(
            "evt-demand-suggestion-existing-001",
            DemandPlanningIntegrationEventTypes.PlanningSuggestionAccepted,
            DemandPlanningIntegrationEventVersions.V1,
            DateTimeOffset.Parse("2026-08-13T09:00:00Z"),
            DemandPlanningIntegrationEventSources.BusinessDemandPlanning,
            "corr-demand-suggestion-existing-001",
            "demand-command-001",
            "org-001",
            "env-dev",
            "user:planner",
            "demand-planning:planning-suggestion-accepted:org-001:env-dev:SUG-EXISTING-001",
            new PlanningSuggestionAcceptedPayload(
                "SUG-EXISTING-001",
                "MRP-001",
                DemandPlanningSuggestionTypes.PlannedWorkOrder,
                "SKU-FG-1000",
                "PCS",
                "SITE-A",
                12m,
                new DateOnly(2026, 8, 20),
                new DateOnly(2026, 8, 13),
                "SO-001",
                "PV-FG-1000",
                DemandPlanningDownstreamReferences.BusinessMes,
                DemandPlanningDownstreamReferences.WorkOrder,
                "WO-EXISTING-001"));
    }

    private static StockMovementPostedIntegrationEvent CreateStockMovementPostedEvent(
        string sourceDocumentId,
        decimal quantity)
    {
        return new StockMovementPostedIntegrationEvent(
            $"evt-inventory-posted-{sourceDocumentId}",
            InventoryIntegrationEventTypes.StockMovementPosted,
            InventoryIntegrationEventVersions.V1,
            DateTimeOffset.Parse("2026-08-13T09:05:00Z"),
            InventoryIntegrationEventSources.BusinessInventory,
            "corr-inventory-posted-001",
            "inventory-command-001",
            "org-001",
            "env-dev",
            "system:business-inventory",
            $"inventory:posted:{sourceDocumentId}",
            new StockMovementPostedPayload(
                "INV-MOV-001",
                "inbound",
                InventoryIntegrationEventSources.BusinessMes,
                sourceDocumentId,
                "WO-001",
                $"mes:finished-goods-receipt:org-001:env-dev:{sourceDocumentId}",
                "SKU-FG",
                "PCS",
                "SITE-001",
                "WH-WB-FG-01",
                "LOT-FG-001",
                null,
                "Unrestricted",
                "production",
                null,
                quantity,
                DateTimeOffset.Parse("2026-08-13T09:05:00Z"),
                null,
                null));
    }

    private static StockMovementPostingFailedIntegrationEvent CreateUnknownStockMovementFailureEvent()
    {
        return new StockMovementPostingFailedIntegrationEvent(
            "evt-inventory-failed-unknown-001",
            InventoryIntegrationEventTypes.StockMovementPostingFailed,
            InventoryIntegrationEventVersions.V1,
            DateTimeOffset.Parse("2026-08-13T09:10:00Z"),
            InventoryIntegrationEventSources.BusinessInventory,
            "corr-inventory-failed-unknown-001",
            "inventory-command-unknown-001",
            "org-001",
            "env-dev",
            "system:business-inventory",
            "inventory:failed:unknown-001",
            new StockMovementPostingFailedPayload(
                "inbound",
                InventoryIntegrationEventSources.BusinessMes,
                "UNKNOWN-001",
                null,
                "mes:unknown:org-001:env-dev:UNKNOWN-001",
                "SKU-FG",
                "PCS",
                "SITE-001",
                "WH-WB-FG-01",
                "LOT-FG-001",
                null,
                "Unrestricted",
                "production",
                null,
                8m,
                "inventory.validation.failed",
                "posting rejected",
                DateTimeOffset.Parse("2026-08-13T09:10:00Z")));
    }

    private sealed class NoopMediator : IMediator
    {
        public static readonly NoopMediator Instance = new();

        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification => Task.CompletedTask;

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest => throw new NotSupportedException();

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(
            object request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
