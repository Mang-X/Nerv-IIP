using DotNetCore.CAP;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.WorkOrderCostAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.GLAccountAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.JournalVoucherAggregate;
using Nerv.IIP.Business.Erp.Infrastructure;
using Nerv.IIP.Business.Erp.Web.Application.Commands;
using Nerv.IIP.Business.Erp.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Business.Erp.Web.Application.Validation;
using Nerv.IIP.Contracts.IntegrationEvents;
using Nerv.IIP.Contracts.Inventory;
using Nerv.IIP.Contracts.Mes;
using Nerv.IIP.Messaging.CAP;
using NetCorePal.Extensions.DistributedTransactions;
using NetCorePal.Extensions.Repository;
using NetCorePal.Extensions.Repository.EntityFrameworkCore;

namespace Nerv.IIP.Business.Erp.Web.Application.IntegrationEventHandlers;

[IntegrationEventConsumer("Nerv.IIP.Contracts.Mes.ReworkWorkOrderCreatedIntegrationEvent", ConsumerName)]
public sealed class ReworkWorkOrderCreatedIntegrationEventHandlerForAttributeCost(
    ApplicationDbContext dbContext,
    ITransactionUnitOfWork unitOfWork,
    IWorkOrderCostMutationLock mutationLock,
    IIntegrationEventDeadLetterStore deadLetterStore)
    : IIntegrationEventHandler<ReworkWorkOrderCreatedIntegrationEvent>, ICapSubscribe
{
    public const string ConsumerName = "business-erp.rework-work-order-cost-origin";

    private readonly IntegrationEventConsumerGuard<ReworkWorkOrderCreatedIntegrationEvent> consumerGuard = new(
        new IntegrationEventEnvelopeValidator(),
        deadLetterStore,
        new IntegrationEventConsumerOptions(
            ConsumerName,
            MesIntegrationEventTypes.ReworkWorkOrderCreated,
            MesIntegrationEventVersions.V1));

    public Task HandleAsync(
        ReworkWorkOrderCreatedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
        => consumerGuard.HandleAsync(integrationEvent, HandleValidAsync, cancellationToken);

    private Task HandleValidAsync(
        ReworkWorkOrderCreatedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(
                integrationEvent.SourceService,
                MesIntegrationEventSources.BusinessMes,
                StringComparison.OrdinalIgnoreCase))
        {
            return Task.CompletedTask;
        }

        return CostingIntegrationEventUnitOfWork.ExecuteAsync(
            dbContext,
            unitOfWork,
            async () =>
            {
                await mutationLock.AcquireAsync(
                    integrationEvent.OrganizationId,
                    integrationEvent.EnvironmentId,
                    integrationEvent.Payload.ReworkWorkOrderId,
                    cancellationToken);
                if (!await ErpProcessedIntegrationEventInbox.TryRecordAsync(
                        dbContext,
                        ConsumerName,
                        integrationEvent,
                        cancellationToken))
                {
                    return;
                }

                var cost = await dbContext.WorkOrderCosts
                    .Include(x => x.Details)
                    .SingleOrDefaultAsync(
                        x => x.OrganizationId == integrationEvent.OrganizationId
                            && x.EnvironmentId == integrationEvent.EnvironmentId
                            && x.WorkOrderId == integrationEvent.Payload.ReworkWorkOrderId,
                        cancellationToken);
                if (cost is null)
                {
                    cost = WorkOrderCost.Open(
                        integrationEvent.OrganizationId,
                        integrationEvent.EnvironmentId,
                        integrationEvent.Payload.ReworkWorkOrderId,
                        integrationEvent.Payload.SkuCode);
                    dbContext.WorkOrderCosts.Add(cost);
                }

                cost.AttributeRework(
                    integrationEvent.Payload.SourceNcrId,
                    integrationEvent.Payload.SourceNcrCode,
                    integrationEvent.Payload.SourceWorkOrderId,
                    integrationEvent.Payload.SkuCode);
            },
            cancellationToken);
    }

    [CapSubscribe(nameof(ReworkWorkOrderCreatedIntegrationEvent), Group = ConsumerName)]
    public Task HandleCapAsync(
        ReworkWorkOrderCreatedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken) => HandleAsync(integrationEvent, cancellationToken);
}

