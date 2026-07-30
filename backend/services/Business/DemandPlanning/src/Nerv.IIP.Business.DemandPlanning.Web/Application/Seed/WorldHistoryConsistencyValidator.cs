using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.DemandPlanning.Domain.AggregatesModel.MasterProductionScheduleAggregate;
using Nerv.IIP.Business.DemandPlanning.Domain.AggregatesModel.PlanningSuggestionAggregate;

namespace Nerv.IIP.Business.DemandPlanning.Web.Application.Seed;

/// <summary>
/// 《工厂世界观设定集》§7 一致性校验器的 **计划域侧**。
///
/// 覆盖：需求来源与订单计划表逐字段配对（含取消单的取消态）、已接受建议的下游工单号
/// 与共享公式 <see cref="WorldHistorySpec.WorkOrderNo"/> 逐字对上（即 MES 侧真实存在的工单）、
/// MPS 周桶数量 = 该周活跃需求量之和、MRP 运行时间链单调且全部时间戳落在历史窗口内、
/// 与固定演示事实（<c>*-DEMO-*</c> / <c>*-SCALE-*</c>）隔离。
/// **fail-closed**：任何一条不成立即抛 <see cref="WorldHistoryConsistencyException"/>（中文累积）。
///
/// 跨服务的「需求 ↔ ERP 订单 ↔ MES 工单」对账不在这里做（本服务看不到 ERP/MES 的库）：
/// 配对由共享 <see cref="WorldHistorySpec"/> 的确定性与各侧黄金向量测试保证。
/// </summary>
public sealed class WorldHistoryConsistencyValidator(ApplicationDbContext dbContext)
{
    public const int SampleSize = 10;

    private static readonly string[] ReservedInfixes = ["-DEMO-", "-SCALE-"];

