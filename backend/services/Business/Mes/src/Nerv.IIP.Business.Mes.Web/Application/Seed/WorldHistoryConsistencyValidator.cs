using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;
using Nerv.IIP.Business.Mes.Infrastructure;
using System.Globalization;
using System.Text;

namespace Nerv.IIP.Business.Mes.Web.Application.Seed;

/// <summary>
/// 《工厂世界观设定集》§7 一致性校验器的 **MES 侧**。
///
/// 覆盖：工单–工序任务–报工–完工入库的数量链对账、工单状态与销售订单阶段一致、
/// 报工时间落在工序窗口内且全链单调、工序任务数在 6–8 之间、齐套快照齐全。
/// **fail-closed**：任何一条不成立即抛 <see cref="WorldHistoryConsistencyException"/>。
///
/// 跨服务的「订单 ↔ 工单」对账不在这里做（MES 看不到 ERP 的库）：
/// 两侧的配对由 <see cref="WorldHistorySpec"/> 的确定性与双侧黄金向量测试保证，
/// 端到端的跨库抽样核对由 <c>scripts/verify-world-history.ps1</c> 承担。
/// </summary>
public sealed class WorldHistoryConsistencyValidator(ApplicationDbContext dbContext)
{
    public const int SampleSize = 20;

    private const decimal QuantityTolerance = 0.005m;

    public async Task<WorldHistoryMesValidationReport> ValidateAsync(
        string organizationId,
        string environmentId,
        DateOnly asOfDate,
        double scale,
        CancellationToken cancellationToken = default)
    {
        var plans = WorldHistorySpec.BuildOrderPlans(asOfDate, scale)
            .Where(plan => plan.HasWorkOrder)
            .ToDictionary(plan => plan.WorkOrderNo, StringComparer.Ordinal);
        var failures = new List<string>();

        var workOrders = await LoadWorkOrdersAsync(organizationId, environmentId, cancellationToken);
        var taskCounts = await LoadTaskCountsAsync(organizationId, environmentId, cancellationToken);
        var reports = await LoadReportTotalsAsync(organizationId, environmentId, cancellationToken);
        var receipts = await LoadReceiptsAsync(organizationId, environmentId, cancellationToken);
        var kitting = await LoadKittingCountsAsync(organizationId, environmentId, cancellationToken);

        var lowerBound = new DateTimeOffset(WorldHistoryCalendar.GoLiveDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).AddDays(-1);
        var upperBound = new DateTimeOffset(asOfDate.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero).AddDays(1);

        CheckPopulation(plans, workOrders, failures);

        foreach (var (workOrderId, workOrder) in workOrders)
        {
            var isRework = workOrderId.StartsWith("WO-2026-R", StringComparison.Ordinal);
            if (!isRework && !plans.TryGetValue(workOrderId, out _))
            {
                failures.Add($"库内工单 {workOrderId} 不在本次计划内（号段被外部占用？）。");
                continue;
            }

            CheckWorkOrder(workOrderId, workOrder, taskCounts, reports, receipts, kitting, lowerBound, upperBound, failures);
        }

        if (failures.Count > 0)
        {
            throw new WorldHistoryConsistencyException(failures);
        }

        return new WorldHistoryMesValidationReport(
            WorkOrdersChecked: workOrders.Count,
            OperationTasksChecked: taskCounts.Values.Sum(x => x.Total),
            ProductionReportsChecked: reports.Values.Sum(x => x.ReportCount),
            FinishedGoodsReceiptsChecked: receipts.Count,
            Sample: BuildSample(workOrders, taskCounts, reports, receipts));
    }

    #region 校验项

    private static void CheckPopulation(
        Dictionary<string, WorldHistoryOrderPlan> plans,
        Dictionary<string, WorkOrderProjection> workOrders,
        List<string> failures)
    {
        var missing = plans.Keys.Where(workOrderNo => !workOrders.ContainsKey(workOrderNo)).Take(5).ToArray();
        foreach (var workOrderNo in missing)
        {
            failures.Add($"计划中的工单 {workOrderNo} 未落库。");
        }
    }