[IntegrationEventConsumer("Nerv.IIP.Contracts.Mes.ProductionReportRecordedIntegrationEvent", ConsumerName)]
public sealed class ProductionReportRecordedIntegrationEventHandlerForAccumulateLaborCost(
    ApplicationDbContext dbContext,
    IIntegrationEventDeadLetterStore deadLetterStore,
    ITransactionUnitOfWork unitOfWork,
    IWorkOrderCostMutationLock mutationLock,
    ErpCodingService codingService)
    : IIntegrationEventHandler<ProductionReportRecordedIntegrationEvent>, ICapSubscribe
{
    public const string ConsumerName = "business-erp.production-report-labor-cost";
    public Task HandleAsync(ProductionReportRecordedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        if (!string.Equals(integrationEvent.SourceService, MesIntegrationEventSources.BusinessMes, StringComparison.OrdinalIgnoreCase))
            return Task.CompletedTask;
        return CostingIntegrationEventUnitOfWork.ExecuteAsync(
            dbContext,
            unitOfWork,
            async () =>
            {
                await mutationLock.AcquireAsync(
                    integrationEvent.OrganizationId,
                    integrationEvent.EnvironmentId,
                    integrationEvent.Payload.WorkOrderId,
                    cancellationToken);
                await HandleValidAsync(integrationEvent, cancellationToken);
            },
            cancellationToken);
    }
    [CapSubscribe(nameof(ProductionReportRecordedIntegrationEvent), Group = ConsumerName)]
    public Task HandleCapAsync(ProductionReportRecordedIntegrationEvent integrationEvent, CancellationToken cancellationToken) => HandleAsync(integrationEvent, cancellationToken);

    private async Task HandleValidAsync(ProductionReportRecordedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        var outputQuantity = Math.Abs(integrationEvent.Payload.GoodQuantity + integrationEvent.Payload.ScrapQuantity + integrationEvent.Payload.ReworkQuantity);
        var isCoveredByActualSettlement = await dbContext.OperationLaborCoveredReports.AnyAsync(
            x => x.OrganizationId == integrationEvent.OrganizationId
                && x.EnvironmentId == integrationEvent.EnvironmentId
                && x.ReportNo == integrationEvent.Payload.ReportNo,
            cancellationToken);
        var workOrderUsesActualLabor = await dbContext.OperationLaborSettlements.AnyAsync(
            settlement => settlement.OrganizationId == integrationEvent.OrganizationId
                && settlement.EnvironmentId == integrationEvent.EnvironmentId
                && settlement.WorkOrderId == integrationEvent.Payload.WorkOrderId
                && dbContext.OperationLaborSettlementStates.Any(
                    state => state.OrganizationId == settlement.OrganizationId
                        && state.EnvironmentId == settlement.EnvironmentId
                        && state.OperationTaskId == settlement.OperationTaskId
                        && state.ActiveRevision == settlement.SettlementRevision),
            cancellationToken);
        var hasLaborBasis = !isCoveredByActualSettlement
            && !workOrderUsesActualLabor
            && !string.IsNullOrWhiteSpace(integrationEvent.Payload.WorkCenterId)
            && integrationEvent.Payload.TheoreticalRatePerHour is > 0m
            && outputQuantity > 0m;
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
        var cost = await dbContext.WorkOrderCosts.Include(x => x.Details).SingleOrDefaultAsync(x => x.OrganizationId == integrationEvent.OrganizationId && x.EnvironmentId == integrationEvent.EnvironmentId && x.WorkOrderId == integrationEvent.Payload.WorkOrderId, cancellationToken);
        if (cost is null)
        {
            cost = WorkOrderCost.Open(integrationEvent.OrganizationId, integrationEvent.EnvironmentId, integrationEvent.Payload.WorkOrderId, integrationEvent.Payload.WorkOrderId);
            dbContext.WorkOrderCosts.Add(cost);
        }
        if (hasLaborBasis)
        {
            var selectedRate = rate ?? throw new InvalidOperationException("A priced production report requires an applicable standard labor rate.");
            if (!cost.TryFreezeLaborCurrency(selectedRate.CurrencyCode))
            {
                await deadLetterStore.AddAsync(IntegrationEventDeadLetterMessage.Create(ConsumerName, integrationEvent, "incompatible-work-order-labor-currency", $"Work order '{integrationEvent.Payload.WorkOrderId}' already contains priced labor in a currency incompatible with '{selectedRate.CurrencyCode}'."), cancellationToken);
                return;
            }
        }
        if (!await ErpProcessedIntegrationEventInbox.TryRecordAsync(dbContext, ConsumerName, integrationEvent, cancellationToken)) return;
        dbContext.OperationLaborReportSnapshots.Add(OperationLaborReportSnapshot.Create(
            integrationEvent.OrganizationId,
            integrationEvent.EnvironmentId,
            integrationEvent.Payload.WorkOrderId,
            integrationEvent.Payload.OperationTaskId,
            integrationEvent.Payload.WorkCenterId,
            integrationEvent.Payload.ReportNo,
            integrationEvent.Payload.GoodQuantity,
            integrationEvent.Payload.ScrapQuantity,
            integrationEvent.Payload.ReworkQuantity,
            integrationEvent.Payload.UomCode,
            integrationEvent.Payload.TheoreticalRatePerHour,
            integrationEvent.Payload.ReportedAtUtc,
            integrationEvent.Payload.IsReversal,
            integrationEvent.Payload.ReversedReportNo,
            integrationEvent.EventId));
        var priorTotal = cost.TotalAccumulatedCost;
        if (hasLaborBasis)
        {
            var theoreticalRate = integrationEvent.Payload.TheoreticalRatePerHour
                ?? throw new InvalidOperationException("A priced production report requires a theoretical output rate.");
            var selectedRate = rate
                ?? throw new InvalidOperationException("A priced production report requires an applicable standard labor rate.");
            cost.RecordLabor(integrationEvent.Payload.ReportNo, integrationEvent.Payload.WorkCenterId, outputQuantity / theoreticalRate, selectedRate.HourlyRate, selectedRate.CurrencyCode, integrationEvent.Payload.IsReversal, integrationEvent.Payload.ReportedAtUtc);
        }
        else
            cost.RecordUncostedReport(integrationEvent.Payload.ReportNo, integrationEvent.Payload.IsReversal, integrationEvent.Payload.ReportedAtUtc);
        if (cost.IsFullyCapitalized && integrationEvent.Payload.IsReversal
            && !await CostVariancePosting.PostLateAdjustmentAsync(dbContext, codingService, cost, cost.TotalAccumulatedCost - priorTotal, integrationEvent.Payload.ReportNo, integrationEvent.Payload.ReportedAtUtc, cancellationToken))
        {
            await SkipOnVoucherNumberFailureAsync(integrationEvent, cancellationToken);
            return;
        }
        var pending = await dbContext.PendingMaterialCosts.Where(x => x.OrganizationId == integrationEvent.OrganizationId && x.EnvironmentId == integrationEvent.EnvironmentId && x.ReportNo == integrationEvent.Payload.ReportNo).ToListAsync(cancellationToken);
        foreach (var item in pending)
        {
            var priorPendingTotal = cost.TotalAccumulatedCost;
            cost.RecordMaterial(item.MovementId, item.ReportNo, item.SkuCode, item.SignedQuantity, item.UnitCost, item.PostedAtUtc);
            if (cost.CapitalizationPublished && item.SignedQuantity < 0m
                && !await CostVariancePosting.PostLateAdjustmentAsync(dbContext, codingService, cost, cost.TotalAccumulatedCost - priorPendingTotal, item.MovementId, item.PostedAtUtc, cancellationToken))
            {
                await SkipOnVoucherNumberFailureAsync(integrationEvent, cancellationToken);
                return;
            }
            dbContext.PendingMaterialCosts.Remove(item);
        }
    }

    /// <summary>
    /// #3278 / S7：迟到调整凭证号没分配下来时的 gate-and-skip。
    /// 不能 throw：CAP 消费者里的业务异常会逃逸成 poison message（#877 仍 OPEN）。
    /// 先 Clear 再写死信：本次所有未提交变更（含 inbox 行、成本累计）一并丢弃，
    /// 而死信库自己 SaveChanges，所以只有死信行落库。
    /// </summary>
    private async Task SkipOnVoucherNumberFailureAsync(
        ProductionReportRecordedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        dbContext.ChangeTracker.Clear();
        await deadLetterStore.AddAsync(
            IntegrationEventDeadLetterMessage.Create(
                ConsumerName,
                integrationEvent,
                ConsumerJournalVoucherNumber.AllocationFailureCode,
                "Journal voucher number could not be allocated for the late cost adjustment."),
            cancellationToken);
    }
}

