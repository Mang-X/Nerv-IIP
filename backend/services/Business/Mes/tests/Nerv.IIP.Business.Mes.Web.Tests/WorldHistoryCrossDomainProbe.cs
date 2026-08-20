using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Business.Mes.Web.Application.Seed;
using Nerv.IIP.Testing;

namespace Nerv.IIP.Business.Mes.Web.Tests;

/// <summary>
/// MES 侧的跨域抽样探针（#1826）。见 <c>scripts/verify-world-history.ps1</c> 的对账口径。
///
/// <para>
/// 查库一律**用证据行里打印的那个单据号本身作查询键**，并回读单据上的工单反向引用，
/// 确认它确实挂在本抽样序号的链上——按外键查、却打印按号段推出来的号，两者从不比对，
/// 号段漂移时证据表照样写「存在」。
/// </para>
/// </summary>
internal static class WorldHistoryCrossDomainProbe
{
    public const string Prefix = "mes-world-history";

    public static async Task<IReadOnlyList<string>> BuildAsync(
        ApplicationDbContext dbContext,
        string organizationId,
        string environmentId,
        DateOnly asOfDate,
        double scale,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dbContext);

        var plans = WorldHistorySpec.BuildOrderPlans(asOfDate, scale);
        var indexes = CrossServiceSampleProbe.SampleIndexes(plans.Count);
        var lines = new List<string>(1 + (indexes.Count * 6))
        {
            CrossServiceSampleProbe.FormatBasis(Prefix, asOfDate, scale, plans.Count, indexes),
        };

        var sampledPlans = indexes.Select(index => plans[index - 1]).ToArray();
        var workOrderNos = sampledPlans.Select(plan => plan.WorkOrderNo).ToArray();
        var finalOperationTaskIds = sampledPlans
            .Select(plan => WorldHistoryMesSpec.OperationTaskId(plan.WorkOrderNo, WorldHistoryMesSpec.QualityInspectionSequence))
            .ToArray();
        var receiptNos = sampledPlans.Select(plan => WorldHistoryMesSpec.FinishedGoodsReceiptNo(plan.WorkOrderNo)).ToArray();

