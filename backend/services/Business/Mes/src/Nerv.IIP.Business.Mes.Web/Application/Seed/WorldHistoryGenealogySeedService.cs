using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.ProductionReportAggregate;
using Nerv.IIP.Business.Mes.Infrastructure;

namespace Nerv.IIP.Business.Mes.Web.Application.Seed;

/// <summary>
/// 《工厂世界观设定集》L1 背景历史的**追溯断点**块：产出批次谱系 + 报工物料消耗。
///
/// 必须在 <see cref="WorldHistorySeedService"/> 之后运行——两张表都有指向
/// <c>production_reports</c> / <c>work_orders</c> / <c>operation_tasks</c> 的真实外键，
/// 候选集只能从**已落库**的 L1 报工行里取，绝不凭空造报工号。
///
/// 与既有 L1 块同一套约束：确定性纯函数产出事实（<see cref="WorldHistoryGenealogySpec"/>）、
/// 分批写入 + 批末清变更跟踪器、按自然键幂等预查、fail-closed 一致性校验。
/// </summary>
public sealed class WorldHistoryGenealogySeedService(ApplicationDbContext dbContext)
{
    /// <summary>每批写入条数。批末 <c>SaveChanges</c> 并清变更跟踪器。</summary>
    public const int BatchSize = 200;

    public async Task<WorldHistoryGenealogySeedReport> SeedAsync(
        string organizationId,
        string environmentId,
        CancellationToken cancellationToken = default)
    {
        var anchors = await LoadAnchorsAsync(organizationId, environmentId, cancellationToken);
        if (anchors.Length == 0)
        {
            // 工单链还没落库（或本环境没有 L1 历史）：追溯断点无处可挂。
            return new WorldHistoryGenealogySeedReport(0, 0, new WorldHistoryGenealogyValidationReport(0, 0));
        }

        var genealogiesWritten = await SeedOutputLotGenealogiesAsync(organizationId, environmentId, anchors, cancellationToken);
        var consumptionsWritten = await SeedMaterialConsumptionsAsync(organizationId, environmentId, anchors, cancellationToken);

        var validation = await new WorldHistoryConsistencyValidator(dbContext)
            .ValidateGenealogyAsync(organizationId, environmentId, cancellationToken);

        return new WorldHistoryGenealogySeedReport(genealogiesWritten, consumptionsWritten, validation);
    }

    #region 产出批次谱系

    /// <summary>
    /// 每张工单**一行**谱系断点。
    ///
    /// 这不是取舍而是模型约束：<c>ux_output_lot_genealogies_scope_lot</c> 要求
    /// (org, env, produced_lot_no) 唯一，而一张工单的全部报工共用同一个成品批号
    /// <c>LOT-{工单号}</c>，因此谱系只能落一行。断点挂在**末次报工**上（那是完工的那次），
    /// 数量取工单的报工好品累计——与完工入库单、发货数量逐件对得上。
    /// </summary>
    private async Task<int> SeedOutputLotGenealogiesAsync(
        string organizationId,
        string environmentId,
        WorkOrderAnchor[] anchors,
        CancellationToken cancellationToken)
    {
        var written = 0;
        for (var batchStart = 0; batchStart < anchors.Length; batchStart += BatchSize)
        {
            var batch = anchors.Skip(batchStart).Take(BatchSize).ToArray();
            var existing = await LoadExistingCodesAsync(
                dbContext.OutputLotGenealogies
                    .AsNoTracking()
                    .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId)
                    .Select(x => x.ProducedLotNo),
                batch.Select(x => x.ProducedLotNo).ToArray(),
                cancellationToken);

            var added = 0;
            foreach (var anchor in batch.Where(x => !existing.Contains(x.ProducedLotNo)))
            {
                dbContext.OutputLotGenealogies.Add(OutputLotGenealogy.Create(
                    organizationId,
                    environmentId,
                    anchor.WorkOrderId,
                    anchor.LastReport.OperationTaskId,
                    anchor.LastReport.ReportNo,
                    anchor.ProducedLotNo,
                    serialNo: null,
                    quantity: anchor.TotalGoodQuantity,
                    createdAtUtc: anchor.LastReport.ReportedAtUtc));
                added++;
            }

            if (added > 0)
            {
                await dbContext.SaveChangesAsync(cancellationToken);
                written += added;
            }

            dbContext.ChangeTracker.Clear();
        }

