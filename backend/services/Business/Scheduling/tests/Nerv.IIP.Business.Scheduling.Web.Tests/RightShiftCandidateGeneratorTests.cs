using System.Text.Json;
using Nerv.IIP.Contracts.EquipmentRuntime;
using Nerv.IIP.Business.Scheduling.Web.Application.Scheduling;
using Nerv.IIP.Contracts.Scheduling;

namespace Nerv.IIP.Business.Scheduling.Web.Tests;

// DomainInvariant：#4214、#3617 获批 r1、ADR 0014 §12/17/18、ADR 0032 §3/4/6。
public sealed class RightShiftCandidateGeneratorTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 8, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Long_downtime_crosses_calendar_and_moves_successors_in_baseline_queue_order()
    {
        var a = Assignment("A", "first", "R1", 0, 60);
        var b = Assignment("A", "second", "R2", 60, 120);
        var c = Assignment("C", "queue", "R1", 60, 120);
        var independent = Assignment("I", "independent", "R3", 0, 60);
        var problem = Problem(Order("A", Operation("first", "R1"), Operation("second", "R2", "first")),
            Order("C", Operation("queue", "R1") with { Priority = 100, IsRush = true }),
            Order("I", Operation("independent", "R3"))) with
        {
            Calendars = [new("CAL", [new(At, At.AddHours(8), "day"), new(At.AddDays(1), At.AddDays(1).AddHours(8), "day")])]
        };
        var input = Input(problem, [a, b, c, independent], [Downtime(0, 600)]);
        var before = JsonSerializer.Serialize(input);
        var result = RightShiftCandidateGenerator.Generate(input);
        Assert.Equal(SchedulePlanStatusContract.Preview, result.Plan.Status);
        Assert.Equal(At.AddDays(1), Find(result, "A", "first").StartUtc);
        Assert.Equal(At.AddDays(1).AddHours(1), Find(result, "A", "second").StartUtc);
        Assert.Equal(At.AddDays(1).AddHours(1), Find(result, "C", "queue").StartUtc);
        Assert.Equal("R1", Find(result, "A", "first").ResourceId);
        Assert.Equal(independent, Find(result, "I", "independent"));
        Assert.Empty(result.Plan.UnscheduledOperations);
        Assert.Equal(3, result.Movements.Count);
        var move = Assert.Single(result.Movements, x => x.Original == c);
        Assert.Equal(c, move.Original);
        Assert.Equal(Find(result, "C", "queue"), move.Candidate);
        Assert.Contains(move.Reasons, x => x.Source.SourceReference == "maintenance/outage" && x.Source.SourceVersion == "v1");
        Assert.Contains(move.Reasons, x => x.Code == ReschedulingImpactReasonCode.ResourceUnavailable);
        Assert.Equal(before, JsonSerializer.Serialize(input));
    }

    [Fact]
    public void Frozen_segments_and_unaffected_assignments_are_preserved_and_downtime_conflict_is_visible()
    {
        var frozen = Assignment("A", "frozen", "R1", 0, 180) with
        { Segments = [new(At, At.AddMinutes(60)), new(At.AddMinutes(120), At.AddMinutes(180))] };
        var other = Assignment("I", "independent", "R3", 0, 60);
        var input = Input(Problem(Order("A", Operation("frozen", "R1")), Order("I", Operation("independent", "R3"))),
            [frozen, other], [Downtime(130, 240)]) with { Execution = [new("A", "frozen", At, null)] };
        var result = RightShiftCandidateGenerator.Generate(input);
        Assert.Equal(frozen, Find(result, "A", "frozen"));
        Assert.Equal(other, Find(result, "I", "independent"));
        Assert.Contains(result.Plan.Conflicts, x => x.OperationId == "frozen" && x.ReasonCode == ScheduleConflictReasonCodeContract.InvalidLockedAssignment);
        Assert.Empty(result.Movements);
        Assert.Equal(SchedulingFreezeReason.Started, Assert.Single(result.Impact.FrozenAssignments).Reasons);
    }

    [Fact]
    public void Resource_queue_waits_for_an_earlier_items_predecessor_before_higher_priority_later_item()
    {
        var predecessor = Assignment("A", "prep", "R2", 0, 60);
        var first = Assignment("A", "first", "R1", 60, 120);
        var next = Assignment("C", "next", "R1", 120, 180);
        var problem = Problem(Order("A", Operation("prep", "R2"), Operation("first", "R1", "prep")),
            Order("C", Operation("next", "R1") with { Priority = 100 }));
        var input = Input(problem, [next, first, predecessor],
            [new SchedulingResourceUnavailableDeviation("maintenance/prep", "v2", At, "down", "R2", At, At.AddHours(4)), Downtime(60, 240)]);
        var result = RightShiftCandidateGenerator.Generate(input);
        Assert.Equal(At.AddHours(5), Find(result, "A", "first").StartUtc);
        Assert.Equal(At.AddHours(6), Find(result, "C", "next").StartUtc);
        Assert.Empty(result.Plan.UnscheduledOperations);
    }

    [Fact]
    public void Spare_capacity_keeps_independent_occupancy_and_does_not_serialize_movable_queue()
    {
        var a = Assignment("A", "a", "R1", 0, 60);
        var b = Assignment("B", "b", "R1", 30, 90);
        var independent = Assignment("I", "i", "R1", 90, 150);
        var problem = Problem(Order("A", Operation("a", "R1")), Order("B", Operation("b", "R1")), Order("I", Operation("i", "R1"))) with
        { Resources = Problem().Resources.Select(x => x with { CapacityUnits = 3 }).ToArray() };
        var result = RightShiftCandidateGenerator.Generate(Input(problem, [a, b, independent], [Downtime(0, 90)]));
        Assert.Equal(At.AddMinutes(90), Find(result, "A", "a").StartUtc);
        Assert.Equal(At.AddMinutes(90), Find(result, "B", "b").StartUtc);
        Assert.Equal(independent, Find(result, "I", "i"));
    }

    [Fact]
    public void Unquantified_delay_retains_baseline_and_explains_missing_delay_instead_of_inventing_it()
    {
        var a = Assignment("A", "a", "R1", 0, 60);
        var input = Input(Problem(Order("A", Operation("a", "R1"))), [a],
            [new SchedulingOperationDeviation("MES/a", "v3", At, "delay", "A", "a")]);
        var result = RightShiftCandidateGenerator.Generate(input);
        Assert.Equal(a, Find(result, "A", "a"));
        Assert.Empty(result.Movements);
        Assert.Contains(result.Explanations, x => x.OrderId == "A" && x.OperationId == "a" && x.Code == "unquantified-delay");
    }

    [Fact]
    public void No_feasible_slot_reports_unscheduled_and_preserves_independent_chain()
    {
        var a = Assignment("A", "a", "R1", 0, 60);
        var other = Assignment("I", "i", "R3", 0, 60);
        var result = RightShiftCandidateGenerator.Generate(Input(Problem(Order("A", Operation("a", "R1")), Order("I", Operation("i", "R3"))),
            [a, other], [Downtime(0, 4000)]));
        Assert.Equal(other, Assert.Single(result.Plan.Assignments));
        Assert.Contains(result.Plan.UnscheduledOperations, x => x.OrderId == "A" && x.OperationId == "a");
        Assert.Contains(result.Explanations, x => x.OrderId == "A" && x.OperationId == "a");
    }

    [Fact]
    public void Repeated_reordered_and_duplicate_deviations_have_identical_result_and_fingerprint()
    {
        var a = Assignment("A", "a", "R1", 0, 60);
        var b = Assignment("B", "b", "R1", 60, 120);
        var input = Input(Problem(Order("A", Operation("a", "R1")), Order("B", Operation("b", "R1"))), [a, b], [Downtime(0, 90)]);
        var original = RightShiftCandidateGenerator.Generate(input);
        var shuffled = RightShiftCandidateGenerator.Generate(input with
        {
            Problem = input.Problem with { Orders = input.Problem.Orders.Reverse().ToArray(), Resources = input.Problem.Resources.Reverse().ToArray() },
            Baseline = input.Baseline with { Assignments = [b, a] },
            Deviations = [Downtime(0, 90), Downtime(0, 90)]
        });
        Assert.Equal(JsonSerializer.Serialize(original), JsonSerializer.Serialize(RightShiftCandidateGenerator.Generate(input)));
        Assert.Equal(original.InputFingerprint, shuffled.InputFingerprint);
        Assert.Equal(JsonSerializer.Serialize(original.Plan), JsonSerializer.Serialize(shuffled.Plan));
        Assert.NotEqual(original.InputFingerprint, RightShiftCandidateGenerator.Generate(input with { Policy = input.Policy with { AsOfUtc = At.AddMinutes(1) } }).InputFingerprint);
    }

    [Fact]
    public void Capacity_only_successor_has_source_and_propagation_path_and_moves_after_root()
    {
        var a = Assignment("A", "a", "R1", 0, 60);
        var b = Assignment("B", "b", "R1", 60, 120);
        var result = RightShiftCandidateGenerator.Generate(Input(Problem(Order("A", Operation("a", "R1")), Order("B", Operation("b", "R1"))),
            [a, b], [Downtime(0, 30)]));
        var moved = Assert.Single(result.Movements, x => x.Original == b);
        Assert.Equal(At.AddMinutes(90), moved.Candidate.StartUtc);
        Assert.Contains(moved.Paths.SelectMany(x => x.Steps), x => x.From == new ReschedulingImpactOperation("A", "a")
            && x.To == new ReschedulingImpactOperation("B", "b") && x.Code == ReschedulingImpactReasonCode.ResourceCapacity);
        Assert.All(moved.Reasons, x => Assert.Equal(Downtime(0, 30), x.Source));
    }

    [Theory]
    [InlineData("material", ScheduleConflictReasonCodeContract.Material)]
    [InlineData("quality", ScheduleConflictReasonCodeContract.Quality)]
    [InlineData("skill", ScheduleConflictReasonCodeContract.NoEligibleResource)]
    [InlineData("tooling", ScheduleConflictReasonCodeContract.Tooling)]
    public void Existing_hard_constraints_report_real_unscheduled_reason(string constraint, ScheduleConflictReasonCodeContract expected)
    {
        var operation = Operation("a", "R1");
        if (constraint == "tooling") operation = operation with { ToolingAvailable = false };
        if (constraint == "skill") operation = operation with { RequiredSkillCodes = ["missing"] };
        var problem = Problem(Order("A", operation));
        if (constraint == "material") problem = problem with { MaterialReadiness = [new("order", "A", null, false, ["shortage"])] };
        if (constraint == "quality") problem = problem with { QualityBlocks = [new("operation", "a", "quality-hold", null)] };
        var input = Input(problem, [Assignment("A", "a", "R1", 0, 60)], [Downtime(0, 30)]) with { MaterialMode = SchedulingMaterialConstraintModeContract.Hard };
        var result = RightShiftCandidateGenerator.Generate(input);
        Assert.Empty(result.Plan.Assignments);
        Assert.Equal(expected, Assert.Single(result.Plan.UnscheduledOperations).ReasonCode);
        Assert.Contains(result.Explanations, x => x.Code == expected.ToString() && x.Reasons.Count > 0);
    }

    [Fact]
    public void Material_eta_and_quality_release_delay_the_actual_candidate()
    {
        var problem = Problem(Order("A", Operation("a", "R1") with { MaterialReadyUtc = At.AddHours(3) })) with
        { QualityBlocks = [new("operation", "a", "hold", At.AddHours(4))] };
        var result = RightShiftCandidateGenerator.Generate(Input(problem, [Assignment("A", "a", "R1", 0, 60)], [Downtime(0, 30)]));
        Assert.Equal(At.AddHours(4), Assert.Single(result.Plan.Assignments).StartUtc);
    }

    [Fact]
    public void Interruptible_movable_assignment_uses_actual_segments_across_calendar()
    {
        var problem = Problem(Order("A", Operation("a", "R1") with { DurationMinutes = 120, SplitPolicy = ScheduleSplitPolicyContract.Interruptible })) with
        { Calendars = [new("CAL", [new(At, At.AddHours(2), "morning"), new(At.AddHours(4), At.AddHours(6), "afternoon")])] };
        var result = RightShiftCandidateGenerator.Generate(Input(problem, [Assignment("A", "a", "R1", 0, 120)], [Downtime(0, 60)]));
        var assignment = Assert.Single(result.Plan.Assignments);
        Assert.Equal([new ScheduleAssignmentSegmentContract(At.AddHours(1), At.AddHours(2)), new(At.AddHours(4), At.AddHours(5))], assignment.Segments);
        Assert.Equal(At.AddHours(5), assignment.EndUtc);
    }

    [Fact]
    public void Manual_stable_and_completed_freeze_all_keep_baseline_without_erasing_conflicts()
    {
        var assignments = new[] { Assignment("A", "manual", "R1", 0, 60), Assignment("B", "stable", "R1", 60, 120), Assignment("C", "done", "R1", 120, 180) };
        var input = Input(Problem(Order("A", Operation("manual", "R1")), Order("B", Operation("stable", "R1")), Order("C", Operation("done", "R1"))), assignments, [Downtime(0, 200)]) with
        {
            ManualLocks = [("A", "manual")], Execution = [new("C", "done", At, At.AddHours(3))],
            Policy = new(At.AddHours(1), TimeSpan.FromMinutes(30), new Dictionary<string, TimeSpan>())
        };
        var result = RightShiftCandidateGenerator.Generate(input);
        Assert.All(assignments, a => Assert.Equal(a, Find(result, a.OrderId, a.OperationId)));
        Assert.Equal(3, result.Impact.FrozenAssignments.Count);
        Assert.Equal(3, result.Explanations.Count(x => x.Code == "frozen-conflict"));
        Assert.Empty(result.Movements);
    }

    [Theory]
    [InlineData("explicit-etr", 180, false)]
    [InlineData("device-mttr", 180, false)]
    [InlineData("configured-default", 180, false)]
    [InlineData("explicit-etr", -60, true)]
    public void Recovery_prediction_does_not_shorten_authoritative_downtime_and_is_retained(string source, int predictionMinutes, bool expired)
    {
        var problem = Problem(Order("A", Operation("a", "R1")));
        var window = new EquipmentRuntimeAvailabilityWindowContract("R1", "WC-R1", EquipmentRuntimeAvailabilityStatus.Unavailable,
            "downtime", EquipmentRuntimeSeverity.Blocked, At, At.AddHours(4), EquipmentRuntimeSourceType.Downtime,
            "maintenance/outage", "down", [], ExpectedRestoreAtUtc: At.AddMinutes(predictionMinutes), RestorePredictionSource: source, RestorePredictionSourceVersion: "r2");
        var snapshot = SchedulingEquipmentAvailabilitySnapshot.Create(new(1, "org", "env", At, At.AddDays(2), [window]), At);
        var input = Input(problem, [Assignment("A", "a", "R1", 0, 60)], [Downtime(0, 240)]) with { EquipmentAvailability = snapshot };
        var result = RightShiftCandidateGenerator.Generate(input);
        Assert.Equal(At.AddHours(4), Assert.Single(result.Plan.Assignments).StartUtc);
        Assert.Equal(snapshot, result.EquipmentAvailability);
        Assert.Equal(expired, Assert.Single(result.EquipmentAvailability!.Windows).RestorePredictionExpired);
        Assert.NotEqual(result.InputFingerprint, RightShiftCandidateGenerator.Generate(input with { EquipmentAvailability = null }).InputFingerprint);
    }

    [Fact]
    public void Actual_recovery_uses_actual_window_end_and_unknown_state_remains_risk()
    {
        var problem = Problem(Order("A", Operation("a", "R1")));
        var actual = new EquipmentRuntimeAvailabilityWindowContract("R1", "WC-R1", EquipmentRuntimeAvailabilityStatus.Unavailable,
            "downtime", EquipmentRuntimeSeverity.Blocked, At, At.AddHours(1), EquipmentRuntimeSourceType.Downtime,
            "maintenance/outage", "down", [], ExpectedRestoreAtUtc: At.AddHours(5), RestorePredictionSource: "explicit-etr", RestorePredictionSourceVersion: "r2");
        var unknown = actual with { AvailabilityStatus = EquipmentRuntimeAvailabilityStatus.Unknown, ReasonCode = "stale-source", EndUtc = At.AddDays(2) };
        var snapshot = SchedulingEquipmentAvailabilitySnapshot.Create(new(1, "org", "env", At, At.AddDays(2), [actual, unknown]), At);
        var input = Input(problem, [Assignment("A", "a", "R1", 0, 60)], [Downtime(0, 60)]) with { EquipmentAvailability = snapshot };
        var result = RightShiftCandidateGenerator.Generate(input);
        Assert.Equal(At.AddHours(1), Assert.Single(result.Plan.Assignments).StartUtc);
        Assert.Contains(result.Plan.EquipmentRisks!, x => x.ReasonCodes.Contains("stale-source"));
        var hard = RightShiftCandidateGenerator.Generate(input with { EquipmentUnknownMode = SchedulingEquipmentUnknownModeContract.Hard });
        Assert.Empty(hard.Plan.Assignments);
        Assert.NotEmpty(hard.Plan.UnscheduledOperations);
        Assert.NotEqual(result.InputFingerprint, hard.InputFingerprint);
    }

    [Fact]
    public void Frozen_successor_keeps_position_and_reports_its_new_predecessor_conflict()
    {
        var a = Assignment("A", "a", "R1", 0, 60);
        var b = Assignment("A", "b", "R2", 60, 120);
        var input = Input(Problem(Order("A", Operation("a", "R1"), Operation("b", "R2", "a"))), [a, b], [Downtime(0, 240)]) with { ManualLocks = [("A", "b")] };
        var result = RightShiftCandidateGenerator.Generate(input);
        Assert.Equal(b, Find(result, "A", "b"));
        Assert.Equal(At.AddHours(4), Find(result, "A", "a").StartUtc);
        Assert.Contains(result.Plan.Conflicts, x => x.OperationId == "b" && x.ReasonCode == ScheduleConflictReasonCodeContract.InvalidLockedAssignment);
        Assert.Contains(result.Explanations, x => x.OperationId == "b" && x.Code == "frozen-conflict");
    }

    internal static ReschedulingCandidateInput Input(SchedulingProblemContract problem, ScheduleAssignmentContract[] baseline, SchedulingDeviation[] deviations) =>
        new(problem, new FiniteCapacityScheduler().Schedule(problem, "baseline", At.AddHours(-1)) with { Assignments = baseline }, deviations,
            [], [], new(At, TimeSpan.Zero, new Dictionary<string, TimeSpan>()));

    internal static ScheduleAssignmentContract Find(ReschedulingCandidate result, string order, string operation) =>
        result.Plan.Assignments.Single(x => x.OrderId == order && x.OperationId == operation);
    internal static SchedulingResourceUnavailableDeviation Downtime(int start, int end) => new("maintenance/outage", "v1", At, "downtime", "R1", At.AddMinutes(start), At.AddMinutes(end));
    internal static ScheduleAssignmentContract Assignment(string order, string operation, string resource, int start, int end) =>
        new($"{order}/{operation}", order, operation, 1, resource, $"WC-{resource}", At.AddMinutes(start), At.AddMinutes(end), false, "baseline");
    internal static SchedulingOperationContract Operation(string id, string resource, params string[] predecessors) =>
        new(id, predecessors.Length + 1, predecessors, 60, "CAP", [resource], resource, At, At.AddDays(2), 1, false,
            ScheduleSplitPolicyContract.NonSplittable, null, null, "route");
    internal static SchedulingOrderContract Order(string id, params SchedulingOperationContract[] operations) => new(id, "SKU", 1, At.AddDays(2), 1, false, operations);
    internal static SchedulingProblemContract Problem(params SchedulingOrderContract[] orders) => new(1, "problem", "org", "env", At, At.AddDays(2), orders,
        [new("R1", "WC-R1", ["CAP"], 1, "CAL", "1"), new("R2", "WC-R2", ["CAP"], 1, "CAL", "2"), new("R3", "WC-R3", ["CAP"], 1, "CAL", "3")],
        [new("CAL", [new(At, At.AddDays(2), "continuous")])], [], [], [], []);
}