    private static void CheckWorkOrder(
        string workOrderId,
        WorkOrderProjection workOrder,
        Dictionary<string, TaskCounts> taskCounts,
        Dictionary<string, ReportTotals> reports,
        Dictionary<string, ReceiptProjection> receipts,
        Dictionary<string, int> kitting,
        DateTimeOffset lowerBound,
        DateTimeOffset upperBound,
        List<string> failures)
    {
        if (workOrder.CreatedAtUtc < lowerBound || workOrder.CreatedAtUtc > upperBound)
        {
            failures.Add($"{workOrderId} 创建时间 {workOrder.CreatedAtUtc:O} 落在历史区间之外。");
        }

        if (!taskCounts.TryGetValue(workOrderId, out var tasks))
        {
            failures.Add($"{workOrderId} 没有任何工序任务。");
            return;
        }

        if (tasks.Total is < 6 or > 8)
        {
            failures.Add($"{workOrderId} 工序任务数 {tasks.Total} 超出设定集 §7 的 6–8 道。");
        }

        if (tasks.DispatchGaps > 0)
        {
            failures.Add($"{workOrderId} 有 {tasks.DispatchGaps} 道已开工/完工工序缺排程或派工痕迹（排程时间/方案、派工人及姓名、设备绑定必须齐全）。");
        }

        if (!kitting.TryGetValue(workOrderId, out var kittingCount) || kittingCount == 0)
        {
            failures.Add($"{workOrderId} 缺少齐套需求快照。");
        }

        reports.TryGetValue(workOrderId, out var reported);
        var reportedGood = reported?.GoodQuantity ?? 0m;
        var reportedScrap = reported?.ScrapQuantity ?? 0m;

        // 数量链：工单累计完工/报废 == 报工累计完工/报废。
        if (Math.Abs(workOrder.CompletedQuantity - reportedGood) > QuantityTolerance ||
            Math.Abs(workOrder.ScrapQuantity - reportedScrap) > QuantityTolerance)
        {
            failures.Add(
                $"{workOrderId} 报工数量链不平：工单完工 {workOrder.CompletedQuantity}/报废 {workOrder.ScrapQuantity}，" +
                $"报工累计 {reportedGood}/{reportedScrap}。");
        }

        if (reported is not null && (reported.FirstReportedAtUtc < lowerBound || reported.LastReportedAtUtc > upperBound))
        {
            failures.Add($"{workOrderId} 报工时间越界：{reported.FirstReportedAtUtc:O} – {reported.LastReportedAtUtc:O}。");
        }

        if (reported is not null && reported.FirstReportedAtUtc < workOrder.CreatedAtUtc)
        {
            failures.Add($"{workOrderId} 首次报工早于工单创建时间。");
        }

        if (reported is not null && reported.ReportCount is < 1 or > 5)
        {
            failures.Add($"{workOrderId} 报工条数 {reported.ReportCount} 超出设定集 §7 的 2–5 条上限。");
        }

        switch (workOrder.Status)
        {
            case WorkOrder.ClosedStatus:
                CheckClosedWorkOrder(workOrderId, workOrder, tasks, receipts, reportedGood, upperBound, failures);
                break;

            case WorkOrder.StartedStatus:
                if (reportedGood <= 0m)
                {
                    failures.Add($"{workOrderId} 状态为 started 却没有任何报工。");
                }

                if (receipts.ContainsKey(workOrderId))
                {
                    failures.Add($"{workOrderId} 尚未完工却存在完工入库请求。");
                }

                break;

            case WorkOrder.ReleasedStatus:
                if (reportedGood > 0m || reportedScrap > 0m)
                {
                    failures.Add($"{workOrderId} 状态为 released 却已有报工。");
                }

                if (tasks.Completed > 0)
                {
                    failures.Add($"{workOrderId} 状态为 released 却已有完工工序。");
                }

                break;

            default:
                failures.Add($"{workOrderId} 出现未预期的历史状态 '{workOrder.Status}'。");
                break;
        }
    }