[IntegrationEventConsumer("Nerv.IIP.Contracts.Inventory.StockMovementPostedIntegrationEvent", ConsumerName)]
public sealed class StockMovementPostedIntegrationEventHandlerForAccumulateMaterialCost(
    ApplicationDbContext dbContext,
    IIntegrationEventDeadLetterStore deadLetterStore,
    ITransactionUnitOfWork unitOfWork,
    ErpCodingService codingService)
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
            var isFinalReceipt = completedCost.IsFullyCapitalized;
            var variance = isFinalReceipt ? completedCost.TotalAccumulatedCost - completedCost.CapitalizedCost : 0m;
            var wipClearance = isFinalReceipt ? completedCost.TotalAccumulatedCost - priorWipCleared : movementAmount;
            var lines = new List<JournalVoucherLineDraft>
            {
                new("1406-FINISHED-GOODS", movementAmount, 0m, $"Finished goods {completedCost.WorkOrderId}"),
                wipClearance >= 0m
                    ? new("1405-WIP", 0m, wipClearance, $"Clear WIP {completedCost.WorkOrderId}")
                    : new("1405-WIP", -wipClearance, 0m, $"Restore WIP {completedCost.WorkOrderId}"),
            };
            if (variance > 0m) lines.Add(new("5101-PRODUCTION-VARIANCE", variance, 0m, $"Uncapitalized variance {completedCost.WorkOrderId}"));
            else if (variance < 0m) lines.Add(new("5101-PRODUCTION-VARIANCE", 0m, -variance, $"Over-capitalized variance {completedCost.WorkOrderId}"));
            // #3278 / S7：凭证号改分配器短号，分配键取 (WOC, 库存移动号)。
            // 先分配再改聚合状态：拿不到号时 RecordWipClearance 还没发生，
            // Clear 之后本次零落库，而不是「清了 WIP 却没凭证」。
            var capitalizationVoucher = await ConsumerJournalVoucherNumber.TryAllocateAsync(
                codingService,
                integrationEvent.OrganizationId,
                integrationEvent.EnvironmentId,
                JournalVoucherSourceType.WorkOrderCapitalization,
                payload.InventoryMovementId,
                cancellationToken);
            if (capitalizationVoucher.Code is null)
            {
                dbContext.ChangeTracker.Clear();
                await deadLetterStore.AddAsync(IntegrationEventDeadLetterMessage.Create(ConsumerName, integrationEvent, ConsumerJournalVoucherNumber.AllocationFailureCode, capitalizationVoucher.FailureMessage), cancellationToken);
                return;
            }
            completedCost.RecordWipClearance(wipClearance);
            dbContext.JournalVouchers.Add(JournalVoucher.Post(integrationEvent.OrganizationId, integrationEvent.EnvironmentId, capitalizationVoucher.Code, DateOnly.FromDateTime(payload.PostedAtUtc.UtcDateTime), lines, JournalVoucherSourceType.WorkOrderCapitalization, payload.InventoryMovementId));
            await CostingIntegrationEventUnitOfWork.SaveEntitiesAsync(dbContext, unitOfWork, cancellationToken);
            return;
        }
        var cost = await dbContext.WorkOrderCosts.Include(x => x.Details).SingleOrDefaultAsync(x => x.OrganizationId == integrationEvent.OrganizationId && x.EnvironmentId == integrationEvent.EnvironmentId && x.Details.Any(d => d.SourceDocumentId == payload.SourceDocumentId), cancellationToken);
        var signedCostQuantity = payload.Quantity < 0m ? Math.Abs(payload.Quantity) : -Math.Abs(payload.Quantity);
        if (cost is null)
        {
            dbContext.PendingMaterialCosts.Add(PendingMaterialCost.Create(integrationEvent.OrganizationId, integrationEvent.EnvironmentId, payload.InventoryMovementId, payload.SourceDocumentId, payload.SkuCode, signedCostQuantity, unitCost.Value, payload.PostedAtUtc));
            await CostingIntegrationEventUnitOfWork.SaveEntitiesAsync(dbContext, unitOfWork, cancellationToken);
            return;
        }
        var priorMaterialTotal = cost.TotalAccumulatedCost;
        cost.RecordMaterial(payload.InventoryMovementId, payload.SourceDocumentId, payload.SkuCode, signedCostQuantity, unitCost.Value, payload.PostedAtUtc);
        if (cost.CapitalizationPublished && signedCostQuantity < 0m
            && !await CostVariancePosting.PostLateAdjustmentAsync(dbContext, codingService, cost, cost.TotalAccumulatedCost - priorMaterialTotal, payload.InventoryMovementId, payload.PostedAtUtc, cancellationToken))
        {
            dbContext.ChangeTracker.Clear();
            await deadLetterStore.AddAsync(IntegrationEventDeadLetterMessage.Create(ConsumerName, integrationEvent, ConsumerJournalVoucherNumber.AllocationFailureCode, "Journal voucher number could not be allocated for the late cost adjustment."), cancellationToken);
            return;
        }
        await CostingIntegrationEventUnitOfWork.SaveEntitiesAsync(dbContext, unitOfWork, cancellationToken);
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
    /// <summary>
    /// 记一张工单成本迟到调整凭证（<c>WOCADJ</c> 族）。
    /// </summary>
    /// <returns>
    /// <see langword="true"/> = 已完成（记了凭证，或 <paramref name="costDelta"/> 为 0 的空转）；
    /// <see langword="false"/> = **凭证号没分配到**，本方法没改任何状态，调用方必须 gate-and-skip。
    /// </returns>
    /// <remarks>
    /// #3278 / S7：凭证号从 <c>ErpVoucherNoPolicy.Compose(WorkOrderCostAdjustment, workOrderId, sourceId)</c>
    /// 改成分配器短号。本方法是静态辅助、被 5 个生产调用点共用，手里没有事件信封，
    /// 所以分配器的幂等键取 <c>(WOCADJ, sourceId)</c>——正好就是 S5 给本族定的唯一键，
    /// 也正好就在参数里，不需要改 5 个调用点的取值。
    /// ⚠️ 这条承接的强度不超过 S5：<c>JournalVoucherSourceType.WorkOrderCostAdjustment</c> 的注释已登记
    /// 「<c>(WOCADJ, sourceId)</c> 比旧凭证号少了 <c>workOrderId</c> 一段」是「今天成立」而非不变量；
    /// 本票不改变那个论证，只是让凭证号与它同粒度。
    ///
    /// **分配放在最前面**（两条早退之后、任何写入之前）：拿不到号时本方法零副作用，
    /// 不会出现「补建了科目 / 清了 WIP 却没凭证」的半截状态。
    /// </remarks>
    public static async Task<bool> PostLateAdjustmentAsync(ApplicationDbContext dbContext, ErpCodingService codingService, WorkOrderCost cost, decimal costDelta, string sourceId, DateTimeOffset occurredAtUtc, CancellationToken cancellationToken)
    {
        if (costDelta == 0m) return true;
        var voucherAllocation = await ConsumerJournalVoucherNumber.TryAllocateAsync(
            codingService,
            cost.OrganizationId,
            cost.EnvironmentId,
            JournalVoucherSourceType.WorkOrderCostAdjustment,
            sourceId,
            cancellationToken);
        if (voucherAllocation.Code is null) return false;
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
        if (cost.IsFullyCapitalized)
            cost.RecordWipClearance(costDelta);
        dbContext.JournalVouchers.Add(JournalVoucher.Post(cost.OrganizationId, cost.EnvironmentId, voucherAllocation.Code, DateOnly.FromDateTime(occurredAtUtc.UtcDateTime), lines, JournalVoucherSourceType.WorkOrderCostAdjustment, sourceId));
        return true;
    }
}

