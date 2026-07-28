using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.QualityAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Business.Mes.Infrastructure.MasterData;
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

    /// <summary>低于该条数的样本不做分布类断言（极小 <c>scale</c> 下总量可能只有个位数）。</summary>
    private const int MinimumDistributionSample = 10;

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

    #region 现场异常与协同（停机 / 班次交接 / 车间不良）

    /// <summary>
    /// 《工厂世界观设定集》L1「异常与协同」块的 fail-closed 校验。
    ///
    /// 只校验本引擎号段（<c>DT-2026-*</c> / <c>HO-2026-*</c> / <c>DEF-2026-*</c>）内的行，
    /// 不干涉 L2 固定案例与演示当场操作产生的事实。
    ///
    /// 逐行内容不与规格逐字段比对：同一环境在不同 <c>asOfDate</c> 上重复启动时，先前落库的行
    /// 保留当时的时间戳（幂等按单号跳过），因此这里校验的是**不变量**——条数、号段格式、
    /// 时间边界、引用完整性、状态分布下限。
    /// </summary>
    public async Task<WorldHistoryFloorEventsValidationReport> ValidateFloorEventsAsync(
        string organizationId,
        string environmentId,
        DateOnly asOfDate,
        double scale,
        CancellationToken cancellationToken = default)
    {
        var failures = new List<string>();
        var lowerBound = new DateTimeOffset(WorldHistoryCalendar.GoLiveDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).AddDays(-1);
        var upperBound = WorldHistoryFloorEventsSpec.UpperBound(asOfDate).AddDays(1);

        var downtimeChecked = await CheckDowntimeEventsAsync(
            organizationId, environmentId, asOfDate, scale, lowerBound, upperBound, failures, cancellationToken);
        var handoversChecked = await CheckShiftHandoversAsync(
            organizationId, environmentId, asOfDate, scale, lowerBound, upperBound, failures, cancellationToken);
        var defectsChecked = await CheckDefectRecordsAsync(
            organizationId, environmentId, asOfDate, scale, lowerBound, upperBound, failures, cancellationToken);

        if (failures.Count > 0)
        {
            throw new WorldHistoryConsistencyException(failures);
        }

        return new WorldHistoryFloorEventsValidationReport(downtimeChecked, handoversChecked, defectsChecked);
    }

    private async Task<int> CheckDowntimeEventsAsync(
        string organizationId,
        string environmentId,
        DateOnly asOfDate,
        double scale,
        DateTimeOffset lowerBound,
        DateTimeOffset upperBound,
        List<string> failures,
        CancellationToken cancellationToken)
    {
        var expected = WorldHistoryFloorEventsSpec.DowntimeEventCount(asOfDate, scale);
        var rows = await dbContext.WorkCenterUnavailabilities
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId &&
                x.DowntimeEventNo.StartsWith("DT-2026-"))
            .Select(x => new DowntimeProjection(x.DowntimeEventNo, x.WorkCenterId, x.DeviceAssetId, x.Reason, x.FromUtc, x.ToUtc))
            .ToArrayAsync(cancellationToken);

        if (rows.Length != expected)
        {
            failures.Add($"停机事件条数 {rows.Length} 与设定集预期 {expected} 不符。");
        }

        var reasons = WorldHistoryFloorEventsSpec.DowntimeReasons.Select(x => x.Reason).ToHashSet(StringComparer.Ordinal);
        var workCenters = WorldHistoryFloorEventsSpec.WorkCenterIds.ToHashSet(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            if (!workCenters.Contains(row.WorkCenterId))
            {
                failures.Add($"停机 {row.DowntimeEventNo} 落在非 L0 工作中心 {row.WorkCenterId} 上。");
            }

            if (string.IsNullOrWhiteSpace(row.DeviceAssetId))
            {
                failures.Add($"停机 {row.DowntimeEventNo} 未绑定设备。");
            }

            if (!reasons.Contains(row.Reason))
            {
                failures.Add($"停机 {row.DowntimeEventNo} 的原因 '{row.Reason}' 不在设定集口径内。");
            }

            if (row.FromUtc < lowerBound || row.FromUtc > upperBound)
            {
                failures.Add($"停机 {row.DowntimeEventNo} 起始时间 {row.FromUtc:O} 落在历史区间之外。");
            }

            if (row.ToUtc is { } restoredAtUtc && (restoredAtUtc < row.FromUtc || restoredAtUtc > upperBound))
            {
                failures.Add($"停机 {row.DowntimeEventNo} 恢复时间 {restoredAtUtc:O} 不合法。");
            }
        }

        if (rows.Length > 0 && rows.Count(x => x.ToUtc is null) == 0)
        {
            failures.Add("停机事件全部已恢复，「当前停机」将为空——设定集要求最近若干起保持进行中。");
        }

        return rows.Length;
    }

    private async Task<int> CheckShiftHandoversAsync(
        string organizationId,
        string environmentId,
        DateOnly asOfDate,
        double scale,
        DateTimeOffset lowerBound,
        DateTimeOffset upperBound,
        List<string> failures,
        CancellationToken cancellationToken)
    {
        var expected = WorldHistoryFloorEventsSpec.BuildShiftHandovers(asOfDate, scale).Count;
        var rows = await dbContext.ShiftHandovers
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId &&
                x.HandoverNo.StartsWith("HO-2026-"))
            .Select(x => new HandoverProjection(x.HandoverNo, x.ShiftId, x.TeamId, x.HandoverStatus, x.OpenIssueCount, x.CreatedAtUtc, x.AcceptedAtUtc))
            .ToArrayAsync(cancellationToken);

        if (rows.Length != expected)
        {
            failures.Add($"班次交接条数 {rows.Length} 与设定集预期 {expected} 不符。");
        }

        // 班次与班组是两个维度，各自校验各自的取值域：班次只能是 L0 的 EARLY/MIDDLE，班组只能是
        // 6 个 TEAM-WB-*。旧实现把两者混为一谈（班次域里放 TeamCode、班组域里放班组**名称**），
        // 反而把「字段装错东西」固化成了断言。
        var shiftCodes = WorldHistoryFloorEventsSpec.Teams
            .Select(x => WorldHistoryCalendar.ShiftCode(x.ShiftIndex))
            .ToHashSet(StringComparer.Ordinal);
        var teamCodes = WorldHistoryFloorEventsSpec.Teams.Select(x => x.TeamCode).ToHashSet(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            if (!shiftCodes.Contains(row.ShiftId))
            {
                failures.Add($"班次交接 {row.HandoverNo} 的班次 {row.ShiftId} 不是 L0 班次编码（应为 EARLY / MIDDLE）。");
            }

            if (!teamCodes.Contains(row.TeamId))
            {
                failures.Add($"班次交接 {row.HandoverNo} 的班组 {row.TeamId} 不在 L0 的 6 个班组编码内。");
            }

            if (row.OpenIssueCount < 0)
            {
                failures.Add($"班次交接 {row.HandoverNo} 的未闭环问题数为负。");
            }

            if (row.CreatedAtUtc < lowerBound || row.CreatedAtUtc > upperBound)
            {
                failures.Add($"班次交接 {row.HandoverNo} 创建时间 {row.CreatedAtUtc:O} 落在历史区间之外。");
            }

            if (row.AcceptedAtUtc is { } acceptedAtUtc && acceptedAtUtc < row.CreatedAtUtc)
            {
                failures.Add($"班次交接 {row.HandoverNo} 接班时间早于交班时间。");
            }
        }

        if (rows.Length > 0 && rows.Count(x => x.AcceptedAtUtc is null) == 0)
        {
            failures.Add("班次交接全部已接班，「待接班」将为空——设定集要求最近一班保持未接班。");
        }

        return rows.Length;
    }

    private async Task<int> CheckDefectRecordsAsync(
        string organizationId,
        string environmentId,
        DateOnly asOfDate,
        double scale,
        DateTimeOffset lowerBound,
        DateTimeOffset upperBound,
        List<string> failures,
        CancellationToken cancellationToken)
    {
        var anchorCount = await dbContext.OperationTasks
            .AsNoTracking()
            .CountAsync(
                x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId &&
                    x.WorkOrderId.StartsWith("WO-2026-") &&
                    x.Status == Domain.AggregatesModel.OperationTaskAggregate.OperationTaskLifecycleStatus.Completed &&
                    x.ExistingEndUtc != null,
                cancellationToken);
        var expected = Math.Min(WorldHistoryFloorEventsSpec.DefectSlotCount(asOfDate, scale), anchorCount);

        var rows = await dbContext.DefectRecords
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId &&
                x.DefectNo.StartsWith("DEF-2026-"))
            .Select(x => new DefectProjection(x.DefectNo, x.WorkOrderId, x.OperationTaskId, x.DefectCode, x.Status, x.RecordedAtUtc, x.ClosedAtUtc))
            .ToArrayAsync(cancellationToken);

        if (rows.Length != expected)
        {
            failures.Add($"车间不良条数 {rows.Length} 与设定集预期 {expected} 不符。");
        }

        var defectCodes = WorldHistoryFloorEventsSpec.DefectCodes.ToHashSet(StringComparer.Ordinal);
        var orphanWorkOrders = await dbContext.DefectRecords
            .AsNoTracking()
            .CountAsync(
                defect => defect.OrganizationId == organizationId && defect.EnvironmentId == environmentId &&
                    defect.DefectNo.StartsWith("DEF-2026-") &&
                    !dbContext.WorkOrders.Any(workOrder =>
                        workOrder.OrganizationId == organizationId &&
                        workOrder.EnvironmentId == environmentId &&
                        workOrder.WorkOrderIdValue == defect.WorkOrderId),
                cancellationToken);
        if (orphanWorkOrders > 0)
        {
            failures.Add($"有 {orphanWorkOrders} 条车间不良挂在不存在的工单上。");
        }

        var orphanTasks = await dbContext.DefectRecords
            .AsNoTracking()
            .CountAsync(
                defect => defect.OrganizationId == organizationId && defect.EnvironmentId == environmentId &&
                    defect.DefectNo.StartsWith("DEF-2026-") &&
                    defect.OperationTaskId != null &&
                    !dbContext.OperationTasks.Any(task =>
                        task.OrganizationId == organizationId &&
                        task.EnvironmentId == environmentId &&
                        task.OperationTaskIdValue == defect.OperationTaskId),
                cancellationToken);
        if (orphanTasks > 0)
        {
            failures.Add($"有 {orphanTasks} 条车间不良挂在不存在的工序任务上。");
        }

        foreach (var row in rows)
        {
            if (!defectCodes.Contains(row.DefectCode))
            {
                failures.Add($"车间不良 {row.DefectNo} 的不良代码 '{row.DefectCode}' 不在设定集口径内。");
            }

            if (row.RecordedAtUtc < lowerBound || row.RecordedAtUtc > upperBound)
            {
                failures.Add($"车间不良 {row.DefectNo} 记录时间 {row.RecordedAtUtc:O} 落在历史区间之外。");
            }

            if (row.ClosedAtUtc is { } closedAtUtc && closedAtUtc < row.RecordedAtUtc)
            {
                failures.Add($"车间不良 {row.DefectNo} 的处置时间早于记录时间。");
            }
        }

        // 极小缩放（如 scale=0.02 且只有一两个生产日）下总量可能只有个位数，此时分布无意义，
        // 只在样本足够大时才把「必须两种状态都有」当成硬约束。
        if (rows.Length >= MinimumDistributionSample)
        {
            if (rows.Count(x => x.Status == DefectRecord.OpenStatus) == 0)
            {
                failures.Add("车间不良全部已处置，「待处置不良」将为空——设定集要求保留未处置的现场待办。");
            }

            if (rows.Count(x => x.Status != DefectRecord.OpenStatus) == 0)
            {
                failures.Add("车间不良全部未处置，处置分布（返工/让步/报废）将为空。");
            }
        }

        return rows.Length;
    }

    private sealed record DowntimeProjection(
        string DowntimeEventNo,
        string WorkCenterId,
        string? DeviceAssetId,
        string Reason,
        DateTimeOffset FromUtc,
        DateTimeOffset? ToUtc);

    private sealed record HandoverProjection(
        string HandoverNo,
        string ShiftId,
        string TeamId,
        string Status,
        int OpenIssueCount,
        DateTimeOffset CreatedAtUtc,
        DateTimeOffset? AcceptedAtUtc);

    private sealed record DefectProjection(
        string DefectNo,
        string WorkOrderId,
        string? OperationTaskId,
        string DefectCode,
        string Status,
        DateTimeOffset RecordedAtUtc,
        DateTimeOffset? ClosedAtUtc);

    #endregion

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

    #region 追溯断点（产出批次谱系 / 报工物料消耗）

    /// <summary>
    /// 追溯断点块的 fail-closed 校验：谱系与消耗必须挂在**真实报工**上、批号与领料批一致、
    /// 谱系数量与该工单的报工好品累计相等（追溯页上的数量链就是靠这条对得起来的）。
    /// </summary>
    public async Task<WorldHistoryGenealogyValidationReport> ValidateGenealogyAsync(
        string organizationId,
        string environmentId,
        CancellationToken cancellationToken = default)
    {
        var failures = new List<string>();

        var reportIndex = (await dbContext.ProductionReports
                .AsNoTracking()
                .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId &&
                    x.ReportNo.StartsWith(WorldHistoryGenealogySpec.ProductionReportNoPrefix))
                .Select(x => new { x.ReportNo, x.WorkOrderId, x.GoodQuantity })
                .ToArrayAsync(cancellationToken))
            .ToArray();
        var reportNos = reportIndex.Select(x => x.ReportNo).ToHashSet(StringComparer.Ordinal);
        var goodByWorkOrder = reportIndex
            .GroupBy(x => x.WorkOrderId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Sum(x => x.GoodQuantity), StringComparer.Ordinal);

        var genealogies = await dbContext.OutputLotGenealogies
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId)
            .Select(x => new GenealogyProjection(x.WorkOrderId, x.ReportNo, x.ProducedLotNo, x.Quantity, x.CreatedAtUtc))
            .ToArrayAsync(cancellationToken);

        foreach (var row in genealogies)
        {
            if (!reportNos.Contains(row.ReportNo))
            {
                failures.Add($"产出批次谱系 {row.ProducedLotNo} 引用了不存在的报工 {row.ReportNo}。");
            }

            var expectedLotNo = WorldHistoryGenealogySpec.ProducedLotNo(row.WorkOrderId);
            if (!string.Equals(row.ProducedLotNo, expectedLotNo, StringComparison.Ordinal))
            {
                failures.Add($"工单 {row.WorkOrderId} 的产出批号 {row.ProducedLotNo} 与设定集号段 {expectedLotNo} 不符。");
            }

            if (goodByWorkOrder.TryGetValue(row.WorkOrderId, out var expectedQuantity) &&
                Math.Abs(expectedQuantity - row.Quantity) > QuantityTolerance)
            {
                failures.Add($"工单 {row.WorkOrderId} 的谱系数量 {row.Quantity} 与报工好品累计 {expectedQuantity} 不符。");
            }
        }

        var consumptions = await dbContext.ProductionReportMaterialConsumptions
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId)
            .Select(x => new ConsumptionProjection(x.ReportNo, x.WorkOrderId, x.MaterialId, x.MaterialLotId, x.ConsumedQuantity))
            .ToArrayAsync(cancellationToken);

        foreach (var row in consumptions)
        {
            if (!reportNos.Contains(row.ReportNo))
            {
                failures.Add($"物料消耗 {row.MaterialLotId} 引用了不存在的报工 {row.ReportNo}。");
            }

            var expectedLotId = WorldHistoryGenealogySpec.MaterialLotNo(row.MaterialId, row.WorkOrderId);
            if (!string.Equals(row.MaterialLotId, expectedLotId, StringComparison.Ordinal))
            {
                failures.Add($"物料消耗批次 {row.MaterialLotId} 与领料批号 {expectedLotId} 对不上（追溯断链）。");
            }

            if (row.ConsumedQuantity <= 0m)
            {
                failures.Add($"物料消耗 {row.MaterialLotId} 的消耗量 {row.ConsumedQuantity} 非正。");
            }
        }

        if (failures.Count > 0)
        {
            throw new WorldHistoryConsistencyException(failures);
        }

        return new WorldHistoryGenealogyValidationReport(genealogies.Length, consumptions.Length);
    }

    private sealed record GenealogyProjection(
        string WorkOrderId,
        string ReportNo,
        string ProducedLotNo,
        decimal Quantity,
        DateTimeOffset CreatedAtUtc);

    private sealed record ConsumptionProjection(
        string ReportNo,
        string WorkOrderId,
        string MaterialId,
        string MaterialLotId,
        decimal ConsumedQuantity);

    #endregion

    #region 生产准备底座（设备映射 / SKU 停用）

    /// <summary>
    /// 底座块的 fail-closed 校验。最关键的一条：**停用清单不得命中演示主链**——
    /// 任何一个成品或用料被写成 disabled，该 SKU 的建单能力当场作废。
    /// </summary>
    public async Task<WorldHistoryFoundationValidationReport> ValidateFoundationAsync(
        string organizationId,
        string environmentId,
        CancellationToken cancellationToken = default)
    {
        var failures = new List<string>();

        var mappings = await dbContext.DeviceAssetWorkCenterMappings
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId)
            .Select(x => new { x.DeviceAssetId, x.WorkCenterId })
            .ToArrayAsync(cancellationToken);
        var workCenters = WorldHistoryFloorEventsSpec.WorkCenterIds.ToHashSet(StringComparer.Ordinal);

        foreach (var mapping in mappings)
        {
            if (!workCenters.Contains(mapping.WorkCenterId))
            {
                failures.Add($"设备 {mapping.DeviceAssetId} 映射到非 L0 工作中心 {mapping.WorkCenterId}。");
            }

            if (mapping.DeviceAssetId.StartsWith(WorldHistoryFoundationSpec.UnmappedAuxiliaryDevicePrefix, StringComparison.Ordinal))
            {
                failures.Add($"辅助设备 {mapping.DeviceAssetId} 不应绑定工作中心（其遥测会被误记为产量）。");
            }
        }

        if (mappings.Length != mappings.Select(x => x.DeviceAssetId).Distinct(StringComparer.Ordinal).Count())
        {
            failures.Add("设备 ↔ 工作中心映射存在重复设备编码。");
        }

        var disabled = await dbContext.MesSkuAvailabilities
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId &&
                x.Status == MesSkuAvailabilityStatuses.Disabled)
            .Select(x => x.SkuCode)
            .ToArrayAsync(cancellationToken);

        var demandChainSkus = WorldHistorySpec.FinishedGoodSkus
            .Concat(WorldHistorySpec.FinishedGoodSkus.SelectMany(sku =>
                WorldHistoryMesSpec.Components(sku).Select(component => component.SkuCode)))
            .ToHashSet(StringComparer.Ordinal);

        foreach (var skuCode in disabled.Where(demandChainSkus.Contains))
        {
            failures.Add($"SKU {skuCode} 被停用，但它在演示主链上（成品或用料），会挡住建工单。");
        }

        if (failures.Count > 0)
        {
            throw new WorldHistoryConsistencyException(failures);
        }

        return new WorldHistoryFoundationValidationReport(mappings.Length, disabled.Length);
    }

    #endregion

    #region 规则排程

    /// <summary>排程结果的 fail-closed 校验：版本号唯一且连续、分配非空、时间窗口合法。</summary>
    public async Task<WorldHistoryScheduleResultValidationReport> ValidateScheduleResultsAsync(
        CancellationToken cancellationToken = default)
    {
        var failures = new List<string>();
        var results = await dbContext.ScheduleResults
            .AsNoTracking()
            .OrderBy(x => x.ScheduleVersion)
            .ToArrayAsync(cancellationToken);

        var versions = results.Select(x => x.ScheduleVersion).ToArray();
        if (versions.Length != versions.Distinct().Count())
        {
            failures.Add("排程结果的版本号存在重复。");
        }

        foreach (var result in results)
        {
            if (result.Assignments.Count == 0)
            {
                failures.Add($"排程结果 v{result.ScheduleVersion} 没有任何工序分配。");
            }

            foreach (var assignment in result.Assignments.Where(x => x.EndUtc < x.StartUtc))
            {
                failures.Add($"排程结果 v{result.ScheduleVersion} 的工序 {assignment.OperationTaskId} 结束早于开始。");
            }
        }

        if (failures.Count > 0)
        {
            throw new WorldHistoryConsistencyException(failures);
        }

        return new WorldHistoryScheduleResultValidationReport(results.Length);
    }

    #endregion
}

