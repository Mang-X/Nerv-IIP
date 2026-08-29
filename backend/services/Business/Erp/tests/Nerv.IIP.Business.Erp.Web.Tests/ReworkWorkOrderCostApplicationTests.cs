using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.AccountingPeriodAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.WorkCenterMachineOverheadRateAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.WorkOrderCostAggregate;
using Nerv.IIP.Business.Erp.Infrastructure;
using Nerv.IIP.Business.Erp.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Business.Erp.Web.Application.Queries.Finance;
using Nerv.IIP.Business.Erp.Web.Application.Auth;
using Nerv.IIP.Business.Erp.Web.Endpoints.Erp;
using Nerv.IIP.ServiceAuth;
using Nerv.IIP.Contracts.Mes;
using Nerv.IIP.Contracts.Inventory;
using Nerv.IIP.Messaging.CAP;

namespace Nerv.IIP.Business.Erp.Web.Tests;

public sealed class ReworkWorkOrderCostApplicationTests
{
    [Fact]
    public async Task Rework_creation_attributes_the_existing_cost_engine_to_its_ncr_and_source_work_order()
    {
        await using var db = CreateDb();
        var integrationEvent = ReworkCreated("evt-rework-created-001");
        var handler = new ReworkWorkOrderCreatedIntegrationEventHandlerForAttributeCost(
            db,
            db,
            TestWorkOrderCostMutationLock.Instance);

        await handler.HandleAsync(integrationEvent, CancellationToken.None);

        var cost = await db.WorkOrderCosts.SingleAsync();
        Assert.True(cost.IsRework);
        Assert.Equal("WO-RW-001", cost.WorkOrderId);
        Assert.Equal("FG-001", cost.SkuCode);
        Assert.Equal("ncr-001", cost.SourceNcrId);
        Assert.Equal("NCR-2026-0001", cost.SourceNcrCode);
        Assert.Equal("WO-SOURCE-001", cost.SourceWorkOrderId);
    }

    [Fact]
    public async Task Cost_readback_filters_by_ncr_and_separates_ordinary_from_rework_totals()
    {
        await using var db = CreateDb();
        var occurredAtUtc = DateTimeOffset.Parse("2026-08-30T02:00:00Z");
        var ordinary = WorkOrderCost.Open("org-001", "env-dev", "WO-SOURCE-001", "FG-001");
        ordinary.RecordLabor("RPT-SOURCE-001", "WC-01", 1m, 25m, "CNY", false, occurredAtUtc);
        var rework = WorkOrderCost.Open("org-001", "env-dev", "WO-RW-001", "FG-001");
        rework.AttributeRework("ncr-001", "NCR-2026-0001", "WO-SOURCE-001", "FG-001");
        rework.RecordLabor("RPT-RW-001", "WC-01", 2m, 15m, "CNY", false, occurredAtUtc);
        db.WorkOrderCosts.AddRange(ordinary, rework);
        await db.SaveChangesAsync();

        var response = await new ListWorkOrderCostsQueryHandler(db).Handle(
            new ListWorkOrderCostsQuery(
                "org-001",
                "env-dev",
                SourceNcrId: "ncr-001"),
            CancellationToken.None);

        var item = Assert.Single(response.Items);
        Assert.Equal("rework", item.CostKind);
        Assert.Equal("WO-RW-001", item.WorkOrderId);
        Assert.Equal("WO-SOURCE-001", item.SourceWorkOrderId);
        Assert.Equal(0m, response.OrdinaryCostTotal);
        Assert.Equal(30m, response.ReworkCostTotal);
    }

    [Fact]
    public void Work_order_cost_readback_is_a_governed_public_finance_contract()
    {
        Assert.NotNull(typeof(ListWorkOrderCostsRequest).GetProperty("WorkOrderId"));
        Assert.NotNull(typeof(ListWorkOrderCostsRequest).GetProperty("SourceNcrId"));
        Assert.NotNull(typeof(ListWorkOrderCostsRequest).GetProperty("SourceWorkOrderId"));

        var contract = ErpEndpointContracts.All.Single(
            x => x.OperationId == "listErpWorkOrderCosts");
        Assert.Equal("GET", contract.HttpMethod);
        Assert.Equal("/api/business/v1/erp/finance/work-order-costs", contract.Route);
        Assert.Equal(ErpPermissionCodes.FinanceRead, contract.PermissionCode);
        Assert.Equal(InternalServiceAuthorizationPolicy.Name, contract.AuthorizationPolicy);
    }