    private static void CheckClosedWorkOrder(
        string workOrderId,
        WorkOrderProjection workOrder,
        TaskCounts tasks,
        Dictionary<string, ReceiptProjection> receipts,
        decimal reportedGood,
        DateTimeOffset upperBound,
        List<string> failures)
    {
        if (tasks.Completed != tasks.Total)
        {
            failures.Add($"{workOrderId} 已关单，但 {tasks.Total} 道工序里只有 {tasks.Completed} 道完工。");
        }

        if (Math.Abs(workOrder.CompletedQuantity + workOrder.ScrapQuantity - workOrder.Quantity) > QuantityTolerance)
        {
            failures.Add(
                $"{workOrderId} 已关单但投料未做完：投料 {workOrder.Quantity}，完工 {workOrder.CompletedQuantity} + 报废 {workOrder.ScrapQuantity}。");
        }

        if (!receipts.TryGetValue(workOrderId, out var receipt))
        {
            failures.Add($"{workOrderId} 已关单却没有完工入库请求。");
            return;
        }

        if (!string.Equals(receipt.Status, "Posted", StringComparison.Ordinal))
        {
            failures.Add($"{workOrderId} 的完工入库请求状态为 '{receipt.Status}'，应为 Posted。");
        }

        // 入库量必须等于好品产出量——这是「工单 → 入库 → 发货」数量链的中间一环。
        if (Math.Abs(receipt.Quantity - reportedGood) > QuantityTolerance ||
            Math.Abs(receipt.PostedQuantity - reportedGood) > QuantityTolerance)
        {
            failures.Add(
                $"{workOrderId} 入库量不平：报工好品 {reportedGood}，请求 {receipt.Quantity}，已过账 {receipt.PostedQuantity}。");
        }

        if (workOrder.ClosedAtUtc is null)
        {
            failures.Add($"{workOrderId} 已关单却没有关单时间。");
        }
        else if (workOrder.ClosedAtUtc > upperBound)
        {
            failures.Add($"{workOrderId} 关单时间 {workOrder.ClosedAtUtc:O} 越过历史截止日。");
        }
    }

    #endregion

    #region 载入紧凑投影

