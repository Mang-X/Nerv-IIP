using MediatR;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.WorkOrderCostAggregate;
using Nerv.IIP.Business.Erp.Domain.DomainEvents;
using Nerv.IIP.Business.Erp.Infrastructure;
using Nerv.IIP.Business.Erp.Web.Application.IntegrationEventConverters;
using Nerv.IIP.Business.Erp.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Contracts.Inventory;
using Nerv.IIP.Contracts.Mes;
using Nerv.IIP.Messaging.CAP;

namespace Nerv.IIP.Business.Erp.Web.Tests;

public sealed class WorkOrderCostEventClosureTests
{
    [Fact]
    public async Task Real_mes_and_inventory_events_close_actual_work_order_cost()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase($"erp-cost-{Guid.CreateVersion7():N}").Options;
        await using var db = new ApplicationDbContext(options, new NoopMediator());
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        db.WorkCenterCostRates.Add(WorkCenterCostRate.Define("org-001", "env-dev", "WC-01", 50m));
        await db.SaveChangesAsync();

        var report = new ProductionReportRecordedIntegrationEvent("evt-report", MesIntegrationEventTypes.ProductionReportRecorded, 1, DateTimeOffset.Parse("2026-07-11T01:00:00Z"), MesIntegrationEventSources.BusinessMes, "RPT-001", "WO-001", "org-001", "env-dev", "operator", "report-001",
            new ProductionReportRecordedPayload("RPT-001", "WO-001", "OP-001", "WC-01", null, 8m, 2m, 0m, "ea", 5m, DateTimeOffset.Parse("2026-07-11T01:00:00Z"), false, MaterialMovementCount: 1));
        var movement = new StockMovementPostedIntegrationEvent("evt-material", InventoryIntegrationEventTypes.StockMovementPosted, 1, DateTimeOffset.Parse("2026-07-11T02:00:00Z"), InventoryIntegrationEventSources.BusinessInventory, "RPT-001", "RPT-001", "org-001", "env-dev", "inventory", "move-001",
            new StockMovementPostedPayload("MOVE-001", "outbound", InventoryIntegrationEventSources.BusinessMes, "RPT-001", "MIR-001", "mes:production-consumption:001", "RM-001", "kg", "production", "line-side", "LOT-001", null, "unrestricted", "organization", "org-001", -3m, DateTimeOffset.Parse("2026-07-11T02:00:00Z"), 20m, -60m));
        await new StockMovementPostedIntegrationEventHandlerForAccumulateMaterialCost(db, deadLetters).HandleAsync(movement, CancellationToken.None);
        await db.SaveChangesAsync();
        Assert.Single(db.PendingMaterialCosts);

        await new ProductionReportRecordedIntegrationEventHandlerForAccumulateLaborCost(db, deadLetters).HandleAsync(report, CancellationToken.None);
        await db.SaveChangesAsync();
        Assert.Empty(db.PendingMaterialCosts);

        var uncostedReport = report with
        {
            EventId = "evt-report-uncosted",
            IdempotencyKey = "report-uncosted-001",
            Payload = report.Payload with { ReportNo = "RPT-UNCOSTED", WorkCenterId = string.Empty, TheoreticalRatePerHour = null, GoodQuantity = 1m, ScrapQuantity = 0m, MaterialMovementCount = 0 },
        };
        await new ProductionReportRecordedIntegrationEventHandlerForAccumulateLaborCost(db, deadLetters).HandleAsync(uncostedReport, CancellationToken.None);
        await db.SaveChangesAsync();

        var completed = new WorkOrderCompletedIntegrationEvent("evt-completed", MesIntegrationEventTypes.WorkOrderCompleted, 1, DateTimeOffset.Parse("2026-07-11T03:00:00Z"), MesIntegrationEventSources.BusinessMes, "WO-001", "WO-001", "org-001", "env-dev", "mes", "completed-001",
            new WorkOrderCompletedPayload("WO-001", "FG-001", 10m, 8m, 2m, DateTimeOffset.Parse("2026-07-11T03:00:00Z"), 2, 1));
        await new WorkOrderCompletedIntegrationEventHandlerForCapitalizeCost(db).HandleAsync(completed, CancellationToken.None);
        await db.SaveChangesAsync();

