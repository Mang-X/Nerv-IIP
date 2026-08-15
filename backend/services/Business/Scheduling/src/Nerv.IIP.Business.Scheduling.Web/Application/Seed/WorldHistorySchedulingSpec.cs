using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Nerv.IIP.Business.Scheduling.Domain.AggregatesModel.SchedulePlanAggregate;
using Nerv.IIP.Business.Scheduling.Domain.Services;
using Nerv.IIP.Contracts.Scheduling;

namespace Nerv.IIP.Business.Scheduling.Web.Application.Seed;

/// <summary>
/// 《工厂世界观设定集》L1 背景历史引擎的 **排产域侧规格**：
/// 把 ERP/MES 共享的 <see cref="WorldHistorySpec.BuildOrderPlans"/> 订单计划表
/// 投影成「每周一版排产方案」的确定性事实流——问题快照、方案头、资源分配、资源负荷、
/// 冲突、不可排工序与订单紧急度快照。
///
/// 与 MES 的一致性靠 <see cref="WorldHistoryMesSpec"/> 的确定性纯函数镜像达成：
/// 工单号 <c>WO-2026-#####</c>、工序号 <c>{工单号}-OP-{序号}</c>、工作中心 <c>WC-*</c>
/// 两侧逐字对上，排产库既不跨库查 MES 也不建跨 schema 外键。
///
/// 生命周期形状（受 <c>ux_schedule_plans_scope_active_release</c> 约束：同一 org/env 下
/// **最多只能有一个 Released 方案**）：
/// 历史周方案逐版被后一版取代（Superseded，<c>SupersededByPlanId</c> 串成链）→
/// 当前周一版已发布（Released）→ 队尾若干版待发布（Generated）→ 少量被显式撤销（Revoked）。
/// <c>ReleaseRevision</c> 按发布链单调递增，满足 <c>ux_schedule_plans_scope_release_revision</c>。
/// </summary>
public static class WorldHistorySchedulingSpec
{
    /// <summary>排产契约版本（与 <c>SchedulingProblemProducer</c> 产出的快照同版）。</summary>
    public const int ContractVersion = 1;

    /// <summary>单个方案纳入的工单上限：60 单 × 6–8 工序 ≈ 200–500 条资源分配。</summary>
    public const int MaxOrdersPerPlan = 60;

    /// <summary>某周额外出一版「重排」方案的概率（计划员当周改过一次选单）。</summary>
    public const double WeeklyRevisionProbability = 0.40;

    /// <summary>队尾待发布方案数（工作台默认展示的就是它们）。</summary>
    public const int PendingGeneratedPlanCount = 2;

    /// <summary>被显式撤销的方案上限（带撤销时间戳，状态 Revoked）。</summary>
    public const int MaxRevokedPlanCount = 2;
    public const double RevokeProbability = 0.06;

    /// <summary>排产展望期：两周滚动。</summary>
    public const int HorizonDays = 14;

    /// <summary>手工锁定工序比例（呼应工作台「锁定重预览」）。</summary>
    public const double LockedAssignmentProbability = 0.04;

    /// <summary>班次探测上限，防止时间轴推进出现死循环。</summary>
    private const int MaxShiftProbes = 240;

    #region §9 号段（与 -DEMO- / -SCALE- 保留段严格隔离）

    public const string PlanNumberPrefix = "SP-2026-";
    public const string ProblemNumberPrefix = "SPB-2026-";

    public static string PlanId(int index) => $"{PlanNumberPrefix}{index:D4}";
    public static string ProblemId(int index) => $"{ProblemNumberPrefix}{index:D4}";

    /// <summary>本引擎产出的全部编号前缀，供隔离性回归测试断言。</summary>
    public static readonly string[] NumberSegmentPrefixes = [PlanNumberPrefix, ProblemNumberPrefix];

    /// <summary>保留号段：固定演示事实与千单规模块，世界观历史绝不可撞入。</summary>
    public static readonly string[] ReservedInfixes = ["-DEMO-", "-SCALE-"];

    #endregion

    private static readonly Dictionary<string, string> ResourceWorkCenters = BuildResourceWorkCenters();

