using Nerv.IIP.Business.Scheduling.Web.Application.Scheduling;
using Nerv.IIP.Contracts.Scheduling;

namespace Nerv.IIP.Business.Scheduling.Web.Tests;

/// <summary>
/// #1399 M9：「生成首版排不满」的两个真原因各锁一组用例。
///
/// 实机复现（3 工单 24 道工序，只排进 8 道）拆开后只有 3 道是真原因，其余 13 道全是
/// <c>predecessorUnscheduled</c> 级联放大：
///   · 2 道 <c>calendar</c>——工序时长 965 分钟 &gt; 最长单条班次窗口 720 分钟；
///     而早班 08:00–20:00 与晚班 20:00–08:00 本就首尾相接，是连续 24h 生产。
///   · 1 道 <c>quality</c>——工艺路线上标了「需质检」就被无条件判为不可排。
/// </summary>
public sealed class SchedulingCapacityFillTests
{
    private static readonly DateTimeOffset Day = new(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);

    // ---------- 班次窗口合并 ----------

    /// <summary>
    /// 首尾相接的两条班次窗口必须当成一段连续可生产区间：600 分钟的工序放不进任何一条
    /// 480 分钟的单班窗口，但早班+中班连起来是 960 分钟，现场本来就是跨班连做的。
    /// </summary>
    [Fact]
    public void Adjacent_shift_windows_are_one_continuous_production_run()
    {
        var problem = ProblemWith(
            durationMinutes: 600,
            windows:
            [
                new(Day.AddHours(8), Day.AddHours(16), "early"),
                new(Day.AddHours(16), Day.AddHours(24), "middle")
            ]);

        var plan = new FiniteCapacityScheduler().Schedule(problem, "plan-merge-adjacent", Day);

        Assert.Empty(plan.UnscheduledOperations);
        var assignment = Assert.Single(plan.Assignments);
        Assert.Equal(Day.AddHours(8), assignment.StartUtc);
        Assert.Equal(Day.AddHours(18), assignment.EndUtc);
    }

    /// <summary>重叠窗口同样合并——同一份种子会把多套班次都铺到每个工作日上。</summary>
    [Fact]
    public void Overlapping_shift_windows_are_merged_instead_of_double_counted()
    {
        var problem = ProblemWith(
            durationMinutes: 600,
            windows:
            [
                new(Day.AddHours(8), Day.AddHours(20), "day"),
                new(Day.AddHours(8), Day.AddHours(16), "early"),
                new(Day.AddHours(16), Day.AddHours(24), "middle")
            ]);

        var plan = new FiniteCapacityScheduler().Schedule(problem, "plan-merge-overlap", Day);

        Assert.Empty(plan.UnscheduledOperations);
        Assert.Single(plan.Assignments);
    }

    /// <summary>
    /// 反向护栏：中间真的停产（窗口之间有空档）就不许合并，否则会把工序排到停产时段上。
    /// </summary>
    [Fact]
    public void Windows_separated_by_a_real_gap_are_not_merged()
    {
        var problem = ProblemWith(
            durationMinutes: 600,
            windows:
            [
                new(Day.AddHours(8), Day.AddHours(16), "early"),
                // 16:00–18:00 停产，不接续
                new(Day.AddHours(18), Day.AddHours(24), "night")
            ]);

        var plan = new FiniteCapacityScheduler().Schedule(problem, "plan-merge-gap", Day);

        Assert.Empty(plan.Assignments);
        var unscheduled = Assert.Single(plan.UnscheduledOperations);
        Assert.Equal(ScheduleConflictReasonCodeContract.Calendar, unscheduled.ReasonCode);
    }

    // ---------- 质检软约束 ----------

    /// <summary>
    /// 默认软口径：工艺路线上的「需质检」是开工/放行门槛，不是排产门槛（与物料 #1318、
    /// 设备状态未知 #1325 同一裁决）。工序照排，另出一条预警级冲突提示开工前先质检。
    /// </summary>
    [Fact]
    public void Routing_quality_gate_is_soft_by_default_and_still_schedules()
    {
        var problem = ProblemWith(
            durationMinutes: 60,
            windows: [new(Day.AddHours(8), Day.AddHours(16), "early")],
            qualityBlockReason: "quality.inspectionRequired");

        var plan = new FiniteCapacityScheduler().Schedule(problem, "plan-quality-soft", Day);

        Assert.Empty(plan.UnscheduledOperations);
        Assert.Single(plan.Assignments);

        var conflict = Assert.Single(plan.Conflicts);
        Assert.Equal(ScheduleConflictReasonCodeContract.Quality, conflict.ReasonCode);
        Assert.Equal(ScheduleConflictSeverityContract.Warning, conflict.Severity);
        // 措辞必须说清「已排入计划」,不能让人误以为没排进来。
        Assert.Contains("已排入计划", conflict.Message, StringComparison.Ordinal);
    }

