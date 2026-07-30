using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.DemandPlanning.Domain.AggregatesModel.DemandSourceAggregate;
using Nerv.IIP.Business.DemandPlanning.Domain.AggregatesModel.ForecastInputAggregate;
using Nerv.IIP.Business.DemandPlanning.Domain.AggregatesModel.MasterProductionScheduleAggregate;
using Nerv.IIP.Business.DemandPlanning.Domain.AggregatesModel.MrpRunAggregate;
using Nerv.IIP.Business.DemandPlanning.Domain.AggregatesModel.PlanningSuggestionAggregate;

namespace Nerv.IIP.Business.DemandPlanning.Web.Application.Seed;

/// <summary>
/// 《工厂世界观设定集》L1 背景历史引擎的 **计划域侧**：
/// 需求来源（销售订单投影）→ 月度预测 → MPS 周桶 → 周度 MRP 运行 → 计划建议（含 pegging）。
///
/// 与 ERP/MES 的一致性靠 <see cref="WorldHistorySpec.BuildOrderPlans"/> 与
/// <see cref="WorldHistoryTimeline"/> 两个确定性纯函数达成：两侧不通信、不跨库查询、
/// 不建跨 schema 外键；订单 MRP 建议 ID 与 MES 侧按同一确定性算法计算，
/// 仅以 <c>SO-2026-#####</c> ↔ <c>WO-2026-#####</c> 及建议 ID 相互引用。
///
/// 领域事件说明：本仓栈里 <c>DbContext.SaveChangesAsync()</c> 不派发领域事件（派发只发生在
/// netcorepal 的 UnitOfWork/命令管线上），因此这里可以放心调用会 <c>AddDomainEvent</c> 的聚合方法，
/// 历史数据不会反向触发 CAP 集成事件风暴——与 ERP/MES/Quality 引擎同一前提。
/// </summary>
public sealed class WorldHistorySeedService(ApplicationDbContext dbContext)
{
    /// <summary>每批单据数。批内共享一次预查与一次 <c>SaveChanges</c>，批末清变更跟踪器。</summary>
    public const int BatchSize = 200;

    /// <summary>已接受建议的下游服务/单据类型（MES 工单）。</summary>
    public const string DownstreamService = "business-mes";
    public const string DownstreamDocumentType = "work-order";

    public async Task<WorldHistoryPlanningSeedReport> SeedAsync(
        string organizationId,
        string environmentId,
        DateOnly asOfDate,
        double scale,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(scale);

        var facts = WorldHistoryPlanningSpec.BuildPlanningFacts(asOfDate, scale);

        var demandsWritten = await SeedDemandSourcesAsync(organizationId, environmentId, facts.Demands, cancellationToken);
        var forecastsWritten = await SeedForecastInputsAsync(organizationId, environmentId, facts.Forecasts, cancellationToken);
        var mpsWritten = await SeedMpsBucketsAsync(organizationId, environmentId, facts.MpsBuckets, cancellationToken);
        // 当前 10 周活动 MRP 与更早历史订单的回填批次共用同一落库路径；
        // 活动需求/MPS 仍只来自 facts.Demands/MpsBuckets，不扩大现行窗口语义。
        var mrpFacts = facts.MrpRuns
            .Concat(facts.HistoricalMrpRuns)
            .ToArray();
        var (runsWritten, suggestionsWritten) = await SeedMrpRunsAsync(
            organizationId,
            environmentId,
            mrpFacts,
            cancellationToken);

        // fail-closed：需求/建议配对、MPS 对账或时间链对不上就让 seed 失败。
        var validation = await new WorldHistoryConsistencyValidator(dbContext)
            .ValidateAsync(organizationId, environmentId, asOfDate, scale, cancellationToken);

        return new WorldHistoryPlanningSeedReport(
            DemandSourcesWritten: demandsWritten,
            ForecastInputsWritten: forecastsWritten,
            MpsBucketsWritten: mpsWritten,
            MrpRunsWritten: runsWritten,
            PlanningSuggestionsWritten: suggestionsWritten,
            Validation: validation);
    }