        var workOrders = (await dbContext.WorkOrders
                .AsNoTracking()
                .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId &&
                    workOrderNos.Contains(x.WorkOrderIdValue))
                .Select(x => new { x.WorkOrderIdValue, x.Quantity, x.CreatedAtUtc })
                .ToArrayAsync(cancellationToken))
            .ToDictionary(x => x.WorkOrderIdValue, StringComparer.Ordinal);

        var inspectionTasks = (await dbContext.OperationTasks
                .AsNoTracking()
                .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId &&
                    finalOperationTaskIds.Contains(x.OperationTaskIdValue) &&
                    x.OperationSequence == WorldHistoryMesSpec.QualityInspectionSequence)
                .Select(x => new { x.WorkOrderId, x.OperationTaskIdValue, x.PlannedQuantity, x.CreatedAtUtc })
                .ToArrayAsync(cancellationToken))
            .ToDictionary(x => x.OperationTaskIdValue, StringComparer.Ordinal);

        var receipts = (await dbContext.FinishedGoodsReceiptRequests
                .AsNoTracking()
                .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId &&
                    receiptNos.Contains(x.RequestNo))
                .Select(x => new { x.WorkOrderId, x.RequestNo, x.Quantity, x.PostedAtUtc })
                .ToArrayAsync(cancellationToken))
            .ToDictionary(x => x.RequestNo, StringComparer.Ordinal);

        foreach (var plan in sampledPlans)
        {
            // 与 WorldHistorySeedService.ResolveExecution 同一张映射表：
            // 已完工（含 #1374 待发货）→ 有完工入库；在制 → 有报工无入库；其余只有工序队列。
            var workOrderPlan = WorldHistoryMesSpec.BuildWorkOrderPlan(plan.WorkOrderNo, plan.SkuCode, plan.Quantity);
            var finalOperationTaskId = WorldHistoryMesSpec.OperationTaskId(
                plan.WorkOrderNo,
                WorldHistoryMesSpec.QualityInspectionSequence);
            var receiptNo = WorldHistoryMesSpec.FinishedGoodsReceiptNo(plan.WorkOrderNo);
            workOrders.TryGetValue(plan.WorkOrderNo, out var workOrder);
            inspectionTasks.TryGetValue(finalOperationTaskId, out var inspectionTask);
            receipts.TryGetValue(receiptNo, out var receipt);

            // 单据号命中但挂在别的工单上，对跨域抽样来说和不存在是一回事。
            if (inspectionTask is not null && !string.Equals(inspectionTask.WorkOrderId, plan.WorkOrderNo, StringComparison.Ordinal))
            {
                inspectionTask = null;
            }

            if (receipt is not null && !string.Equals(receipt.WorkOrderId, plan.WorkOrderNo, StringComparison.Ordinal))
            {
                receipt = null;
            }

            // 见证：销售订单归 ERP 管，MES 按同一张订单计划表声明它的数量与金额。
            lines.Add(CrossServiceSampleProbe.FormatRow(Prefix, new CrossServiceSampleProbeRow(
                Index: plan.Index,
                Link: CrossServiceSampleProbe.Links.SalesOrder,
                Kind: "mes-sales-order-witness",
                DocumentNo: plan.SalesOrderNo,
                Expected: true,
                Exists: null,
                Quantity: plan.Quantity,
                Amount: plan.TotalAmount)));

            lines.Add(CrossServiceSampleProbe.FormatRow(Prefix, new CrossServiceSampleProbeRow(
                Index: plan.Index,
                Link: CrossServiceSampleProbe.Links.WorkOrder,
                Kind: "mes-work-order",
                DocumentNo: plan.WorkOrderNo,
                Expected: plan.HasWorkOrder,
                Exists: workOrder is not null,
                // 工单数量是**投料量**（好品 + 报废放大），与订单数量本就不等，
                // 因此这一列只与同样按投料量记的质量检验任务对账。
                Quantity: workOrder?.Quantity,
                TimestampUtc: workOrder?.CreatedAtUtc)));

            lines.Add(CrossServiceSampleProbe.FormatRow(Prefix, new CrossServiceSampleProbeRow(
                Index: plan.Index,
                Link: CrossServiceSampleProbe.Links.OperationInspection,
                Kind: "mes-final-operation-task",
                DocumentNo: finalOperationTaskId,
                Expected: plan.HasWorkOrder && workOrderPlan.RequiresQualityInspection,
                Exists: inspectionTask is not null,
                Quantity: inspectionTask?.PlannedQuantity,
                TimestampUtc: inspectionTask?.CreatedAtUtc)));

            lines.Add(CrossServiceSampleProbe.FormatRow(Prefix, new CrossServiceSampleProbeRow(
                Index: plan.Index,
                Link: CrossServiceSampleProbe.Links.FinishedGoodsReceipt,
                Kind: "mes-finished-goods-receipt",
                DocumentNo: receiptNo,
                Expected: plan.IsProductionClosed,
                Exists: receipt is not null,
                Quantity: receipt?.Quantity,
                TimestampUtc: receipt?.PostedAtUtc)));

            // 见证：发货侧归 ERP / 库存 / 仓储管，MES 只声明「这单该不该发运、发多少」。
            lines.Add(CrossServiceSampleProbe.FormatRow(Prefix, new CrossServiceSampleProbeRow(
                Index: plan.Index,
                Link: CrossServiceSampleProbe.Links.DeliveryOrder,
                Kind: "mes-delivery-order-witness",
                DocumentNo: WorldHistorySpec.DeliveryOrderNo(plan.Index),
                Expected: plan.HasDelivery || plan.HasPendingShipment,
                Exists: null)));

            lines.Add(CrossServiceSampleProbe.FormatRow(Prefix, new CrossServiceSampleProbeRow(
                Index: plan.Index,
                Link: CrossServiceSampleProbe.Links.Shipment,
                Kind: "mes-shipment-witness",
                DocumentNo: WorldHistorySpec.DeliveryOrderNo(plan.Index),
                Expected: plan.HasDelivery,
                Exists: null,
                Quantity: plan.Quantity)));
        }

        return lines;
    }
}
