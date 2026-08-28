using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Erp.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Messaging.CAP;

namespace Nerv.IIP.Business.Erp.Web.Tests;

public sealed partial class OperationLaborSettlementHandlerTests
{
    [Fact]
    public async Task Settlement_after_capitalization_posts_one_balanced_delta_without_revaluing_capitalized_cost()
    {
        await using var db = CreateDb();
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        db.WorkCenterCostRates.Add(Rate(
            7, 80m, DateTimeOffset.Parse("2026-08-01T00:00:00Z"), null));
        await db.SaveChangesAsync();
        await new ProductionReportRecordedIntegrationEventHandlerForAccumulateLaborCost(db, deadLetters, db, TestWorkOrderCostMutationLock.Instance)
            .HandleAsync(Report("evt-report-001", "RPT-001", AugustCompletedAtUtc.AddMinutes(-10)), CancellationToken.None);
        var cost = await db.WorkOrderCosts.Include(x => x.Details).SingleAsync();
        cost.Complete(10m, 1, 0, AugustCompletedAtUtc);
        cost.Capitalize("MOVE-FG-001", 10m, 16m, AugustCompletedAtUtc.AddMinutes(1));
        cost.RecordWipClearance(160m);
        await db.SaveChangesAsync();

        await new MesOperationActualTimeSettledIntegrationEventHandlerForAccumulateLaborCost(db, db, TestWorkOrderCostMutationLock.Instance, new OperationLaborSettlementOrchestrator(db, deadLetters))
            .HandleAsync(
                Settled("evt-settled-r1", 1, AugustCompletedAtUtc,
                    90 * TimeSpan.TicksPerMinute, ["RPT-001"]),
                CancellationToken.None);

        cost = await db.WorkOrderCosts.Include(x => x.Details).SingleAsync();
        Assert.Equal(120m, cost.LaborCost);
        Assert.Equal(160m, cost.CapitalizedCost);
        Assert.Equal(120m, cost.WipClearedCost);
        var voucher = await db.JournalVouchers.Include(x => x.Lines).SingleAsync();
        Assert.Equal(voucher.Lines.Sum(x => x.DebitAmount), voucher.Lines.Sum(x => x.CreditAmount));
        Assert.Equal(40m, voucher.Lines.Sum(x => x.DebitAmount));
    }

    [Theory]
    [InlineData(false, 120)]
    [InlineData(true, 0)]
    public async Task Partial_finished_goods_then_labor_delta_then_final_receipt_stays_balanced(
        bool voidAfterSettlement,
        decimal expectedLaborCost)
    {
        await using var db = CreateDb();
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        db.WorkCenterCostRates.Add(Rate(7, 80m, DateTimeOffset.Parse("2026-08-01T00:00:00Z"), null));
        await db.SaveChangesAsync();
        await new ProductionReportRecordedIntegrationEventHandlerForAccumulateLaborCost(
                db, deadLetters, db, TestWorkOrderCostMutationLock.Instance)
            .HandleAsync(Report("evt-report-partial", "RPT-PARTIAL", AugustCompletedAtUtc.AddMinutes(-10)), CancellationToken.None);
        var cost = await db.WorkOrderCosts.Include(x => x.Details).SingleAsync();
        cost.Complete(10m, 1, 0, AugustCompletedAtUtc);
        Assert.False(cost.IsFullyCapitalized);
        await db.SaveChangesAsync();
        var receiptConsumer = new StockMovementPostedIntegrationEventHandlerForAccumulateMaterialCost(db, deadLetters, db);
        await receiptConsumer.HandleAsync(FinishedGoodsReceipt("evt-fg-partial", "MOVE-FG-PARTIAL", "FGR-PARTIAL", 5m), CancellationToken.None);
        cost = await db.WorkOrderCosts.Include(x => x.Details).SingleAsync();
        Assert.False(cost.IsFullyCapitalized);

        var settled = Settled("evt-settle-partial", 1, AugustCompletedAtUtc, 90 * TimeSpan.TicksPerMinute, ["RPT-PARTIAL"]);
        await new MesOperationActualTimeSettledIntegrationEventHandlerForAccumulateLaborCost(
                db, db, TestWorkOrderCostMutationLock.Instance,
                new OperationLaborSettlementOrchestrator(db, deadLetters))
            .HandleAsync(settled, CancellationToken.None);
        if (voidAfterSettlement)
            await new MesOperationActualTimeSettlementVoidedIntegrationEventHandlerForReverseLaborCost(
                    db, db, TestWorkOrderCostMutationLock.Instance,
                    new OperationLaborSettlementOrchestrator(db, deadLetters))
                .HandleAsync(Voided("evt-void-partial", settled, AugustCompletedAtUtc.AddMinutes(4)), CancellationToken.None);

        var vouchersBeforeFinalReceipt = await db.JournalVouchers.Include(x => x.Lines).ToListAsync();
        Assert.Single(vouchersBeforeFinalReceipt);
        Assert.DoesNotContain(
            vouchersBeforeFinalReceipt.SelectMany(x => x.Lines),
            line => line.AccountCode == "5101-PRODUCTION-VARIANCE");

        await receiptConsumer.HandleAsync(FinishedGoodsReceipt("evt-fg-final", "MOVE-FG-FINAL", "FGR-FINAL", 5m), CancellationToken.None);

        cost = await db.WorkOrderCosts.Include(x => x.Details).SingleAsync();
        Assert.Equal(expectedLaborCost, cost.LaborCost);
        Assert.Equal(10m, cost.CapitalizedQuantity);
        Assert.True(cost.IsFullyCapitalized);
        Assert.Equal(expectedLaborCost, cost.WipClearedCost);
        var vouchers = await db.JournalVouchers.Include(x => x.Lines).ToListAsync();
        Assert.Equal(2, vouchers.Count);
        Assert.Single(vouchers.SelectMany(x => x.Lines),
            line => line.AccountCode == "5101-PRODUCTION-VARIANCE");
        Assert.All(vouchers, voucher =>
            Assert.Equal(voucher.Lines.Sum(x => x.DebitAmount), voucher.Lines.Sum(x => x.CreditAmount)));
    }
}
