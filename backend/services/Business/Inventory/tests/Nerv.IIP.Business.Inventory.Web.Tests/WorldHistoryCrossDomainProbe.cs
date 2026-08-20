using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Inventory.Infrastructure;
using Nerv.IIP.Business.Inventory.Web.Application.Seed;
using Nerv.IIP.Testing;

namespace Nerv.IIP.Business.Inventory.Web.Tests;

/// <summary>
/// 库存域侧的跨域抽样探针（#1826）。见 <c>scripts/verify-world-history.ps1</c> 的对账口径。
/// </summary>
internal static class WorldHistoryCrossDomainProbe
{
    public const string Prefix = "inventory-world-history";

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
        var lines = new List<string>(1 + (indexes.Count * 4))
        {
            CrossServiceSampleProbe.FormatBasis(Prefix, asOfDate, scale, plans.Count, indexes),
        };

        var factByWorkOrderNo = WorldHistoryPhase2Spec.BuildWorkOrderFacts(asOfDate, scale)
            .Where(fact => !fact.IsRework)
            .ToDictionary(fact => fact.Plan.WorkOrderNo, StringComparer.Ordinal);

        var sampledPlans = indexes.Select(index => plans[index - 1]).ToArray();
        var movementKeys = new List<string>(sampledPlans.Length * 2);
        foreach (var plan in sampledPlans)
        {
            movementKeys.Add(FinishedGoodsMovementKey(plan.WorkOrderNo));
            movementKeys.Add(DeliveryMovementKey(plan.Index));
        }

        var movements = (await dbContext.StockMovements
                .AsNoTracking()
                .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId &&
                    movementKeys.Contains(x.IdempotencyKey))
                .Select(x => new { x.IdempotencyKey, x.Quantity, x.PostedAtUtc })
                .ToArrayAsync(cancellationToken))
            .ToDictionary(x => x.IdempotencyKey, StringComparer.Ordinal);

        foreach (var plan in sampledPlans)
        {
            movements.TryGetValue(FinishedGoodsMovementKey(plan.WorkOrderNo), out var finishedGoods);
            movements.TryGetValue(DeliveryMovementKey(plan.Index), out var delivery);

            lines.Add(CrossServiceSampleProbe.FormatRow(Prefix, new CrossServiceSampleProbeRow(
                Index: plan.Index,
                Link: CrossServiceSampleProbe.Links.FinishedGoodsReceipt,
                Kind: "inventory-finished-goods-movement",
                DocumentNo: FinishedGoodsMovementKey(plan.WorkOrderNo),
                Expected: factByWorkOrderNo.TryGetValue(plan.WorkOrderNo, out var fact) && fact.HasFinishedGoodsReceipt,
                Exists: finishedGoods is not null,
                Quantity: finishedGoods?.Quantity,
                TimestampUtc: finishedGoods?.PostedAtUtc)));

            lines.Add(CrossServiceSampleProbe.FormatRow(Prefix, new CrossServiceSampleProbeRow(
                Index: plan.Index,
                Link: CrossServiceSampleProbe.Links.Shipment,
                Kind: "inventory-delivery-movement",
                DocumentNo: DeliveryMovementKey(plan.Index),
                Expected: plan.HasDelivery,
                Exists: delivery is not null,
                // 出库流水按负数记账；跨域对账比的是「发了多少」，所以取绝对值。
                Quantity: delivery is null ? null : Math.Abs(delivery.Quantity),
                TimestampUtc: delivery?.PostedAtUtc)));

            // 见证：订单与工单归 ERP / MES 管，库存域按同一份共享形状声明。
            lines.Add(CrossServiceSampleProbe.FormatRow(Prefix, new CrossServiceSampleProbeRow(
                Index: plan.Index,
                Link: CrossServiceSampleProbe.Links.SalesOrder,
                Kind: "inventory-sales-order-witness",
                DocumentNo: plan.SalesOrderNo,
                Expected: true,
                Exists: null,
                Quantity: plan.Quantity)));

            lines.Add(CrossServiceSampleProbe.FormatRow(Prefix, new CrossServiceSampleProbeRow(
                Index: plan.Index,
                Link: CrossServiceSampleProbe.Links.WorkOrder,
                Kind: "inventory-work-order-witness",
                DocumentNo: plan.WorkOrderNo,
                Expected: plan.HasWorkOrder,
                Exists: null)));
        }

        return lines;
    }

    private static string FinishedGoodsMovementKey(string workOrderNo) => $"INV-{workOrderNo}";

    private static string DeliveryMovementKey(int index) =>
        WorldHistoryPhase2Spec.MovementKey(
            WorldHistorySpec.DeliveryOrderNo(index),
            WorldHistoryInventorySpec.DeliveryOutPurpose);
}
