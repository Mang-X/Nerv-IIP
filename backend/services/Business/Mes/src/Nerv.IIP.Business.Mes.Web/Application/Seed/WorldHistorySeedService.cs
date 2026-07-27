using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.FinishedGoodsReceiptRequestAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.MaterialSupplyAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.OperationTaskAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.ProductionReportAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;
using Nerv.IIP.Business.Mes.Infrastructure;

namespace Nerv.IIP.Business.Mes.Web.Application.Seed;

/// <summary>
/// 《工厂世界观设定集》L1 背景历史引擎的 **MES 侧**：与 ERP 侧同一张订单计划表配对的工单全链历史。
///
/// 产出（设定集 §7）：约 3600 张工单（<c>WO-2026-#####</c> 订单工单 + <c>WO-2026-R####</c> 内部补产），
/// 每单 6–8 道工序任务、齐套需求快照、领料单（部分分批）、2–5 条报工、完工入库请求（已过账）。
///
/// 与 ERP 侧的一致性靠 <see cref="WorldHistorySpec.BuildOrderPlans"/> 与
/// <see cref="WorldHistoryTimeline"/> 两个确定性纯函数达成：两侧不通信、不跨库查询、不建跨 schema 外键，
/// 仅以业务编码 <c>SO-2026-#####</c> ↔ <c>WO-2026-#####</c> 相互引用。
///
/// 关键数量不变量：**好品产出 == 销售订单数量**。工单投料量按报废量放大
/// （<see cref="WorldHistoryWorkOrderPlan.WorkOrderQuantity"/>），于是「报工 → 完工入库 → 发货」
/// 的数量链逐件对得上。
/// </summary>
public sealed class WorldHistorySeedService(
    ApplicationDbContext dbContext,
    IWorldHistoryProductionVersionResolver productionVersionResolver)
{
    /// <summary>每批工单数。批内共享一次预查与一次 <c>SaveChanges</c>，批末清变更跟踪器。</summary>
    public const int BatchSize = 50;

    private const string SourceSystem = "seed:world-history";
    private const string DispatchActor = "system:mes";

    public async Task<WorldHistoryMesSeedReport> SeedAsync(
        string organizationId,
        string environmentId,
        DateOnly asOfDate,
        double scale,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(scale);

        var plans = WorldHistorySpec.BuildOrderPlans(asOfDate, scale)
            .Where(plan => plan.HasWorkOrder)
            .ToArray();

        var productionVersions = await productionVersionResolver.ResolveAsync(
            organizationId,
            environmentId,
            plans.Select(plan => plan.SkuCode).Distinct(StringComparer.Ordinal).ToArray(),
            cancellationToken);

        var orderWorkOrders = await SeedOrderWorkOrdersAsync(
            organizationId, environmentId, plans, productionVersions, asOfDate, cancellationToken);
        var reworkWorkOrders = await SeedReworkWorkOrdersAsync(
            organizationId, environmentId, plans, productionVersions, asOfDate, cancellationToken);

        // fail-closed：数量链对不上就让 seed 失败。
        var validation = await new WorldHistoryConsistencyValidator(dbContext)
            .ValidateAsync(organizationId, environmentId, asOfDate, scale, cancellationToken);

        return new WorldHistoryMesSeedReport(orderWorkOrders, reworkWorkOrders, validation);
    }

    #region 订单工单

    private async Task<int> SeedOrderWorkOrdersAsync(
        string organizationId,
        string environmentId,
        IReadOnlyList<WorldHistoryOrderPlan> plans,
        IReadOnlyDictionary<string, string> productionVersions,
        DateOnly asOfDate,
        CancellationToken cancellationToken)
    {
        var written = 0;
        for (var batchStart = 0; batchStart < plans.Count; batchStart += BatchSize)
        {
            var batch = plans.Skip(batchStart).Take(BatchSize).ToArray();
            var existingSet = await LoadExistingWorkOrderIdsAsync(
                organizationId, environmentId, batch.Select(x => x.WorkOrderNo).ToArray(), cancellationToken);

            var added = 0;
            foreach (var plan in batch.Where(plan => !existingSet.Contains(plan.WorkOrderNo)))
            {
                var timeline = WorldHistoryTimeline.For(plan, asOfDate);
                var workOrderPlan = WorldHistoryMesSpec.BuildWorkOrderPlan(plan.WorkOrderNo, plan.SkuCode, plan.Quantity);
                WriteWorkOrderChain(
                    organizationId,
                    environmentId,
                    workOrderPlan,
                    productionVersions.GetValueOrDefault(plan.SkuCode),
                    timeline,
                    ResolveExecution(plan.Stage),
                    plan.SalesOrderNo,
                    plan.RequiredDate);
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

    /// <summary>销售订单阶段 → 工单执行深度。</summary>
    private static WorldHistoryExecution ResolveExecution(WorldHistoryOrderStage stage) => stage switch
    {
        WorldHistoryOrderStage.Settled or WorldHistoryOrderStage.Shipped => WorldHistoryExecution.Closed,
        WorldHistoryOrderStage.InProgress => WorldHistoryExecution.Partial,
        _ => WorldHistoryExecution.ReleasedOnly,
    };

    #endregion

    #region 补产工单（设定集 §7「含内部补产」）

    private async Task<int> SeedReworkWorkOrdersAsync(
        string organizationId,
        string environmentId,
        IReadOnlyList<WorldHistoryOrderPlan> plans,
        IReadOnlyDictionary<string, string> productionVersions,
        DateOnly asOfDate,
        CancellationToken cancellationToken)
    {
        // 补产只挂在已结案/已发货的订单后面：补的是已交付批次里的不良件。
        var candidates = plans.Where(plan => plan.HasDelivery).ToArray();
        var reworkCount = (int)Math.Round(plans.Count * WorldHistoryMesSpec.ReworkWorkOrderRatio, MidpointRounding.AwayFromZero);
        if (candidates.Length == 0 || reworkCount == 0)
        {
            return 0;
        }

        var written = 0;
        for (var batchStart = 1; batchStart <= reworkCount; batchStart += BatchSize)
        {
            var batchEnd = Math.Min(batchStart + BatchSize - 1, reworkCount);
            var sequences = Enumerable.Range(batchStart, batchEnd - batchStart + 1).ToArray();
            var existingSet = await LoadExistingWorkOrderIdsAsync(
                organizationId,
                environmentId,
                sequences.Select(WorldHistoryMesSpec.ReworkWorkOrderNo).ToArray(),
                cancellationToken);

            var added = 0;
            foreach (var sequence in sequences)
            {
                var workOrderNo = WorldHistoryMesSpec.ReworkWorkOrderNo(sequence);
                if (existingSet.Contains(workOrderNo))
                {
                    continue;
                }

                var source = candidates[(sequence - 1) * candidates.Length / reworkCount];
                var random = new WorldHistoryRandom($"rework:{workOrderNo}");
                var quantity = Math.Max(2m, decimal.Round(source.Quantity * (random.NextInt(3, 9) / 100m), 0, MidpointRounding.AwayFromZero));
                var workOrderPlan = WorldHistoryMesSpec.BuildWorkOrderPlan(workOrderNo, source.SkuCode, quantity);
                var timeline = WorldHistoryTimeline.For(source, asOfDate);

                WriteWorkOrderChain(
                    organizationId,
                    environmentId,
                    workOrderPlan,
                    productionVersions.GetValueOrDefault(source.SkuCode),
                    timeline,
                    WorldHistoryExecution.Closed,
                    // 补产工单的来源单据是被补的工单，不是销售订单。
                    sourceDocumentId: source.WorkOrderNo,
                    dueDate: source.RequiredDate,
                    isRework: true);
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

    private async Task<HashSet<string>> LoadExistingWorkOrderIdsAsync(
        string organizationId,
        string environmentId,
        string[] workOrderIds,
        CancellationToken cancellationToken) =>
        (await dbContext.WorkOrders
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId &&
                workOrderIds.Contains(x.WorkOrderIdValue))
            .Select(x => x.WorkOrderIdValue)
            .ToArrayAsync(cancellationToken))
        .ToHashSet(StringComparer.Ordinal);

    /// <summary>写一张历史工单的完整链路：工单 → 工序任务 → 齐套需求 → 领料 → 报工 → 完工入库 → 关单。</summary>
    private void WriteWorkOrderChain(
        string organizationId,
        string environmentId,
        WorldHistoryWorkOrderPlan plan,
        string? productionVersionId,
        WorldHistoryTimeline timeline,
        WorldHistoryExecution execution,
        string sourceDocumentId,
        DateOnly dueDate,
        bool isRework = false)
    {
        var dueUtc = new DateTimeOffset(dueDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var createdAtUtc = MomentOn(timeline.OrderDate, plan.WorkOrderNo, "workorder-created");
        var releasedAtUtc = MomentOn(timeline.WorkOrderReleaseDate, plan.WorkOrderNo, "workorder-released");

        // 时间线贴近 asOfDate 时（尾部单），OrderDate/ReleaseDate/ProductionStartDate 被压到同一天，
        // 班次内随机时刻可能倒挂出「报工早于工单创建」（asOfDate=2026-07-27 实翻车，校验器
        // fail-closed 拦停 MES 启动）。夹取保证 创建 ≤ 下达 ≤ 首个工序窗口开始。
        var productionFloorUtc = WorldHistoryCalendar.ShiftMoment(timeline.ProductionStartDate, 0, 0);
        if (releasedAtUtc >= productionFloorUtc)
        {
            releasedAtUtc = productionFloorUtc.AddHours(-1);
        }

        if (createdAtUtc >= releasedAtUtc)
        {
            createdAtUtc = releasedAtUtc.AddHours(-1);
        }

        var workOrder = WorkOrder.Create(
            organizationId,
            environmentId,
            plan.WorkOrderNo,
            plan.SkuCode,
            productionVersionId,
            plan.WorkOrderQuantity,
            priority: isRework ? 90 : 10,
            dueUtc,
            WorldHistorySpec.UomCode,
            new SourcePlanReference(
                sourceSystem: isRework ? "mes" : "erp",
                sourceDocumentType: isRework ? "rework-work-order" : "sales-order",
                sourceDocumentId: sourceDocumentId,
                sourceDemandReference: isRework ? null : $"{sourceDocumentId}:10"));
        workOrder.MarkReleased();
        dbContext.WorkOrders.Add(workOrder);
        Backdate(workOrder, x => x.CreatedAtUtc, createdAtUtc);

        var tasks = WriteOperationTasks(organizationId, environmentId, plan, timeline, execution, releasedAtUtc);
        WriteMaterialFacts(organizationId, environmentId, plan, timeline, execution, tasks);

        if (execution == WorldHistoryExecution.ReleasedOnly)
        {
            return;
        }

        var finalTask = tasks[^1];
        var completedAtUtc = WriteProductionReports(organizationId, environmentId, plan, execution, workOrder, finalTask);

        if (execution != WorldHistoryExecution.Closed)
        {
            return;
        }

        WriteFinishedGoodsReceipt(organizationId, environmentId, plan, timeline, completedAtUtc);
        workOrder.Close(completedAtUtc.AddMinutes(30));
    }

    #region 工序任务

    private IReadOnlyList<OperationTaskWindow> WriteOperationTasks(
        string organizationId,
        string environmentId,
        WorldHistoryWorkOrderPlan plan,
        WorldHistoryTimeline timeline,
        WorldHistoryExecution execution,
        DateTimeOffset releasedAtUtc)
    {
        var sequences = plan.OperationSequences;
        var windows = new List<OperationTaskWindow>(sequences.Count);
        var workingDays = WorkingDaysBetween(timeline.ProductionStartDate, timeline.ProductionCompletionDate);

        // 在制单只做完前面若干道工序，最后一道在制、其余排队。
        var completedThrough = execution switch
        {
            WorldHistoryExecution.Closed => sequences.Count,
            WorldHistoryExecution.Partial => Math.Max(1, sequences.Count * 2 / 3),
            _ => 0,
        };

        for (var position = 0; position < sequences.Count; position++)
        {
            var sequence = sequences[position];
            var operation = WorldHistoryMesSpec.Operation(sequence);
            var operatorPool = WorldHistoryMesSpec.OperatorsIn(operation.Workshop);
            var random = new WorldHistoryRandom($"task:{plan.WorkOrderNo}:{sequence}");
            var assignee = random.Pick(operatorPool);

            var dayIndex = sequences.Count <= 1 ? 0 : position * (workingDays.Count - 1) / (sequences.Count - 1);
            var workingDay = workingDays[Math.Clamp(dayIndex, 0, workingDays.Count - 1)];
            var duration = WorldHistoryMesSpec.OperationDuration(operation, plan.WorkOrderQuantity);

            // 工序落在该员工的班次窗口内，开工点随机但收工不越班次末（设定集 §1 两班制）。
            var minutesIntoShift = random.NextInt(0, (WorldHistoryCalendar.ShiftLengthHours * 60) / 2);
            var startUtc = WorldHistoryCalendar.ShiftMoment(workingDay, assignee.ShiftIndex, minutesIntoShift);
            var shiftEndUtc = WorldHistoryCalendar.ShiftEnd(workingDay, assignee.ShiftIndex);
            var endUtc = startUtc + duration;
            if (endUtc > shiftEndUtc)
            {
                endUtc = shiftEndUtc;
            }

            var task = OperationTask.Queue(
                organizationId,
                environmentId,
                plan.WorkOrderNo,
                WorldHistoryMesSpec.OperationTaskId(plan.WorkOrderNo, sequence),
                sequence,
                WorldHistoryMesSpec.WorkCenterCode(plan.SkuCode, sequence),
                [],
                releasedAtUtc,
                duration,
                plan.SkuCode,
                WorldHistorySpec.UomCode,
                plan.WorkOrderQuantity,
                operation.RequiresQualityInspection,
                operation.OperationCode);

            var isCompleted = position < completedThrough;
            var isInProgress = execution == WorldHistoryExecution.Partial && position == completedThrough;
            if (isCompleted || isInProgress)
            {
                // 历史上真实发生过的工序必须带完整排程与派工痕迹：
                // 先按周版排程方案落 Scheduled 事实（SP-2026-W## 段，不侵占 SCALE/L2 排程），
                // 再按设定集 §3 的设备段派到人 + 设备（派工看板据此显示排程状态与派工人）。
                var deviceAssetId = WorldHistoryMesSpec.DeviceAssetCode(sequence, random);
                task.ApplyScheduleAssignment(
                    WorldHistoryMesSpec.WorkCenterCode(plan.SkuCode, sequence),
                    deviceAssetId,
                    plannedStartUtc: startUtc,
                    plannedEndUtc: endUtc,
                    assignedAtUtc: releasedAtUtc,
                    operationCode: operation.OperationCode,
                    schedulePlanId: WorldHistoryMesSpec.SchedulePlanId(workingDay),
                    scheduleReleaseRevision: 1);
                task.Assign(
                    assignee.UserId,
                    deviceAssetId,
                    shiftId: assignee.TeamCode,
                    assignedAtUtc: startUtc,
                    DispatchActor,
                    assignedUserName: assignee.Name);
                task.Start(startUtc);
            }

            if (isCompleted)
            {
                task.Complete(endUtc);
            }

            dbContext.OperationTasks.Add(task);
            Backdate(task, x => x.CreatedAtUtc, releasedAtUtc);
            windows.Add(new OperationTaskWindow(task, sequence, startUtc, endUtc, assignee, isCompleted));
        }

        return windows;
    }

    private static IReadOnlyList<DateOnly> WorkingDaysBetween(DateOnly from, DateOnly to)
    {
        var days = new List<DateOnly>();
        var cursor = WorldHistoryCalendar.SnapToWorkingDay(from);
        var limit = to < cursor ? cursor : to;
        while (cursor <= limit)
        {
            if (WorldHistoryCalendar.IsWorkingDay(cursor))
            {
                days.Add(cursor);
            }

            cursor = cursor.AddDays(1);
        }

        return days.Count == 0 ? [WorldHistoryCalendar.SnapToWorkingDay(from)] : days;
    }

    #endregion

    #region 齐套需求与领料

    private void WriteMaterialFacts(
        string organizationId,
        string environmentId,
        WorldHistoryWorkOrderPlan plan,
        WorldHistoryTimeline timeline,
        WorldHistoryExecution execution,
        IReadOnlyList<OperationTaskWindow> tasks)
    {
        var components = WorldHistoryMesSpec.Components(plan.SkuCode);
        var kittingTask = tasks[0];
        var capturedAtUtc = MomentOn(timeline.WorkOrderReleaseDate, plan.WorkOrderNo, "kitting");

        foreach (var component in components)
        {
            var required = component.QuantityPer * plan.WorkOrderQuantity;
            var requirement = MaterialRequirement.Capture(
                organizationId,
                environmentId,
                plan.WorkOrderNo,
                kittingTask.Task.OperationTaskId,
                component.SkuCode,
                materialLotId: $"LOT-{component.SkuCode}-{plan.WorkOrderNo}",
                requiredQuantity: required,
                // 齐套检查通过：可用与已备料都覆盖需求（未齐套的场景属于二期库存域）。
                availableQuantity: required,
                stagedQuantity: required,
                sourceSystem: SourceSystem,
                sourceSnapshotId: $"{plan.WorkOrderNo}-KIT",
                capturedAtUtc: capturedAtUtc);
            dbContext.MaterialRequirements.Add(requirement);
        }

        if (execution == WorldHistoryExecution.ReleasedOnly)
        {
            // 已下达未开工：只留齐套快照，尚未领料。
            return;
        }

        var ordinal = 0;
        foreach (var component in components)
        {
            var required = component.QuantityPer * plan.WorkOrderQuantity;
            // 部分工单分两批领料（设定集 §7「领料单（部分分批领）」）。
            var portions = plan.SplitMaterialIssue
                ? new[] { decimal.Round(required * 0.6m, 2), required - decimal.Round(required * 0.6m, 2) }
                : [required];

            for (var portionIndex = 0; portionIndex < portions.Length; portionIndex++)
            {
                ordinal++;
                var requestedAtUtc = MomentOn(
                    portionIndex == 0 ? timeline.ProductionStartDate : timeline.ProductionCompletionDate,
                    $"{plan.WorkOrderNo}:{ordinal}",
                    "issue-requested");
                var request = MaterialIssueRequest.Create(
                    organizationId,
                    environmentId,
                    WorldHistoryMesSpec.MaterialIssueRequestNo(plan.WorkOrderNo, ordinal),
                    plan.WorkOrderNo,
                    kittingTask.Task.OperationTaskId,
                    component.SkuCode,
                    component.UomCode,
                    portions[portionIndex],
                    requestedAtUtc);
                request.ConfirmLineSideReceipt(
                    requestedAtUtc.AddMinutes(25),
                    portions[portionIndex],
                    $"LOT-{component.SkuCode}-{plan.WorkOrderNo}");
                dbContext.MaterialIssueRequests.Add(request);
            }
        }
    }

    #endregion

    #region 报工与完工入库

    /// <summary>
    /// 报工记在**最后一道工序**上：那是产出成品的工序，工单累计完工量因此等于实际产出量，
    /// 不会被前道工序的流转量重复累加（裁决点见 PR 正文）。
    /// </summary>
    private DateTimeOffset WriteProductionReports(
        string organizationId,
        string environmentId,
        WorldHistoryWorkOrderPlan plan,
        WorldHistoryExecution execution,
        WorkOrder workOrder,
        OperationTaskWindow finalTask)
    {
        var totalGood = plan.GoodQuantity;
        var totalScrap = plan.ScrapQuantity;
        if (execution == WorldHistoryExecution.Partial)
        {
            // 在制单只报出一部分，剩余量留在车间。
            var random = new WorldHistoryRandom($"partial:{plan.WorkOrderNo}");
            var reportedShare = random.NextInt(30, 71) / 100m;
            totalGood = Math.Max(1m, decimal.Round(plan.GoodQuantity * reportedShare, 0, MidpointRounding.AwayFromZero));
            totalScrap = 0m;
        }

        var reportCount = Math.Max(1, execution == WorldHistoryExecution.Partial ? Math.Min(2, plan.ReportCount) : plan.ReportCount);
        var goodSplits = Split(totalGood, reportCount);
        var scrapSplits = Split(totalScrap, reportCount);

        var lastMoment = finalTask.EndUtc;
        for (var index = 0; index < reportCount; index++)
        {
            var good = goodSplits[index];
            var scrap = scrapSplits[index];
            if (good + scrap <= 0m)
            {
                continue;
            }

            // 报工时间沿最后一道工序的窗口顺序推进，末次报工落在工序收尾时刻。
            var offsetMinutes = (index + 1) * (int)Math.Max(1, (finalTask.EndUtc - finalTask.StartUtc).TotalMinutes / (reportCount + 1));
            var reportedAtUtc = finalTask.StartUtc.AddMinutes(offsetMinutes);
            if (reportedAtUtc > finalTask.EndUtc)
            {
                reportedAtUtc = finalTask.EndUtc;
            }

            var isLast = index == reportCount - 1;
            var report = ProductionReport.Record(
                organizationId,
                environmentId,
                WorldHistoryMesSpec.ProductionReportNo(plan.WorkOrderNo, index + 1),
                plan.WorkOrderNo,
                finalTask.Task.OperationTaskId,
                good,
                scrap,
                completesOperation: isLast && execution == WorldHistoryExecution.Closed,
                reportedAtUtc,
                scrapReasonCode: scrap > 0m ? "SCRAP-DIM" : null,
                producedLotNo: WorldHistoryMesSpec.ProducedLotNo(plan.WorkOrderNo));
            dbContext.ProductionReports.Add(report);

            workOrder.RecordProductionProgress(good, scrap, reportedAtUtc);
            lastMoment = reportedAtUtc;
        }

        return lastMoment;
    }

    private void WriteFinishedGoodsReceipt(
        string organizationId,
        string environmentId,
        WorldHistoryWorkOrderPlan plan,
        WorldHistoryTimeline timeline,
        DateTimeOffset completedAtUtc)
    {
        var receipt = FinishedGoodsReceiptRequest.Create(
            organizationId,
            environmentId,
            WorldHistoryMesSpec.FinishedGoodsReceiptNo(plan.WorkOrderNo),
            plan.WorkOrderNo,
            plan.SkuCode,
            plan.GoodQuantity,
            WorldHistorySpec.UomCode,
            completedAtUtc.AddMinutes(10),
            producedLotNo: WorldHistoryMesSpec.ProducedLotNo(plan.WorkOrderNo),
            serialNo: null,
            // unitCost 留空：成本归集属于 ERP 成本域，这里不伪造单位成本，
            // 也因此不触发 FinishedGoodsReceiptRequestedDomainEvent。
            unitCost: null,
            ProductionDate: timeline.ProductionCompletionDate);
        receipt.MarkPosted($"INV-{plan.WorkOrderNo}", completedAtUtc.AddMinutes(20));
        dbContext.FinishedGoodsReceiptRequests.Add(receipt);
    }

    /// <summary>把总量拆成 <paramref name="parts"/> 份，余数补到最后一份，保证求和等于总量。</summary>
    private static decimal[] Split(decimal total, int parts)
    {
        var result = new decimal[parts];
        if (total <= 0m)
        {
            return result;
        }

        var each = decimal.Round(total / parts, 0, MidpointRounding.AwayFromZero);
        var allocated = 0m;
        for (var index = 0; index < parts - 1; index++)
        {
            var value = Math.Min(each, Math.Max(0m, total - allocated - (parts - 1 - index)));
            result[index] = value;
            allocated += value;
        }

        result[parts - 1] = total - allocated;
        return result;
    }

    #endregion

    private void Backdate<TEntity, TProperty>(
        TEntity entity,
        System.Linq.Expressions.Expression<Func<TEntity, TProperty>> property,
        TProperty value)
        where TEntity : class
    {
        dbContext.Entry(entity).Property(property).CurrentValue = value;
    }

    private static DateTimeOffset MomentOn(DateOnly date, string streamKey, string purpose)
    {
        var workingDay = WorldHistoryCalendar.SnapToWorkingDay(date);
        var random = new WorldHistoryRandom($"{purpose}:{streamKey}");
        var shiftIndex = random.NextInt(0, 2);
        var minutesIntoShift = random.NextInt(0, WorldHistoryCalendar.ShiftLengthHours * 60);
        return WorldHistoryCalendar.ShiftMoment(workingDay, shiftIndex, minutesIntoShift);
    }

    private sealed record OperationTaskWindow(
        OperationTask Task,
        int Sequence,
        DateTimeOffset StartUtc,
        DateTimeOffset EndUtc,
        WorldHistoryOperator Assignee,
        bool IsCompleted);
}

/// <summary>工单的执行深度，由销售订单阶段决定。</summary>
public enum WorldHistoryExecution
{
    /// <summary>已下达待开工：工序任务排队，无领料、无报工。</summary>
    ReleasedOnly,

    /// <summary>在制：部分工序完工、当前工序进行中，有部分报工。</summary>
    Partial,

    /// <summary>已完工关单：全部工序完工、报工齐全、完工入库已过账。</summary>
    Closed,
}

/// <summary>一次 L1 MES 历史生成的产出摘要。</summary>
public sealed record WorldHistoryMesSeedReport(
    int OrderWorkOrdersWritten,
    int ReworkWorkOrdersWritten,
    WorldHistoryMesValidationReport Validation);