[IntegrationEventConsumer("Nerv.IIP.Contracts.Mes.WorkOrderCompletedIntegrationEvent", ConsumerName)]
public sealed class WorkOrderCompletedIntegrationEventHandlerForCapitalizeCost(
    ApplicationDbContext dbContext,
    IIntegrationEventDeadLetterStore deadLetterStore,
    ITransactionUnitOfWork unitOfWork)
    : IIntegrationEventHandler<WorkOrderCompletedIntegrationEvent>, ICapSubscribe
{
    public const string ConsumerName = "business-erp.work-order-cost-capitalization";
    public Task HandleAsync(WorkOrderCompletedIntegrationEvent integrationEvent, CancellationToken cancellationToken) => HandleValidAsync(integrationEvent, cancellationToken);
    [CapSubscribe(nameof(WorkOrderCompletedIntegrationEvent), Group = ConsumerName)]
    public Task HandleCapAsync(WorkOrderCompletedIntegrationEvent integrationEvent, CancellationToken cancellationToken) => HandleAsync(integrationEvent, cancellationToken);
    private async Task HandleValidAsync(WorkOrderCompletedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        var payload = integrationEvent.Payload;
        var cost = await dbContext.WorkOrderCosts.Include(x => x.Details).SingleOrDefaultAsync(x => x.OrganizationId == integrationEvent.OrganizationId && x.EnvironmentId == integrationEvent.EnvironmentId && x.WorkOrderId == payload.WorkOrderId, cancellationToken);
        var expectedReportCount = Math.Max(payload.ExpectedCostReportCount, cost?.ReceivedReportCount ?? 0);
        if (expectedReportCount <= 0)
        {
            await deadLetterStore.AddAsync(IntegrationEventDeadLetterMessage.Create(
                ConsumerName,
                integrationEvent,
                "invalid-expected-report-count",
                $"Work order '{payload.WorkOrderId}' completed without an expected or received cost report."), cancellationToken);
            return;
        }
        if (!await ErpProcessedIntegrationEventInbox.TryRecordAsync(dbContext, ConsumerName, integrationEvent, cancellationToken)) return;
        if (cost is null)
        {
            cost = WorkOrderCost.Open(integrationEvent.OrganizationId, integrationEvent.EnvironmentId, payload.WorkOrderId, payload.SkuCode);
            dbContext.WorkOrderCosts.Add(cost);
        }
        cost.AssignSku(payload.SkuCode);
        cost.Complete(payload.GoodQuantity, expectedReportCount, payload.ExpectedMaterialMovementCount, payload.CompletedAtUtc);
        await CostingIntegrationEventUnitOfWork.SaveEntitiesAsync(dbContext, unitOfWork, cancellationToken);
    }
}