    private static Dictionary<string, string> BuildResourceWorkCenters()
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var workCenter in WorldHistoryMesSpec.WorkCenterCodes)
        {
            foreach (var resource in WorldHistoryMesSpec.ResourcesIn(workCenter))
            {
                map[resource] = workCenter;
            }
        }

        return map;
    }

    /// <summary>某设备所属的工作中心。</summary>
    public static string WorkCenterOf(string resourceId) => ResourceWorkCenters[resourceId];

    /// <summary>历史窗口上界：<paramref name="asOfDate"/> 当日 23:59:59.999（UTC）。</summary>
    public static DateTimeOffset HistoryUpperBound(DateOnly asOfDate) =>
        new(asOfDate.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero);

    /// <summary>生成排产域全量事实流。</summary>
    public static WorldHistorySchedulingFacts BuildSchedulingFacts(DateOnly asOfDate, double scale)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(scale);
        if (asOfDate < WorldHistoryCalendar.GoLiveDate)
        {
            asOfDate = WorldHistoryCalendar.GoLiveDate;
        }

        var orderPlans = WorldHistorySpec.BuildOrderPlans(asOfDate, scale);
        var slots = BuildPlanSlots(orderPlans, asOfDate);
        var plans = BuildPlanFacts(slots, asOfDate);
        var urgencies = BuildUrgencyFacts(plans, asOfDate);
        return new WorldHistorySchedulingFacts(plans, urgencies);
    }

    #region 方案槽位（周节奏 + 生命周期分布）

    private sealed record PlanSlot(
        int Index,
        int WeekIndex,
        bool IsRevision,
        DateOnly WeekStart,
        IReadOnlyList<WorldHistoryOrderPlan> Orders);

    private static IReadOnlyList<PlanSlot> BuildPlanSlots(
        IReadOnlyList<WorldHistoryOrderPlan> orderPlans,
        DateOnly asOfDate)
    {
        var byWeek = orderPlans
            .Where(plan => plan.Stage != WorldHistoryOrderStage.Cancelled)
            .GroupBy(plan => WeekStartOf(plan.OrderDate))
            .ToDictionary(group => group.Key, group => group.OrderBy(plan => plan.Index).ToArray());

        var slots = new List<PlanSlot>();
        var index = 0;
        var weeks = WorldHistoryCalendar.WeekCount(asOfDate);
        for (var weekIndex = 0; weekIndex < weeks; weekIndex++)
        {
            var weekStart = WorldHistoryCalendar.WeekStart(weekIndex);
            if (!byWeek.TryGetValue(weekStart, out var weekOrders) || weekOrders.Length == 0)
            {
                continue;
            }

            var selected = weekOrders.Take(MaxOrdersPerPlan).ToArray();
            slots.Add(new PlanSlot(++index, weekIndex, false, weekStart, selected));

            var random = new WorldHistoryRandom($"scheduling-week:{weekIndex:D3}");
            if (random.Chance(WeeklyRevisionProbability) && selected.Length > 1)
            {
                // 重排版：计划员当周缩小了选单范围（去掉队尾约两成）。
                var trimmed = selected.Take(Math.Max(1, selected.Length * 4 / 5)).ToArray();
                slots.Add(new PlanSlot(++index, weekIndex, true, weekStart, trimmed));
            }
        }

        return slots;
    }

    /// <summary>以上线日（周一）为锚的所在周周一。</summary>
    public static DateOnly WeekStartOf(DateOnly date)
    {
        var offset = date.DayNumber - WorldHistoryCalendar.GoLiveDate.DayNumber;
        return offset < 0
            ? WorldHistoryCalendar.GoLiveDate
            : WorldHistoryCalendar.GoLiveDate.AddDays((offset / 7) * 7);
    }

    /// <summary>把日期夹到 [上线日, asOfDate] 并落在工作日（越界向前回退）。</summary>
    public static DateOnly ClampToWindow(DateOnly candidate, DateOnly asOfDate)
    {
        var cursor = candidate < WorldHistoryCalendar.GoLiveDate ? WorldHistoryCalendar.GoLiveDate : candidate;
        if (cursor > asOfDate)
        {
            cursor = asOfDate;
        }

        while (!WorldHistoryCalendar.IsWorkingDay(cursor) && cursor > WorldHistoryCalendar.GoLiveDate)
        {
            cursor = cursor.AddDays(-1);
        }

        return WorldHistoryCalendar.IsWorkingDay(cursor) ? cursor : WorldHistoryCalendar.SnapToWorkingDay(cursor);
    }

    private static IReadOnlyList<WorldHistorySchedulePlanFact> BuildPlanFacts(
        IReadOnlyList<PlanSlot> slots,
        DateOnly asOfDate)
    {
        if (slots.Count == 0)
        {
            return [];
        }

        var upperBound = HistoryUpperBound(asOfDate);
        var pending = Math.Min(PendingGeneratedPlanCount, slots.Count - 1);
        var releasedCount = slots.Count - pending;

        // 撤销候选：不含发布链最后一版（它就是当前唯一的 Released 方案）。
        var revoked = new HashSet<int>();
        for (var position = 0; position < releasedCount - 1 && revoked.Count < MaxRevokedPlanCount; position++)
        {
            var random = new WorldHistoryRandom($"scheduling-revoke:{slots[position].Index:D4}");
            if (random.Chance(RevokeProbability))
            {
                revoked.Add(position);
            }
        }

        var facts = new List<WorldHistorySchedulePlanFact>(slots.Count);
        for (var position = 0; position < slots.Count; position++)
        {
            var slot = slots[position];
            var isReleasedChain = position < releasedCount;
            var status = !isReleasedChain
                ? SchedulePlanLifecycleStatus.Generated
                : revoked.Contains(position)
                    ? SchedulePlanLifecycleStatus.Revoked
                    : position == releasedCount - 1
                        ? SchedulePlanLifecycleStatus.Released
                        : SchedulePlanLifecycleStatus.Superseded;

            var successorPlanId = status == SchedulePlanLifecycleStatus.Superseded
                ? PlanId(slots[position + 1].Index)
                : null;

            facts.Add(BuildPlanFact(
                slot,
                status,
                isReleasedChain ? position + 1L : null,
                successorPlanId,
                asOfDate,
                upperBound));
        }

        return facts;
    }

    #endregion

    #region 单个方案

    private static WorldHistorySchedulePlanFact BuildPlanFact(
        PlanSlot slot,
        SchedulePlanLifecycleStatus status,
        long? releaseRevision,
        string? successorPlanId,
        DateOnly asOfDate,
        DateTimeOffset upperBound)
    {
        var planId = PlanId(slot.Index);
        var problemId = ProblemId(slot.Index);
        var random = new WorldHistoryRandom($"scheduling-plan:{planId}");

        var planningDay = ClampToWindow(slot.WeekStart.AddDays(slot.IsRevision ? 2 : 0), asOfDate);
        var generatedAtUtc = Min(WorldHistoryCalendar.ShiftMoment(planningDay, 0, random.NextInt(0, 420)), upperBound);
        var releasedAtUtc = releaseRevision is null
            ? (DateTimeOffset?)null
            : Min(generatedAtUtc.AddMinutes(random.NextInt(45, 330)), upperBound);
        var revokedAtUtc = status is SchedulePlanLifecycleStatus.Superseded or SchedulePlanLifecycleStatus.Revoked
            ? Min(releasedAtUtc!.Value.AddHours(random.NextInt(20, 140)), upperBound)
            : (DateTimeOffset?)null;

        var horizonStartUtc = WorldHistoryCalendar.ShiftMoment(WorldHistoryCalendar.SnapToWorkingDay(slot.WeekStart), 0, 0);
        var horizonEndUtc = WorldHistoryCalendar.ShiftEnd(
            WorldHistoryCalendar.SnapToWorkingDay(slot.WeekStart.AddDays(HorizonDays - 1)), 1);

        var candidates = BuildOperationCandidates(slot.Orders);
        var unscheduledPositions = ResolveUnscheduledPositions(random, candidates.Count);
        var (assignments, unscheduled) = Schedule(
            planId, candidates, unscheduledPositions, horizonStartUtc, horizonEndUtc);
        var resourceLoads = BuildResourceLoads(planId, assignments, horizonStartUtc, horizonEndUtc);
        var conflicts = BuildConflicts(planId, random, assignments, slot.Orders);
        var metrics = BuildMetrics(assignments, unscheduled, resourceLoads, slot.Orders);
        var problem = BuildProblem(
            problemId, slot.Orders, assignments, unscheduled, horizonStartUtc, horizonEndUtc);
        var fingerprint = Fingerprint(
            $"{planId}|{problemId}|{slot.Orders.Count}|{assignments.Count}|{unscheduled.Count}|{horizonStartUtc:O}|{horizonEndUtc:O}");

        return new WorldHistorySchedulePlanFact(
            Index: slot.Index,
            PlanId: planId,
            ProblemId: problemId,
            ProblemFingerprint: fingerprint,
            WeekStart: slot.WeekStart,
            HorizonStartUtc: horizonStartUtc,
            HorizonEndUtc: horizonEndUtc,
            CapturedAtUtc: generatedAtUtc,
            GeneratedAtUtc: generatedAtUtc,
            ReleasedAtUtc: releasedAtUtc,
            ReleaseRevision: releaseRevision,
            RevokedAtUtc: revokedAtUtc,
            Status: status,
            SupersededByPlanId: successorPlanId,
            Orders: slot.Orders,
            Problem: problem,
            Metrics: metrics,
            Assignments: assignments,
            ResourceLoads: resourceLoads,
            Conflicts: conflicts,
            UnscheduledOperations: unscheduled);
    }

    private sealed record OperationCandidate(
        WorldHistoryOrderPlan Order,
        WorldHistoryOperation Operation,
        string OperationId,
        string WorkCenterId,
        string ResourceId,
        int DurationMinutes);

    private static IReadOnlyList<OperationCandidate> BuildOperationCandidates(
        IReadOnlyList<WorldHistoryOrderPlan> orders)
    {
        var candidates = new List<OperationCandidate>(orders.Count * 8);
        foreach (var order in orders)
        {
            foreach (var sequence in WorldHistoryMesSpec.OperationSequences(order.WorkOrderNo))
            {
                var operation = WorldHistoryMesSpec.Operation(sequence);
                var workCenterId = WorldHistoryMesSpec.WorkCenterCode(order.SkuCode, sequence);
                var pool = WorldHistoryMesSpec.ResourcesIn(workCenterId);
                var resourceId = pool[(order.Index + (sequence / 10)) % pool.Count];
                candidates.Add(new OperationCandidate(
                    order,
                    operation,
                    WorldHistoryMesSpec.OperationTaskId(order.WorkOrderNo, sequence),
                    workCenterId,
                    resourceId,
                    WorldHistoryMesSpec.OperationMinutes(operation, order.Quantity)));
            }
        }

        return candidates;
    }

    /// <summary>不可排工序位置：每方案 0–15 条，且不超过总工序数的 1/12。</summary>
    private static HashSet<int> ResolveUnscheduledPositions(WorldHistoryRandom random, int candidateCount)
    {
        var target = Math.Min(random.NextInt(0, 16), candidateCount / 12);
        var positions = new HashSet<int>();
        for (var step = 1; step <= target; step++)
        {
            positions.Add(step * candidateCount / (target + 1));
        }

        return positions;
    }

    private static (IReadOnlyList<GeneratedScheduleAssignmentSnapshot> Assignments,
        IReadOnlyList<GeneratedUnscheduledOperationSnapshot> Unscheduled) Schedule(
        string planId,
        IReadOnlyList<OperationCandidate> candidates,
        HashSet<int> unscheduledPositions,
        DateTimeOffset horizonStartUtc,
        DateTimeOffset horizonEndUtc)
    {
        var assignments = new List<GeneratedScheduleAssignmentSnapshot>(candidates.Count);
        var unscheduled = new List<GeneratedUnscheduledOperationSnapshot>();
        var resourceCursors = new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);
        var orderCursors = new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);

        for (var position = 0; position < candidates.Count; position++)
        {
            var candidate = candidates[position];
            if (unscheduledPositions.Contains(position))
            {
                unscheduled.Add(BuildUnscheduled(candidate, position));
                continue;
            }

            var earliest = Max(
                horizonStartUtc,
                Max(
                    resourceCursors.GetValueOrDefault(candidate.ResourceId, horizonStartUtc),
                    orderCursors.GetValueOrDefault(candidate.Order.WorkOrderNo, horizonStartUtc)));
            var start = TryPlaceInShift(earliest, candidate.DurationMinutes, horizonEndUtc);
            if (start is null)
            {
                unscheduled.Add(new GeneratedUnscheduledOperationSnapshot(
                    candidate.Order.WorkOrderNo,
                    candidate.OperationId,
                    ScheduleConflictReasonCode.OutsideHorizon,
                    $"{candidate.Order.WorkOrderNo} 的「{candidate.Operation.OperationName}」超出本次两周排产窗口，需并入下一版方案。"));
                continue;
            }

            var end = start.Value.AddMinutes(candidate.DurationMinutes);
            var assignmentRandom = new WorldHistoryRandom($"scheduling-assign:{planId}:{candidate.OperationId}");
            var isLocked = assignmentRandom.Chance(LockedAssignmentProbability);
            assignments.Add(new GeneratedScheduleAssignmentSnapshot(
                AssignmentId: $"{planId}-{candidate.OperationId}",
                OrderId: candidate.Order.WorkOrderNo,
                OperationId: candidate.OperationId,
                OperationSequence: candidate.Operation.Sequence,
                ResourceId: candidate.ResourceId,
                WorkCenterId: candidate.WorkCenterId,
                StartUtc: start.Value,
                EndUtc: end,
                IsLocked: isLocked,
                ExplanationCode: isLocked ? "planner-draft-lock" : "scheduled",
                StandardOperationCode: candidate.Operation.OperationCode));

            resourceCursors[candidate.ResourceId] = end;
            orderCursors[candidate.Order.WorkOrderNo] = end;
        }

        return (assignments, unscheduled);
    }

    private static GeneratedUnscheduledOperationSnapshot BuildUnscheduled(OperationCandidate candidate, int position)
    {
        var (reason, message) = (position % 4) switch
        {
            0 => (ScheduleConflictReasonCode.Material,
                $"{candidate.Order.WorkOrderNo} 的「{candidate.Operation.OperationName}」齐套缺件，物料未到位不参与本次排产。"),
            1 => (ScheduleConflictReasonCode.NoEligibleResource,
                $"{candidate.WorkCenterId} 当前无可用合格设备承接 {candidate.Order.WorkOrderNo} 的「{candidate.Operation.OperationName}」。"),
            2 => (ScheduleConflictReasonCode.Capacity,
                $"{candidate.WorkCenterId} 在本窗口内产能已排满，{candidate.Order.WorkOrderNo} 的「{candidate.Operation.OperationName}」顺延待排。"),
            _ => (ScheduleConflictReasonCode.PredecessorUnscheduled,
                $"{candidate.Order.WorkOrderNo} 的前道工序未能排入，「{candidate.Operation.OperationName}」随之挂起。"),
        };

        return new GeneratedUnscheduledOperationSnapshot(
            candidate.Order.WorkOrderNo,
            candidate.OperationId,
            reason,
            message);
    }

    /// <summary>把工序放进最近一个容得下它的班次窗口（早班 08:00–16:00 / 中班 16:00–24:00，周日停产）。</summary>
    public static DateTimeOffset? TryPlaceInShift(
        DateTimeOffset earliest,
        int durationMinutes,
        DateTimeOffset horizonEndUtc)
    {
        var local = earliest.ToOffset(WorldHistoryCalendar.SiteUtcOffset);
        var day = WorldHistoryCalendar.SnapToWorkingDay(DateOnly.FromDateTime(local.DateTime));
        var shift = 0;
        for (var probe = 0; probe < MaxShiftProbes; probe++)
        {
            var shiftStart = WorldHistoryCalendar.ShiftMoment(day, shift, 0);
            if (shiftStart > horizonEndUtc)
            {
                return null;
            }

            var shiftEnd = shiftStart.AddHours(WorldHistoryCalendar.ShiftLengthHours);
            var start = Max(earliest, shiftStart);
            if (start.AddMinutes(durationMinutes) <= shiftEnd)
            {
                return start.AddMinutes(durationMinutes) > horizonEndUtc ? null : start;
            }

            (day, shift) = shift == 0
                ? (day, 1)
                : (WorldHistoryCalendar.SnapToWorkingDay(day.AddDays(1)), 0);
        }

        return null;
    }

    #endregion

    #region 资源负荷 / 冲突 / 指标

    private static IReadOnlyList<GeneratedScheduleResourceLoadSnapshot> BuildResourceLoads(
        string planId,
        IReadOnlyList<GeneratedScheduleAssignmentSnapshot> assignments,
        DateTimeOffset horizonStartUtc,
        DateTimeOffset horizonEndUtc)
    {
        var loads = new List<GeneratedScheduleResourceLoadSnapshot>();
        foreach (var group in assignments
                     .GroupBy(x => x.ResourceId, StringComparer.Ordinal)
                     .OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            var assigned = (int)group.Sum(x => (x.EndUtc - x.StartUtc).TotalMinutes);
            if (assigned <= 0)
            {
                continue;
            }

            var random = new WorldHistoryRandom($"scheduling-load:{planId}:{group.Key}");
            var isBottleneck = WorldHistoryMesSpec.BottleneckWorkCenters.Contains(WorkCenterOf(group.Key));
            // 瓶颈线（电泳 / 性能终检）利用率压在 0.95 以上，其余落在 0.55–0.95。
            var target = isBottleneck
                ? 0.95 + (random.NextInt(0, 8) / 100.0)
                : 0.55 + (random.NextInt(0, 40) / 100.0);
            var available = Math.Max(1, (int)Math.Round(assigned / target, MidpointRounding.AwayFromZero));
            loads.Add(new GeneratedScheduleResourceLoadSnapshot(
                group.Key,
                horizonStartUtc,
                horizonEndUtc,
                assigned,
                available,
                decimal.Round((decimal)assigned / available, 6, MidpointRounding.AwayFromZero)));
        }

        return loads;
    }

    private static IReadOnlyList<GeneratedScheduleConflictSnapshot> BuildConflicts(
        string planId,
        WorldHistoryRandom random,
        IReadOnlyList<GeneratedScheduleAssignmentSnapshot> assignments,
        IReadOnlyList<WorldHistoryOrderPlan> orders)
    {
        var count = assignments.Count == 0 ? 0 : random.NextInt(0, 9);
        if (count == 0)
        {
            return [];
        }

        var dueByOrder = orders.ToDictionary(
            order => order.WorkOrderNo,
            order => order.RequiredDate,
            StringComparer.Ordinal);
        var conflicts = new List<GeneratedScheduleConflictSnapshot>(count);
        for (var ordinal = 0; ordinal < count; ordinal++)
        {
            var anchor = assignments[(ordinal + 1) * assignments.Count / (count + 1) % assignments.Count];
            var operationName = OperationNameOf(anchor.OperationSequence);
            var (reason, message) = (ordinal % 5) switch
            {
                0 => (ScheduleConflictReasonCode.Capacity,
                    $"{anchor.WorkCenterId} 在 {Local(anchor.StartUtc):MM-dd HH:mm} 前后产能超载，{anchor.OrderId} 的「{operationName}」需要顺延。"),
                1 => (ScheduleConflictReasonCode.Material,
                    $"{anchor.OrderId} 的「{operationName}」齐套时间晚于计划开工时间 {Local(anchor.StartUtc):MM-dd HH:mm}，存在待料风险。"),
                2 => (ScheduleConflictReasonCode.DueDate,
                    $"{anchor.OrderId} 按当前排程将于 {Local(anchor.EndUtc):MM-dd} 完工，晚于交期 {dueByOrder.GetValueOrDefault(anchor.OrderId, DateOnly.FromDateTime(anchor.EndUtc.UtcDateTime)):MM-dd}。"),
                3 => (ScheduleConflictReasonCode.NoEligibleResource,
                    $"{anchor.WorkCenterId} 内符合「{operationName}」能力要求的设备均已占满，{anchor.OrderId} 暂无合格资源。"),
                _ => (ScheduleConflictReasonCode.Equipment,
                    $"{anchor.ResourceId} 处于维保窗口，{anchor.OrderId} 的「{operationName}」被迫改派其他设备。"),
            };

            conflicts.Add(new GeneratedScheduleConflictSnapshot(
                ConflictId: $"{planId}-CF-{ordinal + 1:D2}",
                ReasonCode: reason,
                // 少量 Error、多数 Warning：真实排产里硬冲突远少于提示。
                Severity: ordinal % 4 == 0 ? ScheduleConflictSeverity.Error : ScheduleConflictSeverity.Warning,
                OrderId: anchor.OrderId,
                OperationId: anchor.OperationId,
                ResourceId: anchor.ResourceId,
                Message: message));
        }

        return conflicts;
    }

    private static GeneratedSchedulePlanMetricsSnapshot BuildMetrics(
        IReadOnlyList<GeneratedScheduleAssignmentSnapshot> assignments,
        IReadOnlyList<GeneratedUnscheduledOperationSnapshot> unscheduled,
        IReadOnlyList<GeneratedScheduleResourceLoadSnapshot> resourceLoads,
        IReadOnlyList<WorldHistoryOrderPlan> orders)
    {
        var dueByOrder = orders.ToDictionary(
            order => order.WorkOrderNo,
            order => DueUtc(order),
            StringComparer.Ordinal);
        var locked = assignments.Count(x => x.IsLocked);
        var optimizable = assignments.Count - locked;
        var lateAssignments = assignments
            .Where(x => !x.IsLocked && dueByOrder.TryGetValue(x.OrderId, out var due) && x.EndUtc > due)
            .ToArray();
        var tardiness = (int)lateAssignments.Sum(x => (x.EndUtc - dueByOrder[x.OrderId]).TotalMinutes);
        var assignedMinutes = (int)assignments.Sum(x => (x.EndUtc - x.StartUtc).TotalMinutes);
        var makespan = assignments.Count == 0
            ? 0
            : (int)(assignments.Max(x => x.EndUtc) - assignments.Min(x => x.StartUtc)).TotalMinutes;
        var totalAvailable = resourceLoads.Sum(x => (long)x.AvailableMinutes);

        return new GeneratedSchedulePlanMetricsSnapshot(
            ScheduledOperationCount: assignments.Count,
            UnscheduledOperationCount: unscheduled.Count,
            AssignedMinutes: assignedMinutes,
            MakespanMinutes: makespan,
            TotalTardinessMinutes: tardiness,
            LateOperationCount: lateAssignments.Length,
            OnTimeRate: optimizable == 0
                ? 1m
                : decimal.Round((decimal)(optimizable - lateAssignments.Length) / optimizable, 6, MidpointRounding.AwayFromZero),
            AverageResourceUtilization: totalAvailable == 0
                ? 0m
                : decimal.Round((decimal)assignedMinutes / totalAvailable, 6, MidpointRounding.AwayFromZero),
            LockedOperationCount: locked,
            OptimizableOperationCount: optimizable);
    }

    #endregion

    #region 问题快照（必须能被 CreateSchedulePlanRevisionCommandHandler 反序列化重建）

    /// <summary>
    /// 资源不可用窗口（换型 / 换线 / 设备维护 / 计划停机）：按确定性规则塞进每台设备**已排工序之间的空档**，
    /// 不与任何已排工序重叠——真实工厂的换型和保养就发生在两批活之间。
    /// 甘特读面据此画出可辨识的底纹，图例也只列真正出现过的那几类。
    /// </summary>
    private static IReadOnlyList<SchedulingUnavailabilityWindowContract> BuildUnavailabilityWindows(
        IReadOnlyList<GeneratedScheduleAssignmentSnapshot> assignments,
        DateTimeOffset horizonEndUtc)
    {
        // (语义, 原因码, 目标时长分钟)。按资源轮换，保证四类语义在一版方案里都能出现。
        (string ReasonCode, int Minutes)[] kinds =
        [
            ("changeover.setup", 30),
            ("line-change", 45),
            ("maintenance.preventive", 90),
            ("downtime.planned", 60),
        ];
        const int MinGapMinutes = 40;
        const int MaxWindowsPerResource = 2;
        const int MaxWindows = 32;

        var windows = new List<SchedulingUnavailabilityWindowContract>();
        var resourceIndex = 0;
        foreach (var group in assignments
                     .GroupBy(x => x.ResourceId, StringComparer.Ordinal)
                     .OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            var kind = kinds[resourceIndex++ % kinds.Length];
            var ordered = group.OrderBy(x => x.StartUtc).ToArray();
            var placed = 0;
            for (var i = 1; i < ordered.Length && placed < MaxWindowsPerResource && windows.Count < MaxWindows; i++)
            {
                var gapStart = ordered[i - 1].EndUtc;
                var gapEnd = ordered[i].StartUtc;
                var gapMinutes = (gapEnd - gapStart).TotalMinutes;
                if (gapMinutes < MinGapMinutes)
                {
                    continue;
                }

                var minutes = (int)Math.Min(kind.Minutes, gapMinutes - 10);
                var end = gapStart.AddMinutes(minutes);
                if (end > horizonEndUtc)
                {
                    break;
                }

                windows.Add(new SchedulingUnavailabilityWindowContract(
                    ResourceId: group.Key,
                    WorkCenterId: ordered[i - 1].WorkCenterId,
                    StartUtc: gapStart,
                    EndUtc: end,
                    ReasonCode: kind.ReasonCode));
                placed++;
            }
        }

        return windows
            .OrderBy(x => x.StartUtc)
            .ThenBy(x => x.ResourceId, StringComparer.Ordinal)
            .ToArray();
    }

    private static SchedulingProblemContract BuildProblem(
        string problemId,
        IReadOnlyList<WorldHistoryOrderPlan> orders,
        IReadOnlyList<GeneratedScheduleAssignmentSnapshot> assignments,
        IReadOnlyList<GeneratedUnscheduledOperationSnapshot> unscheduled,
        DateTimeOffset horizonStartUtc,
        DateTimeOffset horizonEndUtc)
    {
        var assignedResources = assignments.ToDictionary(x => x.OperationId, x => x.ResourceId, StringComparer.Ordinal);
        var orderContracts = new List<SchedulingOrderContract>(orders.Count);
        foreach (var order in orders)
        {
            var random = new WorldHistoryRandom($"scheduling-order:{order.WorkOrderNo}");
            var isRush = random.Chance(0.12);
            var priority = isRush ? random.NextInt(80, 120) : random.NextInt(1, 40);
            var dueUtc = DueUtc(order);

            var operations = new List<SchedulingOperationContract>(8);
            string? predecessorId = null;
            foreach (var sequence in WorldHistoryMesSpec.OperationSequences(order.WorkOrderNo))
            {
                var operation = WorldHistoryMesSpec.Operation(sequence);
                var workCenterId = WorldHistoryMesSpec.WorkCenterCode(order.SkuCode, sequence);
                var operationId = WorldHistoryMesSpec.OperationTaskId(order.WorkOrderNo, sequence);
                var pool = WorldHistoryMesSpec.ResourcesIn(workCenterId);
                operations.Add(new SchedulingOperationContract(
                    OperationId: operationId,
                    OperationSequence: sequence,
                    PredecessorOperationIds: predecessorId is null ? [] : [predecessorId],
                    DurationMinutes: WorldHistoryMesSpec.OperationMinutes(operation, order.Quantity),
                    RequiredCapabilityCode: WorldHistoryMesSpec.CapabilityCode(workCenterId),
                    EligibleResourceIds: pool,
                    PrimaryResourceId: assignedResources.GetValueOrDefault(operationId, pool[0]),
                    EarliestStartUtc: horizonStartUtc,
                    DueUtc: dueUtc,
                    Priority: priority,
                    IsRush: isRush,
                    SplitPolicy: ScheduleSplitPolicyContract.NonSplittable,
                    MaterialReadyUtc: horizonStartUtc,
                    QualityBlockReason: null,
                    SourceReference: order.SalesOrderNo,
                    SetupMinutes: operation.SetupMinutes));
                predecessorId = operationId;
            }

            orderContracts.Add(new SchedulingOrderContract(
                OrderId: order.WorkOrderNo,
                SkuCode: order.SkuCode,
                Quantity: order.Quantity,
                DueUtc: dueUtc,
                Priority: priority,
                IsRush: isRush,
                Operations: operations,
                BusinessReference: order.SalesOrderNo));
        }

        var resources = new List<SchedulingResourceContract>();
        var sortKey = 0;
        foreach (var workCenterId in WorldHistoryMesSpec.WorkCenterCodes)
        {
            foreach (var resourceId in WorldHistoryMesSpec.ResourcesIn(workCenterId))
            {
                resources.Add(new SchedulingResourceContract(
                    resourceId,
                    workCenterId,
                    [WorldHistoryMesSpec.CapabilityCode(workCenterId)],
                    1,
                    WorldHistoryMesSpec.CalendarId,
                    (++sortKey * 10).ToString("D4", CultureInfo.InvariantCulture)));
            }
        }

        var shiftWindows = new List<SchedulingTimeWindowContract>();
        for (var offset = 0; offset < HorizonDays; offset++)
        {
            var day = DateOnly.FromDateTime(horizonStartUtc.ToOffset(WorldHistoryCalendar.SiteUtcOffset).DateTime).AddDays(offset);
            if (!WorldHistoryCalendar.IsWorkingDay(day))
            {
                continue;
            }

            for (var shift = 0; shift < 2; shift++)
            {
                var start = WorldHistoryCalendar.ShiftMoment(day, shift, 0);
                shiftWindows.Add(new SchedulingTimeWindowContract(
                    start,
                    start.AddHours(WorldHistoryCalendar.ShiftLengthHours),
                    shift == 0 ? "early-shift" : "middle-shift"));
            }
        }

        var materialReadiness = unscheduled
            .Where(x => x.ReasonCode == ScheduleConflictReasonCode.Material)
            .Select(x => new SchedulingMaterialReadinessContract(
                ScopeType: "order",
                ScopeId: x.OrderId,
                MaterialReadyUtc: null,
                IsReady: false,
                ReasonCodes: ["material.shortage"]))
            .DistinctBy(x => x.ScopeId, StringComparer.Ordinal)
            .ToArray();

        var lockedAssignments = assignments
            .Where(x => x.IsLocked)
            .Select(x => new SchedulingLockedAssignmentContract(
                x.AssignmentId,
                x.OrderId,
                x.OperationId,
                x.OperationSequence,
                x.ResourceId,
                x.WorkCenterId,
                x.StartUtc,
                x.EndUtc,
                "planner-draft-lock"))
            .ToArray();

        return new SchedulingProblemContract(
            ContractVersion: ContractVersion,
            ProblemId: problemId,
            OrganizationId: string.Empty,
            EnvironmentId: string.Empty,
            HorizonStartUtc: horizonStartUtc,
            HorizonEndUtc: horizonEndUtc,
            Orders: orderContracts,
            Resources: resources,
            Calendars: [new SchedulingCalendarContract(WorldHistoryMesSpec.CalendarId, shiftWindows)],
            UnavailabilityWindows: BuildUnavailabilityWindows(assignments, horizonEndUtc),
            MaterialReadiness: materialReadiness,
            QualityBlocks: [],
            LockedAssignments: lockedAssignments);
    }

    #endregion

    #region 订单紧急度

    private static IReadOnlyList<WorldHistoryUrgencyFact> BuildUrgencyFacts(
        IReadOnlyList<WorldHistorySchedulePlanFact> plans,
        DateOnly asOfDate)
    {
        // 每个工单只留最后一次出现的方案上下文：紧急度读面只看最新一条快照。
        var latest = new Dictionary<string, (WorldHistorySchedulePlanFact Plan, WorldHistoryOrderPlan Order)>(StringComparer.Ordinal);
        foreach (var plan in plans)
        {
            foreach (var order in plan.Orders)
            {
                latest[order.WorkOrderNo] = (plan, order);
            }
        }

        var upperBound = HistoryUpperBound(asOfDate);
        var facts = new List<WorldHistoryUrgencyFact>(latest.Count);
        foreach (var (workOrderNo, context) in latest.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            var (plan, order) = context;
            var random = new WorldHistoryRandom($"scheduling-urgency:{plan.PlanId}:{workOrderNo}");
            var remainingMinutes = plan.Assignments
                .Where(x => string.Equals(x.OrderId, workOrderNo, StringComparison.Ordinal))
                .Sum(x => (x.EndUtc - x.StartUtc).TotalMinutes);
            var calculatedAtUtc = Min(plan.GeneratedAtUtc, upperBound);
            var observedAtUtc = calculatedAtUtc.AddMinutes(-random.NextInt(5, 180));
            var isStale = random.Chance(0.15);

            var risks = new List<ExecutionRiskFact>(2);
            if (random.Chance(0.18))
            {
                risks.Add(new ExecutionRiskFact(
                    "material.shortage", ExecutionRiskCategory.Material, true, plan.PlanId, observedAtUtc));
            }

            if (random.Chance(0.12))
            {
                risks.Add(new ExecutionRiskFact(
                    "equipment.maintenanceWindow", ExecutionRiskCategory.Equipment, false, plan.PlanId, observedAtUtc));
            }

            facts.Add(new WorldHistoryUrgencyFact(
                OrderId: workOrderNo,
                BusinessReference: order.SalesOrderNo,
                CalculatedAtUtc: calculatedAtUtc,
                CalculationBucketUtc: Bucket(calculatedAtUtc),
                DueUtc: DueUtc(order),
                RemainingCycle: TimeSpan.FromMinutes(remainingMinutes),
                ExecutionRisks: risks,
                IsSourceStale: isStale,
                FactsObservedAtUtc: observedAtUtc,
                InputFingerprint: Fingerprint($"world-history|{plan.PlanId}|{workOrderNo}|{remainingMinutes}")));
        }

        return facts;
    }

    /// <summary>
    /// 与 <c>OrderUrgencyService</c> 同一的 15 分钟计算桶（该服务里的同名方法是私有的，
    /// 这里按同一字面量重复声明，保证 seed 写入的快照与运行时刷新互相幂等）。
    /// </summary>
    public static DateTimeOffset Bucket(DateTimeOffset value)
    {
        var utc = value.ToUniversalTime();
        var minutes = utc.Minute - (utc.Minute % 15);
        return new DateTimeOffset(utc.Year, utc.Month, utc.Day, utc.Hour, minutes, 0, TimeSpan.Zero);
    }

    /// <summary>把紧急度事实转换成模型输入（业务优先级取权威默认 P2：优先级表本身不由本引擎写入）。</summary>
    public static OrderUrgencyCalculationInput ToCalculationInput(WorldHistoryUrgencyFact fact)
    {
        ArgumentNullException.ThrowIfNull(fact);
        return new OrderUrgencyCalculationInput(
            fact.OrderId,
            fact.BusinessReference,
            fact.CalculatedAtUtc,
            fact.DueUtc,
            fact.RemainingCycle,
            DefaultBusinessPriority,
            fact.ExecutionRisks,
            IsSourceMissing: false,
            IsSourceStale: fact.IsSourceStale,
            FactsObservedAtUtc: fact.FactsObservedAtUtc,
            InputFingerprint: fact.InputFingerprint);
    }

    /// <summary>与 <c>OrderUrgencyService.DefaultPriority()</c> 同形：无人工干预时的权威默认。</summary>
    public static BusinessPriorityFact DefaultBusinessPriority { get; } = new(
        BusinessPriorityLevel.P2,
        "authoritative-default",
        "No manual business-priority override.",
        DateTimeOffset.UnixEpoch,
        null,
        0);

    #endregion

    #region 工具

    /// <summary>
    /// 给问题快照打上租户作用域。事实流本身与 org/env 无关（保持纯函数、可跨环境复用），
    /// 落库前才补上——「锁定重预览」会把快照里的 <c>OrganizationId</c>/<c>EnvironmentId</c>
    /// 原样带进新方案，留空会让重预览写出无主的方案。
    /// </summary>
    public static SchedulingProblemContract Scope(
        SchedulingProblemContract problem,
        string organizationId,
        string environmentId)
    {
        ArgumentNullException.ThrowIfNull(problem);
        return problem with { OrganizationId = organizationId, EnvironmentId = environmentId };
    }

    /// <summary>订单交期（本地当日收班时刻换算成 UTC）。</summary>
    public static DateTimeOffset DueUtc(WorldHistoryOrderPlan order)
    {
        ArgumentNullException.ThrowIfNull(order);
        return new DateTimeOffset(
            order.RequiredDate.ToDateTime(new TimeOnly(23, 59)),
            WorldHistoryCalendar.SiteUtcOffset).ToUniversalTime();
    }

    public static string OperationNameOf(int sequence) => WorldHistoryMesSpec.Operation(sequence).OperationName;

    private static DateTimeOffset Local(DateTimeOffset value) => value.ToOffset(WorldHistoryCalendar.SiteUtcOffset);

    private static DateTimeOffset Min(DateTimeOffset left, DateTimeOffset right) => left <= right ? left : right;

    private static DateTimeOffset Max(DateTimeOffset left, DateTimeOffset right) => left >= right ? left : right;

    private static string Fingerprint(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    #endregion
}