    #region 需求来源（sales-order 投影，自然键 (DemandType, SourceReference, SourceLineReference)）

    private async Task<int> SeedDemandSourcesAsync(
        string organizationId,
        string environmentId,
        IReadOnlyList<WorldHistoryDemandFact> facts,
        CancellationToken cancellationToken)
    {
        var written = 0;
        for (var batchStart = 0; batchStart < facts.Count; batchStart += BatchSize)
        {
            var batch = facts.Skip(batchStart).Take(BatchSize).ToArray();
            var references = batch.Select(fact => fact.Plan.SalesOrderNo).ToArray();
            var existing = (await dbContext.DemandSources
                    .AsNoTracking()
                    .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId &&
                        x.DemandType == "sales-order" && references.Contains(x.SourceReference))
                    .Select(x => x.SourceReference)
                    .ToArrayAsync(cancellationToken))
                .ToHashSet(StringComparer.Ordinal);

            var added = 0;
            foreach (var fact in batch.Where(fact => !existing.Contains(fact.Plan.SalesOrderNo)))
            {
                var demand = DemandSource.CreateSalesOrderDemand(
                    organizationId,
                    environmentId,
                    sourceDocumentId: fact.Plan.SalesOrderNo,
                    salesOrderNo: fact.Plan.SalesOrderNo,
                    salesOrderLineNo: WorldHistoryPlanningSpec.SalesOrderLineNo,
                    customerCode: fact.Plan.CustomerCode,
                    skuCode: fact.Plan.SkuCode,
                    uomCode: WorldHistoryPlanningSpec.UomCode,
                    siteCode: WorldHistoryPlanningSpec.SiteCode,
                    quantity: fact.Plan.Quantity,
                    dueDate: fact.Plan.RequiredDate,
                    sourceVersion: 1);
                if (fact.IsCancelled)
                {
                    demand.CancelFromSalesOrder(sourceVersion: 2);
                }

                dbContext.DemandSources.Add(demand);
                Backdate(demand, x => x.CreatedAtUtc, fact.CreatedAtUtc);
                Backdate(demand, x => x.UpdatedAtUtc, fact.IsCancelled ? fact.CreatedAtUtc.AddHours(1) : fact.CreatedAtUtc);
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

    #region 月度预测（自然键 ForecastReference）

    private async Task<int> SeedForecastInputsAsync(
        string organizationId,
        string environmentId,
        IReadOnlyList<WorldHistoryForecastFact> facts,
        CancellationToken cancellationToken)
    {
        var references = facts.Select(fact => fact.ForecastReference).ToArray();
        var existing = (await dbContext.ForecastInputs
                .AsNoTracking()
                .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId &&
                    references.Contains(x.ForecastReference))
                .Select(x => x.ForecastReference)
                .ToArrayAsync(cancellationToken))
            .ToHashSet(StringComparer.Ordinal);

        var written = 0;
        foreach (var fact in facts.Where(fact => !existing.Contains(fact.ForecastReference)))
        {
            var forecast = ForecastInput.Create(
                organizationId,
                environmentId,
                fact.ForecastReference,
                fact.SkuCode,
                WorldHistoryPlanningSpec.UomCode,
                WorldHistoryPlanningSpec.SiteCode,
                fact.PeriodStartDate,
                fact.PeriodEndDate,
                fact.Quantity,
                backwardConsumptionDays: 7,
                forwardConsumptionDays: 14);
            dbContext.ForecastInputs.Add(forecast);
            Backdate(forecast, x => x.CreatedAtUtc, fact.CreatedAtUtc);
            Backdate(forecast, x => x.UpdatedAtUtc, fact.CreatedAtUtc);
            written++;
        }

        if (written > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        dbContext.ChangeTracker.Clear();
        return written;
    }

    #endregion

    #region MPS 周桶（自然键 (SkuCode, SiteCode, BucketDate)）

    private async Task<int> SeedMpsBucketsAsync(
        string organizationId,
        string environmentId,
        IReadOnlyList<WorldHistoryMpsFact> facts,
        CancellationToken cancellationToken)
    {
        var written = 0;
        for (var batchStart = 0; batchStart < facts.Count; batchStart += BatchSize)
        {
            var batch = facts.Skip(batchStart).Take(BatchSize).ToArray();
            var bucketDates = batch.Select(fact => fact.BucketDate).Distinct().ToArray();
            var existing = (await dbContext.MasterProductionSchedules
                    .AsNoTracking()
                    .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId &&
                        x.SiteCode == WorldHistoryPlanningSpec.SiteCode && bucketDates.Contains(x.BucketDate))
                    .Select(x => new { x.SkuCode, x.BucketDate })
                    .ToArrayAsync(cancellationToken))
                .Select(x => (x.SkuCode, x.BucketDate))
                .ToHashSet();

            var added = 0;
            foreach (var fact in batch.Where(fact => !existing.Contains((fact.SkuCode, fact.BucketDate))))
            {
                var bucket = MasterProductionSchedule.Create(
                    organizationId,
                    environmentId,
                    fact.SkuCode,
                    WorldHistoryPlanningSpec.UomCode,
                    WorldHistoryPlanningSpec.SiteCode,
                    fact.BucketDate,
                    fact.Quantity);
                if (fact.Status >= MasterProductionScheduleStatus.Reviewed)
                {
                    bucket.MarkReviewed(fact.ReviewedBy!);
                }

                if (fact.Status == MasterProductionScheduleStatus.Released)
                {
                    bucket.Release(fact.ReleasedBy!);
                }

                dbContext.MasterProductionSchedules.Add(bucket);
                Backdate(bucket, x => x.CreatedAtUtc, fact.CreatedAtUtc);
                Backdate(bucket, x => x.UpdatedAtUtc, fact.ReleasedAtUtc ?? fact.ReviewedAtUtc ?? fact.CreatedAtUtc);
                if (fact.ReviewedAtUtc is { } reviewedAtUtc)
                {
                    Backdate(bucket, x => x.ReviewedAtUtc, (DateTimeOffset?)reviewedAtUtc);
                }

                if (fact.ReleasedAtUtc is { } releasedAtUtc)
                {
                    Backdate(bucket, x => x.ReleasedAtUtc, (DateTimeOffset?)releasedAtUtc);
                }

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

    #region MRP 运行与建议（自然键 HorizonStart；建议随运行整体写入）

    private async Task<(int Runs, int Suggestions)> SeedMrpRunsAsync(
        string organizationId,
        string environmentId,
        IReadOnlyList<WorldHistoryMrpRunFact> facts,
        CancellationToken cancellationToken)
    {
        var horizonStarts = facts.Select(fact => fact.HorizonStart).ToArray();
        var existing = (await dbContext.MrpRuns
                .AsNoTracking()
                .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId &&
                    horizonStarts.Contains(x.HorizonStart))
                .Select(x => x.HorizonStart)
                .ToArrayAsync(cancellationToken))
            .ToHashSet();

        var runsWritten = 0;
        var suggestionsWritten = 0;
        foreach (var fact in facts.Where(fact => !existing.Contains(fact.HorizonStart)))
        {
            var run = MrpRun.Create(organizationId, environmentId, fact.HorizonStart, fact.HorizonEnd);
            run.Start(new PlanningInputSnapshot(
                fact.ProductionEngineeringSnapshotSource,
                fact.InventorySnapshotSource,
                fact.DemandCount,
                fact.AvailabilityCount,
                fact.InputSources,
                fact.InputCoverageStart,
                fact.InputCoverageEnd));
            run.Complete(fact.Suggestions.Count);
            dbContext.MrpRuns.Add(run);
            Backdate(run, x => x.CreatedAtUtc, fact.CreatedAtUtc);
            Backdate(run, x => x.StartedAtUtc, (DateTimeOffset?)fact.StartedAtUtc);
            Backdate(run, x => x.CompletedAtUtc, (DateTimeOffset?)fact.CompletedAtUtc);

            foreach (var suggestionFact in fact.Suggestions)
            {
                WriteSuggestion(organizationId, environmentId, run.Id, suggestionFact);
                suggestionsWritten++;
            }

            runsWritten++;
            await dbContext.SaveChangesAsync(cancellationToken);
            dbContext.ChangeTracker.Clear();
        }

        return (runsWritten, suggestionsWritten);
    }

    private void WriteSuggestion(
        string organizationId,
        string environmentId,
        MrpRunId runId,
        WorldHistorySuggestionFact fact)
    {
        var suggestion = PlanningSuggestion.Create(
            organizationId,
            environmentId,
            runId,
            suggestionType: "planned-work-order",
            fact.SkuCode,
            WorldHistoryPlanningSpec.UomCode,
            WorldHistoryPlanningSpec.SiteCode,
            fact.PlannedQuantity,
            fact.RequiredDate,
            fact.ReleaseDate,
            reasonCode: "net-requirement",
            suggestionId: new PlanningSuggestionId(fact.SuggestionId));
        suggestion.SetNetRequirementExplanation(
            grossDemandQuantity: fact.GrossQuantity,
            onHandQuantity: fact.OnHandQuantity,
            reservedQuantity: 0m,
            availableToNetQuantity: fact.OnHandQuantity,
            scheduledReceiptQuantity: 0m,
            safetyStockQuantity: 0m,
            netRequirementQuantity: fact.NetQuantity,
            plannedQuantity: fact.PlannedQuantity,
            scrapRate: fact.ScrapRate,
            yieldRate: 1m,
            primarySourceType: fact.SourceType,
            formula: BuildFormula(fact),
            uomConversionSummary: null);
        suggestion.AddPeggingLink(
            peggingType: "demand",
            demandSourceReference: fact.DemandSourceReference,
            parentSkuCode: fact.SkuCode,
            componentSkuCode: null,
            quantity: fact.GrossQuantity,
            productionVersionReference: null,
            manufacturingBomReference: null,
            routingReference: null,
            sourceType: fact.SourceType,
            grossDemandQuantity: fact.GrossQuantity);
        if (fact.IsAccepted)
        {
            suggestion.Accept(DownstreamService, DownstreamDocumentType, fact.DownstreamDocumentId);
        }

        dbContext.PlanningSuggestions.Add(suggestion);
        Backdate(suggestion, x => x.CreatedAtUtc, fact.CreatedAtUtc);
        if (fact.AcceptedAtUtc is { } acceptedAtUtc)
        {
            Backdate(suggestion, x => x.AcceptedAtUtc, (DateTimeOffset?)acceptedAtUtc);
        }
    }

    /// <summary>与 <c>MrpCalculator.BuildFormula</c> 同形的净需求公式文本。</summary>
    private static string BuildFormula(WorldHistorySuggestionFact fact) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{fact.GrossQuantity:g29} - {fact.OnHandQuantity:g29} - 0 = {fact.NetQuantity:g29}; scrap/yield {fact.ScrapRate:g29}/1");

    #endregion

    private void Backdate<TEntity, TProperty>(
        TEntity entity,
        System.Linq.Expressions.Expression<Func<TEntity, TProperty>> property,
        TProperty value)
        where TEntity : class
    {
        dbContext.Entry(entity).Property(property).CurrentValue = value;
    }
}

/// <summary>一次 L1 计划域历史生成的产出摘要。</summary>
public sealed record WorldHistoryPlanningSeedReport(
    int DemandSourcesWritten,
    int ForecastInputsWritten,
    int MpsBucketsWritten,
    int MrpRunsWritten,
    int PlanningSuggestionsWritten,
    WorldHistoryPlanningValidationReport Validation);
