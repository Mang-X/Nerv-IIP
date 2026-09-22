using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Nerv.IIP.Business.Scheduling.Domain.Services;
using Nerv.IIP.Contracts.EquipmentRuntime;
using Nerv.IIP.Contracts.Scheduling;

namespace Nerv.IIP.Business.Scheduling.Web.Application.Seed;

/// <summary>
/// 《工厂世界观设定集》L1 背景历史引擎的 **排产域侧规格**：
/// 把 ERP/MES 共享的 <see cref="WorldHistorySpec.BuildOrderPlans"/> 订单计划表
/// 投影成「每周一个排产问题」的确定性事实流——问题快照（工单 / 路线 / 日历 / 资源 / 不可用窗口）
/// 与订单紧急度快照。
///
/// **本规格只造引擎的输入，不造引擎的产出**：排产方案、资源分配、资源负荷、冲突与不可排工序
/// 一律由 <c>FiniteCapacityScheduler</c> 在用户点「生成」时现场算出来。种子回填方案会
/// 盖着 <c>aps-lite-v1</c> 的算法版本章、却从来没有被该算法算过，任何排程行为变更都不会反映到
/// 演示数据上（#3594）。
///
/// 与 MES 的一致性靠 <see cref="WorldHistoryMesSpec"/> 的确定性纯函数镜像达成：
/// 工单号 <c>WO-2026-#####</c>、工序号 <c>{工单号}-OP-{序号}</c>、工作中心 <c>WC-*</c>
/// 两侧逐字对上，排产库既不跨库查 MES 也不建跨 schema 外键。
/// </summary>
public static class WorldHistorySchedulingSpec
{
    /// <summary>排产契约版本（与 <c>SchedulingProblemProducer</c> 产出的快照同版）。</summary>
    public const int ContractVersion = 1;

    /// <summary>单个问题纳入的工单上限：60 单 × 6–8 工序 ≈ 200–500 道工序。</summary>
    public const int MaxOrdersPerProblem = 60;

    /// <summary>排产展望期：两周滚动。</summary>
    public const int HorizonDays = 14;

    #region §9 号段（与 -DEMO- / -SCALE- 保留段严格隔离）

    public const string ProblemNumberPrefix = "SPB-2026-";

    public static string ProblemId(int index) => $"{ProblemNumberPrefix}{index:D4}";

    /// <summary>保留号段：固定演示事实与千单规模块，世界观历史绝不可撞入。</summary>
    public static readonly string[] ReservedInfixes = ["-DEMO-", "-SCALE-"];

    #endregion

    /// <summary>历史窗口上界：<paramref name="asOfDate"/> 当日 23:59:59.999（UTC）。</summary>
    public static DateTimeOffset HistoryUpperBound(DateOnly asOfDate) =>
        new(asOfDate.ToDateTime(new TimeOnly(23, 59, 59, 999)), TimeSpan.Zero);

    /// <summary>生成排产域全量事实流。</summary>
    public static WorldHistorySchedulingFacts BuildSchedulingFacts(DateOnly asOfDate, double scale)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(scale);
        if (asOfDate < WorldHistoryCalendar.GoLiveDate)
        {
            asOfDate = WorldHistoryCalendar.GoLiveDate;
        }

