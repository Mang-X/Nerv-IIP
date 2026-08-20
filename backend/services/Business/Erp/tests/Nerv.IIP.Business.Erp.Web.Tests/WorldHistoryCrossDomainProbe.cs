using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Erp.Infrastructure;
using Nerv.IIP.Business.Erp.Web.Application.Seed;
using Nerv.IIP.Testing;

namespace Nerv.IIP.Business.Erp.Web.Tests;

/// <summary>
/// ERP 侧的跨域抽样探针（#1826）。
///
/// <para>
/// 对 <see cref="CrossServiceSampleProbe.SampleIndexes"/> 取到的 20 个订单序号，
/// 逐个**真查 ERP 库**确认自己那几张单存不存在，并对不归自己管的单据
/// （工单 / 完工入库）按同一份 <see cref="WorldHistorySpec"/> 出一份**见证**声明。
/// 见证行没有 <c>exists</c>——它只说「按算术这张单应该存在、数量应该是多少」，
/// 由脚本拿去和 MES / 库存 / 仓储上报的实查结果对账。
/// </para>
///
/// <para>
/// 查库一律**用证据行里打印的那个单据号本身作查询键**，并回读单据上的反向引用
/// （发货单的销售订单号、应收的来源单据号）确认它确实挂在本抽样序号的链上。
/// 若改用外键去查、却打印按号段推出来的号，两者从不比对，号段一旦漂移证据表照样
/// 写「DO-2026-00001 存在」——那正是本票要根治的那类「看起来可追、其实没验」。
/// </para>
/// </summary>
internal static class WorldHistoryCrossDomainProbe
{
    public const string Prefix = "erp-world-history";

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
        var lines = new List<string>(1 + (indexes.Count * 7))
        {
            CrossServiceSampleProbe.FormatBasis(Prefix, asOfDate, scale, plans.Count, indexes),
        };

        var sampledPlans = indexes.Select(index => plans[index - 1]).ToArray();
        var salesOrderNos = sampledPlans.Select(plan => plan.SalesOrderNo).ToArray();
        var deliveryOrderNos = sampledPlans.Select(plan => WorldHistorySpec.DeliveryOrderNo(plan.Index)).ToArray();
        var receivableNos = sampledPlans.Select(plan => WorldHistorySpec.ReceivableNo(plan.Index)).ToArray();