        var cost = await db.WorkOrderCosts.Include(x => x.Details).SingleAsync();
        Assert.Equal(100m, cost.LaborCost);
        Assert.Equal(60m, cost.MaterialCost);
        Assert.Equal(160m, cost.TotalAccumulatedCost);
        var domainEvent = Assert.IsType<WorkOrderCostCompletedDomainEvent>(Assert.Single(cost.GetDomainEvents()));
        var integrationEvent = new WorkOrderCostCompletedIntegrationEventConverter().Convert(domainEvent);
        Assert.Equal(20m, integrationEvent.Payload.UnitCost);
        Assert.Equal("WO-001", integrationEvent.Payload.WorkOrderId);

        var receiptPosting = movement with
        {
            EventId = "evt-finished-goods",
            IdempotencyKey = "move-fg-001",
            Payload = movement.Payload with
            {
                InventoryMovementId = "MOVE-FG-001",
                MovementType = "inbound",
                SourceDocumentId = "FGR-001",
                SourceDocumentLineId = "WO-001",
                IdempotencyKey = "mes:finished-goods-receipt:FGR-001",
                SkuCode = "FG-001",
                Quantity = 4m,
                UnitCost = 20m,
                MovementAmount = 80m,
            },
        };
        await new StockMovementPostedIntegrationEventHandlerForAccumulateMaterialCost(db, deadLetters).HandleAsync(receiptPosting, CancellationToken.None);
        await db.SaveChangesAsync();

        Assert.Equal(80m, cost.CapitalizedCost);
        Assert.Equal(80m, cost.WipClearedCost);

        var finalReceiptPosting = receiptPosting with
        {
            EventId = "evt-finished-goods-2",
            IdempotencyKey = "move-fg-002",
            Payload = receiptPosting.Payload with { InventoryMovementId = "MOVE-FG-002", IdempotencyKey = "mes:finished-goods-receipt:FGR-002", SourceDocumentId = "FGR-002" },
        };
        await new StockMovementPostedIntegrationEventHandlerForAccumulateMaterialCost(db, deadLetters).HandleAsync(finalReceiptPosting, CancellationToken.None);
        await db.SaveChangesAsync();

        Assert.Equal(160m, cost.CapitalizedCost);
        Assert.Equal(0m, cost.VarianceCost);
        var vouchers = await db.JournalVouchers.Include(x => x.Lines).OrderBy(x => x.VoucherNo).ToListAsync();
        Assert.Equal(2, vouchers.Count);
        Assert.All(vouchers, voucher => Assert.Equal(80m, voucher.Lines.Sum(x => x.LocalDebitAmount)));
        Assert.All(vouchers, voucher => Assert.Equal(80m, voucher.Lines.Sum(x => x.LocalCreditAmount)));
        Assert.All(vouchers.SelectMany(x => x.Lines), line => Assert.Contains(awaitAccountCodes(db), code => code == line.AccountCode));

        var materialReversal = movement with
        {
            EventId = "evt-material-reversal",
            IdempotencyKey = "move-reversal-001",
            Payload = movement.Payload with { InventoryMovementId = "MOVE-REV-001", SourceDocumentId = "RPT-REV-001", Quantity = 3m, MovementAmount = 60m, IdempotencyKey = "mes:production-consumption:reversal-001" },
        };
        await new StockMovementPostedIntegrationEventHandlerForAccumulateMaterialCost(db, deadLetters).HandleAsync(materialReversal, CancellationToken.None);
        await db.SaveChangesAsync();
        Assert.Single(db.PendingMaterialCosts);

        var reportReversal = report with
        {
            EventId = "evt-report-reversal",
            IdempotencyKey = "report-reversal-001",
            Payload = report.Payload with { ReportNo = "RPT-REV-001", IsReversal = true, ReversedReportNo = "RPT-001", MaterialMovementCount = 0 },
        };
        await new ProductionReportRecordedIntegrationEventHandlerForAccumulateLaborCost(db, deadLetters).HandleAsync(reportReversal, CancellationToken.None);
        await db.SaveChangesAsync();

        Assert.Equal(1, cost.ReceivedMaterialMovementCount);
        Assert.Equal(2, cost.ReceivedReportCount);
        Assert.Equal(0m, cost.TotalAccumulatedCost);
        Assert.Equal(-160m, cost.VarianceCost);
        Assert.Equal(0m, cost.WipClearedCost);
        Assert.Empty(db.PendingMaterialCosts);
        Assert.Equal(4, await db.JournalVouchers.CountAsync());
    }

    private static string[] awaitAccountCodes(ApplicationDbContext db) => db.GLAccounts.Select(x => x.Code).ToArray();

    private sealed class NoopMediator : IMediator
    {
        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default) where TNotification : INotification => Task.CompletedTask;
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest => throw new NotSupportedException();
        public Task<object?> Send(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
