using Nerv.IIP.Business.Mes.Domain.AggregatesModel.ScheduleAggregate;
using System.Globalization;

namespace Nerv.IIP.Business.Mes.Web.Application.Seed;

/// <summary>
/// 《工厂世界观设定集》L1 背景历史的**规则排程**形状：历次排程运行及其触发原因。
///
/// 对应业务前端「规则排程」页。该页此前只能显示**本次会话刚跑的那一次**排程结果
/// （<c>useMesSchedules()</c> 把结果存在内存 <c>shallowRef</c> 里），刷新即空、
/// 历史排程一条都查不到——因为 <c>mes.schedule_results</c> 从来没有 seed 写过，
/// 服务端也没有列表读面。本规格补历史事实，读面另由 <c>ListScheduleResultsQuery</c> 承担。
///
/// 排程运行**挂在 L1 工序任务已有的周计划号 <c>SP-2026-W##</c> 上**（见
/// <see cref="WorldHistoryMesSpec.SchedulePlanId"/>）：每周一次基线排程，周内按确定性规则
/// 追加若干次重排（急件 / 设备不可用 / 设备恢复）。于是「派工看板上这道工序是哪一版计划派的」
/// 与「排程页上第几次运行」能对得起来。
///
/// 确定性纯函数，随机源一律走 <see cref="WorldHistoryRandom"/> 的计划号流键。
/// </summary>
public static class WorldHistoryScheduleResultSpec
{
    /// <summary>L1 周计划号前缀——本规格只认这一段（不碰 L2 演示排程与规模块）。</summary>
    public const string SchedulePlanIdPrefix = "SP-2026-";

    /// <summary>单次排程结果里保留的工序分配上限：页面按分页看，JSON 也不必无限膨胀。</summary>
    public const int MaxAssignmentsPerRun = 60;

    /// <summary>重排触发原因及其权重（基线排程之外的追加运行）。</summary>
    public static readonly IReadOnlyList<ScheduleTrigger> ReschedulingTriggers =
    [
        ScheduleTrigger.RushOrder, ScheduleTrigger.AssetUnavailable, ScheduleTrigger.AssetRestored,
    ];

    private static readonly IReadOnlyList<int> ReschedulingTriggerWeights = [45, 32, 23];

    /// <summary>每周追加重排次数的取值与权重（0–3 次，均值约 1.5）。</summary>
    private static readonly IReadOnlyList<int> WeeklyRescheduleCounts = [0, 1, 2, 3];

    private static readonly IReadOnlyList<int> WeeklyRescheduleWeights = [15, 35, 33, 17];

    /// <summary>触发原因 → 分配行上的中文原因文案。</summary>
    public static string ReasonText(ScheduleTrigger trigger) => trigger switch
    {
        ScheduleTrigger.Manual => "周计划基线排程",
        ScheduleTrigger.RushOrder => "急件插单重排",
        ScheduleTrigger.AssetUnavailable => "设备停机改派",
        ScheduleTrigger.AssetRestored => "设备恢复回排",
        _ => "规则排程",
    };

    /// <summary>
    /// 由 L1 工序任务实际用过的周计划号，生成历次排程运行。
    ///
    /// <paramref name="schedulePlanIds"/> 必须是**库里真实存在**的计划号（升序）——
    /// 排程结果不凭空造周次。版本号自 1 起沿时间顺序递增，与运行时
    /// <c>AddScheduleResultAsync</c> 的「已有条数 + 1」口径衔接，不会撞号。
    /// </summary>
    public static IReadOnlyList<WorldHistoryScheduleRun> BuildRuns(
        IReadOnlyList<string> schedulePlanIds,
        double scale)
    {
        ArgumentNullException.ThrowIfNull(schedulePlanIds);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(scale);
        if (schedulePlanIds.Count == 0)
        {
            return [];
        }

        var runs = new List<WorldHistoryScheduleRun>(schedulePlanIds.Count * 3);
        var version = 0;

        foreach (var planId in schedulePlanIds.Order(StringComparer.Ordinal))
        {
            var weekStart = WeekStartUtc(planId);
            var random = new WorldHistoryRandom($"schedule:{planId}");

            // 周一早班开班前跑基线排程。
            runs.Add(new WorldHistoryScheduleRun(
                ++version,
                planId,
                ScheduleTrigger.Manual,
                weekStart.AddHours(7).AddMinutes(30),
                AssignmentTake: MaxAssignmentsPerRun,
                AssignmentOffset: 0));

            var rescheduleCount = (int)Math.Round(
                random.PickWeighted(WeeklyRescheduleCounts, WeeklyRescheduleWeights) * scale,
                MidpointRounding.AwayFromZero);
            for (var ordinal = 1; ordinal <= rescheduleCount; ordinal++)
            {
                var trigger = random.PickWeighted(ReschedulingTriggers, ReschedulingTriggerWeights);
                // 重排落在周内的生产日（周一~周六），只影响一部分工序。
                var dayOffset = random.NextInt(1, 6);
                var minuteOffset = random.NextInt(0, 8 * 60);
                runs.Add(new WorldHistoryScheduleRun(
                    ++version,
                    planId,
                    trigger,
                    weekStart.AddDays(dayOffset).AddHours(8).AddMinutes(minuteOffset),
                    AssignmentTake: random.NextInt(4, 21),
                    AssignmentOffset: random.NextInt(0, 12)));
            }
        }

        return runs;
    }

    /// <summary>周计划号 <c>SP-YYYY-Www</c> → 该 ISO 周的周一零点（UTC）。</summary>
    public static DateTimeOffset WeekStartUtc(string schedulePlanId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(schedulePlanId);
        var parts = schedulePlanId.Split('-');
        if (parts.Length != 3 ||
            !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var year) ||
            parts[2].Length < 2 ||
            !int.TryParse(parts[2].AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out var week))
        {
            throw new ArgumentOutOfRangeException(
                nameof(schedulePlanId), schedulePlanId, "Unrecognized world-bible schedule plan id.");
        }

        return new DateTimeOffset(ISOWeek.ToDateTime(year, week, DayOfWeek.Monday), TimeSpan.Zero);
    }
}

/// <summary>一次历史排程运行（工序分配由 SeedService 绑到真实工序任务上）。</summary>
public sealed record WorldHistoryScheduleRun(
    int ScheduleVersion,
    string SchedulePlanId,
    ScheduleTrigger Trigger,
    DateTimeOffset ScheduledAtUtc,
    int AssignmentTake,
    int AssignmentOffset);
