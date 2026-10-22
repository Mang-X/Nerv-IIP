using System.Text.Json;
using Nerv.IIP.Contracts.EquipmentRuntime;
using Nerv.IIP.Contracts.Scheduling;
using Nerv.IIP.Business.Scheduling.Web.Application.Scheduling;
using static Nerv.IIP.Business.Scheduling.Web.Tests.RightShiftCandidateGeneratorTests;

namespace Nerv.IIP.Business.Scheduling.Web.Tests;

// DomainInvariant：#4215、#3617 获批 r1、ADR 0014 §12/13/17/18、ADR 0032 §3/4/6。
public sealed class ResourceTransferCandidateGeneratorTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 8, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Qualified_alternative_transfers_and_preserves_independent_assignment_and_deviation_source()
    {
        var original = Assignment("A", "a", "R1", 0, 60);
        var other = Assignment("I", "i", "R3", 0, 60);
        var problem = Problem(Order("A", Operation("a", "R1") with { EligibleResourceIds = ["R1", "R2"] }),
            Order("I", Operation("i", "R3")));
        var input = Input(problem, [original, other], [Downtime(0, 240)]);
        var before = JsonSerializer.Serialize(input);
        var result = ResourceTransferCandidateGenerator.Generate(input);
        Assert.Equal(SchedulePlanStatusContract.Preview, result.Plan.Status);
        Assert.Equal("R2", Find(result, "A", "a").ResourceId);
        Assert.Equal(At, Find(result, "A", "a").StartUtc);
        Assert.Equal(other, Find(result, "I", "i"));
        var moved = Assert.Single(result.Movements);
        Assert.Equal(original, moved.Original);
        Assert.All(moved.Reasons, x => Assert.Equal(Downtime(0, 240), x.Source));
        Assert.NotEmpty(moved.Paths);
        Assert.Equal(before, JsonSerializer.Serialize(input));
    }

    [Fact]
    public void Empty_segments_downtime_without_eligible_alternative_is_unscheduled_instead_of_waiting_on_original_device()
    {
        var original = Assignment("A", "a", "R1", 0, 60) with { Segments = [] };
        var input = Input(Problem(Order("A", Operation("a", "R1"))), [original], [Downtime(0, 240)]);
        var result = ResourceTransferCandidateGenerator.Generate(input);
        var affected = Assert.Single(result.Impact.AffectedOperations);
        Assert.Contains(affected.Reasons, x => x.Code == ReschedulingImpactReasonCode.ResourceUnavailable);
        Assert.Single(result.Impact.RecalculateAssignments);
        Assert.Empty(result.Plan.Assignments);
        Assert.Empty(result.Transfers);
        Assert.Empty(result.Movements);
        var unscheduled = Assert.Single(result.Plan.UnscheduledOperations);
        Assert.Equal(("A", "a"), (unscheduled.OrderId, unscheduled.OperationId));
        Assert.Equal(ScheduleConflictReasonCodeContract.NoEligibleResource, unscheduled.ReasonCode);
        Assert.Contains(result.Explanations, x => x.OrderId == "A" && x.OperationId == "a"
            && x.Code == "NoEligibleResource" && x.Reasons.Count > 0 && x.Paths.Count > 0);
    }

    [Fact]
    public void Substitute_fact_cannot_expand_qualification_when_the_only_eligible_alternative_is_unavailable()
    {
        var problem = Problem(Order("A", Operation("a", "R1") with { EligibleResourceIds = ["R1", "R2"] })) with
        { UnavailabilityWindows = [new("R2", null, At, At.AddDays(2), "maintenance")] };
        var input = Input(problem, [Assignment("A", "a", "R1", 0, 60)], [Downtime(0, 240)]) with
        { EquipmentAvailability = Snapshot(Window("R1", EquipmentRuntimeAvailabilityStatus.Unavailable, 240, ["R3"])) };
        var result = ResourceTransferCandidateGenerator.Generate(input);
        Assert.Empty(result.Plan.Assignments);
        Assert.Empty(result.Transfers);
        Assert.Contains(result.Plan.UnscheduledOperations, x => x.OrderId == "A" && x.OperationId == "a");
        Assert.Contains(result.Explanations, x => x.OrderId == "A" && x.Reasons.Count > 0 && x.Paths.Count > 0);
    }

    [Theory]
    [InlineData("capability")]
    [InlineData("skill")]
    [InlineData("tooling")]
    [InlineData("calendar")]
    [InlineData("capacity")]
    public void Alternative_must_satisfy_existing_resource_constraints(string constraint)
    {
        var operation = Operation("a", "R1") with { EligibleResourceIds = ["R1", "R2"] };
        if (constraint == "skill") operation = operation with { RequiredSkillCodes = ["missing"] };
        if (constraint == "tooling") operation = operation with { ToolingAvailable = false };
        var problem = Problem(Order("A", operation));
        if (constraint == "capability") problem = problem with { Resources = problem.Resources.Select(r => r.ResourceId == "R2" ? r with { CapabilityCodes = [] } : r).ToArray() };
        if (constraint == "calendar") problem = problem with
        {
            Resources = problem.Resources.Select(r => r.ResourceId == "R2" ? r with { CalendarId = "empty" } : r).ToArray(),
            Calendars = problem.Calendars.Append(new("empty", [])).ToArray()
        };
        var original = Assignment("A", "a", "R1", 0, 60);
        var baseline = new[] { original };
        if (constraint == "capacity")
        {
            problem = problem with { Orders = problem.Orders.Append(Order("I", Operation("i", "R2") with { DurationMinutes = 2880 })).ToArray() };
            baseline = [original, Assignment("I", "i", "R2", 0, 2880)];
        }
        var result = ResourceTransferCandidateGenerator.Generate(Input(problem, baseline, [Downtime(0, 240)]));
        Assert.DoesNotContain(result.Plan.Assignments, x => x.OrderId == "A");
        Assert.Contains(result.Plan.UnscheduledOperations, x => x.OrderId == "A");
        Assert.Empty(result.Transfers);
    }

    [Fact]
    public void Changeover_occupancy_changes_resource_selection_and_actual_setup_cost_is_reported()
    {
        var operation = Operation("a", "R1") with
        {
            EligibleResourceIds = ["R1", "R2", "R3"],
            Changeovers = [new("OLD", 90, [], true), new("SKU", 0, [], true)]
        };
        var problem = Problem(Order("A", operation), Order("P", Operation("p", "R2")) with { SkuCode = "OLD" },
            Order("Q", Operation("q", "R3")));
        var originals = new[] { Assignment("A", "a", "R1", 60, 120), Assignment("P", "p", "R2", 0, 60), Assignment("Q", "q", "R3", 0, 90) };
        var input = Input(problem, originals, [Downtime(60, 240)]);
        var result = ResourceTransferCandidateGenerator.Generate(input);
        Assert.Equal("R3", Find(result, "A", "a").ResourceId);
        Assert.Equal(At.AddMinutes(90), Find(result, "A", "a").StartUtc);
        Assert.Equal(0, Assert.Single(result.Transfers).SetupMinutes);
        var cheaper = problem with { Orders = problem.Orders.Select(o => o.OrderId == "A" ? o with
        { Operations = [operation with { Changeovers = [new("OLD", 15, [], true), new("SKU", 0, [], true)] }] } : o).ToArray() };
        var changed = ResourceTransferCandidateGenerator.Generate(input with { Problem = cheaper });
        Assert.Equal("R2", Find(changed, "A", "a").ResourceId);
        Assert.Equal(At.AddMinutes(75), Find(changed, "A", "a").StartUtc);
        Assert.Equal(15, Assert.Single(changed.Transfers).SetupMinutes);
        Assert.Equal(135, changed.Plan.ResourceLoads.Single(x => x.ResourceId == "R2").AssignedMinutes);
        Assert.NotEqual(result.InputFingerprint, changed.InputFingerprint);
    }

    [Fact]
    public void Setup_minutes_without_changeover_uses_the_existing_occupied_gap()
    {
        var problem = Problem(Order("A", Operation("a", "R1") with { EligibleResourceIds = ["R1", "R2"], SetupMinutes = 20 }), Order("P", Operation("p", "R2")));
        var result = ResourceTransferCandidateGenerator.Generate(Input(problem,
            [Assignment("A", "a", "R1", 60, 120), Assignment("P", "p", "R2", 0, 60)], [Downtime(60, 240)]));
        Assert.Equal(At.AddMinutes(80), Find(result, "A", "a").StartUtc);
        Assert.Equal(20, Assert.Single(result.Transfers).SetupMinutes);
    }

    [Theory]
    [InlineData("IIoT/outage", "device-code")]
    [InlineData("", null)]
    public void Selected_substitute_retains_actual_source_and_expired_prediction_without_inventing_labels(string source, string? label)
    {
        var fact = Window("R1", EquipmentRuntimeAvailabilityStatus.Unavailable, 240, ["R3", "R2"]) with
        { SourceReferenceId = source, SourceReferenceLabel = label, ExpectedRestoreAtUtc = At.AddMinutes(-60), RestorePredictionSource = "explicit-etr", RestorePredictionSourceVersion = "r2" };
        var input = Input(Problem(Order("A", Operation("a", "R1") with { EligibleResourceIds = ["R1", "R2"] })),
            [Assignment("A", "a", "R1", 0, 60)], [Downtime(0, 240)]) with { EquipmentAvailability = Snapshot(fact) };
        var result = ResourceTransferCandidateGenerator.Generate(input);
        var transfer = Assert.Single(result.Transfers);
        Assert.Equal(("R1", "R2"), (transfer.OriginalResourceId, transfer.ResourceId));
        var basis = Assert.Single(transfer.DeviceSources);
        Assert.Equal(source, basis.Source.Window.SourceReferenceId);
        Assert.Equal(label, basis.Source.Window.SourceReferenceLabel);
        Assert.Equal("explicit-etr", basis.Source.Window.RestorePredictionSource);
        Assert.Equal("r2", basis.Source.Window.RestorePredictionSourceVersion);
        Assert.True(basis.Source.RestorePredictionExpired);
        Assert.Equal(At.AddHours(4), basis.Source.Window.EndUtc);
        Assert.Contains(result.Explanations, x => x.Code == "restore-prediction-expired");
        var withoutFacts = ResourceTransferCandidateGenerator.Generate(input with { EquipmentAvailability = null });
        Assert.Empty(Assert.Single(withoutFacts.Transfers).DeviceSources);
        Assert.NotEqual(result.InputFingerprint, withoutFacts.InputFingerprint);
    }

    [Fact]
    public void Unknown_target_is_a_soft_risk_or_hard_block_and_predictions_do_not_restore_it()
    {
        var window = Window("R2", EquipmentRuntimeAvailabilityStatus.Unknown, 2880, []) with
        { ExpectedRestoreAtUtc = At.AddMinutes(-1), RestorePredictionSource = "explicit-etr" };
        var input = Input(Problem(Order("A", Operation("a", "R1") with { EligibleResourceIds = ["R1", "R2"] })),
            [Assignment("A", "a", "R1", 0, 60)], [Downtime(0, 240)]) with { EquipmentAvailability = Snapshot(window) };
        var soft = ResourceTransferCandidateGenerator.Generate(input);
        Assert.Equal("R2", Assert.Single(soft.Plan.Assignments).ResourceId);
        Assert.Contains(soft.Plan.EquipmentRisks!, risk => risk.ResourceId == "R2");
        Assert.Null(Assert.Single(soft.EquipmentAvailability!.Windows).Window.ExpectedRestoreAtUtc);
        var hard = ResourceTransferCandidateGenerator.Generate(input with { EquipmentUnknownMode = SchedulingEquipmentUnknownModeContract.Hard });
        Assert.Empty(hard.Transfers);
        Assert.Empty(hard.Plan.Assignments);
        Assert.NotEmpty(hard.Plan.UnscheduledOperations);
    }

    [Fact]
    public void Target_downtime_and_calendar_remain_authoritative_when_restore_prediction_is_earlier()
    {
        var problem = Problem(Order("A", Operation("a", "R1") with { EligibleResourceIds = ["R1", "R2"] })) with
        { Calendars = [new("CAL", [new(At, At.AddHours(2), "first"), new(At.AddHours(5), At.AddHours(8), "second")])] };
        var input = Input(problem, [Assignment("A", "a", "R1", 0, 60)], [Downtime(0, 240)]) with
        { EquipmentAvailability = Snapshot(Window("R2", EquipmentRuntimeAvailabilityStatus.Unavailable, 240, []) with
          { ExpectedRestoreAtUtc = At.AddMinutes(60), RestorePredictionSource = "device-mttr", RestorePredictionSourceVersion = "v1" }) };
        var result = ResourceTransferCandidateGenerator.Generate(input);
        Assert.Equal(At.AddHours(5), Assert.Single(result.Plan.Assignments).StartUtc);
        Assert.Equal(At.AddHours(4), Assert.Single(result.EquipmentAvailability!.Windows).Window.EndUtc);
    }

    [Fact]
    public void Transfer_and_right_shift_share_impact_and_freeze_and_keep_segments_and_independent_positions()
    {
        var original = Assignment("A", "a", "R1", 0, 60);
        var successor = Assignment("A", "b", "R3", 60, 120);
        var frozen = Assignment("F", "f", "R1", 120, 240) with { Segments = [new(At.AddMinutes(120), At.AddMinutes(150)), new(At.AddMinutes(210), At.AddMinutes(240))] };
        var independent = Assignment("I", "i", "R3", 180, 240);
        var problem = Problem(Order("A", Operation("a", "R1") with { EligibleResourceIds = ["R1", "R2"] }, Operation("b", "R3", "a")),
            Order("F", Operation("f", "R1")), Order("I", Operation("i", "R3"))) with
        { UnavailabilityWindows = [new("R2", null, At, At.AddMinutes(90), "busy")] };
        var input = Input(problem, [original, successor, frozen, independent], [Downtime(0, 240)]) with { ManualLocks = [("F", "f")] };
        var result = ResourceTransferCandidateGenerator.Generate(input);
        var right = RightShiftCandidateGenerator.Generate(input);
        Assert.Equal(right.Impact.InputFingerprint, result.Impact.InputFingerprint);
        Assert.Equal(right.InputFingerprint, result.InputFingerprint);
        Assert.Equal("R2", Find(result, "A", "a").ResourceId);
        // 后继最早150，但目标资源独立占用180–240，连续60分钟只能从240开始。
        Assert.Equal(At.AddMinutes(240), Find(result, "A", "b").StartUtc);
        Assert.Equal(frozen, Find(result, "F", "f"));
        Assert.Equal(independent, Find(result, "I", "i"));
        Assert.Contains(result.Explanations, x => x.OrderId == "F" && x.Code == "frozen-conflict");
        var move = Assert.Single(result.Movements, x => x.Candidate.OperationId == "b");
        Assert.Contains(move.Paths.SelectMany(x => x.Steps), x => x.From.OperationId == "a" && x.To.OperationId == "b");
    }

    [Fact]
    public void Repeated_and_reordered_complete_inputs_produce_identical_candidate_and_fingerprint()
    {
        var input = Input(Problem(Order("A", Operation("a", "R1") with { EligibleResourceIds = ["R1", "R2", "R3"] })),
            [Assignment("A", "a", "R1", 0, 60)], [Downtime(0, 240)]) with
        { EquipmentAvailability = Snapshot(Window("R1", EquipmentRuntimeAvailabilityStatus.Unavailable, 240, ["R3", "R2"]),
            Window("R3", EquipmentRuntimeAvailabilityStatus.Unknown, 2880, [])) };
        var result = ResourceTransferCandidateGenerator.Generate(input);
        var reordered = ResourceTransferCandidateGenerator.Generate(input with
        {
            Problem = input.Problem with { Resources = input.Problem.Resources.Reverse().ToArray(), Orders = input.Problem.Orders.Reverse().ToArray() },
            Deviations = [Downtime(0, 240), Downtime(0, 240)],
            EquipmentAvailability = Snapshot(input.EquipmentAvailability!.Windows.Reverse().Select(x => x.Window with
            { SubstituteDeviceAssetIds = x.Window.SubstituteDeviceAssetIds.Reverse().ToArray() }).ToArray())
        });
        Assert.Equal(JsonSerializer.Serialize(result), JsonSerializer.Serialize(ResourceTransferCandidateGenerator.Generate(input)));
        Assert.Equal(JsonSerializer.Serialize(result), JsonSerializer.Serialize(reordered));
        Assert.NotEqual(result.InputFingerprint, ResourceTransferCandidateGenerator.Generate(input with { Policy = input.Policy with { AsOfUtc = At.AddMinutes(1) } }).InputFingerprint);
    }

    [Theory]
    [InlineData("started")]
    [InlineData("completed")]
    [InlineData("manual")]
    [InlineData("stable")]
    public void Each_authoritative_freeze_keeps_the_original_device_and_segments_even_when_an_alternative_exists(string reason)
    {
        var original = Assignment("A", "a", "R1", 0, 120) with
        { Segments = [new(At, At.AddMinutes(30)), new(At.AddMinutes(90), At.AddMinutes(120))] };
        var input = Input(Problem(Order("A", Operation("a", "R1") with { EligibleResourceIds = ["R1", "R2"] })), [original], [Downtime(0, 240)]);
        if (reason == "started") input = input with { Execution = [new("A", "a", At, null)] };
        if (reason == "completed") input = input with { Execution = [new("A", "a", At, At.AddMinutes(120))] };
        if (reason == "manual") input = input with { ManualLocks = [("A", "a")] };
        if (reason == "stable") input = input with { Policy = input.Policy with { DefaultWindow = TimeSpan.FromMinutes(1) } };
        var result = ResourceTransferCandidateGenerator.Generate(input);
        Assert.Equal(original, Assert.Single(result.Plan.Assignments));
        Assert.Empty(result.Transfers);
        Assert.Empty(result.Movements);
        Assert.Contains(result.Explanations, x => x.Code == "frozen-conflict");
    }

    [Theory]
    [InlineData("material", ScheduleConflictReasonCodeContract.Material)]
    [InlineData("quality", ScheduleConflictReasonCodeContract.Quality)]
    public void Existing_material_and_quality_hard_blocks_are_not_bypassed_by_transfer(string constraint, ScheduleConflictReasonCodeContract reason)
    {
        var problem = Problem(Order("A", Operation("a", "R1") with { EligibleResourceIds = ["R1", "R2"] }));
        if (constraint == "material") problem = problem with { MaterialReadiness = [new("order", "A", null, false, ["shortage"])] };
        if (constraint == "quality") problem = problem with { QualityBlocks = [new("operation", "a", "hold", null)] };
        var input = Input(problem, [Assignment("A", "a", "R1", 0, 60)], [Downtime(0, 240)]) with { MaterialMode = SchedulingMaterialConstraintModeContract.Hard };
        var result = ResourceTransferCandidateGenerator.Generate(input);
        Assert.Empty(result.Transfers);
        Assert.Equal(reason, Assert.Single(result.Plan.UnscheduledOperations).ReasonCode);
    }

    private static SchedulingEquipmentAvailabilitySnapshotContract Snapshot(params EquipmentRuntimeAvailabilityWindowContract[] windows) =>
        SchedulingEquipmentAvailabilitySnapshot.Create(new(1, "org", "env", At, At.AddDays(2), windows), At);
    private static EquipmentRuntimeAvailabilityWindowContract Window(string resource, EquipmentRuntimeAvailabilityStatus status, int end, string[] substitutes) =>
        new(resource, $"WC-{resource}", status, "equipment-state", EquipmentRuntimeSeverity.Blocked, At, At.AddMinutes(end),
            EquipmentRuntimeSourceType.Downtime, "IIoT/outage", "down", substitutes);
}