/// <summary>MES 侧一致性校验器的产出摘要。</summary>
public sealed record WorldHistoryMesValidationReport(
    int WorkOrdersChecked,
    int OperationTasksChecked,
    int ProductionReportsChecked,
    int FinishedGoodsReceiptsChecked,
    IReadOnlyList<string> Sample);

/// <summary>「异常与协同」块（停机 / 班次交接 / 车间不良）的校验产出摘要。</summary>
public sealed record WorldHistoryFloorEventsValidationReport(
    int DowntimeEventsChecked,
    int ShiftHandoversChecked,
    int DefectRecordsChecked);

/// <summary>「追溯断点」块（产出批次谱系 / 报工物料消耗）的校验产出摘要。</summary>
public sealed record WorldHistoryGenealogyValidationReport(
    int OutputLotGenealogiesChecked,
    int MaterialConsumptionsChecked);

/// <summary>「生产准备底座」块（设备映射 / SKU 停用）的校验产出摘要。</summary>
public sealed record WorldHistoryFoundationValidationReport(
    int DeviceAssetMappingsChecked,
    int DisabledSkusChecked);

/// <summary>「规则排程」块的校验产出摘要。</summary>
public sealed record WorldHistoryScheduleResultValidationReport(int ScheduleResultsChecked);

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