    public async Task<WorldHistoryPlanningValidationReport> ValidateAsync(
        string organizationId,
        string environmentId,
        DateOnly asOfDate,
        double scale,
        CancellationToken cancellationToken = default)
    {
        var facts = WorldHistoryPlanningSpec.BuildPlanningFacts(asOfDate, scale);
        var failures = new List<string>();
        var lowerBound = new DateTimeOffset(WorldHistoryCalendar.GoLiveDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var upperBound = new DateTimeOffset(asOfDate.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero);

        var demands = await dbContext.DemandSources.AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId &&
                x.DemandType == "sales-order" && x.SourceReference.StartsWith("SO-2026-"))
            .ToListAsync(cancellationToken);
        var mpsBuckets = await dbContext.MasterProductionSchedules.AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId &&
                x.SiteCode == WorldHistoryPlanningSpec.SiteCode)
            .ToListAsync(cancellationToken);
        var runs = await dbContext.MrpRuns.AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId)
            .ToListAsync(cancellationToken);
        var runIds = runs.Select(x => x.Id).ToArray();
        var suggestions = await dbContext.PlanningSuggestions.AsNoTracking()
            .Include(x => x.PeggingLinks)
            .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId &&
                runIds.Contains(x.MrpRunId))
            .ToListAsync(cancellationToken);

        CheckDemands(facts, demands, lowerBound, upperBound, failures);
        CheckMpsBuckets(facts, mpsBuckets, failures);
        CheckRunsAndSuggestions(facts, runs, suggestions, lowerBound, upperBound, failures);
        CheckIsolation(demands, suggestions, failures);

        if (failures.Count > 0)
        {
            throw new WorldHistoryConsistencyException(failures);
        }

        var sample = facts.Demands
            .Where(fact => !fact.IsCancelled)
            .Take(SampleSize)
            .Select(fact => string.Create(
                CultureInfo.InvariantCulture,
                $"{fact.Plan.SalesOrderNo} → {fact.Plan.SkuCode} x{fact.Plan.Quantity} → {fact.Plan.WorkOrderNo}（{fact.Plan.Stage}）"))
            .ToArray();

        return new WorldHistoryPlanningValidationReport(
            DemandSourcesChecked: demands.Count,
            MpsBucketsChecked: facts.MpsBuckets.Count,
            MrpRunsChecked: facts.MrpRuns.Count + facts.HistoricalMrpRuns.Count,
            PlanningSuggestionsChecked: suggestions.Count,
            AcceptedSuggestionsChecked: suggestions.Count(x => x.Status == PlanningSuggestionStatus.Accepted),
            Sample: sample);
    }

    private static void CheckDemands(
        WorldHistoryPlanningFacts facts,
        IReadOnlyList<Domain.AggregatesModel.DemandSourceAggregate.DemandSource> demands,
        DateTimeOffset lowerBound,
        DateTimeOffset upperBound,
        List<string> failures)
    {
        var byReference = demands.ToDictionary(x => x.SourceReference, StringComparer.Ordinal);
        if (demands.Count != facts.Demands.Count)
        {
            failures.Add($"库内世界观需求来源 {demands.Count} 条，与计划表 {facts.Demands.Count} 条不一致。");
        }

        foreach (var fact in facts.Demands)
        {
            if (!byReference.TryGetValue(fact.Plan.SalesOrderNo, out var demand))
            {
                failures.Add($"需求来源 {fact.Plan.SalesOrderNo} 缺失。");
                continue;
            }

            if (fact.IsCancelled)
            {
                if (!string.Equals(demand.SourceStatus, "cancelled", StringComparison.Ordinal))
                {
                    failures.Add($"废弃订单 {fact.Plan.SalesOrderNo} 的需求来源不是取消态。");
                }
            }
            else if (demand.Quantity != fact.Plan.Quantity || demand.DueDate != fact.Plan.RequiredDate)
            {
                failures.Add($"需求来源 {fact.Plan.SalesOrderNo} 的数量/交期与订单计划不一致。");
            }

            if (!string.Equals(demand.SourceLineReference, WorldHistoryPlanningSpec.SalesOrderLineNo, StringComparison.Ordinal))
            {
                failures.Add($"需求来源 {fact.Plan.SalesOrderNo} 的行号不是 '{WorldHistoryPlanningSpec.SalesOrderLineNo}'，与 MES SourceDemandReference 配不上。");
            }

            if (demand.CreatedAtUtc < lowerBound || demand.CreatedAtUtc > upperBound)
            {
                failures.Add($"需求来源 {fact.Plan.SalesOrderNo} 的创建时间越出历史窗口。");
            }
        }
    }

    private static void CheckMpsBuckets(
        WorldHistoryPlanningFacts facts,
        IReadOnlyList<MasterProductionSchedule> mpsBuckets,
        List<string> failures)
    {
        var byKey = mpsBuckets.ToDictionary(x => (x.SkuCode, x.BucketDate));
        foreach (var fact in facts.MpsBuckets)
        {
            if (!byKey.TryGetValue((fact.SkuCode, fact.BucketDate), out var bucket))
            {
                failures.Add($"MPS 桶 {fact.SkuCode}@{fact.BucketDate:yyyy-MM-dd} 缺失。");
                continue;
            }

            if (bucket.Quantity != fact.Quantity)
            {
                failures.Add($"MPS 桶 {fact.SkuCode}@{fact.BucketDate:yyyy-MM-dd} 数量 {bucket.Quantity} 与该周活跃需求合计 {fact.Quantity} 不一致。");
            }

            if (bucket.Status != fact.Status)
            {
                failures.Add($"MPS 桶 {fact.SkuCode}@{fact.BucketDate:yyyy-MM-dd} 状态 {bucket.Status} 与期望 {fact.Status} 不一致。");
            }

            if (bucket.Status == MasterProductionScheduleStatus.Released &&
                (string.IsNullOrWhiteSpace(bucket.ReviewedBy) || string.IsNullOrWhiteSpace(bucket.ReleasedBy)))
            {
                failures.Add($"已发布 MPS 桶 {fact.SkuCode}@{fact.BucketDate:yyyy-MM-dd} 缺少评审人/发布人。");
            }
        }
    }

    private static void CheckRunsAndSuggestions(
        WorldHistoryPlanningFacts facts,
        IReadOnlyList<Domain.AggregatesModel.MrpRunAggregate.MrpRun> runs,
        IReadOnlyList<PlanningSuggestion> suggestions,
        DateTimeOffset lowerBound,
        DateTimeOffset upperBound,
        List<string> failures)
    {
        var runsByHorizonStart = runs
            .GroupBy(x => x.HorizonStart)
            .ToDictionary(group => group.Key, group => group.First());
        var suggestionsByRunId = suggestions.ToLookup(x => x.MrpRunId);

        var mrpFacts = facts.MrpRuns
            .Concat(facts.HistoricalMrpRuns)
            .ToArray();

        foreach (var fact in mrpFacts)
        {
            if (!runsByHorizonStart.TryGetValue(fact.HorizonStart, out var run))
            {
                failures.Add($"MRP 运行 {fact.HorizonStart:yyyy-MM-dd} 缺失。");
                continue;
            }

            if (run.CreatedAtUtc < lowerBound || run.CompletedAtUtc is null || run.CompletedAtUtc > upperBound ||
                run.StartedAtUtc is null || run.CreatedAtUtc > run.StartedAtUtc || run.StartedAtUtc > run.CompletedAtUtc)
            {
                failures.Add($"MRP 运行 {fact.HorizonStart:yyyy-MM-dd} 的时间链非单调或越出历史窗口。");
            }

            var runSuggestions = suggestionsByRunId[run.Id].ToArray();
            if (runSuggestions.Length != fact.Suggestions.Count)
            {
                failures.Add($"MRP 运行 {fact.HorizonStart:yyyy-MM-dd} 的建议 {runSuggestions.Length} 条，与计划 {fact.Suggestions.Count} 条不一致。");
                continue;
            }

            var byReference = runSuggestions.ToLookup(
                x => x.PeggingLinks.Select(link => link.DemandSourceReference).FirstOrDefault() ?? string.Empty,
                StringComparer.Ordinal);
            foreach (var suggestionFact in fact.Suggestions)
            {
                var suggestion = byReference[suggestionFact.DemandSourceReference].FirstOrDefault();
                if (suggestion is null)
                {
                    failures.Add($"建议缺失：{suggestionFact.DemandSourceReference}。");
                    continue;
                }

                if (suggestionFact.IsAccepted)
                {
                    if (suggestion.Status != PlanningSuggestionStatus.Accepted ||
                        !string.Equals(suggestion.AcceptedDownstreamDocumentId, suggestionFact.DownstreamDocumentId, StringComparison.Ordinal))
                    {
                        failures.Add($"建议 {suggestionFact.DemandSourceReference} 未按计划接受到下游工单 {suggestionFact.DownstreamDocumentId}。");
                    }
                    else if (suggestion.AcceptedAtUtc is null || suggestion.AcceptedAtUtc < suggestion.CreatedAtUtc ||
                        suggestion.AcceptedAtUtc > upperBound)
                    {
                        failures.Add($"建议 {suggestionFact.DemandSourceReference} 的接受时间非单调或越出历史窗口。");
                    }
                }
                else if (suggestion.Status != PlanningSuggestionStatus.Open)
                {
                    failures.Add($"预测建议 {suggestionFact.DemandSourceReference} 应为待处理态。");
                }
            }
        }

        // 已接受建议的下游工单号必须与共享公式一致：SO-2026-x 的建议 → WO-2026-x。
        foreach (var fact in mrpFacts.SelectMany(run => run.Suggestions).Where(x => x.IsAccepted))
        {
            var index = ParseOrderIndex(fact.DemandSourceReference);
            if (index is null || !string.Equals(WorldHistorySpec.WorkOrderNo(index.Value), fact.DownstreamDocumentId, StringComparison.Ordinal))
            {
                failures.Add($"建议 {fact.DemandSourceReference} 的下游工单号 {fact.DownstreamDocumentId} 不符合共享号段公式。");
            }
        }
    }

    private static void CheckIsolation(
        IReadOnlyList<Domain.AggregatesModel.DemandSourceAggregate.DemandSource> demands,
        IReadOnlyList<PlanningSuggestion> suggestions,
        List<string> failures)
    {
        var references = demands.Select(x => x.SourceReference)
            .Concat(suggestions.SelectMany(x => x.PeggingLinks).Select(x => x.DemandSourceReference))
            .Concat(suggestions.Select(x => x.AcceptedDownstreamDocumentId).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!));
        foreach (var reference in references)
        {
            foreach (var infix in ReservedInfixes)
            {
                if (reference.Contains(infix, StringComparison.Ordinal))
                {
                    failures.Add($"世界观引用 {reference} 撞入保留号段 {infix}。");
                }
            }
        }
    }

    private static int? ParseOrderIndex(string salesOrderNo)
    {
        const string prefix = "SO-2026-";
        if (!salesOrderNo.StartsWith(prefix, StringComparison.Ordinal))
        {
            return null;
        }

        return int.TryParse(salesOrderNo[prefix.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out var index)
            ? index
            : null;
    }
}

