using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Quality.Infrastructure;
using Nerv.IIP.Business.Quality.Web.Application.Seed;
using Nerv.IIP.Testing;
using System.Globalization;

namespace Nerv.IIP.Business.Quality.Web.Tests;

/// <summary>
/// 质量域侧的跨域抽样探针（#1826）。见 <c>scripts/verify-world-history.ps1</c> 的对账口径。
/// </summary>
internal static class WorldHistoryCrossDomainProbe
{
    public const string Prefix = "quality-world-history";

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
        var lines = new List<string>(1 + (indexes.Count * 3))
        {
            CrossServiceSampleProbe.FormatBasis(Prefix, asOfDate, scale, plans.Count, indexes),
        };

        var factByWorkOrderNo = WorldHistoryPhase2Spec.BuildWorkOrderFacts(asOfDate, scale)
            .Where(fact => !fact.IsRework)
            .ToDictionary(fact => fact.Plan.WorkOrderNo, StringComparer.Ordinal);

        var sampledPlans = indexes.Select(index => plans[index - 1]).ToArray();
        var triggerKeys = new List<string>(sampledPlans.Length * 2);
        foreach (var plan in sampledPlans)
        {
            triggerKeys.Add(OperationInspectionKey(plan.WorkOrderNo));
            triggerKeys.Add(OutboundInspectionKey(plan.Index));
        }

        var tasks = (await dbContext.InspectionTasks
                .AsNoTracking()
                .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId &&
                    triggerKeys.Contains(x.TriggerIdempotencyKey))
                .Select(x => new { x.TriggerIdempotencyKey, x.SourceDocumentId, x.Quantity, x.CreatedAtUtc })
                .ToArrayAsync(cancellationToken))
            .ToDictionary(x => x.TriggerIdempotencyKey, StringComparer.Ordinal);

        foreach (var plan in sampledPlans)
        {
            tasks.TryGetValue(OperationInspectionKey(plan.WorkOrderNo), out var operationTask);
            tasks.TryGetValue(OutboundInspectionKey(plan.Index), out var outboundTask);

            lines.Add(CrossServiceSampleProbe.FormatRow(Prefix, new CrossServiceSampleProbeRow(
                Index: plan.Index,
                Link: CrossServiceSampleProbe.Links.OperationInspection,
                Kind: "quality-operation-inspection",
                DocumentNo: plan.WorkOrderNo,
                // 终检检验任务挂在「有性能终检工序」的工单上，与执行深度无关。
                Expected: factByWorkOrderNo.TryGetValue(plan.WorkOrderNo, out var fact) && fact.HasFinalInspection,
                Exists: operationTask is not null,
                // 检验数量按工单投料量记，与 MES 的终检工序任务计划数量同一口径。
                Quantity: operationTask?.Quantity,
                TimestampUtc: operationTask?.CreatedAtUtc)));

            lines.Add(CrossServiceSampleProbe.FormatRow(Prefix, new CrossServiceSampleProbeRow(
                Index: plan.Index,
                Link: CrossServiceSampleProbe.Links.OutboundInspection,
                Kind: "quality-outbound-inspection",
                DocumentNo: WorldHistorySpec.DeliveryOrderNo(plan.Index),
                // #1374：出货检验在完工装箱环节成立，与发运与否无关。
                Expected: plan.IsProductionClosed,
                Exists: outboundTask is not null,
                Quantity: outboundTask?.Quantity,
                TimestampUtc: outboundTask?.CreatedAtUtc)));

            // 见证：销售订单归 ERP 管，质量域按同一张订单计划表声明数量。
            lines.Add(CrossServiceSampleProbe.FormatRow(Prefix, new CrossServiceSampleProbeRow(
                Index: plan.Index,
                Link: CrossServiceSampleProbe.Links.SalesOrder,
                Kind: "quality-sales-order-witness",
                DocumentNo: plan.SalesOrderNo,
                Expected: true,
                Exists: null,
                Quantity: plan.Quantity)));
        }

        return lines;
    }

    private static string OperationInspectionKey(string workOrderNo) =>
        WorldHistoryQualitySpec.TriggerIdempotencyKey(
            "mes",
            workOrderNo,
            WorldHistoryMesSpec.QualityInspectionSequence.ToString(CultureInfo.InvariantCulture));

    private static string OutboundInspectionKey(int index) =>
        WorldHistoryQualitySpec.TriggerIdempotencyKey("erp", WorldHistorySpec.DeliveryOrderNo(index), null);
}