        var orders = (await dbContext.SalesOrders
                .AsNoTracking()
                .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId &&
                    salesOrderNos.Contains(x.SalesOrderNo))
                .Select(x => new
                {
                    x.SalesOrderNo,
                    x.TotalAmount,
                    x.CreatedAtUtc,
                    OrderedQuantity = x.Lines.Sum(line => line.OrderedQuantity),
                })
                .ToArrayAsync(cancellationToken))
            .ToDictionary(x => x.SalesOrderNo, StringComparer.Ordinal);

        var deliveries = (await dbContext.DeliveryOrders
                .AsNoTracking()
                .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId &&
                    deliveryOrderNos.Contains(x.DeliveryOrderNo))
                .Select(x => new
                {
                    x.DeliveryOrderNo,
                    x.SalesOrderNo,
                    x.ReleasedAtUtc,
                    x.ShippedAtUtc,
                    ShippedQuantity = x.Lines.Sum(line => line.ShippedQuantity),
                })
                .ToArrayAsync(cancellationToken))
            .ToDictionary(x => x.DeliveryOrderNo, StringComparer.Ordinal);

        var receivables = (await dbContext.AccountReceivables
                .AsNoTracking()
                .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId &&
                    receivableNos.Contains(x.ReceivableNo))
                .Select(x => new { x.ReceivableNo, x.SourceDocumentNo, x.Amount, x.CreatedAtUtc })
                .ToArrayAsync(cancellationToken))
            .ToDictionary(x => x.ReceivableNo, StringComparer.Ordinal);

        foreach (var plan in sampledPlans)
        {
            var deliveryOrderNo = WorldHistorySpec.DeliveryOrderNo(plan.Index);
            var receivableNo = WorldHistorySpec.ReceivableNo(plan.Index);
            orders.TryGetValue(plan.SalesOrderNo, out var order);
            deliveries.TryGetValue(deliveryOrderNo, out var delivery);
            receivables.TryGetValue(receivableNo, out var receivable);

            // 反向引用不对就不算「这张单存在」：单据号命中但挂在别的订单上，
            // 对跨域抽样来说和不存在是一回事。
            if (delivery is not null && !string.Equals(delivery.SalesOrderNo, plan.SalesOrderNo, StringComparison.Ordinal))
            {
                delivery = null;
            }

            if (receivable is not null && !string.Equals(receivable.SourceDocumentNo, plan.SalesOrderNo, StringComparison.Ordinal))
            {
                receivable = null;
            }

            lines.Add(CrossServiceSampleProbe.FormatRow(Prefix, new CrossServiceSampleProbeRow(
                Index: plan.Index,
                Link: CrossServiceSampleProbe.Links.SalesOrder,
                Kind: "erp-sales-order",
                DocumentNo: plan.SalesOrderNo,
                // 废弃单也是一张真实存在的订单（只是被取消），因此销售订单恒定应存在。
                Expected: true,
                Exists: order is not null,
                Quantity: order?.OrderedQuantity,
                Amount: order?.TotalAmount,
                TimestampUtc: order is null ? null : new DateTimeOffset(order.CreatedAtUtc, TimeSpan.Zero))));

            // 见证：工单归 MES 管，ERP 只按同一张订单计划表声明它该不该有。
            lines.Add(CrossServiceSampleProbe.FormatRow(Prefix, new CrossServiceSampleProbeRow(
                Index: plan.Index,
                Link: CrossServiceSampleProbe.Links.WorkOrder,
                Kind: "erp-work-order-witness",
                DocumentNo: plan.WorkOrderNo,
                Expected: plan.HasWorkOrder,
                Exists: null)));

            // 见证：完工入库归 MES / 库存 / 仓储三家写，好品产出恒等于订单数量
            // （WorldHistoryMesSpec.BuildWorkOrderPlan 的不变量），所以数量可以由 ERP 见证。
            lines.Add(CrossServiceSampleProbe.FormatRow(Prefix, new CrossServiceSampleProbeRow(
                Index: plan.Index,
                Link: CrossServiceSampleProbe.Links.FinishedGoodsReceipt,
                Kind: "erp-finished-goods-receipt-witness",
                // 见证行用工单号定位：完工入库的号段（FGR-/INV-/IB-）归二期各域自己声明，
                // ERP 不该把别人的号段抄一份过来。脚本不跨服务比对单据号，只比对
                // (index, link) 下的 expected / 数量 / 时间戳。
                DocumentNo: plan.WorkOrderNo,
                Expected: plan.IsProductionClosed,
                Exists: null,
                Quantity: plan.Quantity)));

            lines.Add(CrossServiceSampleProbe.FormatRow(Prefix, new CrossServiceSampleProbeRow(
                Index: plan.Index,
                Link: CrossServiceSampleProbe.Links.DeliveryOrder,
                Kind: "erp-delivery-order",
                DocumentNo: deliveryOrderNo,
                Expected: plan.HasDelivery || plan.HasPendingShipment,
                Exists: delivery is not null,
                Quantity: delivery?.ShippedQuantity,
                TimestampUtc: delivery is null ? null : new DateTimeOffset(delivery.ReleasedAtUtc, TimeSpan.Zero))));

            // 发运是与「发货单开出」不同的一件事：#1374 的待发货档有单无发运。
            lines.Add(CrossServiceSampleProbe.FormatRow(Prefix, new CrossServiceSampleProbeRow(
                Index: plan.Index,
                Link: CrossServiceSampleProbe.Links.Shipment,
                Kind: "erp-shipment",
                DocumentNo: deliveryOrderNo,
                Expected: plan.HasDelivery,
                Exists: delivery?.ShippedAtUtc is not null,
                Quantity: delivery?.ShippedAtUtc is null ? null : delivery.ShippedQuantity,
                TimestampUtc: delivery?.ShippedAtUtc is null
                    ? null
                    : new DateTimeOffset(delivery.ShippedAtUtc.Value, TimeSpan.Zero))));

            lines.Add(CrossServiceSampleProbe.FormatRow(Prefix, new CrossServiceSampleProbeRow(
                Index: plan.Index,
                Link: CrossServiceSampleProbe.Links.Receivable,
                Kind: "erp-receivable",
                DocumentNo: receivableNo,
                Expected: plan.HasDelivery,
                Exists: receivable is not null,
                Amount: receivable?.Amount,
                TimestampUtc: receivable is null ? null : new DateTimeOffset(receivable.CreatedAtUtc, TimeSpan.Zero))));

            // 见证：出货检验归质量管，判据是「完工装箱即成立」，与发运与否无关（#1374）。
            lines.Add(CrossServiceSampleProbe.FormatRow(Prefix, new CrossServiceSampleProbeRow(
                Index: plan.Index,
                Link: CrossServiceSampleProbe.Links.OutboundInspection,
                Kind: "erp-outbound-inspection-witness",
                DocumentNo: deliveryOrderNo,
                Expected: plan.IsProductionClosed,
                Exists: null,
                Quantity: plan.Quantity)));
        }

        return lines;
    }
}
