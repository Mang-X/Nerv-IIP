using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Quality.Infrastructure;
using System.Globalization;
using System.Text;

namespace Nerv.IIP.Business.Quality.Web.Application.Seed;

/// <summary>
/// 《工厂世界观设定集》§7 一致性校验器的 **质量域侧**（二期）。
///
/// 覆盖：任务–记录链完整、检验数 ≈ 报工数对账（本服务够得着的那一段）、不合格率与处置分布落在容差内、
/// 报废量不越过一期投料放大量、全链时间戳单调且不落在周日、关单引用与处置类型匹配、与固定演示事实隔离。
/// **fail-closed**：任何一条不成立即抛 <see cref="WorldHistoryConsistencyException"/>。
///
/// 跨服务的「工单 ↔ 检验 ↔ 库存」对账不在这里做（质量看不到 MES / Inventory 的库）：
/// 配对由 <see cref="WorldHistoryQualitySpec"/> 的确定性与各侧黄金向量测试保证，
/// 端到端抽样核对由 <c>scripts/verify-world-history.ps1</c> 承担。
/// </summary>
public sealed class WorldHistoryConsistencyValidator(ApplicationDbContext dbContext)
{
    public const int SampleSize = 20;

    /// <summary>分布类校验的相对容差：至少 ±3%，样本小时放宽到 3σ（与一期同口径）。</summary>
    public const double MinimumRelativeTolerance = 0.03;

    private const decimal QuantityTolerance = 0.005m;

    private static readonly string[] ReservedInfixes = ["-DEMO-", "-SCALE-"];