        return written;
    }

    #endregion

    #region 报工物料消耗

    /// <summary>
    /// 每张工单的物料消耗挂在**首次报工**上（投料在开工时一次性发生）。
    /// 一张工单 4 个组件 → 4 行，自然键是 (报工号, 物料, 物料批次)，与唯一索引同构。
    /// </summary>
    private async Task<int> SeedMaterialConsumptionsAsync(
        string organizationId,
        string environmentId,
        WorkOrderAnchor[] anchors,
        CancellationToken cancellationToken)
    {
        var written = 0;
        for (var batchStart = 0; batchStart < anchors.Length; batchStart += BatchSize)
        {
            var batch = anchors.Skip(batchStart).Take(BatchSize).ToArray();
            var reportNos = batch.Select(x => x.FirstReport.ReportNo).ToArray();
            var existing = await LoadExistingCodesAsync(
                dbContext.ProductionReportMaterialConsumptions
                    .AsNoTracking()
                    .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId)
                    .Select(x => x.ReportNo),
                reportNos,
                cancellationToken);

            var added = 0;
            foreach (var anchor in batch.Where(x => !existing.Contains(x.FirstReport.ReportNo)))
            {
                foreach (var consumption in WorldHistoryGenealogySpec.BuildConsumptions(
                    anchor.WorkOrderId,
                    anchor.SkuCode,
                    anchor.WorkOrderQuantity))
                {
                    var fact = ProductionReportMaterialConsumption.Record(
                        organizationId,
                        environmentId,
                        anchor.FirstReport.ReportNo,
                        anchor.WorkOrderId,
                        anchor.FirstReport.OperationTaskId,
                        consumption.MaterialId,
                        consumption.MaterialLotId,
                        consumption.UomCode,
                        consumption.ConsumedQuantity,
                        consumption.MaterialIssueRequestNo);

                    // 历史回填不重放当时的领域事件：ProductionMaterialConsumed 会带着 29 周前的
                    // 业务事实、今天的时间戳去要求 Inventory 再扣一次线边库存，账立刻就不平了。
                    fact.ClearDomainEvents();
                    dbContext.ProductionReportMaterialConsumptions.Add(fact);
                    added++;
                }
            }

            if (added > 0)
            {
                await dbContext.SaveChangesAsync(cancellationToken);
                written += added;
            }

            dbContext.ChangeTracker.Clear();
        }

        return written;
    }

    #endregion

    /// <summary>
    /// 可挂靠的工单候选集：L1 号段内**已有报工**的工单，连同首/末次报工与好品累计。
    /// 已下达未开工的工单没有报工，自然不在内（它们本来也没有产出批次可追）。
    /// </summary>
    private async Task<WorkOrderAnchor[]> LoadAnchorsAsync(
        string organizationId,
        string environmentId,
        CancellationToken cancellationToken)
    {
        var reports = await dbContext.ProductionReports
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId &&
                x.ReportNo.StartsWith(WorldHistoryGenealogySpec.ProductionReportNoPrefix) &&
                x.ReversedReportNo == null)
            .Select(x => new ReportAnchor(x.ReportNo, x.WorkOrderId, x.OperationTaskId, x.GoodQuantity, x.ReportedAtUtc))
            .ToArrayAsync(cancellationToken);
        if (reports.Length == 0)
        {
            return [];
        }

        var workOrders = await dbContext.WorkOrders
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId)
            .Select(x => new { x.WorkOrderIdValue, x.SkuId, x.Quantity })
            .ToArrayAsync(cancellationToken);
        var workOrderIndex = workOrders.ToDictionary(x => x.WorkOrderIdValue, StringComparer.Ordinal);

        return
        [
            .. reports
                .GroupBy(x => x.WorkOrderId, StringComparer.Ordinal)
                .Where(group => workOrderIndex.ContainsKey(group.Key))
                .Select(group =>
                {
                    // 报工号内含序号（RPT-{工单号}-{NN}），字典序即时间序，因此排序确定且与时间同向。
                    var ordered = group.OrderBy(x => x.ReportNo, StringComparer.Ordinal).ToArray();
                    var workOrder = workOrderIndex[group.Key];
                    return new WorkOrderAnchor(
                        group.Key,
                        workOrder.SkuId,
                        workOrder.Quantity,
                        WorldHistoryGenealogySpec.ProducedLotNo(group.Key),
                        ordered[0],
                        ordered[^1],
                        ordered.Sum(x => x.GoodQuantity));
                })
                .Where(anchor => anchor.TotalGoodQuantity > 0m)
                .OrderBy(anchor => anchor.WorkOrderId, StringComparer.Ordinal),
        ];
    }

    private sealed record ReportAnchor(
        string ReportNo,
        string WorkOrderId,
        string OperationTaskId,
        decimal GoodQuantity,
        DateTimeOffset ReportedAtUtc);

    private sealed record WorkOrderAnchor(
        string WorkOrderId,
        string SkuCode,
        decimal WorkOrderQuantity,
        string ProducedLotNo,
        ReportAnchor FirstReport,
        ReportAnchor LastReport,
        decimal TotalGoodQuantity);

    private static async Task<HashSet<string>> LoadExistingCodesAsync(
        IQueryable<string> source,
        string[] codes,
        CancellationToken cancellationToken) =>
        (await source.Where(code => codes.Contains(code)).ToArrayAsync(cancellationToken))
        .ToHashSet(StringComparer.Ordinal);
}

/// <summary>一次 L1 追溯断点块生成的产出摘要。</summary>
public sealed record WorldHistoryGenealogySeedReport(
    int OutputLotGenealogiesWritten,
    int MaterialConsumptionsWritten,
    WorldHistoryGenealogyValidationReport Validation);