/// <summary>计划域一致性校验通过后的对账摘要。</summary>
public sealed record WorldHistoryPlanningValidationReport(
    int DemandSourcesChecked,
    int MpsBucketsChecked,
    int MrpRunsChecked,
    int PlanningSuggestionsChecked,
    int AcceptedSuggestionsChecked,
    IReadOnlyList<string> Sample);

/// <summary>世界观一致性校验失败（fail-closed，中文累积原因）。</summary>
public sealed class WorldHistoryConsistencyException : InvalidOperationException
{
    public WorldHistoryConsistencyException(IReadOnlyList<string> failures)
        : base(BuildMessage(failures))
    {
        Failures = failures;
    }

    public WorldHistoryConsistencyException()
        : base("World-history consistency validation failed.")
    {
        Failures = [];
    }

    public WorldHistoryConsistencyException(string message)
        : base(message)
    {
        Failures = [message];
    }

    public WorldHistoryConsistencyException(string message, Exception innerException)
        : base(message, innerException)
    {
        Failures = [message];
    }

    public IReadOnlyList<string> Failures { get; }

    private static string BuildMessage(IReadOnlyList<string> failures) =>
        $"世界观计划域历史一致性校验失败（{failures.Count} 条）：{Environment.NewLine}{string.Join(Environment.NewLine, failures.Take(20))}";
}
