using DotNetCore.CAP;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.WorkOrderCostAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.GLAccountAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.JournalVoucherAggregate;
using Nerv.IIP.Business.Erp.Infrastructure;
using Nerv.IIP.Business.Erp.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Contracts.IntegrationEvents;
using Nerv.IIP.Contracts.Inventory;
using Nerv.IIP.Contracts.Mes;
using Nerv.IIP.Messaging.CAP;
using NetCorePal.Extensions.DistributedTransactions;

namespace Nerv.IIP.Business.Erp.Web.Application.IntegrationEventHandlers;

[IntegrationEventConsumer("Nerv.IIP.Contracts.Mes.ProductionReportRecordedIntegrationEvent", ConsumerName)]
public sealed class ProductionReportRecordedIntegrationEventHandlerForAccumulateLaborCost(ApplicationDbContext dbContext, IIntegrationEventDeadLetterStore deadLetterStore)
    : IIntegrationEventHandler<ProductionReportRecordedIntegrationEvent>, ICapSubscribe
{
    public const string ConsumerName = "business-erp.production-report-labor-cost";
    public Task HandleAsync(ProductionReportRecordedIntegrationEvent integrationEvent, CancellationToken cancellationToken) => HandleValidAsync(integrationEvent, cancellationToken);
    [CapSubscribe(nameof(ProductionReportRecordedIntegrationEvent), Group = ConsumerName)]
    public Task HandleCapAsync(ProductionReportRecordedIntegrationEvent integrationEvent, CancellationToken cancellationToken) => HandleAsync(integrationEvent, cancellationToken);

    private async Task HandleValidAsync(ProductionReportRecordedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        if (!string.Equals(integrationEvent.SourceService, MesIntegrationEventSources.BusinessMes, StringComparison.OrdinalIgnoreCase)) return;
        var outputQuantity = Math.Abs(integrationEvent.Payload.GoodQuantity + integrationEvent.Payload.ScrapQuantity + integrationEvent.Payload.ReworkQuantity);
        var hasLaborBasis = !string.IsNullOrWhiteSpace(integrationEvent.Payload.WorkCenterId) && integrationEvent.Payload.TheoreticalRatePerHour is > 0m && outputQuantity > 0m;
        WorkCenterCostRate? rate = null;
        if (hasLaborBasis)
        {
            rate = await dbContext.WorkCenterCostRates
                .Where(x => x.OrganizationId == integrationEvent.OrganizationId
                    && x.EnvironmentId == integrationEvent.EnvironmentId
                    && x.WorkCenterId == integrationEvent.Payload.WorkCenterId
                    && x.EffectiveFromUtc <= integrationEvent.Payload.ReportedAtUtc
                    && (x.EffectiveToUtc == null || integrationEvent.Payload.ReportedAtUtc < x.EffectiveToUtc))
                .OrderByDescending(x => x.Revision)
                .FirstOrDefaultAsync(cancellationToken);
            if (rate is null)
            {
                await deadLetterStore.AddAsync(IntegrationEventDeadLetterMessage.Create(ConsumerName, integrationEvent, "missing-work-center-cost-rate", $"Work-center cost rate '{integrationEvent.Payload.WorkCenterId}' has no active revision at '{integrationEvent.Payload.ReportedAtUtc:O}'."), cancellationToken);
                return;
            }
        }
        if (!await ErpProcessedIntegrationEventInbox.TryRecordAsync(dbContext, ConsumerName, integrationEvent, cancellationToken)) return;
        var cost = await dbContext.WorkOrderCosts.Include(x => x.Details).SingleOrDefaultAsync(x => x.OrganizationId == integrationEvent.OrganizationId && x.EnvironmentId == integrationEvent.EnvironmentId && x.WorkOrderId == integrationEvent.Payload.WorkOrderId, cancellationToken);
        if (cost is null)
        {
            cost = WorkOrderCost.Open(integrationEvent.OrganizationId, integrationEvent.EnvironmentId, integrationEvent.Payload.WorkOrderId, integrationEvent.Payload.WorkOrderId);
            dbContext.WorkOrderCosts.Add(cost);
        }
        var priorTotal = cost.TotalAccumulatedCost;
        if (hasLaborBasis)
            cost.RecordLabor(integrationEvent.Payload.ReportNo, integrationEvent.Payload.WorkCenterId, outputQuantity / integrationEvent.Payload.TheoreticalRatePerHour!.Value, rate!.HourlyRate, integrationEvent.Payload.IsReversal, integrationEvent.Payload.ReportedAtUtc);
        else
            cost.RecordUncostedReport(integrationEvent.Payload.ReportNo, integrationEvent.Payload.IsReversal, integrationEvent.Payload.ReportedAtUtc);
        if (cost.CapitalizationPublished && integrationEvent.Payload.IsReversal)
            await CostVariancePosting.PostLateAdjustmentAsync(dbContext, cost, cost.TotalAccumulatedCost - priorTotal, integrationEvent.Payload.ReportNo, integrationEvent.Payload.ReportedAtUtc, cancellationToken);
        var pending = await dbContext.PendingMaterialCosts.Where(x => x.OrganizationId == integrationEvent.OrganizationId && x.EnvironmentId == integrationEvent.EnvironmentId && x.ReportNo == integrationEvent.Payload.ReportNo).ToListAsync(cancellationToken);
        foreach (var item in pending)
        {
            var priorPendingTotal = cost.TotalAccumulatedCost;
            cost.RecordMaterial(item.MovementId, item.ReportNo, item.SkuCode, item.SignedQuantity, item.UnitCost, item.PostedAtUtc);
            if (cost.CapitalizationPublished && item.SignedQuantity < 0m)
                await CostVariancePosting.PostLateAdjustmentAsync(dbContext, cost, cost.TotalAccumulatedCost - priorPendingTotal, item.MovementId, item.PostedAtUtc, cancellationToken);
            dbContext.PendingMaterialCosts.Remove(item);
        }
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}

[IntegrationEventConsumer("Nerv.IIP.Contracts.Inventory.StockMovementPostedIntegrationEvent", ConsumerName)]
public sealed class StockMovementPostedIntegrationEventHandlerForAccumulateMaterialCost(ApplicationDbContext dbContext, IIntegrationEventDeadLetterStore deadLetterStore)
    : IIntegrationEventHandler<StockMovementPostedIntegrationEvent>, ICapSubscribe
{
    public const string ConsumerName = "business-erp.production-material-cost";
    public Task HandleAsync(StockMovementPostedIntegrationEvent integrationEvent, CancellationToken cancellationToken) => HandleValidAsync(integrationEvent, cancellationToken);
    [CapSubscribe(nameof(StockMovementPostedIntegrationEvent), Group = ConsumerName)]
    public Task HandleCapAsync(StockMovementPostedIntegrationEvent integrationEvent, CancellationToken cancellationToken) => HandleAsync(integrationEvent, cancellationToken);
    private async Task HandleValidAsync(StockMovementPostedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        var payload = integrationEvent.Payload;
        if (!string.Equals(payload.SourceService, InventoryIntegrationEventSources.BusinessMes, StringComparison.OrdinalIgnoreCase)) return;
        var isFinishedGoods = payload.IdempotencyKey.StartsWith("mes:finished-goods-receipt:", StringComparison.OrdinalIgnoreCase);
        var isProductionMaterial = payload.IdempotencyKey.StartsWith("mes:production-consumption:", StringComparison.OrdinalIgnoreCase);
        if (!isFinishedGoods && !isProductionMaterial) return;
        var unitCost = payload.UnitCost ?? (payload.MovementAmount is not null && payload.Quantity != 0m ? Math.Abs(payload.MovementAmount.Value / payload.Quantity) : null);
        if (unitCost is null or <= 0m)
        {
            await deadLetterStore.AddAsync(IntegrationEventDeadLetterMessage.Create(ConsumerName, integrationEvent, "missing-actual-unit-cost", $"Inventory movement '{payload.InventoryMovementId}' has no actual moving-average cost."), cancellationToken);
            return;
        }
        if (!await ErpProcessedIntegrationEventInbox.TryRecordAsync(dbContext, ConsumerName, integrationEvent, cancellationToken)) return;
        if (isFinishedGoods)
        {
            if (string.IsNullOrWhiteSpace(payload.SourceDocumentLineId))
            {
                await deadLetterStore.AddAsync(IntegrationEventDeadLetterMessage.Create(ConsumerName, integrationEvent, "missing-work-order-id", "Finished-goods Inventory posting must carry the MES work-order id."), cancellationToken);
                return;
            }
            var completedCost = await dbContext.WorkOrderCosts.Include(x => x.Details).SingleOrDefaultAsync(x => x.OrganizationId == integrationEvent.OrganizationId && x.EnvironmentId == integrationEvent.EnvironmentId && x.WorkOrderId == payload.SourceDocumentLineId, cancellationToken)
                ?? throw new InvalidOperationException($"Work-order cost '{payload.SourceDocumentLineId}' was not found for capitalization posting.");
            var priorCapitalized = completedCost.CapitalizedCost;
            var priorWipCleared = completedCost.WipClearedCost;
            completedCost.Capitalize(payload.InventoryMovementId, payload.Quantity, unitCost.Value, payload.PostedAtUtc);
            await EnsureCapitalizationAccountsAsync(dbContext, integrationEvent.OrganizationId, integrationEvent.EnvironmentId, cancellationToken);
            var movementAmount = completedCost.CapitalizedCost - priorCapitalized;
            var isFinalReceipt = completedCost.CapitalizedQuantity >= completedCost.CompletedQuantity - 0.000001m;
            var variance = isFinalReceipt ? completedCost.TotalAccumulatedCost - completedCost.CapitalizedCost : 0m;
            var wipClearance = isFinalReceipt ? completedCost.TotalAccumulatedCost - priorWipCleared : movementAmount;
            var lines = new List<JournalVoucherLineDraft>
            {
                new("1406-FINISHED-GOODS", movementAmount, 0m, $"Finished goods {completedCost.WorkOrderId}"),
                new("1405-WIP", 0m, wipClearance, $"Clear WIP {completedCost.WorkOrderId}"),
            };
            if (variance > 0m) lines.Add(new("5101-PRODUCTION-VARIANCE", variance, 0m, $"Uncapitalized variance {completedCost.WorkOrderId}"));
            else if (variance < 0m) lines.Add(new("5101-PRODUCTION-VARIANCE", 0m, -variance, $"Over-capitalized variance {completedCost.WorkOrderId}"));
            completedCost.RecordWipClearance(wipClearance);
            dbContext.JournalVouchers.Add(JournalVoucher.Post(integrationEvent.OrganizationId, integrationEvent.EnvironmentId, $"JV-WOC-{completedCost.WorkOrderId}-{payload.InventoryMovementId}", DateOnly.FromDateTime(payload.PostedAtUtc.UtcDateTime), lines));
            await dbContext.SaveChangesAsync(cancellationToken);
            return;
        }
        var cost = await dbContext.WorkOrderCosts.Include(x => x.Details).SingleOrDefaultAsync(x => x.OrganizationId == integrationEvent.OrganizationId && x.EnvironmentId == integrationEvent.EnvironmentId && x.Details.Any(d => d.SourceDocumentId == payload.SourceDocumentId), cancellationToken);
        var signedCostQuantity = payload.Quantity < 0m ? Math.Abs(payload.Quantity) : -Math.Abs(payload.Quantity);
        if (cost is null)
        {
            dbContext.PendingMaterialCosts.Add(PendingMaterialCost.Create(integrationEvent.OrganizationId, integrationEvent.EnvironmentId, payload.InventoryMovementId, payload.SourceDocumentId, payload.SkuCode, signedCostQuantity, unitCost.Value, payload.PostedAtUtc));
            return;
        }
        var priorMaterialTotal = cost.TotalAccumulatedCost;
        cost.RecordMaterial(payload.InventoryMovementId, payload.SourceDocumentId, payload.SkuCode, signedCostQuantity, unitCost.Value, payload.PostedAtUtc);
        if (cost.CapitalizationPublished && signedCostQuantity < 0m)
            await CostVariancePosting.PostLateAdjustmentAsync(dbContext, cost, cost.TotalAccumulatedCost - priorMaterialTotal, payload.InventoryMovementId, payload.PostedAtUtc, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static async Task EnsureCapitalizationAccountsAsync(ApplicationDbContext dbContext, string organizationId, string environmentId, CancellationToken cancellationToken)
    {
        var existing = await dbContext.GLAccounts.Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId).Select(x => x.Code).ToListAsync(cancellationToken);
        if (!existing.Contains("1405-WIP", StringComparer.Ordinal)) dbContext.GLAccounts.Add(GLAccount.Create(organizationId, environmentId, "1405-WIP", "Work in process", GLAccountType.Asset));
        if (!existing.Contains("1406-FINISHED-GOODS", StringComparer.Ordinal)) dbContext.GLAccounts.Add(GLAccount.Create(organizationId, environmentId, "1406-FINISHED-GOODS", "Finished goods inventory", GLAccountType.Asset));
        if (!existing.Contains("5101-PRODUCTION-VARIANCE", StringComparer.Ordinal)) dbContext.GLAccounts.Add(GLAccount.Create(organizationId, environmentId, "5101-PRODUCTION-VARIANCE", "Production cost variance", GLAccountType.Expense));
    }
}

internal static class CostVariancePosting
{
    public static async Task PostLateAdjustmentAsync(ApplicationDbContext dbContext, WorkOrderCost cost, decimal costDelta, string sourceId, DateTimeOffset occurredAtUtc, CancellationToken cancellationToken)
    {
        if (costDelta == 0m) return;
        var required = new[]
        {
            ("1405-WIP", "Work in process", GLAccountType.Asset),
            ("5101-PRODUCTION-VARIANCE", "Production cost variance", GLAccountType.Expense),
        };
        var existing = await dbContext.GLAccounts.Where(x => x.OrganizationId == cost.OrganizationId && x.EnvironmentId == cost.EnvironmentId).Select(x => x.Code).ToListAsync(cancellationToken);
        foreach (var account in required.Where(x => !existing.Contains(x.Item1, StringComparer.Ordinal)))
            dbContext.GLAccounts.Add(GLAccount.Create(cost.OrganizationId, cost.EnvironmentId, account.Item1, account.Item2, account.Item3));

        var amount = Math.Abs(costDelta);
        var lines = costDelta < 0m
            ? new[] { new JournalVoucherLineDraft("1405-WIP", amount, 0m, $"Late cost reversal {sourceId}"), new JournalVoucherLineDraft("5101-PRODUCTION-VARIANCE", 0m, amount, $"Favorable variance {sourceId}") }
            : new[] { new JournalVoucherLineDraft("5101-PRODUCTION-VARIANCE", amount, 0m, $"Unfavorable variance {sourceId}"), new JournalVoucherLineDraft("1405-WIP", 0m, amount, $"Late cost input {sourceId}") };
        cost.RecordWipClearance(costDelta);
        dbContext.JournalVouchers.Add(JournalVoucher.Post(cost.OrganizationId, cost.EnvironmentId, $"JV-WOC-ADJ-{cost.WorkOrderId}-{sourceId}", DateOnly.FromDateTime(occurredAtUtc.UtcDateTime), lines));
    }
}

[IntegrationEventConsumer("Nerv.IIP.Contracts.Mes.WorkOrderCompletedIntegrationEvent", ConsumerName)]
public sealed class WorkOrderCompletedIntegrationEventHandlerForCapitalizeCost(ApplicationDbContext dbContext)
    : IIntegrationEventHandler<WorkOrderCompletedIntegrationEvent>, ICapSubscribe
{
    public const string ConsumerName = "business-erp.work-order-cost-capitalization";
    public Task HandleAsync(WorkOrderCompletedIntegrationEvent integrationEvent, CancellationToken cancellationToken) => HandleValidAsync(integrationEvent, cancellationToken);
    [CapSubscribe(nameof(WorkOrderCompletedIntegrationEvent), Group = ConsumerName)]
    public Task HandleCapAsync(WorkOrderCompletedIntegrationEvent integrationEvent, CancellationToken cancellationToken) => HandleAsync(integrationEvent, cancellationToken);
    private async Task HandleValidAsync(WorkOrderCompletedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        if (!await ErpProcessedIntegrationEventInbox.TryRecordAsync(dbContext, ConsumerName, integrationEvent, cancellationToken)) return;
        var payload = integrationEvent.Payload;
        var cost = await dbContext.WorkOrderCosts.Include(x => x.Details).SingleOrDefaultAsync(x => x.OrganizationId == integrationEvent.OrganizationId && x.EnvironmentId == integrationEvent.EnvironmentId && x.WorkOrderId == payload.WorkOrderId, cancellationToken);
        if (cost is null || cost.TotalAccumulatedCost <= 0m) throw new InvalidOperationException($"Work order '{payload.WorkOrderId}' has no actual cost to capitalize.");
        cost.AssignSku(payload.SkuCode);
        cost.Complete(payload.GoodQuantity, Math.Max(payload.ExpectedCostReportCount, cost.ReceivedReportCount), payload.ExpectedMaterialMovementCount, payload.CompletedAtUtc);
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
