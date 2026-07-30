using Nerv.IIP.Contracts.Scheduling;

namespace Nerv.IIP.Business.Scheduling.Web.Application.Scheduling;

/// <summary>
/// 把排程问题里的「工作日历(班次窗口)」与「资源不可用窗口」投影到计划读面。
/// 这两类事实本来就是排程输入的一部分,读面复用它们即可,不新增端点、不新增持久化。
/// 生成路径投影的是真正参与排程的问题(含设备可用性适配后的窗口);
/// 重新读取已落库的方案时投影的是问题快照(ProblemJson),两者口径一致地按计划期裁剪。
/// </summary>
public static class SchedulePlanCalendarProjector
{
    public static SchedulePlanContract Attach(SchedulePlanContract plan, SchedulingProblemContract? problem)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (problem is null)
        {
            return plan;
        }

        return plan with
        {
            Calendars = ProjectCalendars(problem),
            BlockWindows = ProjectBlockWindows(problem),
        };
    }

    public static IReadOnlyCollection<SchedulePlanCalendarContract> ProjectCalendars(SchedulingProblemContract problem)
    {
        ArgumentNullException.ThrowIfNull(problem);

        var resources = problem.Resources ?? [];
        return (problem.Calendars ?? [])
            .Where(calendar => calendar is not null)
            .Select(calendar =>
            {
                var users = resources
                    .Where(resource => string.Equals(resource.CalendarId, calendar.CalendarId, StringComparison.Ordinal))
                    .ToArray();
                return new SchedulePlanCalendarContract(
                    CalendarId: calendar.CalendarId,
                    ResourceIds: users
                        .Select(x => x.ResourceId)
                        .Distinct(StringComparer.Ordinal)
                        .Order(StringComparer.Ordinal)
                        .ToArray(),
                    WorkCenterIds: users
                        .Select(x => x.WorkCenterId)
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .Distinct(StringComparer.Ordinal)
                        .Order(StringComparer.Ordinal)
                        .ToArray(),
                    ShiftWindows: (calendar.ShiftWindows ?? [])
                        .Where(window => Intersects(window.StartUtc, window.EndUtc, problem))
                        .OrderBy(x => x.StartUtc)
                        .ThenBy(x => x.EndUtc)
                        .Select(x => new SchedulePlanShiftWindowContract(x.StartUtc, x.EndUtc, x.ReasonCode))
                        .ToArray());
            })
            .Where(calendar => calendar.ShiftWindows.Count > 0)
            .OrderBy(x => x.CalendarId, StringComparer.Ordinal)
            .ToArray();
    }

    public static IReadOnlyCollection<SchedulePlanBlockWindowContract> ProjectBlockWindows(SchedulingProblemContract problem)
    {
        ArgumentNullException.ThrowIfNull(problem);

        return (problem.UnavailabilityWindows ?? [])
            .Where(window => window is not null && Intersects(window.StartUtc, window.EndUtc, problem))
            .Select(window => new SchedulePlanBlockWindowContract(
                ResourceId: string.IsNullOrWhiteSpace(window.ResourceId) ? null : window.ResourceId,
                WorkCenterId: string.IsNullOrWhiteSpace(window.WorkCenterId) ? null : window.WorkCenterId,
                StartUtc: window.StartUtc,
                EndUtc: window.EndUtc,
                ReasonCode: window.ReasonCode,
                Kind: ClassifyBlockKind(window.ReasonCode)))
            .OrderBy(x => x.StartUtc)
            .ThenBy(x => x.ResourceId, StringComparer.Ordinal)
            .ThenBy(x => x.WorkCenterId, StringComparer.Ordinal)
            .ThenBy(x => x.ReasonCode, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>
    /// 上游不可用窗口的 ReasonCode 是自由码值(设备运行事实、主数据维护计划都会写),
    /// 这里归并成读面能画出来的四类语义。无法识别时按「停机」处理——不猜、不丢。
    /// </summary>
    public static ScheduleBlockKindContract ClassifyBlockKind(string? reasonCode)
    {
        var code = (reasonCode ?? string.Empty).Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace(".", string.Empty, StringComparison.Ordinal)
            .ToLowerInvariant();

        if (code.Contains("linechange", StringComparison.Ordinal) || code.Contains("changeline", StringComparison.Ordinal))
        {
            return ScheduleBlockKindContract.LineChange;
        }

        if (code.Contains("changeover", StringComparison.Ordinal) || code.Contains("setup", StringComparison.Ordinal) ||
            code.Contains("tooling", StringComparison.Ordinal))
        {
            return ScheduleBlockKindContract.Changeover;
        }

        if (code.Contains("maintenance", StringComparison.Ordinal) || code.Contains("inspection", StringComparison.Ordinal) ||
            code.Contains("overhaul", StringComparison.Ordinal) || code.Contains("calibration", StringComparison.Ordinal))
        {
            return ScheduleBlockKindContract.Maintenance;
        }

        return ScheduleBlockKindContract.Downtime;
    }

    private static bool Intersects(DateTimeOffset startUtc, DateTimeOffset endUtc, SchedulingProblemContract problem) =>
        endUtc > startUtc && endUtc > problem.HorizonStartUtc && startUtc < problem.HorizonEndUtc;
}
