using Nerv.IIP.Business.Scheduling.Web.Application.Scheduling;
using Nerv.IIP.Contracts.Scheduling;

namespace Nerv.IIP.Business.Scheduling.Web.Tests;

// DomainInvariant: ADR 0032 §3、#4119 的冻结并集及基线保持。
public class SchedulingFreezeCalculatorTests
{
    private static readonly DateTimeOffset AsOfUtc = new(2026, 10, 7, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Union_freezes_each_baseline_operation_once_and_retains_all_reasons()
    {
        var assignments = new[]
        {
            Assignment("completed", AsOfUtc.AddHours(-3)),
            Assignment("started", AsOfUtc.AddHours(-2)),
            Assignment("manual", AsOfUtc.AddHours(4)),
            Assignment("window", AsOfUtc.AddMinutes(30)),
            Assignment("all", AsOfUtc.AddMinutes(15)),
            Assignment("movable", AsOfUtc.AddHours(5)),
        };
        SchedulingFreezeExecutionFact[] execution =
        [
            new("WO", "completed", AsOfUtc.AddHours(-3), AsOfUtc.AddHours(-2)),
            new("WO", "started", AsOfUtc.AddHours(-2), null),
            new("WO", "all", AsOfUtc.AddHours(-1), AsOfUtc),
        ];

        var frozen = SchedulingFreezeCalculator.Calculate(assignments, execution,
            [("WO", "manual"), ("WO", "all")], Policy(TimeSpan.FromHours(1)));

        Assert.Equal(["all", "completed", "manual", "started", "window"],
            frozen.Select(x => x.Assignment.OperationId));
        Assert.Equal(SchedulingFreezeReason.Completed | SchedulingFreezeReason.Started |
            SchedulingFreezeReason.ManualLock | SchedulingFreezeReason.StableWindow, frozen[0].Reasons);
        Assert.Equal(SchedulingFreezeReason.Completed | SchedulingFreezeReason.Started, frozen[1].Reasons);
        Assert.Equal(SchedulingFreezeReason.ManualLock, frozen[2].Reasons);
        Assert.Equal(SchedulingFreezeReason.Started, frozen[3].Reasons);
        Assert.Equal(SchedulingFreezeReason.StableWindow, frozen[4].Reasons);
    }

    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, true)]
    [InlineData(59, true)]
    [InlineData(60, false)]
    public void Stable_window_is_left_closed_right_open_and_does_not_infer_start(int startMinutes, bool expectedFrozen)
    {
        var frozen = SchedulingFreezeCalculator.Calculate(
            [Assignment("operation", AsOfUtc.AddMinutes(startMinutes))], [], [], Policy(TimeSpan.FromHours(1)));

        Assert.Equal(expectedFrozen ? 1 : 0, frozen.Count);
    }

    [Fact]
    public void Work_center_override_replaces_default_including_zero()
    {
        var assignments = new[]
        {
            Assignment("default-inside", AsOfUtc.AddMinutes(30)),
            Assignment("default-outside", AsOfUtc.AddMinutes(90)),
            Assignment("override-inside", AsOfUtc.AddMinutes(90), "WC-LONG"),
            Assignment("override-boundary", AsOfUtc.AddHours(2), "WC-LONG"),
            Assignment("zero", AsOfUtc, "WC-ZERO"),
        };
        var policy = new SchedulingFreezePolicy(AsOfUtc, TimeSpan.FromHours(1),
            new Dictionary<string, TimeSpan>
            {
                ["WC-LONG"] = TimeSpan.FromHours(2),
                ["WC-ZERO"] = TimeSpan.Zero,
            });

        var frozen = SchedulingFreezeCalculator.Calculate(assignments, [], [], policy);

        Assert.Equal(["default-inside", "override-inside"], frozen.Select(x => x.Assignment.OperationId));
    }

    [Fact]
    public void Zero_window_keeps_started_completed_and_manual_locks()
    {
        SchedulingFreezeExecutionFact[] execution =
        [
            new("WO", "paused", AsOfUtc.AddHours(-2), null),
            new("WO", "downtime", AsOfUtc.AddHours(-2), null),
            new("WO", "quality-held", AsOfUtc.AddHours(-2), null),
            new("WO", "completed", null, AsOfUtc.AddHours(-1)),
        ];
        var assignments = new[] { "paused", "downtime", "quality-held", "completed", "manual", "unstarted" }
            .Select(operation => Assignment(operation, AsOfUtc.AddMinutes(20))).ToArray();

        var frozen = SchedulingFreezeCalculator.Calculate(assignments, execution,
            [("WO", "manual")], Policy(TimeSpan.Zero));

        Assert.Equal(["completed", "downtime", "manual", "paused", "quality-held"],
            frozen.Select(x => x.Assignment.OperationId));
        Assert.DoesNotContain(frozen, x => x.Reasons.HasFlag(SchedulingFreezeReason.StableWindow));
    }

    [Fact]
    public void Freeze_keeps_baseline_resource_times_and_segments_and_is_independent_of_input_order()
    {
        var baseline = Assignment("segmented", AsOfUtc) with
        {
            EndUtc = AsOfUtc.AddHours(3),
            Segments = [new(AsOfUtc, AsOfUtc.AddHours(1)), new(AsOfUtc.AddHours(2), AsOfUtc.AddHours(3))],
        };
        var otherOrder = Assignment("segmented", AsOfUtc.AddHours(5)) with { OrderId = "OTHER" };
        var policy = Policy(TimeSpan.FromHours(1));

        var first = SchedulingFreezeCalculator.Calculate([baseline, otherOrder], [], [], policy);
        var repeated = SchedulingFreezeCalculator.Calculate([otherOrder, baseline], [], [], policy);

        Assert.Equal(first, repeated);
        var assignment = Assert.Single(first).Assignment;
        Assert.Same(baseline, assignment);
        Assert.Equal("DEV-01", assignment.ResourceId);
        Assert.Equal(AsOfUtc, assignment.StartUtc);
        Assert.Equal(AsOfUtc.AddHours(3), assignment.EndUtc);
        Assert.Equal([new ScheduleAssignmentSegmentContract(AsOfUtc, AsOfUtc.AddHours(1)),
            new ScheduleAssignmentSegmentContract(AsOfUtc.AddHours(2), AsOfUtc.AddHours(3))], assignment.Segments);
    }

    [Fact]
    public void Execution_and_manual_locks_are_scoped_by_order_and_operation()
    {
        var assignments = new[]
        {
            Assignment("same-operation", AsOfUtc.AddHours(5)),
            Assignment("same-operation", AsOfUtc.AddHours(5)) with { OrderId = "OTHER" },
            Assignment("not-in-baseline", AsOfUtc.AddHours(5)),
        };

        var frozen = SchedulingFreezeCalculator.Calculate(assignments,
            [new("WO", "same-operation", AsOfUtc, null), new("MISSING", "absent", AsOfUtc, null)],
            [("MISSING", "absent")], Policy(TimeSpan.Zero));

        Assert.Equal("WO", Assert.Single(frozen).Assignment.OrderId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Negative_default_or_work_center_window_is_rejected(bool overrideWindow)
    {
        var policy = new SchedulingFreezePolicy(AsOfUtc,
            overrideWindow ? TimeSpan.Zero : TimeSpan.FromMinutes(-1),
            overrideWindow ? new Dictionary<string, TimeSpan> { ["WC"] = TimeSpan.FromMinutes(-1) } :
                new Dictionary<string, TimeSpan>());

        Assert.Throws<ArgumentOutOfRangeException>(() => SchedulingFreezeCalculator.Calculate([], [], [], policy));
    }

    private static SchedulingFreezePolicy Policy(TimeSpan window) => new(AsOfUtc, window,
        new Dictionary<string, TimeSpan>());

    private static ScheduleAssignmentContract Assignment(string operation, DateTimeOffset start, string workCenter = "WC") =>
        new($"assignment-{operation}", "WO", operation, 10, "DEV-01", workCenter,
            start, start.AddHours(1), false, "scheduled");
}