    public async Task<WorldHistoryQualityValidationReport> ValidateAsync(
        string organizationId,
        string environmentId,
        DateOnly asOfDate,
        double scale,
        CancellationToken cancellationToken = default)
    {
        var facts = WorldHistoryQualitySpec.BuildInspectionFacts(asOfDate, scale);
        var factByTriggerKey = facts.ToDictionary(fact => fact.TriggerIdempotencyKey, StringComparer.Ordinal);
        var workOrderFacts = WorldHistoryPhase2Spec.BuildWorkOrderFacts(asOfDate, scale);
        var workOrderQuantities = workOrderFacts
            .Where(fact => fact.HasFinalInspection)
            .ToDictionary(fact => fact.Plan.WorkOrderNo, fact => fact.Plan, StringComparer.Ordinal);
        var workOrderScrapTotal = workOrderFacts.Sum(fact => fact.Plan.ScrapQuantity);
        var failures = new List<string>();

        var tasks = await LoadTasksAsync(organizationId, environmentId, cancellationToken);
        var records = await LoadRecordsAsync(organizationId, environmentId, cancellationToken);
        var reports = await LoadNonconformanceReportsAsync(organizationId, environmentId, cancellationToken);

        var lowerBound = new DateTimeOffset(WorldHistoryCalendar.GoLiveDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var upperBound = new DateTimeOffset(asOfDate.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero);

        CheckPopulation(factByTriggerKey, tasks, failures);

        foreach (var task in tasks)
        {
            if (!factByTriggerKey.TryGetValue(task.TriggerIdempotencyKey, out var fact))
            {
                failures.Add($"库内检验任务 {task.SourceDocumentId} 不在本次计划内（号段被外部占用？）。");
                continue;
            }

            CheckTask(task, fact, records, reports, workOrderQuantities, lowerBound, upperBound, failures);
        }

        CheckDistributions(facts, tasks, reports, failures);
        CheckScrapBoundary(facts, reports, workOrderScrapTotal, failures);
        CheckIsolation(tasks, reports, failures);

        if (failures.Count > 0)
        {
            throw new WorldHistoryConsistencyException(failures);
        }

        var completedTasks = tasks.Count(x => string.Equals(x.Status, "completed", StringComparison.Ordinal));
        var pendingTasks = tasks.Count(x => string.Equals(x.Status, "pending", StringComparison.Ordinal));
        var pendingTasksUnassigned = tasks.Count(x =>
            string.Equals(x.Status, "pending", StringComparison.Ordinal)
            && x.AssignedUserId is null
            && x.AssignedTeamId is null);
        return new WorldHistoryQualityValidationReport(
            InspectionTasksChecked: tasks.Count,
            CompletedInspectionsChecked: completedTasks,
            InspectionRecordsChecked: records.Count,
            NonconformanceReportsChecked: reports.Count,
            NonconformingRate: completedTasks == 0 ? 0d : (double)reports.Count / completedTasks,
            PendingTasksPreassigned: pendingTasks - pendingTasksUnassigned,
            PendingTasksUnassigned: pendingTasksUnassigned,
            Sample: BuildSample(tasks, factByTriggerKey, records, reports));
    }

    #region 校验项

    private static void CheckPopulation(
        Dictionary<string, WorldHistoryInspectionFact> factByTriggerKey,
        IReadOnlyList<TaskProjection> tasks,
        List<string> failures)
    {
        var present = tasks.Select(x => x.TriggerIdempotencyKey).ToHashSet(StringComparer.Ordinal);
        foreach (var missing in factByTriggerKey.Keys.Where(key => !present.Contains(key)).Take(5))
        {
            failures.Add($"计划中的检验任务 {missing} 未落库。");
        }
    }

    private static void CheckTask(
        TaskProjection task,
        WorldHistoryInspectionFact fact,
        Dictionary<string, RecordProjection> records,
        Dictionary<string, ReportProjection> reports,
        Dictionary<string, WorldHistoryWorkOrderPlan> workOrderQuantities,
        DateTimeOffset lowerBound,
        DateTimeOffset upperBound,
        List<string> failures)
    {
        // 1) 任务状态 ↔ 检验记录：已完成必有记录，待检 / 检验中必无。
        records.TryGetValue(RecordKey(task.SourceDocumentId, 1), out var record);
        if (!string.Equals(task.Status, "completed", StringComparison.Ordinal))
        {
            if (task.HasInspectionRecord || record is not null)
            {
                failures.Add($"{task.SourceDocumentId} 检验任务状态为 {task.Status} 却已有检验记录。");
            }

            return;
        }

        if (!task.HasInspectionRecord || record is null)
        {
            failures.Add($"{task.SourceDocumentId} 检验任务已完成却没有检验记录。");
            return;
        }

        // 2) 质量 ↔ 报工数量对账：工序检验的受检量必须等于一期工单的投料量。
        if (string.Equals(task.SourceType, "operation", StringComparison.Ordinal))
        {
            if (!workOrderQuantities.TryGetValue(task.SourceDocumentId, out var workOrderPlan))
            {
                failures.Add($"{task.SourceDocumentId} 的工序检验挂在一张不存在终检工序的工单上。");
            }
            else if (Math.Abs(task.Quantity - workOrderPlan.WorkOrderQuantity) > QuantityTolerance ||
                Math.Abs(record.InspectedQuantity - workOrderPlan.WorkOrderQuantity) > QuantityTolerance)
            {
                failures.Add(
                    $"{task.SourceDocumentId} 检验数量与报工投料量不平：工单投料 {workOrderPlan.WorkOrderQuantity}，" +
                    $"任务 {task.Quantity}，记录 {record.InspectedQuantity}。");
            }
        }

        if (!string.Equals(record.Result, fact.RecordResult, StringComparison.Ordinal))
        {
            failures.Add($"{task.SourceDocumentId} 检验判定为 '{record.Result}'，与计划的 '{fact.RecordResult}' 不符。");
        }

        // 5) 时间戳：落在历史区间内、链上单调、不落在周日。
        CheckTimestamps(task, record, failures, lowerBound, upperBound);

        if (!fact.HasNonconformance)
        {
            if (!string.IsNullOrWhiteSpace(record.NonconformanceReportId))
            {
                failures.Add($"{task.SourceDocumentId} 判定合格却挂了 NCR。");
            }

            return;
        }

        if (!reports.TryGetValue(fact.NcrCode!, out var report))
        {
            failures.Add($"{task.SourceDocumentId} 判定不合格却没有 {fact.NcrCode}。");
            return;
        }

        CheckNonconformanceReport(task, record, fact, report, lowerBound, upperBound, failures);
    }

    private static void CheckTimestamps(
        TaskProjection task,
        RecordProjection record,
        List<string> failures,
        DateTimeOffset lowerBound,
        DateTimeOffset upperBound)
    {
        var moments = new (string Label, DateTimeOffset Value)[]
        {
            ("创建", task.CreatedAtUtc),
            ("领取", task.StartedAtUtc!.Value),
            ("完成", task.CompletedAtUtc!.Value),
            ("记录", new DateTimeOffset(record.CreatedAtUtc, TimeSpan.Zero)),
        };

        for (var index = 0; index < moments.Length; index++)
        {
            var (label, value) = moments[index];
            if (value < lowerBound || value > upperBound)
            {
                failures.Add($"{task.SourceDocumentId} {label}时间 {value:O} 落在历史区间之外。");
            }

            if (!WorldHistoryCalendar.IsWorkingDay(DateOnly.FromDateTime(value.UtcDateTime)))
            {
                failures.Add($"{task.SourceDocumentId} {label}时间 {value:O} 落在周日（停产保养日）。");
            }

            if (index > 0 && value < moments[index - 1].Value)
            {
                failures.Add($"{task.SourceDocumentId} {label}时间早于{moments[index - 1].Label}时间。");
            }
        }
    }

    private static void CheckNonconformanceReport(
        TaskProjection task,
        RecordProjection record,
        WorldHistoryInspectionFact fact,
        ReportProjection report,
        DateTimeOffset lowerBound,
        DateTimeOffset upperBound,
        List<string> failures)
    {
        if (string.IsNullOrWhiteSpace(record.NonconformanceReportId))
        {
            failures.Add($"{task.SourceDocumentId} 的检验记录没有回链 {fact.NcrCode}。");
        }

        if (!string.Equals(report.Status, "closed", StringComparison.Ordinal))
        {
            failures.Add($"{fact.NcrCode} 状态为 '{report.Status}'，历史 NCR 必须已关单。");
        }
        else if (string.IsNullOrWhiteSpace(report.CloseReason))
        {
            // 界面把关闭原因当必填：已关单却无原因 = 假数据穿帮。
            failures.Add($"{fact.NcrCode} 已关单却没有关闭原因。");
        }

        if (!string.Equals(report.DispositionType, fact.DispositionType, StringComparison.Ordinal))
        {
            failures.Add($"{fact.NcrCode} 处置类型为 '{report.DispositionType}'，与计划的 '{fact.DispositionType}' 不符。");
        }

        if (Math.Abs(report.DefectQuantity - fact.DefectQuantity) > QuantityTolerance)
        {
            failures.Add($"{fact.NcrCode} 不良数量 {report.DefectQuantity} 与计划的 {fact.DefectQuantity} 不符。");
        }

        // 6) 关单引用必须与处置类型匹配；让步 NCR 绝不能带报废流水 id。
        switch (fact.Disposition)
        {
            case WorldHistoryInspectionDisposition.Rework when string.IsNullOrWhiteSpace(report.ReworkWorkOrderId):
                failures.Add($"{fact.NcrCode} 判定返工却没有补产工单引用。");
                break;

            case WorldHistoryInspectionDisposition.Scrap when string.IsNullOrWhiteSpace(report.ScrapMovementId):
                failures.Add($"{fact.NcrCode} 判定报废却没有库存报废流水引用。");
                break;

            case WorldHistoryInspectionDisposition.ConditionalRelease when !string.IsNullOrWhiteSpace(report.ScrapMovementId):
                failures.Add($"{fact.NcrCode} 判定让步接收却挂了报废流水 {report.ScrapMovementId}。");
                break;

            default:
                break;
        }

        if (report.MrbReviewCount == 0 || report.ApprovedMrbReviewCount != report.MrbReviewCount)
        {
            failures.Add($"{fact.NcrCode} 缺少全部通过的 MRB 评审（{report.ApprovedMrbReviewCount}/{report.MrbReviewCount}）。");
        }

        // 全链单调：完成检验 ≤ NCR 开单 ≤ MRB 评审 ≤ 关单。
        var openedAtUtc = new DateTimeOffset(report.CreatedAtUtc, TimeSpan.Zero);
        var closedAtUtc = new DateTimeOffset(report.UpdatedAtUtc, TimeSpan.Zero);
        if (openedAtUtc < task.CompletedAtUtc!.Value)
        {
            failures.Add($"{fact.NcrCode} 开单时间早于检验完成时间。");
        }

        if (report.LastMrbReviewedAtUtc is { } reviewedAtUtc && (reviewedAtUtc < openedAtUtc || reviewedAtUtc > closedAtUtc))
        {
            failures.Add($"{fact.NcrCode} MRB 评审时间 {reviewedAtUtc:O} 不在「开单–关单」区间内。");
        }

        if (closedAtUtc < openedAtUtc)
        {
            failures.Add($"{fact.NcrCode} 关单时间早于开单时间。");
        }

        foreach (var (label, value) in new[] { ("开单", openedAtUtc), ("关单", closedAtUtc) })
        {
            if (value < lowerBound || value > upperBound)
            {
                failures.Add($"{fact.NcrCode} {label}时间 {value:O} 落在历史区间之外。");
            }

            if (!WorldHistoryCalendar.IsWorkingDay(DateOnly.FromDateTime(value.UtcDateTime)))
            {
                failures.Add($"{fact.NcrCode} {label}时间 {value:O} 落在周日（停产保养日）。");
            }
        }
    }

    /// <summary>3) 不合格率与处置分布。样本越小容差越宽（3σ），避免小 <c>Scale</c> 快速验证被统计抖动卡死。</summary>
    private static void CheckDistributions(
        IReadOnlyList<WorldHistoryInspectionFact> facts,
        IReadOnlyList<TaskProjection> tasks,
        Dictionary<string, ReportProjection> reports,
        List<string> failures)
    {
        var completed = tasks.Count(x => string.Equals(x.Status, "completed", StringComparison.Ordinal));
        if (completed == 0)
        {
            failures.Add("本次历史没有任何已完成的检验，无法核对不合格率。");
            return;
        }

        var expectedCodes = facts.Where(x => x.HasNonconformance).Select(x => x.NcrCode!).ToHashSet(StringComparer.Ordinal);
        var seededReports = reports.Values.Where(x => expectedCodes.Contains(x.NcrCode)).ToArray();

        if (!WithinTolerance(seededReports.Length, WorldHistoryQualitySpec.NonconformingRate, completed))
        {
            failures.Add(
                $"不合格率偏离设定集口径：{seededReports.Length}/{completed} = " +
                $"{(double)seededReports.Length / completed:P2}，目标 {WorldHistoryQualitySpec.NonconformingRate:P2}。");
        }

        if (seededReports.Length == 0)
        {
            return;
        }

        CheckDispositionShare(seededReports, "rework", WorldHistoryQualitySpec.ReworkDispositionShare, failures);
        CheckDispositionShare(seededReports, "conditional-release", WorldHistoryQualitySpec.ConditionalReleaseDispositionShare, failures);
        CheckDispositionShare(seededReports, "scrap", WorldHistoryQualitySpec.ScrapDispositionShare, failures);
    }

    private static void CheckDispositionShare(
        IReadOnlyList<ReportProjection> reports,
        string dispositionType,
        int targetPercent,
        List<string> failures)
    {
        var actual = reports.Count(x => string.Equals(x.DispositionType, dispositionType, StringComparison.Ordinal));
        var share = targetPercent / 100d;
        if (!WithinTolerance(actual, share, reports.Count))
        {
            failures.Add(
                $"处置分布偏离设定集口径：{dispositionType} {actual}/{reports.Count} = " +
                $"{(double)actual / reports.Count:P1}，目标 {share:P0}。");
        }
    }

    /// <summary>4) 报废量必须落在一期工单投料放大量之内——这是「质量报废 ↔ MES 投料」对账的边界。</summary>
    private static void CheckScrapBoundary(
        IReadOnlyList<WorldHistoryInspectionFact> facts,
        Dictionary<string, ReportProjection> reports,
        decimal workOrderScrapTotal,
        List<string> failures)
    {
        var expectedScrapCodes = facts
            .Where(x => x.Disposition == WorldHistoryInspectionDisposition.Scrap)
            .Select(x => x.NcrCode!)
            .ToHashSet(StringComparer.Ordinal);
        var scrapped = reports.Values
            .Where(x => expectedScrapCodes.Contains(x.NcrCode))
            .Sum(x => x.DefectQuantity);

        if (expectedScrapCodes.Count > 0 && scrapped <= 0m)
        {
            failures.Add("本次历史应有报废处置，但库内报废数量合计为 0。");
        }

        if (scrapped > workOrderScrapTotal)
        {
            failures.Add($"报废处置数量合计 {scrapped} 越过一期工单投料放大量合计 {workOrderScrapTotal}。");
        }
    }

    /// <summary>7) 与 MAN-519 固定演示事实、千单规模块完全隔离。</summary>
    private static void CheckIsolation(
        IReadOnlyList<TaskProjection> tasks,
        Dictionary<string, ReportProjection> reports,
        List<string> failures)
    {
        foreach (var infix in ReservedInfixes)
        {
            var task = tasks.FirstOrDefault(x => x.SourceDocumentId.Contains(infix, StringComparison.Ordinal));
            if (task is not null)
            {
                failures.Add($"检验任务源单据 {task.SourceDocumentId} 落进了保留号段 '{infix}'。");
            }

            var report = reports.Values.FirstOrDefault(x =>
                x.NcrCode.Contains(infix, StringComparison.Ordinal) ||
                x.SourceDocumentId.Contains(infix, StringComparison.Ordinal));
            if (report is not null)
            {
                failures.Add($"NCR {report.NcrCode}（源单据 {report.SourceDocumentId}）落进了保留号段 '{infix}'。");
            }
        }
    }

    /// <summary>相对容差：至少 ±3%，样本小的时候放宽到 3σ。</summary>
    public static bool WithinTolerance(int actual, double expectedShare, int total)
    {
        var expected = total * expectedShare;
        if (expected <= 0d)
        {
            return actual == 0;
        }

        var sigma = Math.Sqrt(total * expectedShare * (1d - expectedShare));
        var allowed = Math.Max(expected * MinimumRelativeTolerance, 3d * sigma);
        return Math.Abs(actual - expected) <= allowed;
    }

    #endregion

    #region 载入紧凑投影

    private static string RecordKey(string sourceDocumentId, int attemptNumber) =>
        $"{sourceDocumentId}#{attemptNumber.ToString(CultureInfo.InvariantCulture)}";

    private async Task<IReadOnlyList<TaskProjection>> LoadTasksAsync(
        string organizationId, string environmentId, CancellationToken cancellationToken) =>
        await dbContext.InspectionTasks
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId &&
                x.TriggerIdempotencyKey.StartsWith(WorldHistoryQualitySpec.TriggerKeyPrefix))
            .Select(x => new TaskProjection(
                x.TriggerIdempotencyKey,
                x.SourceType,
                x.SourceService,
                x.SourceDocumentId,
                x.SkuCode,
                x.Quantity,
                x.Status,
                x.AssignedUserId,
                x.AssignedTeamId,
                x.InspectionRecordId != null,
                x.CreatedAtUtc,
                x.StartedAtUtc,
                x.CompletedAtUtc))
            .ToArrayAsync(cancellationToken);