/// <summary>排产域全量事实流。</summary>
public sealed record WorldHistorySchedulingFacts(
    IReadOnlyList<WorldHistorySchedulePlanFact> Plans,
    IReadOnlyList<WorldHistoryUrgencyFact> Urgencies)
{
    public int AssignmentCount => Plans.Sum(x => x.Assignments.Count);
    public int ResourceLoadCount => Plans.Sum(x => x.ResourceLoads.Count);
    public int ConflictCount => Plans.Sum(x => x.Conflicts.Count);
    public int UnscheduledOperationCount => Plans.Sum(x => x.UnscheduledOperations.Count);

    public int CountOf(SchedulePlanLifecycleStatus status) => Plans.Count(x => x.Status == status);
}

/// <summary>一个历史排产方案的完整事实（问题快照 + 方案头 + 四张明细）。</summary>
public sealed record WorldHistorySchedulePlanFact(
    int Index,
    string PlanId,
    string ProblemId,
    string ProblemFingerprint,
    DateOnly WeekStart,
    DateTimeOffset HorizonStartUtc,
    DateTimeOffset HorizonEndUtc,
    DateTimeOffset CapturedAtUtc,
    DateTimeOffset GeneratedAtUtc,
    DateTimeOffset? ReleasedAtUtc,
    long? ReleaseRevision,
    DateTimeOffset? RevokedAtUtc,
    SchedulePlanLifecycleStatus Status,
    string? SupersededByPlanId,
    IReadOnlyList<WorldHistoryOrderPlan> Orders,
    SchedulingProblemContract Problem,
    GeneratedSchedulePlanMetricsSnapshot Metrics,
    IReadOnlyList<GeneratedScheduleAssignmentSnapshot> Assignments,
    IReadOnlyList<GeneratedScheduleResourceLoadSnapshot> ResourceLoads,
    IReadOnlyList<GeneratedScheduleConflictSnapshot> Conflicts,
    IReadOnlyList<GeneratedUnscheduledOperationSnapshot> UnscheduledOperations)
{
    /// <summary>写入聚合用的生成态快照（<c>Status</c> 恒为 Generated，生命周期由领域动作推进）。</summary>
    public GeneratedSchedulePlanSnapshot ToGeneratedSnapshot(string algorithmVersion) => new(
        PlanId,
        ProblemId,
        ProblemFingerprint,
        algorithmVersion,
        WorldHistorySchedulingSpec.ContractVersion,
        GeneratedAtUtc,
        SchedulePlanInputStatus.Generated,
        Metrics,
        Assignments,
        ResourceLoads,
        Conflicts,
        UnscheduledOperations);
}

/// <summary>一条订单紧急度事实（覆盖方案里出现过的每个工单）。</summary>
public sealed record WorldHistoryUrgencyFact(
    string OrderId,
    string BusinessReference,
    DateTimeOffset CalculatedAtUtc,
    DateTimeOffset CalculationBucketUtc,
    DateTimeOffset DueUtc,
    TimeSpan RemainingCycle,
    IReadOnlyList<ExecutionRiskFact> ExecutionRisks,
    bool IsSourceStale,
    DateTimeOffset FactsObservedAtUtc,
    string InputFingerprint);
