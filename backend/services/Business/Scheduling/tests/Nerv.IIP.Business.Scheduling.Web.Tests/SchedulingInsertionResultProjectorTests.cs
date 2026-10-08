using Nerv.IIP.Business.Scheduling.Web.Application.Scheduling;
using Nerv.IIP.Contracts.Scheduling;

namespace Nerv.IIP.Business.Scheduling.Web.Tests;

// DomainInvariant：#3620 spec r1 / #4186 / ADR 0032 §5，不以当前实现输出生成预期。
public class SchedulingInsertionResultProjectorTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 7, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Order_delay_uses_final_completion_not_sum_of_operation_movements_and_incomplete_orders_have_no_completion()
    {
        var problem = Problem(Order("both", "first", "last"), Order("middle", "first", "last"), Order("rush", "first"), Order("missing", "first"));
        var baseline = Plan(problem, "base", [A("both", "first", 0, 1), A("both", "last", 1, 2),
            A("middle", "first", 0, 1), A("middle", "last", 3, 4), A("missing", "first", 0, 1)]);
        var candidate = Plan(problem, "candidate", [A("both", "first", 24, 25), A("both", "last", 25, 26),
            A("middle", "first", 1, 2), A("middle", "last", 3, 4), A("rush", "first", 0, 1)]) with
        { UnscheduledOperations = [new("missing", "first", ScheduleConflictReasonCodeContract.Capacity, "容量不足")] };
        var result = Project(problem, baseline, candidate);
        Assert.Equal(1m, result.Orders.Single(x => x.OrderId == "both").DelayDays);
        Assert.Equal(SchedulingInsertionOrderStatusContract.Delayed, result.Orders.Single(x => x.OrderId == "both").Status);
        Assert.Equal(0m, result.Orders.Single(x => x.OrderId == "middle").DelayDays);
        Assert.Equal(SchedulingInsertionOrderStatusContract.Unchanged, result.Orders.Single(x => x.OrderId == "middle").Status);
        Assert.Equal(SchedulingInsertionOrderStatusContract.New, result.Orders.Single(x => x.OrderId == "rush").Status);
        var missing = result.Orders.Single(x => x.OrderId == "missing");
        Assert.Equal(SchedulingInsertionOrderStatusContract.Unscheduled, missing.Status);
        Assert.Null(missing.CandidateCompletionUtc);
        Assert.Null(missing.DelayDays);
        Assert.Equal(3, result.Kpis.MovedOperationCount);
        Assert.Equal(new(0, 1, 1), result.Kpis.UnscheduledOperationCount);
        Assert.Contains(result.Operations, x => x.OrderId == "missing" && x.Candidate is null && x.ReasonCodes.Contains("unscheduled"));
    }

    [Fact]
    public void Due_boundaries_are_strict_order_scoped_and_kpis_include_locked_late_orders_but_exclude_them_from_on_time_denominator()
    {
        var equal = Order("equal", "same");
        var problem = Problem(equal with { DueUtc = At.AddHours(26),
            Operations = equal.Operations.Select(x => x with { DueUtc = At.AddHours(26) }).ToArray() },
            Order("new-late", "same"), Order("already-late", "same"), Order("rush", "same"));
        var baseline = Plan(problem, "base", [A("equal", "same", 0, 26), A("new-late", "same", 23, 24),
            A("already-late", "same", 24, 25) with { IsLocked = true }]);
        var candidate = Plan(problem, "candidate", [A("equal", "same", 0, 26), A("new-late", "same", 24, 25),
            A("already-late", "same", 24, 25) with { IsLocked = true }, A("rush", "same", 0, 1)]);
        var result = Project(problem, baseline, candidate);
        Assert.False(result.Orders.Single(x => x.OrderId == "equal").CandidateLate);
        Assert.Equal(At.AddHours(24), result.Operations.Single(x => x.OrderId == "new-late").DueUtc);
        Assert.True(result.Orders.Single(x => x.OrderId == "new-late").NewlyLate);
        var old = result.Orders.Single(x => x.OrderId == "already-late");
        Assert.True(old.BaselineLate);
        Assert.True(old.CandidateLate);
        Assert.False(old.NewlyLate);
        Assert.Equal(new(1, 2, 1), result.Kpis.LateOrderCount);
        Assert.Equal(2, result.Kpis.OnTimeRate.BaselineDenominator);
        Assert.Equal(3, result.Kpis.OnTimeRate.CandidateDenominator);
        Assert.Equal(1m, result.Kpis.OnTimeRate.Baseline);
        Assert.Equal(0.6667m, result.Kpis.OnTimeRate.Candidate);
        Assert.Equal(1, result.Kpis.LockRetention.Total);
        Assert.Equal(1, result.Kpis.LockRetention.Preserved);
        Assert.Empty(result.Kpis.LockRetention.NotPreserved);
        Assert.Equal(0.04m, result.Orders.Single(x => x.OrderId == "new-late").DelayDays);
        Assert.Equal(1, result.Kpis.MovedOperationCount);
    }

    private static SchedulingInsertionPreviewResultContract Project(SchedulingProblemContract problem, SchedulePlanContract baseline, SchedulePlanContract candidate)
    {
        var freeze = new SchedulePlanFreezeContextContract(At, At, [], []);
        var snapshot = new SchedulingInsertionCalculationSnapshotContract(problem, baseline, baseline, freeze, [], [],
            SchedulingMaterialConstraintModeContract.Soft, SchedulingQualityConstraintModeContract.Soft, SchedulingEquipmentUnknownModeContract.Soft);
        return SchedulingInsertionResultProjector.Project(new("fingerprint", candidate, new("impact", [], [], []), At.AddHours(1), []), snapshot, "rush", new());
    }
    private static ScheduleAssignmentContract A(string order, string op, int start, int end) =>
        new(order + op, order, op, 10, "R", "WC", At.AddHours(start), At.AddHours(end), false, "test");
    private static SchedulePlanContract Plan(SchedulingProblemContract problem, string id, ScheduleAssignmentContract[] assignments) =>
        new FiniteCapacityScheduler().Schedule(problem, id, At) with { Assignments = assignments, UnscheduledOperations = [] };
    private static SchedulingOrderContract Order(string id, params string[] ops) => new(id, "SKU", 1, At.AddHours(24), 0, id == "rush",
        ops.Select(op => new SchedulingOperationContract(op, 10, [], 60, "cut", ["R"], "R", At, At.AddHours(24), 0,
            id == "rush", ScheduleSplitPolicyContract.NonSplittable, null, null, null)).ToArray());
    private static SchedulingProblemContract Problem(params SchedulingOrderContract[] orders) => new(1, "problem", "org", "env", At, At.AddDays(3), orders,
        [new("R", "WC", ["cut"], 10, "CAL", "1")], [new("CAL", [new(At, At.AddDays(3), "shift")])], [], [], [], []);
}