    private async Task<Dictionary<string, WorkOrderProjection>> LoadWorkOrdersAsync(
        string organizationId, string environmentId, CancellationToken cancellationToken) =>
        (await dbContext.WorkOrders
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId &&
                x.WorkOrderIdValue.StartsWith("WO-2026-"))
            .Select(x => new WorkOrderProjection(
                x.WorkOrderIdValue,
                x.SkuId,
                x.Status,
                x.Quantity,
                x.CompletedQuantity,
                x.ScrapQuantity,
                x.CreatedAtUtc,
                x.ClosedAtUtc))
            .ToArrayAsync(cancellationToken))
        .ToDictionary(x => x.WorkOrderId, StringComparer.Ordinal);

    private async Task<Dictionary<string, TaskCounts>> LoadTaskCountsAsync(
        string organizationId, string environmentId, CancellationToken cancellationToken) =>
        (await dbContext.OperationTasks
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId &&
                x.WorkOrderId.StartsWith("WO-2026-"))
            .GroupBy(x => x.WorkOrderId)
            .Select(group => new TaskCounts(
                group.Key,
                group.Count(),
                group.Count(task => task.Status == Domain.AggregatesModel.OperationTaskAggregate.OperationTaskLifecycleStatus.Completed),
                group.Count(task =>
                    (task.Status == Domain.AggregatesModel.OperationTaskAggregate.OperationTaskLifecycleStatus.Completed ||
                        task.Status == Domain.AggregatesModel.OperationTaskAggregate.OperationTaskLifecycleStatus.InProgress) &&
                    (task.AssignedUserId == null || task.AssignedUserName == null ||
                        task.ScheduledAtUtc == null || task.DeviceAssetId == null || task.SchedulePlanId == null))))
            .ToArrayAsync(cancellationToken))
        .ToDictionary(x => x.WorkOrderId, StringComparer.Ordinal);

    private async Task<Dictionary<string, ReportTotals>> LoadReportTotalsAsync(
        string organizationId, string environmentId, CancellationToken cancellationToken) =>
        (await dbContext.ProductionReports
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId &&
                x.WorkOrderId.StartsWith("WO-2026-"))
            .GroupBy(x => x.WorkOrderId)
            .Select(group => new ReportTotals(
                group.Key,
                group.Count(),
                group.Sum(report => report.GoodQuantity),
                group.Sum(report => report.ScrapQuantity),
                group.Min(report => report.ReportedAtUtc),
                group.Max(report => report.ReportedAtUtc)))
            .ToArrayAsync(cancellationToken))
        .ToDictionary(x => x.WorkOrderId, StringComparer.Ordinal);

    private async Task<Dictionary<string, ReceiptProjection>> LoadReceiptsAsync(
        string organizationId, string environmentId, CancellationToken cancellationToken) =>
        (await dbContext.FinishedGoodsReceiptRequests
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId &&
                x.WorkOrderId.StartsWith("WO-2026-"))
            .Select(x => new ReceiptProjection(
                x.WorkOrderId,
                x.RequestNo,
                x.Status,
                x.Quantity,
                x.PostedQuantity,
                x.PostedAtUtc))
            .ToArrayAsync(cancellationToken))
        .ToDictionary(x => x.WorkOrderId, StringComparer.Ordinal);

    private async Task<Dictionary<string, int>> LoadKittingCountsAsync(
        string organizationId, string environmentId, CancellationToken cancellationToken) =>
        (await dbContext.MaterialRequirements
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId &&
                x.WorkOrderId.StartsWith("WO-2026-"))
            .GroupBy(x => x.WorkOrderId)
            .Select(group => new { WorkOrderId = group.Key, Count = group.Count() })
            .ToArrayAsync(cancellationToken))
        .ToDictionary(x => x.WorkOrderId, x => x.Count, StringComparer.Ordinal);

    #endregion

    private static IReadOnlyList<string> BuildSample(
        Dictionary<string, WorkOrderProjection> workOrders,
        Dictionary<string, TaskCounts> taskCounts,
        Dictionary<string, ReportTotals> reports,
        Dictionary<string, ReceiptProjection> receipts)
    {
        var ordered = workOrders.Values.OrderBy(x => x.WorkOrderId, StringComparer.Ordinal).ToArray();
        if (ordered.Length == 0)
        {
            return [];
        }

        var stride = Math.Max(1, ordered.Length / SampleSize);
        var sample = new List<string>(SampleSize);
        for (var index = 0; index < ordered.Length && sample.Count < SampleSize; index += stride)
        {
            var workOrder = ordered[index];
            var builder = new StringBuilder();
            builder.Append(CultureInfo.InvariantCulture, $"{workOrder.WorkOrderId} [{workOrder.Status}] {workOrder.SkuId}");
            builder.Append(CultureInfo.InvariantCulture, $" 投料={workOrder.Quantity:0.##} 完工={workOrder.CompletedQuantity:0.##} 报废={workOrder.ScrapQuantity:0.##}");
            builder.Append(CultureInfo.InvariantCulture, $" 创建={workOrder.CreatedAtUtc:yyyy-MM-dd HH:mm}Z");

            if (taskCounts.TryGetValue(workOrder.WorkOrderId, out var tasks))
            {
                builder.Append(CultureInfo.InvariantCulture, $" 工序={tasks.Completed}/{tasks.Total}");
            }

            if (reports.TryGetValue(workOrder.WorkOrderId, out var reported))
            {
                builder.Append(CultureInfo.InvariantCulture, $" 报工={reported.ReportCount}条 末次={reported.LastReportedAtUtc:yyyy-MM-dd HH:mm}Z");
            }

            if (receipts.TryGetValue(workOrder.WorkOrderId, out var receipt))
            {
                builder.Append(CultureInfo.InvariantCulture, $" → {receipt.RequestNo}({receipt.Status}) 入库={receipt.PostedQuantity:0.##}@{receipt.PostedAtUtc:yyyy-MM-dd HH:mm}Z");
            }

            sample.Add(builder.ToString());
        }

        return sample;
    }

    private sealed record WorkOrderProjection(
        string WorkOrderId,
        string SkuId,
        string Status,
        decimal Quantity,
        decimal CompletedQuantity,
        decimal ScrapQuantity,
        DateTimeOffset CreatedAtUtc,
        DateTimeOffset? ClosedAtUtc);

    private sealed record TaskCounts(string WorkOrderId, int Total, int Completed, int DispatchGaps);

    private sealed record ReportTotals(
        string WorkOrderId,
        int ReportCount,
        decimal GoodQuantity,
        decimal ScrapQuantity,
        DateTimeOffset FirstReportedAtUtc,
        DateTimeOffset LastReportedAtUtc);

    private sealed record ReceiptProjection(
        string WorkOrderId,
        string RequestNo,
        string Status,
        decimal Quantity,
        decimal PostedQuantity,
        DateTimeOffset? PostedAtUtc);
}

/// <summary>MES 侧一致性校验器的产出摘要。</summary>
public sealed record WorldHistoryMesValidationReport(
    int WorkOrdersChecked,
    int OperationTasksChecked,
    int ProductionReportsChecked,
    int FinishedGoodsReceiptsChecked,
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
        var builder = new StringBuilder("L1 背景历史一致性校验失败（MES），共 ");
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