    private async Task<Dictionary<string, RecordProjection>> LoadRecordsAsync(
        string organizationId, string environmentId, CancellationToken cancellationToken) =>
        (await dbContext.InspectionRecords
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId)
            .Select(x => new RecordProjection(
                x.SourceDocumentId,
                x.SourceType,
                x.SourceService,
                x.AttemptNumber,
                x.InspectedQuantity,
                x.Result,
                x.NonconformanceReportId,
                x.LocationCode,
                x.CreatedAtUtc))
            .ToArrayAsync(cancellationToken))
        .ToDictionary(x => RecordKey(x.SourceDocumentId, x.AttemptNumber), StringComparer.Ordinal);

    private async Task<Dictionary<string, ReportProjection>> LoadNonconformanceReportsAsync(
        string organizationId, string environmentId, CancellationToken cancellationToken)
    {
        // NCR 只有约不合格率量级的条数，直接连 MRB 评审一起加载，换取校验逻辑的可读性。
        var reports = await dbContext.NonconformanceReports
            .AsNoTracking()
            .Include(x => x.MrbReviews)
            .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId &&
                x.NcrCode.StartsWith("NCR-2026-"))
            .ToArrayAsync(cancellationToken);

        return reports.ToDictionary(
            x => x.NcrCode,
            x => new ReportProjection(
                x.NcrCode,
                x.SourceType,
                x.SourceDocumentId,
                x.SkuCode,
                x.DefectQuantity,
                x.Status,
                x.DispositionType,
                x.ReworkWorkOrderId,
                x.ScrapMovementId,
                x.ReturnDocumentId,
                x.LocationCode,
                x.MrbReviews.Count,
                x.MrbReviews.Count(review => string.Equals(review.Decision, "approved", StringComparison.Ordinal)),
                x.MrbReviews.Count == 0 ? null : x.MrbReviews.Max(review => review.ReviewedAtUtc),
                x.CreatedAtUtc,
                x.UpdatedAtUtc,
                x.CloseReason),
            StringComparer.Ordinal);
    }

    #endregion

    private static IReadOnlyList<string> BuildSample(
        IReadOnlyList<TaskProjection> tasks,
        Dictionary<string, WorldHistoryInspectionFact> factByTriggerKey,
        Dictionary<string, RecordProjection> records,
        Dictionary<string, ReportProjection> reports)
    {
        var ordered = tasks
            .OrderBy(x => x.SourceType, StringComparer.Ordinal)
            .ThenBy(x => x.SourceDocumentId, StringComparer.Ordinal)
            .ToArray();
        if (ordered.Length == 0)
        {
            return [];
        }

        var stride = Math.Max(1, ordered.Length / SampleSize);
        var sample = new List<string>(SampleSize);
        for (var index = 0; index < ordered.Length && sample.Count < SampleSize; index += stride)
        {
            var task = ordered[index];
            var builder = new StringBuilder();
            builder.Append(CultureInfo.InvariantCulture, $"{task.SourceType}/{task.SourceService} {task.SourceDocumentId} [{task.Status}] {task.SkuCode}");
            builder.Append(CultureInfo.InvariantCulture, $" 受检={task.Quantity:0.##} 创建={task.CreatedAtUtc:yyyy-MM-dd HH:mm}Z");

            if (records.TryGetValue(RecordKey(task.SourceDocumentId, 1), out var record))
            {
                builder.Append(CultureInfo.InvariantCulture, $" → 判定={record.Result}");
            }

            if (records.ContainsKey(RecordKey(task.SourceDocumentId, 2)))
            {
                builder.Append(" → 复检合格");
            }

            if (factByTriggerKey.TryGetValue(task.TriggerIdempotencyKey, out var fact) &&
                fact.NcrCode is not null &&
                reports.TryGetValue(fact.NcrCode, out var report))
            {
                builder.Append(CultureInfo.InvariantCulture,
                    $" → {report.NcrCode}({report.DispositionType}/{report.Status}) 不良={report.DefectQuantity:0.##}");
                builder.Append(CultureInfo.InvariantCulture,
                    $" 引用={report.ReworkWorkOrderId ?? report.ScrapMovementId ?? "让步放行"}");
                builder.Append(CultureInfo.InvariantCulture, $" 关单={report.UpdatedAtUtc:yyyy-MM-dd HH:mm}Z");
            }

            sample.Add(builder.ToString());
        }

        return sample;
    }

    private sealed record TaskProjection(
        string TriggerIdempotencyKey,
        string SourceType,
        string SourceService,
        string SourceDocumentId,
        string SkuCode,
        decimal Quantity,
        string Status,
        string? AssignedUserId,
        string? AssignedTeamId,
        bool HasInspectionRecord,
        DateTimeOffset CreatedAtUtc,
        DateTimeOffset? StartedAtUtc,
        DateTimeOffset? CompletedAtUtc);

    private sealed record RecordProjection(
        string SourceDocumentId,
        string SourceType,
        string SourceService,
        int AttemptNumber,
        decimal InspectedQuantity,
        string Result,
        string? NonconformanceReportId,
        string? LocationCode,
        DateTime CreatedAtUtc);

    private sealed record ReportProjection(
        string NcrCode,
        string SourceType,
        string SourceDocumentId,
        string SkuCode,
        decimal DefectQuantity,
        string Status,
        string? DispositionType,
        string? ReworkWorkOrderId,
        string? ScrapMovementId,
        string? ReturnDocumentId,
        string? LocationCode,
        int MrbReviewCount,
        int ApprovedMrbReviewCount,
        DateTimeOffset? LastMrbReviewedAtUtc,
        DateTime CreatedAtUtc,
        DateTime UpdatedAtUtc,
        string? CloseReason);
}

/// <summary>质量域侧一致性校验器的产出摘要。</summary>
public sealed record WorldHistoryQualityValidationReport(
    int InspectionTasksChecked,
    int CompletedInspectionsChecked,
    int InspectionRecordsChecked,
    int NonconformanceReportsChecked,
    double NonconformingRate,
    int PendingTasksPreassigned,
    int PendingTasksUnassigned,
    IReadOnlyList<string> Sample);

/// <summary>一致性校验失败。抛出即代表 seed 失败（fail-closed）。</summary>
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

    private static string BuildMessage(IReadOnlyList<string> failures)
    {
        ArgumentNullException.ThrowIfNull(failures);
        var builder = new StringBuilder("L1 背景历史一致性校验失败（质量域），共 ");
        builder.Append(failures.Count).AppendLine(" 条：");
        foreach (var failure in failures.Take(25))
        {
            builder.Append("  - ").AppendLine(failure);
        }

        if (failures.Count > 25)
        {
            builder.Append("  … 另有 ").Append(failures.Count - 25).AppendLine(" 条未列出。");
        }

        return builder.ToString();
    }
}