    /// <summary>硬口径保留旧行为，供需要严格口径的环境按配置切回。</summary>
    [Fact]
    public void Routing_quality_gate_stays_blocking_under_hard_mode()
    {
        var problem = ProblemWith(
            durationMinutes: 60,
            windows: [new(Day.AddHours(8), Day.AddHours(16), "early")],
            qualityBlockReason: "quality.inspectionRequired");

        var plan = new FiniteCapacityScheduler(
                qualityConstraintMode: SchedulingQualityConstraintModeContract.Hard)
            .Schedule(problem, "plan-quality-hard", Day);

        Assert.Empty(plan.Assignments);
        var unscheduled = Assert.Single(plan.UnscheduledOperations);
        Assert.Equal(ScheduleConflictReasonCodeContract.Quality, unscheduled.ReasonCode);
    }

    /// <summary>
    /// 真实下达的质量封锁（针对具体工序的 <see cref="SchedulingQualityBlockContract"/>）
    /// 在两种口径下都是硬阻——那是已经发生的封锁，不是路线上的常规检验要求。
    /// </summary>
    [Fact]
    public void An_actual_quality_block_still_blocks_under_soft_mode()
    {
        var problem = ProblemWith(
            durationMinutes: 60,
            windows: [new(Day.AddHours(8), Day.AddHours(16), "early")]) with
        {
            QualityBlocks =
            [
                new SchedulingQualityBlockContract("operation", "WO-FILL-001-OP10", "quality.holdOrder", null)
            ]
        };

        var plan = new FiniteCapacityScheduler().Schedule(problem, "plan-quality-real-block", Day);

        Assert.Empty(plan.Assignments);
        var unscheduled = Assert.Single(plan.UnscheduledOperations);
        Assert.Equal(ScheduleConflictReasonCodeContract.Quality, unscheduled.ReasonCode);
    }

    // ---------- fixture ----------

    private static SchedulingProblemContract ProblemWith(
        int durationMinutes,
        IReadOnlyCollection<SchedulingTimeWindowContract> windows,
        string? qualityBlockReason = null)
    {
        return new SchedulingProblemContract(
            ContractVersion: 1,
            ProblemId: "problem-fill-001",
            OrganizationId: "org-001",
            EnvironmentId: "prod",
            HorizonStartUtc: Day,
            HorizonEndUtc: Day.AddDays(1),
            Orders:
            [
                new SchedulingOrderContract(
                    OrderId: "WO-FILL-001",
                    SkuCode: "FG-FILL",
                    Quantity: 1,
                    DueUtc: Day.AddDays(1),
                    Priority: 1,
                    IsRush: false,
                    Operations:
                    [
                        new SchedulingOperationContract(
                            OperationId: "WO-FILL-001-OP10",
                            OperationSequence: 10,
                            PredecessorOperationIds: [],
                            DurationMinutes: durationMinutes,
                            RequiredCapabilityCode: "CAP-FILL",
                            EligibleResourceIds: ["DEV-FILL-01"],
                            PrimaryResourceId: "DEV-FILL-01",
                            EarliestStartUtc: Day,
                            DueUtc: Day.AddDays(1),
                            Priority: 1,
                            IsRush: false,
                            SplitPolicy: ScheduleSplitPolicyContract.NonSplittable,
                            MaterialReadyUtc: null,
                            QualityBlockReason: qualityBlockReason,
                            SourceReference: "TEST:FILL")
                    ])
            ],
            Resources:
            [
                new SchedulingResourceContract(
                    ResourceId: "DEV-FILL-01",
                    WorkCenterId: "WC-FILL",
                    CapabilityCodes: ["CAP-FILL"],
                    CapacityUnits: 1,
                    CalendarId: "CAL-FILL",
                    SortKey: "001")
            ],
            Calendars: [new SchedulingCalendarContract("CAL-FILL", windows)],
            UnavailabilityWindows: [],
            MaterialReadiness: [],
            QualityBlocks: [],
            LockedAssignments: []);
    }
}
