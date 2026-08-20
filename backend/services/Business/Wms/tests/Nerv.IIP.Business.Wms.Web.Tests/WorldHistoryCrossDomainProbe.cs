using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Wms.Infrastructure;
using Nerv.IIP.Business.Wms.Web.Application.Seed;
using Nerv.IIP.Testing;

namespace Nerv.IIP.Business.Wms.Web.Tests;

/// <summary>
/// 仓储域侧的跨域抽样探针（#1826）。见 <c>scripts/verify-world-history.ps1</c> 的对账口径。
/// </summary>
internal static class WorldHistoryCrossDomainProbe
{
    public const string Prefix = "wms-world-history";

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
        var inboundOrderNos = sampledPlans.Select(plan => InboundOrderNo(plan.WorkOrderNo)).ToArray();
        var outboundOrderNos = sampledPlans.Select(plan => OutboundOrderNo(plan.Index)).ToArray();

        var inbounds = (await dbContext.InboundOrders
                .AsNoTracking()
                .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId &&
                    inboundOrderNos.Contains(x.InboundOrderNo))
                .Select(x => new
                {
                    x.InboundOrderNo,
                    x.SourceDocumentId,
                    x.CreatedAtUtc,
                    ReceivedQuantity = x.Lines.Sum(line => line.ReceivedQuantity),
                })
                .ToArrayAsync(cancellationToken))
            .ToDictionary(x => x.InboundOrderNo, StringComparer.Ordinal);

        var outbounds = (await dbContext.OutboundOrders
                .AsNoTracking()
                .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId &&
                    outboundOrderNos.Contains(x.OutboundOrderNo))
                .Select(x => new
                {
                    x.OutboundOrderNo,
                    x.SourceDocumentId,
                    x.CreatedAtUtc,
                    IssuedQuantity = x.Lines.Sum(line => line.IssuedQuantity),
                })
                .ToArrayAsync(cancellationToken))
            .ToDictionary(x => x.OutboundOrderNo, StringComparer.Ordinal);

        foreach (var plan in sampledPlans)
        {
            inbounds.TryGetValue(InboundOrderNo(plan.WorkOrderNo), out var inbound);
            outbounds.TryGetValue(OutboundOrderNo(plan.Index), out var outbound);

            // 单号命中后回读源单据号逐字比对：挂错源单据的仓储单不算「这一张存在」。
            if (inbound is not null &&
                !string.Equals(inbound.SourceDocumentId, WorldHistoryMesSpec.FinishedGoodsReceiptNo(plan.WorkOrderNo), StringComparison.Ordinal))
            {
                inbound = null;
            }

            if (outbound is not null &&
                !string.Equals(outbound.SourceDocumentId, WorldHistorySpec.DeliveryOrderNo(plan.Index), StringComparison.Ordinal))
            {
                outbound = null;
            }

            lines.Add(CrossServiceSampleProbe.FormatRow(Prefix, new CrossServiceSampleProbeRow(
                Index: plan.Index,
                Link: CrossServiceSampleProbe.Links.FinishedGoodsReceipt,
                Kind: "wms-finished-goods-inbound",
                DocumentNo: InboundOrderNo(plan.WorkOrderNo),
                Expected: factByWorkOrderNo.TryGetValue(plan.WorkOrderNo, out var fact) && fact.HasFinishedGoodsReceipt,
                Exists: inbound is not null,
                Quantity: inbound?.ReceivedQuantity,
                TimestampUtc: inbound?.CreatedAtUtc)));

            lines.Add(CrossServiceSampleProbe.FormatRow(Prefix, new CrossServiceSampleProbeRow(
                Index: plan.Index,
                Link: CrossServiceSampleProbe.Links.Shipment,
                Kind: "wms-delivery-outbound",
                DocumentNo: OutboundOrderNo(plan.Index),
                Expected: plan.HasDelivery,
                Exists: outbound is not null,
                Quantity: outbound?.IssuedQuantity,
                TimestampUtc: outbound?.CreatedAtUtc)));

            // 见证：订单与工单归 ERP / MES 管，仓储域按同一份共享形状声明。
            lines.Add(CrossServiceSampleProbe.FormatRow(Prefix, new CrossServiceSampleProbeRow(
                Index: plan.Index,
                Link: CrossServiceSampleProbe.Links.SalesOrder,
                Kind: "wms-sales-order-witness",
                DocumentNo: plan.SalesOrderNo,
                Expected: true,
                Exists: null,
                Quantity: plan.Quantity)));

            lines.Add(CrossServiceSampleProbe.FormatRow(Prefix, new CrossServiceSampleProbeRow(
                Index: plan.Index,
                Link: CrossServiceSampleProbe.Links.WorkOrder,
                Kind: "wms-work-order-witness",
                DocumentNo: plan.WorkOrderNo,
                Expected: plan.HasWorkOrder,
                Exists: null)));
        }

        return lines;
    }

    private static string InboundOrderNo(string workOrderNo) =>
        WorldHistoryPhase2Spec.InboundOrderNo(WorldHistoryMesSpec.FinishedGoodsReceiptNo(workOrderNo));

    private static string OutboundOrderNo(int index) =>
        WorldHistoryPhase2Spec.OutboundOrderNo(WorldHistorySpec.DeliveryOrderNo(index));
}
