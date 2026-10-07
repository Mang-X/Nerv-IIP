using System.Text.Json;
using Nerv.IIP.Business.Scheduling.Web.Application.Scheduling;
using Nerv.IIP.Contracts.Scheduling;

namespace Nerv.IIP.Business.Scheduling.Web.Tests;

// DomainInvariant：#4165、#3616 获批规格修订 1、ADR 0014 §12/17/18、ADR 0032 §3。
public class ReschedulingImpactAnalyzerTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 7, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Downtime_hits_only_actual_occupancy_on_the_named_device()
    {
        var segmented = Assignment("segmented", "DEV-1", 0, 180) with
        {
            Segments = [new(At, At.AddMinutes(60)), new(At.AddMinutes(120), At.AddMinutes(180))],
        };
        var baseline = new[] { segmented, Assignment("other-device", "DEV-2", 0, 180),
            Assignment("before", "DEV-1", -120, -60), Assignment("after", "DEV-1", 240, 300) };
        var gap = Analyze(baseline, [Downtime(60, 120)]);
        Assert.Empty(gap.AffectedOperations);
        Assert.Empty(gap.FrozenAssignments);
        Assert.Empty(gap.RecalculateAssignments);

        var outage = Downtime(150, 240);
        var result = Analyze(baseline, [outage]);
        var hit = Assert.Single(result.AffectedOperations);
        Assert.Equal(segmented, hit.Assignment);
        Assert.Equal(SchedulingFreezeReason.None, hit.FreezeReasons);
        Assert.Equal([new ReschedulingImpactReason(ReschedulingImpactReasonCode.ResourceUnavailable, outage)], hit.Reasons);
        Assert.Empty(result.FrozenAssignments);
        Assert.Equal([segmented], result.RecalculateAssignments);
    }

    [Fact]
    public void Operation_deviation_is_scoped_by_order_and_retains_each_source()
    {
        var targeted = Assignment("op", "DEV-1", 180, 240);
        var other = targeted with { OrderId = "OTHER", AssignmentId = "other" };
        SchedulingDeviation operation = new SchedulingOperationDeviation("MES/op", "v2", At, "execution-delay", "WO", "op");
        var downtime = Downtime(200, 210);
        var result = Analyze([other, targeted], [operation, downtime]);
        Assert.Equal(["OTHER", "WO"], result.AffectedOperations.Select(x => x.Assignment.OrderId));
        Assert.Equal([new ReschedulingImpactReason(ReschedulingImpactReasonCode.ResourceUnavailable, downtime)], result.AffectedOperations[0].Reasons);
        Assert.Equal([new ReschedulingImpactReason(ReschedulingImpactReasonCode.OperationDeviation, operation),
            new ReschedulingImpactReason(ReschedulingImpactReasonCode.ResourceUnavailable, downtime)], result.AffectedOperations[1].Reasons);
        Assert.Equal(SchedulingFreezeReason.None, result.AffectedOperations[1].FreezeReasons);
        Assert.Empty(result.FrozenAssignments);
        Assert.Equal([other, targeted], result.RecalculateAssignments);

        var onlyOperation = Analyze([other, targeted], [operation]);
        Assert.Equal(targeted, Assert.Single(onlyOperation.AffectedOperations).Assignment);
        Assert.Equal([targeted], onlyOperation.RecalculateAssignments);
    }

    [Fact]
    public void Frozen_hits_keep_the_baseline_and_conflict_reasons_while_only_movable_hits_recalculate()
    {
        var started = Assignment("started", "DEV-1", -60, 180) with
        { Segments = [new(At.AddMinutes(-60), At), new(At.AddMinutes(120), At.AddMinutes(180))] };
        var completed = Assignment("completed", "DEV-1", 120, 180);
        var manual = Assignment("manual", "DEV-1", 120, 180);
        var stable = Assignment("stable", "DEV-1", 120, 180) with { WorkCenterId = "WC-STABLE" };
        var movable = Assignment("movable", "DEV-1", 120, 180);
        var unaffectedLock = Assignment("unaffected", "DEV-2", 120, 180);
        var outage = Downtime(130, 140);
        var result = ReschedulingImpactAnalyzer.Analyze(Problem(), [started, completed, manual, stable, movable, unaffectedLock], [outage],
            [new("WO", "started", At.AddMinutes(-60), null), new("WO", "completed", At.AddMinutes(-60), At)],
            [("WO", "manual"), ("WO", "unaffected")],
            new(At, TimeSpan.Zero, new Dictionary<string, TimeSpan> { ["WC-STABLE"] = TimeSpan.FromHours(3) }));
        Assert.Equal(["completed", "manual", "movable", "stable", "started"], result.AffectedOperations.Select(x => x.Assignment.OperationId));
        Assert.Equal([SchedulingFreezeReason.Completed | SchedulingFreezeReason.Started, SchedulingFreezeReason.ManualLock,
            SchedulingFreezeReason.None, SchedulingFreezeReason.StableWindow, SchedulingFreezeReason.Started], result.AffectedOperations.Select(x => x.FreezeReasons));
        Assert.All(result.AffectedOperations, x => Assert.Equal([new ReschedulingImpactReason(ReschedulingImpactReasonCode.ResourceUnavailable, outage)], x.Reasons));
        Assert.Equal(["completed", "manual", "stable", "started", "unaffected"], result.FrozenAssignments.Select(x => x.Assignment.OperationId));
        Assert.Same(started, result.FrozenAssignments.Single(x => x.Assignment.OperationId == "started").Assignment);
        Assert.Equal([movable], result.RecalculateAssignments);
    }

    [Fact]
    public void Repeated_and_reordered_inputs_have_the_same_complete_result_and_fingerprint()
    {
        var a = Assignment("a", "DEV-1", 0, 180) with
        { Segments = [new(At, At.AddMinutes(60)), new(At.AddMinutes(120), At.AddMinutes(180))] };
        var b = Assignment("b", "DEV-2", 0, 180);
        SchedulingDeviation[] deviations = [Downtime(130, 140), new SchedulingOperationDeviation("MES/b", "v3", At, "delay", "WO", "b")];
        SchedulingFreezeExecutionFact[] execution = [new("WO", "a", At, null), new("WO", "b", At, null)];
        var policy = new SchedulingFreezePolicy(At, TimeSpan.Zero, new Dictionary<string, TimeSpan> { ["WC"] = TimeSpan.Zero, ["OTHER"] = TimeSpan.FromHours(2) });
        var first = ReschedulingImpactAnalyzer.Analyze(Problem(), [a, b], deviations, execution, [("WO", "a"), ("WO", "b")], policy);
        var repeated = ReschedulingImpactAnalyzer.Analyze(Problem(), [a, b], deviations, execution, [("WO", "a"), ("WO", "b")], policy);
        var shuffledProblem = Problem() with { Resources = Problem().Resources.Reverse().ToArray() };
        var shuffled = ReschedulingImpactAnalyzer.Analyze(shuffledProblem,
            [b, a with { Segments = a.Segments!.Reverse().ToArray() }], deviations.Reverse().ToArray(), execution.Reverse().ToArray(),
            [("WO", "b"), ("WO", "a")], policy with { WorkCenterWindows = new Dictionary<string, TimeSpan> { ["OTHER"] = TimeSpan.FromHours(2), ["WC"] = TimeSpan.Zero } });
        Assert.Equal(JsonSerializer.Serialize(first), JsonSerializer.Serialize(repeated));
        Assert.Equal(first.InputFingerprint, shuffled.InputFingerprint);
        Assert.Equal(["a", "b"], shuffled.AffectedOperations.Select(x => x.Assignment.OperationId));
        Assert.Equal(first.AffectedOperations.Select(x => (x.Assignment.OperationId, x.FreezeReasons)), shuffled.AffectedOperations.Select(x => (x.Assignment.OperationId, x.FreezeReasons)));
        Assert.Equal(first.AffectedOperations.SelectMany(x => x.Reasons), shuffled.AffectedOperations.SelectMany(x => x.Reasons));
        Assert.Equal(first.FrozenAssignments.Select(x => x.Reasons), shuffled.FrozenAssignments.Select(x => x.Reasons));
        Assert.Empty(shuffled.RecalculateAssignments);
    }

    [Fact]
    public void Fingerprint_covers_problem_baseline_deviations_and_freeze_snapshot_and_uses_utc()
    {
        var baseline = Assignment("op", "DEV-1", 0, 180);
        var deviation = Downtime(130, 140);
        var policy = new SchedulingFreezePolicy(At, TimeSpan.Zero, new Dictionary<string, TimeSpan>());
        string Fingerprint(SchedulingProblemContract? problem = null, ScheduleAssignmentContract? assignment = null,
            SchedulingDeviation? fact = null, SchedulingFreezePolicy? freeze = null,
            SchedulingFreezeExecutionFact[]? execution = null, (string, string)[]? locks = null) =>
            ReschedulingImpactAnalyzer.Analyze(problem ?? Problem(), [assignment ?? baseline], [fact ?? deviation], execution ?? [], locks ?? [], freeze ?? policy).InputFingerprint;
        var original = Fingerprint();
        Assert.NotEqual(original, Fingerprint(problem: Problem() with { HorizonEndUtc = At.AddDays(3) }));
        Assert.NotEqual(original, Fingerprint(assignment: baseline with { EndUtc = At.AddMinutes(200) }));
        Assert.NotEqual(original, Fingerprint(fact: deviation with { SourceVersion = "v2" }));
        Assert.NotEqual(original, Fingerprint(fact: deviation with { OccurredAtUtc = At.AddMinutes(1) }));
        Assert.NotEqual(original, Fingerprint(fact: deviation with { StartUtc = At.AddMinutes(131) }));
        Assert.NotEqual(original, Fingerprint(freeze: policy with { AsOfUtc = At.AddMinutes(1) }));
        Assert.NotEqual(original, Fingerprint(freeze: policy with { DefaultWindow = TimeSpan.FromMinutes(1) }));
        Assert.NotEqual(original, Fingerprint(execution: [new("WO", "op", At, null)]));
        Assert.NotEqual(original, Fingerprint(locks: [("WO", "op")]));
        Assert.Equal(original, Fingerprint(fact: deviation with { OccurredAtUtc = At.ToOffset(TimeSpan.FromHours(8)), StartUtc = deviation.StartUtc.ToOffset(TimeSpan.FromHours(8)) }));
    }

    [Fact]
    public void Invalid_downtime_window_is_an_explicit_input_error()
    {
        Assert.Throws<ArgumentException>(() => Analyze([Assignment("op", "DEV-1", 0, 180)], [Downtime(140, 130)]));
    }

    private static ReschedulingImpact Analyze(ScheduleAssignmentContract[] baseline, SchedulingDeviation[] deviations) =>
        ReschedulingImpactAnalyzer.Analyze(Problem(), baseline, deviations, [], [], new(At, TimeSpan.Zero, new Dictionary<string, TimeSpan>()));

    private static SchedulingResourceUnavailableDeviation Downtime(int start, int end) =>
        new("equipment/outage-1", "v1", At, "downtime", "DEV-1", At.AddMinutes(start), At.AddMinutes(end));

    private static ScheduleAssignmentContract Assignment(string operation, string resource, int start, int end) =>
        new($"assignment-{operation}", "WO", operation, 10, resource, "WC", At.AddMinutes(start), At.AddMinutes(end), false, "scheduled");

    private static SchedulingProblemContract Problem() => new(1, "problem", "org", "env", At, At.AddDays(2), [],
        [new("DEV-1", "WC", ["cut"], 1, "CAL", "1"), new("DEV-2", "WC", ["cut"], 1, "CAL", "2")],
        [new("CAL", [new(At, At.AddDays(2), "shift")])], [], [], [], []);
}
