using Nerv.IIP.Contracts.Scheduling;

namespace Nerv.IIP.Business.Scheduling.Web.Application.Scheduling;

internal sealed record SchedulingFreezePolicy(
    DateTimeOffset AsOfUtc,
    TimeSpan DefaultWindow,
    IReadOnlyDictionary<string, TimeSpan> WorkCenterWindows);

public sealed record SchedulingFreezeSettings(
    TimeSpan DefaultWindow,
    IReadOnlyDictionary<string, TimeSpan> WorkCenterWindows)
{
    internal SchedulingFreezePolicy At(DateTimeOffset asOfUtc) =>
        new(asOfUtc, DefaultWindow, WorkCenterWindows);

    public static SchedulingFreezeSettings Resolve(IConfiguration configuration)
    {
        const string section = "Scheduling:Freeze";
        var defaultMinutes = configuration.GetValue<int?>($"{section}:DefaultWindowMinutes") ?? 0;
        ArgumentOutOfRangeException.ThrowIfNegative(defaultMinutes);
        var workCenterMinutes = configuration.GetSection($"{section}:WorkCenterWindowMinutes")
            .Get<Dictionary<string, int>>() ?? new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var minutes in workCenterMinutes.Values)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(minutes);
        }
        return new SchedulingFreezeSettings(TimeSpan.FromMinutes(defaultMinutes),
            workCenterMinutes.ToDictionary(x => x.Key, x => TimeSpan.FromMinutes(x.Value), StringComparer.Ordinal));
    }
}

internal sealed record SchedulingFreezeExecutionFact(
    string OrderId,
    string OperationId,
    DateTimeOffset? ActualStartedAtUtc,
    DateTimeOffset? ActualCompletedAtUtc);

[Flags]
internal enum SchedulingFreezeReason
{
    None = 0,
    Completed = 1,
    Started = 2,
    ManualLock = 4,
    StableWindow = 8,
}

internal sealed record SchedulingFrozenAssignment(
    ScheduleAssignmentContract Assignment,
    SchedulingFreezeReason Reasons);

/// <summary>
/// ADR 0032 §3：只从给定基线及同一时点的执行/人工锁快照计算冻结集。
/// 暂停、停机和质量阻断不撤销 ActualStartedAtUtc；这些阻断不参与解冻判断。
/// 不推定过期计划已开工，不改变 assignment，也不读取当前时间。
/// </summary>
internal static class SchedulingFreezeCalculator
{
    public static IReadOnlyList<SchedulingFrozenAssignment> Calculate(
        IReadOnlyCollection<ScheduleAssignmentContract> baseline,
        IReadOnlyCollection<SchedulingFreezeExecutionFact> execution,
        IReadOnlyCollection<(string OrderId, string OperationId)> manualLocks,
        SchedulingFreezePolicy policy)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(policy.DefaultWindow, TimeSpan.Zero);
        foreach (var window in policy.WorkCenterWindows.Values)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(window, TimeSpan.Zero);
        }

        var executionByOperation = execution.ToDictionary(x => (x.OrderId, x.OperationId));
        var manuallyLocked = manualLocks.ToHashSet();
        var frozen = new List<SchedulingFrozenAssignment>();
        foreach (var assignment in baseline.OrderBy(x => x.OrderId, StringComparer.Ordinal)
                     .ThenBy(x => x.OperationId, StringComparer.Ordinal))
        {
            var key = (assignment.OrderId, assignment.OperationId);
            var reasons = SchedulingFreezeReason.None;
            if (executionByOperation.TryGetValue(key, out var fact))
            {
                if (fact.ActualCompletedAtUtc.HasValue)
                {
                    reasons |= SchedulingFreezeReason.Completed;
                }
                if (fact.ActualStartedAtUtc.HasValue)
                {
                    reasons |= SchedulingFreezeReason.Started;
                }
            }
            if (manuallyLocked.Contains(key))
            {
                reasons |= SchedulingFreezeReason.ManualLock;
            }

            var window = policy.WorkCenterWindows.TryGetValue(assignment.WorkCenterId, out var specificWindow)
                ? specificWindow
                : policy.DefaultWindow;
            if (assignment.StartUtc >= policy.AsOfUtc && assignment.StartUtc - policy.AsOfUtc < window)
            {
                reasons |= SchedulingFreezeReason.StableWindow;
            }
            if (reasons != SchedulingFreezeReason.None)
            {
                frozen.Add(new SchedulingFrozenAssignment(assignment, reasons));
            }
        }
        return frozen;
    }
}
