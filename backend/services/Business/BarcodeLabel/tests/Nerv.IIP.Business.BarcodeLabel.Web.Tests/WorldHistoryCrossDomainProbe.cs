using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.BarcodeLabel.Infrastructure;
using Nerv.IIP.Business.BarcodeLabel.Web.Application.Seed;
using Nerv.IIP.Testing;

namespace Nerv.IIP.Business.BarcodeLabel.Web.Tests;

/// <summary>
/// 条码标签域侧的跨域抽样探针（#1826）。见 <c>scripts/verify-world-history.ps1</c> 的对账口径。
///
/// <para>
/// 打印批次是**按 900 张预算抽样**产生的：绝大多数完工入库单与发货单合法地没有打印批次。
/// 因此这里的 <c>expected</c> 直接来自 <see cref="WorldHistoryLabelSpec.BuildPrintBatchFacts"/>
/// 的抽样结果，而不是「有源单据就该有批次」——后者会把合法缺失一律判红。
/// </para>
/// </summary>
internal static class WorldHistoryCrossDomainProbe
{
    public const string Prefix = "label-world-history";

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
        var sampledBatchKeys = WorldHistoryLabelSpec.BuildPrintBatchFacts(asOfDate, scale)
            .Select(fact => fact.IdempotencyKey)
            .ToHashSet(StringComparer.Ordinal);

        var lotTemplateCode = WorldHistoryLabelSpec.TemplateFor(WorldHistoryLabelFamily.Lot).TemplateCode;
        var cartonTemplateCode = WorldHistoryLabelSpec.TemplateFor(WorldHistoryLabelFamily.Carton).TemplateCode;

        var sampledPlans = indexes.Select(index => plans[index - 1]).ToArray();
        var batchKeys = new List<string>(sampledPlans.Length * 2);
        foreach (var plan in sampledPlans)
        {
            batchKeys.Add(LotBatchKey(plan.WorkOrderNo, lotTemplateCode));
            batchKeys.Add(CartonBatchKey(plan.Index, cartonTemplateCode));
        }

        var batches = (await dbContext.LabelPrintBatches
                .AsNoTracking()
                .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId &&
                    batchKeys.Contains(x.IdempotencyKey))
                .Select(x => new { x.IdempotencyKey, x.CreatedAtUtc })
                .ToArrayAsync(cancellationToken))
            .ToDictionary(x => x.IdempotencyKey, StringComparer.Ordinal);

        foreach (var plan in sampledPlans)
        {
            var lotKey = LotBatchKey(plan.WorkOrderNo, lotTemplateCode);
            var cartonKey = CartonBatchKey(plan.Index, cartonTemplateCode);
            batches.TryGetValue(lotKey, out var lotBatch);
            batches.TryGetValue(cartonKey, out var cartonBatch);

            lines.Add(CrossServiceSampleProbe.FormatRow(Prefix, new CrossServiceSampleProbeRow(
                Index: plan.Index,
                Link: CrossServiceSampleProbe.Links.LotPrintBatch,
                Kind: "label-lot-print-batch",
                DocumentNo: lotKey,
                Expected: sampledBatchKeys.Contains(lotKey),
                Exists: lotBatch is not null,
                TimestampUtc: lotBatch?.CreatedAtUtc)));

            lines.Add(CrossServiceSampleProbe.FormatRow(Prefix, new CrossServiceSampleProbeRow(
                Index: plan.Index,
                Link: CrossServiceSampleProbe.Links.CartonPrintBatch,
                Kind: "label-carton-print-batch",
                DocumentNo: cartonKey,
                Expected: sampledBatchKeys.Contains(cartonKey),
                Exists: cartonBatch is not null,
                TimestampUtc: cartonBatch?.CreatedAtUtc)));

            // 见证：源单据归 MES / 库存 / 仓储 / ERP 管，标签域按同一份共享形状声明。
            lines.Add(CrossServiceSampleProbe.FormatRow(Prefix, new CrossServiceSampleProbeRow(
                Index: plan.Index,
                Link: CrossServiceSampleProbe.Links.FinishedGoodsReceipt,
                Kind: "label-finished-goods-receipt-witness",
                DocumentNo: WorldHistoryMesSpec.FinishedGoodsReceiptNo(plan.WorkOrderNo),
                Expected: factByWorkOrderNo.TryGetValue(plan.WorkOrderNo, out var fact) && fact.HasFinishedGoodsReceipt,
                Exists: null,
                Quantity: plan.Quantity)));

            lines.Add(CrossServiceSampleProbe.FormatRow(Prefix, new CrossServiceSampleProbeRow(
                Index: plan.Index,
                Link: CrossServiceSampleProbe.Links.Shipment,
                Kind: "label-shipment-witness",
                DocumentNo: WorldHistorySpec.DeliveryOrderNo(plan.Index),
                Expected: plan.HasDelivery,
                Exists: null,
                Quantity: plan.Quantity)));
        }

        return lines;
    }

    private static string LotBatchKey(string workOrderNo, string templateCode) =>
        WorldHistoryPhase2Spec.PrintBatchKey(
            WorldHistoryMesSpec.FinishedGoodsReceiptNo(workOrderNo),
            templateCode);

    private static string CartonBatchKey(int index, string templateCode) =>
        WorldHistoryPhase2Spec.PrintBatchKey(WorldHistorySpec.DeliveryOrderNo(index), templateCode);
}