internal static class CostingIntegrationEventUnitOfWork
{
    public static async Task ExecuteAsync(
        ApplicationDbContext dbContext,
        ITransactionUnitOfWork unitOfWork,
        Func<Task> action,
        CancellationToken cancellationToken)
    {
        if (unitOfWork.CurrentTransaction is not null)
        {
            await action();
            await ((IUnitOfWork)unitOfWork).SaveEntitiesAsync(cancellationToken);
            return;
        }

        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        unitOfWork.CurrentTransaction = transaction;
        try
        {
            await action();
            await ((IUnitOfWork)unitOfWork).SaveEntitiesAsync(cancellationToken);
            await unitOfWork.CommitAsync(cancellationToken);
        }
        catch
        {
            await unitOfWork.RollbackAsync(cancellationToken);
            throw;
        }
        finally
        {
            unitOfWork.CurrentTransaction = null;
        }
    }

    public static async Task SaveEntitiesAsync(
        ApplicationDbContext dbContext,
        ITransactionUnitOfWork unitOfWork,
        CancellationToken cancellationToken)
    {
        if (unitOfWork.CurrentTransaction is not null)
        {
            await ((IUnitOfWork)unitOfWork).SaveEntitiesAsync(cancellationToken);
            return;
        }

        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        unitOfWork.CurrentTransaction = transaction;
        try
        {
            await ((IUnitOfWork)unitOfWork).SaveEntitiesAsync(cancellationToken);
            await unitOfWork.CommitAsync(cancellationToken);
        }
        catch
        {
            await unitOfWork.RollbackAsync(cancellationToken);
            throw;
        }
        finally
        {
            unitOfWork.CurrentTransaction = null;
        }
    }
}