        var orderPlans = WorldHistorySpec.BuildOrderPlans(asOfDate, scale);
        var problems = BuildProblemFacts(BuildProblemSlots(orderPlans, asOfDate), asOfDate);
        var urgencies = BuildUrgencyFacts(problems, asOfDate);
        return new WorldHistorySchedulingFacts(problems, urgencies);
    }

    #region 问题槽位（周节奏）

    private sealed record ProblemSlot(int Index, DateOnly WeekStart, IReadOnlyList<WorldHistoryOrderPlan> Orders);

    private static IReadOnlyList<ProblemSlot> BuildProblemSlots(
        IReadOnlyList<WorldHistoryOrderPlan> orderPlans,
        DateOnly asOfDate)
    {
        var byWeek = orderPlans
            .Where(plan => plan.Stage != WorldHistoryOrderStage.Cancelled)
            .GroupBy(plan => WeekStartOf(plan.OrderDate))
            .ToDictionary(group => group.Key, group => group.OrderBy(plan => plan.Index).ToArray());

        var slots = new List<ProblemSlot>();
        var index = 0;
        var weeks = WorldHistoryCalendar.WeekCount(asOfDate);
        for (var weekIndex = 0; weekIndex < weeks; weekIndex++)
        {
            var weekStart = WorldHistoryCalendar.WeekStart(weekIndex);
            if (!byWeek.TryGetValue(weekStart, out var weekOrders) || weekOrders.Length == 0)
            {
                continue;
            }

            slots.Add(new ProblemSlot(++index, weekStart, weekOrders.Take(MaxOrdersPerProblem).ToArray()));
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

    private static IReadOnlyList<WorldHistoryScheduleProblemFact> BuildProblemFacts(
        IReadOnlyList<ProblemSlot> slots,
        DateOnly asOfDate)
    {
        var upperBound = HistoryUpperBound(asOfDate);
        return slots.Select(slot => BuildProblemFact(slot, asOfDate, upperBound)).ToArray();
    }

    #endregion

    #region 单个问题快照（必须能被 CreateSchedulePlanRevisionCommandHandler 反序列化重建）

    private static WorldHistoryScheduleProblemFact BuildProblemFact(
        ProblemSlot slot,
        DateOnly asOfDate,
        DateTimeOffset upperBound)
    {
        var problemId = ProblemId(slot.Index);
        var random = new WorldHistoryRandom($"scheduling-problem:{problemId}");

        var planningDay = ClampToWindow(slot.WeekStart, asOfDate);
        var capturedAtUtc = Min(WorldHistoryCalendar.ShiftMoment(planningDay, 0, random.NextInt(0, 420)), upperBound);

        var horizonStartUtc = WorldHistoryCalendar.ShiftMoment(WorldHistoryCalendar.SnapToWorkingDay(slot.WeekStart), 0, 0);
        var horizonEndUtc = WorldHistoryCalendar.ShiftEnd(
            WorldHistoryCalendar.SnapToWorkingDay(slot.WeekStart.AddDays(HorizonDays - 1)), 1);

        var problem = BuildProblem(problemId, slot.Orders, horizonStartUtc, horizonEndUtc);
        var fingerprint = Fingerprint(
            $"{problemId}|{slot.Orders.Count}|{problem.Orders.Sum(x => x.Operations.Count)}|{horizonStartUtc:O}|{horizonEndUtc:O}");

        return new WorldHistoryScheduleProblemFact(
            ProblemId: problemId,
            ProblemFingerprint: fingerprint,
            WeekStart: slot.WeekStart,
            HorizonStartUtc: horizonStartUtc,
            HorizonEndUtc: horizonEndUtc,
            CapturedAtUtc: capturedAtUtc,
            Orders: slot.Orders,
            Problem: problem);
    }

    /// <summary>
    /// 资源不可用窗口（换型 / 换线 / 设备维护 / 计划停机）：按确定性规则铺在工作日的班次内，
    /// 每台设备至多一条，四类原因码轮换——真实工厂的换型和保养就发生在班中。
    /// 甘特读面据此画出可辨识的底纹，图例也只列真正出现过的那几类。
    /// </summary>
    private static IReadOnlyList<SchedulingUnavailabilityWindowContract> BuildUnavailabilityWindows(
        IReadOnlyList<SchedulingResourceContract> resources,
        DateTimeOffset horizonStartUtc,
        DateTimeOffset horizonEndUtc)
    {
        (string ReasonCode, int Minutes)[] kinds =
        [
            ("changeover.setup", 30),
            ("line-change", 45),
            ("maintenance.preventive", 90),
            ("downtime.planned", 60),
        ];

        var windows = new List<SchedulingUnavailabilityWindowContract>();
        var resourceIndex = 0;
        foreach (var resource in resources.OrderBy(x => x.ResourceId, StringComparer.Ordinal))
        {
            var ordinal = resourceIndex++;
            var kind = kinds[ordinal % kinds.Length];
            var random = new WorldHistoryRandom($"scheduling-unavailability:{resource.ResourceId}");

            // 落在展望期内第 (ordinal % HorizonDays) 个工作日的早班班中。
            var day = WorldHistoryCalendar.SnapToWorkingDay(
                DateOnly.FromDateTime(horizonStartUtc.ToOffset(WorldHistoryCalendar.SiteUtcOffset).DateTime)
                    .AddDays(ordinal % HorizonDays));
            var start = WorldHistoryCalendar.ShiftMoment(day, ordinal % 2, random.NextInt(1, 6) * 30);
            var end = start.AddMinutes(kind.Minutes);
            if (start < horizonStartUtc || end > horizonEndUtc)
            {
                continue;
            }

            windows.Add(new SchedulingUnavailabilityWindowContract(
                ResourceId: resource.ResourceId,
                WorkCenterId: resource.WorkCenterId,
                StartUtc: start,
                EndUtc: end,
                ReasonCode: kind.ReasonCode));
        }

        return windows
            .OrderBy(x => x.StartUtc)
            .ThenBy(x => x.ResourceId, StringComparer.Ordinal)
            .ToArray();
    }

    private static SchedulingProblemContract BuildProblem(
        string problemId,
        IReadOnlyList<WorldHistoryOrderPlan> orders,
        DateTimeOffset horizonStartUtc,
        DateTimeOffset horizonEndUtc)
    {
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
                    // 计划员的建议设备：按工单序在设备池里轮换，把负荷摊开。排程可以改派。
                    PrimaryResourceId: pool[(order.Index + (sequence / 10)) % pool.Count],
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
            UnavailabilityWindows: BuildUnavailabilityWindows(resources, horizonStartUtc, horizonEndUtc),
            // 齐套与质量门禁是生成方案时的实时读数（ISchedulingMaterialReadinessProvider /
            // 质量域），不属于种子事实；锁定工序是计划员在已有方案上的动作，种子无方案可锁。
            MaterialReadiness: [],
            QualityBlocks: [],
            LockedAssignments: []);
    }

    #endregion

    #region 订单紧急度

    private static IReadOnlyList<WorldHistoryUrgencyFact> BuildUrgencyFacts(
        IReadOnlyList<WorldHistoryScheduleProblemFact> problems,
        DateOnly asOfDate)
    {
        // 每个工单只留最后一次出现的问题上下文：紧急度读面只看最新一条快照。
        var latest = new Dictionary<string, (WorldHistoryScheduleProblemFact Problem, WorldHistoryOrderPlan Order)>(StringComparer.Ordinal);
        foreach (var problem in problems)
        {
            foreach (var order in problem.Orders)
            {
                latest[order.WorkOrderNo] = (problem, order);
            }
        }

        var upperBound = HistoryUpperBound(asOfDate);
        var facts = new List<WorldHistoryUrgencyFact>(latest.Count);
        foreach (var (workOrderNo, context) in latest.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            var (problem, order) = context;
            var random = new WorldHistoryRandom($"scheduling-urgency:{problem.ProblemId}:{workOrderNo}");
            // 剩余工期 = 该工单在本次问题里的全部工序工时（排产前的应有节拍，与引擎读的是同一个数）。
            var remainingMinutes = problem.Problem.Orders
                .Where(x => string.Equals(x.OrderId, workOrderNo, StringComparison.Ordinal))
                .SelectMany(x => x.Operations)
                .Sum(x => (double)x.DurationMinutes);
            var calculatedAtUtc = Min(problem.CapturedAtUtc, upperBound);
            var observedAtUtc = calculatedAtUtc.AddMinutes(-random.NextInt(5, 180));
            var isStale = random.Chance(0.15);

            var risks = new List<ExecutionRiskFact>(2);
            if (random.Chance(0.18))
            {
                risks.Add(new ExecutionRiskFact(
                    "material.shortage", ExecutionRiskCategory.Material, true, problem.ProblemId, observedAtUtc));
            }

            if (random.Chance(0.12))
            {
                risks.Add(new ExecutionRiskFact(
                    EquipmentRuntimeReasonCodes.MaintenanceWindow, ExecutionRiskCategory.Equipment, false, problem.ProblemId, observedAtUtc));
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
                InputFingerprint: Fingerprint($"world-history|{problem.ProblemId}|{workOrderNo}|{remainingMinutes}")));
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
    /// 落库前才补上——用户基于该快照创建方案时会把快照里的 <c>OrganizationId</c>/<c>EnvironmentId</c>
    /// 原样带进新方案，留空会写出无主的方案。
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

    private static DateTimeOffset Min(DateTimeOffset left, DateTimeOffset right) => left <= right ? left : right;

    private static string Fingerprint(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    #endregion
}

/// <summary>排产域全量事实流（只有引擎的输入）。</summary>
public sealed record WorldHistorySchedulingFacts(
    IReadOnlyList<WorldHistoryScheduleProblemFact> Problems,
    IReadOnlyList<WorldHistoryUrgencyFact> Urgencies)
{
    public int OperationCount => Problems.Sum(x => x.Problem.Orders.Sum(order => order.Operations.Count));
}

/// <summary>一个历史排产问题的完整事实（工单 / 路线 / 日历 / 资源 / 不可用窗口）。</summary>
public sealed record WorldHistoryScheduleProblemFact(
    string ProblemId,
    string ProblemFingerprint,
    DateOnly WeekStart,
    DateTimeOffset HorizonStartUtc,
    DateTimeOffset HorizonEndUtc,
    DateTimeOffset CapturedAtUtc,
    IReadOnlyList<WorldHistoryOrderPlan> Orders,
    SchedulingProblemContract Problem);

/// <summary>一条订单紧急度事实（覆盖问题里出现过的每个工单）。</summary>
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