    [Fact]
    public async Task Rework_cost_inputs_close_only_the_rework_order_and_leave_the_source_order_unchanged()
    {
        await using var db = CreateDb();
        var completedAtUtc = DateTimeOffset.Parse("2026-08-30T03:00:00Z");
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        var sourceCost = WorkOrderCost.Open("org-001", "env-dev", "WO-SOURCE-001", "FG-001");
        sourceCost.RecordLabor("RPT-SOURCE-001", "WC-01", 1m, 25m, "CNY", false, completedAtUtc.AddDays(-1));
        db.WorkOrderCosts.Add(sourceCost);
        db.WorkCenterCostRates.Add(WorkCenterCostRate.Define(
            "org-001", "env-dev", "WC-01", 50m, "CNY",
            DateTimeOffset.Parse("2026-01-01T00:00:00Z"), null, 1,
            "system:test", "governed test rate", DateTimeOffset.Parse("2026-01-01T00:00:00Z")));
        db.AccountingPeriods.Add(AccountingPeriod.Open(
            "org-001", "env-dev", "2026-08", new(2026, 8, 1), new(2026, 8, 31)));
        db.WorkCenterMachineOverheadRates.Add(WorkCenterMachineOverheadRate.DefineApplicable(
            "org-001", "env-dev", "WC-01", "2026-08",
            30_000m, 10_000m, 1_000m, "CNY", 1,
            "system:test", "approved machine rate", DateTimeOffset.Parse("2026-08-01T00:00:00Z")));
        await db.SaveChangesAsync();

        var reworkCreated = ReworkCreated("evt-rework-created-closure");
        var attributionHandler = new ReworkWorkOrderCreatedIntegrationEventHandlerForAttributeCost(
            db, db, TestWorkOrderCostMutationLock.Instance);
        await attributionHandler.HandleAsync(reworkCreated, CancellationToken.None);
        await attributionHandler.HandleAsync(reworkCreated, CancellationToken.None);

        var completed = new WorkOrderCompletedIntegrationEvent(
            "evt-rework-completed", MesIntegrationEventTypes.WorkOrderCompleted, MesIntegrationEventVersions.V1,
            completedAtUtc, MesIntegrationEventSources.BusinessMes, "WO-RW-001", "WO-RW-001",
            "org-001", "env-dev", "system:mes", "rework-completed-001",
            new WorkOrderCompletedPayload(
                "WO-RW-001", "FG-001", 2m, 2m, 0m, completedAtUtc, 1, 1));
        await new WorkOrderCompletedIntegrationEventHandlerForCapitalizeCost(db, deadLetters, db)
            .HandleAsync(completed, CancellationToken.None);
        Assert.False((await db.WorkOrderCosts.SingleAsync(x => x.WorkOrderId == "WO-RW-001")).CapitalizationPublished);

        var material = new StockMovementPostedIntegrationEvent(
            "evt-rework-material", InventoryIntegrationEventTypes.StockMovementPosted, 1,
            completedAtUtc.AddMinutes(-20), InventoryIntegrationEventSources.BusinessInventory,
            "RPT-RW-001", "RPT-RW-001", "org-001", "env-dev", "system:inventory",
            "rework-material-001",
            new StockMovementPostedPayload(
                "MOVE-RW-001", "outbound", InventoryIntegrationEventSources.BusinessMes,
                "RPT-RW-001", "MIR-RW-001", "mes:production-consumption:rw-001",
                "RM-001", "kg", "production", "line-side", "LOT-RM-001", null,
                "unrestricted", "organization", "org-001", -3m,
                completedAtUtc.AddMinutes(-20), 20m, -60m));
        await new StockMovementPostedIntegrationEventHandlerForAccumulateMaterialCost(db, deadLetters, db)
            .HandleAsync(material, CancellationToken.None);

        var actualTime = new MesOperationActualTimeSettledV2IntegrationEvent(
            "evt-rework-machine", MesIntegrationEventTypes.OperationActualTimeSettled,
            MesIntegrationEventVersions.V2, completedAtUtc.AddMinutes(-5),
            MesIntegrationEventSources.BusinessMes, "corr-rework-machine", "cause-rework-machine",
            "org-001", "env-dev", "operator:test", "rework-machine-r1",
            new OperationActualTimeSettledV2Payload(
                "WO-RW-001", "OP-RW-001", "WC-01", 1, completedAtUtc.AddMinutes(-10),
                TimeSpan.TicksPerHour, 2 * TimeSpan.TicksPerHour, [], "DEVICE-001",
                MesMachineTimeFactStatus.Available, 2 * TimeSpan.TicksPerHour,
                MesMachineTimeBasisCodes.SingleDeviceActiveMinusExplicitPauseV1));
        var machineHandler = new MesOperationActualTimeSettledV2IntegrationEventHandlerForAccumulateMachineOverhead(
            db,
            db,
            TestWorkOrderCostMutationLock.Instance,
            new OperationMachineOverheadSettlementOrchestrator(
                db,
                deadLetters,
                new PostgreSqlErpAdvisoryLockAllocator(db)));
        await machineHandler.HandleAsync(actualTime, CancellationToken.None);
        await machineHandler.HandleAsync(actualTime, CancellationToken.None);

        var report = new ProductionReportRecordedIntegrationEvent(
            "evt-rework-report", MesIntegrationEventTypes.ProductionReportRecorded,
            MesIntegrationEventVersions.V1, completedAtUtc.AddMinutes(-15),
            MesIntegrationEventSources.BusinessMes, "RPT-RW-001", "WO-RW-001",
            "org-001", "env-dev", "operator:test", "rework-report-001",
            new ProductionReportRecordedPayload(
                "RPT-RW-001", "WO-RW-001", "OP-RW-001", "WC-01", "DEVICE-001",
                2m, 0m, 0m, "ea", 1m, completedAtUtc.AddMinutes(-15), false,
                MaterialMovementCount: 1));
        var reportHandler = new ProductionReportRecordedIntegrationEventHandlerForAccumulateLaborCost(
            db, deadLetters, db, TestWorkOrderCostMutationLock.Instance);
        await reportHandler.HandleAsync(report, CancellationToken.None);
        await reportHandler.HandleAsync(report, CancellationToken.None);

        var costs = await db.WorkOrderCosts.Include(x => x.Details).OrderBy(x => x.WorkOrderId).ToArrayAsync();
        var reworkCost = Assert.Single(costs, x => x.WorkOrderId == "WO-RW-001");
        var unchangedSourceCost = Assert.Single(costs, x => x.WorkOrderId == "WO-SOURCE-001");
        Assert.Equal(100m, reworkCost.LaborCost);
        Assert.Equal(60m, reworkCost.MaterialCost);
        Assert.Equal(80m, reworkCost.MachineOverheadCost);
        Assert.Equal(240m, reworkCost.TotalAccumulatedCost);
        Assert.True(reworkCost.CapitalizationPublished);
        Assert.Equal(25m, unchangedSourceCost.TotalAccumulatedCost);
        Assert.False(unchangedSourceCost.IsRework);
        Assert.Empty(await deadLetters.ListAsync(null, null, CancellationToken.None));

        var bySource = await new ListWorkOrderCostsQueryHandler(db).Handle(
            new ListWorkOrderCostsQuery(
                "org-001", "env-dev", SourceWorkOrderId: "WO-SOURCE-001"),
            CancellationToken.None);
        Assert.Equal("WO-RW-001", Assert.Single(bySource.Items).WorkOrderId);
        Assert.Equal(240m, bySource.ReworkCostTotal);
        Assert.Equal(0m, bySource.OrdinaryCostTotal);

        var reversedReport = report with
        {
            EventId = "evt-rework-report-reversal",
            IdempotencyKey = "rework-report-reversal-001",
            Payload = report.Payload with
            {
                ReportNo = "RPT-RW-REV-001",
                IsReversal = true,
                ReversedReportNo = "RPT-RW-001",
                MaterialMovementCount = 0,
            },
        };
        await reportHandler.HandleAsync(reversedReport, CancellationToken.None);
        await reportHandler.HandleAsync(reversedReport, CancellationToken.None);

        var actualTimeVoid = new MesOperationActualTimeSettlementVoidedV2IntegrationEvent(
            "evt-rework-machine-void", MesIntegrationEventTypes.OperationActualTimeSettlementVoided,
            MesIntegrationEventVersions.V2, completedAtUtc.AddMinutes(5),
            MesIntegrationEventSources.BusinessMes, actualTime.CorrelationId, actualTime.EventId,
            actualTime.OrganizationId, actualTime.EnvironmentId, "operator:test", "rework-machine-r1-void",
            new OperationActualTimeSettlementVoidedV2Payload(
                actualTime.Payload.WorkOrderId,
                actualTime.Payload.OperationTaskId,
                actualTime.Payload.WorkCenterId,
                actualTime.Payload.SettlementRevision,
                actualTime.Payload.CompletedAtUtc,
                completedAtUtc.AddMinutes(5),
                actualTime.Payload.ActualLaborTicks,
                actualTime.Payload.ActualMachineTicks,
                actualTime.Payload.CoveredProductionReportNos,
                actualTime.Payload.DeviceAssetId,
                actualTime.Payload.MachineTimeStatus,
                actualTime.Payload.BillableMachineTicks,
                actualTime.Payload.MachineTimeBasisCode));
        var machineVoidHandler = new MesOperationActualTimeSettlementVoidedV2IntegrationEventHandlerForReverseMachineOverhead(
            db,
            db,
            TestWorkOrderCostMutationLock.Instance,
            new OperationMachineOverheadSettlementOrchestrator(
                db,
                deadLetters,
                new PostgreSqlErpAdvisoryLockAllocator(db)));
        await machineVoidHandler.HandleAsync(actualTimeVoid, CancellationToken.None);
        await machineVoidHandler.HandleAsync(actualTimeVoid, CancellationToken.None);

        reworkCost = await db.WorkOrderCosts.Include(x => x.Details)
            .SingleAsync(x => x.WorkOrderId == "WO-RW-001");
        unchangedSourceCost = await db.WorkOrderCosts.Include(x => x.Details)
            .SingleAsync(x => x.WorkOrderId == "WO-SOURCE-001");
        Assert.Equal(0m, reworkCost.LaborCost);
        Assert.Equal(60m, reworkCost.MaterialCost);
        Assert.Equal(0m, reworkCost.MachineOverheadCost);
        Assert.Equal(60m, reworkCost.TotalAccumulatedCost);
        Assert.Equal(25m, unchangedSourceCost.TotalAccumulatedCost);
        Assert.Empty(await deadLetters.ListAsync(null, null, CancellationToken.None));
    }

    private static ReworkWorkOrderCreatedIntegrationEvent ReworkCreated(string eventId) =>
        new(
            eventId,
            MesIntegrationEventTypes.ReworkWorkOrderCreated,
            MesIntegrationEventVersions.V1,
            DateTimeOffset.Parse("2026-08-30T01:00:00Z"),
            MesIntegrationEventSources.BusinessMes,
            "corr-rework-001",
            "cause-ncr-001",
            "org-001",
            "env-dev",
            "system:business-mes",
            "rework-work-order-created:org-001:env-dev:ncr-001",
            new ReworkWorkOrderCreatedPayload(
                "ncr-001",
                "NCR-2026-0001",
                "WO-RW-001",
                "WO-SOURCE-001",
                "OP-SOURCE-001",
                "FG-001",
                2m,
                "LOT-001",
                null,
                DateTimeOffset.Parse("2026-08-30T01:00:00Z")));

    private static ApplicationDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"erp-rework-cost-{Guid.CreateVersion7():N}")
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new ApplicationDbContext(options, new NoopMediator());
    }

    private sealed class NoopMediator : IMediator
    {
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
