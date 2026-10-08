using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Nerv.IIP.Business.Scheduling.Web.Application.Scheduling;
using Nerv.IIP.Contracts.Scheduling;
using Xunit.Abstractions;

namespace Nerv.IIP.Business.Scheduling.Web.Tests;

// DomainInvariant：#4185、#3620 已批准 spec r1、ADR 0014 §12/17/18、ADR 0032 §3/4/6。
public class SchedulingInsertionCalculatorTests(ITestOutputHelper output)
{
    private static readonly DateTimeOffset At = new(2026, 10, 7, 8, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(120, 30, 180)]
    [InlineData(30, 120, 180)]
    public void Promise_waits_for_both_frozen_capacity_and_material_eta(int capacityUntil, int eta, int completion)
    {
        var existing = Order("existing", Operation("op", "R1", capacityUntil));
        var rush = Order("rush", Operation("op", "R1", 60, rush: true));
        var problem = Problem(existing, rush) with { MaterialReadiness = [new("order", "rush", At.AddMinutes(eta), false, ["eta"])] };
        var baseline = Baseline(problem, existing);
        var result = Calculate(problem, baseline, locks: [("existing", "op")]);
        Assert.Equal(At.AddMinutes(completion), result.PromiseUtc);
        Assert.Equal(baseline.Assignments.Single(), result.Candidate.Assignments.Single(x => x.OrderId == "existing"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Unknown_eta_keeps_the_soft_constraint_candidate_but_cannot_promise(bool alsoHasKnownEta)
    {
        var problem = Problem(Order("rush", Operation("op", "R1", 60, rush: true))) with
        { MaterialReadiness = [new("order", "rush", null, false, ["unknown-eta"])] };
        if (alsoHasKnownEta) problem = problem with { MaterialReadiness = [.. problem.MaterialReadiness, new("operation", "op", At.AddMinutes(30), false, ["known-eta"])] };
        var result = Calculate(problem, Baseline(problem));
        Assert.Single(result.Candidate.Assignments);
        Assert.Null(result.PromiseUtc);
        Assert.Contains(SchedulingInsertionFailure.UnknownMaterialEta, result.Failures);
    }

    [Fact]
    public void Complete_chain_inserts_and_propagates_dependencies_while_independent_assignments_keep_every_field()
    {
        var existing = Order("existing", Operation("first", "R1", 60), Operation("last", "R2", 60, predecessor: "first"));
        var independent = Order("independent", Operation("op", "R3", 60, earliest: 240));
        var rush = Order("rush", Operation("first", "R1", 30, rush: true), Operation("last", "R2", 30, predecessor: "first", rush: true));
        var problem = Problem(existing, independent, rush);
        var baseline = Baseline(problem, existing, independent);
        // 已保存基线可以保留稍晚的人工位置；插单不能把独立工序重新左移。
        baseline = baseline with { Assignments = baseline.Assignments.Select(x => x.OrderId == "independent"
            ? x with { StartUtc = At.AddMinutes(300), EndUtc = At.AddMinutes(360) } : x).ToArray() };
        var result = Calculate(problem, baseline);
        Assert.Equal(At.AddMinutes(60), result.PromiseUtc);
        Assert.Equal(At.AddMinutes(30), result.Candidate.Assignments.Single(x => x.OrderId == "existing" && x.OperationId == "first").StartUtc);
        Assert.Equal(At.AddMinutes(150), result.Candidate.Assignments.Single(x => x.OrderId == "existing" && x.OperationId == "last").EndUtc);
        Assert.Equal(baseline.Assignments.Single(x => x.OrderId == "independent"), result.Candidate.Assignments.Single(x => x.OrderId == "independent"));
        Assert.DoesNotContain(result.Impact.AffectedOperations, x => x.Assignment.OrderId == "independent");
        Assert.Equal(5, result.Candidate.Metrics.ScheduledOperationCount);
        Assert.Equal(240, result.Candidate.Metrics.AssignedMinutes);
    }

    [Fact]
    public void Rush_capacity_window_reaches_long_occupancy_past_an_ended_short_assignment()
    {
        var longOrder = Order("long", Operation("op", "R1", 120));
        var shortOrder = Order("short", Operation("op", "R1", 30, earliest: 30));
        var laterOrder = Order("later", Operation("op", "R1", 90, earliest: 60));
        var rush = Order("rush", Operation("op", "R1", 15, earliest: 90, rush: true));
        var problem = Problem(longOrder, shortOrder, laterOrder, rush) with
        {
            Resources = Problem().Resources.Select(x => x.ResourceId == "R1" ? x with { CapacityUnits = 2 } : x).ToArray(),
        };
        var baseline = Baseline(problem, longOrder, shortOrder, laterOrder);
        var result = Calculate(problem, baseline);
        Assert.Equal(At.AddMinutes(105), result.PromiseUtc);
        Assert.Equal(["later", "long", "rush"], result.Impact.AffectedOperations.Select(x => x.Assignment.OrderId));
        var longHit = result.Impact.AffectedOperations.Single(x => x.Assignment.OrderId == "long");
        var step = Assert.Single(Assert.Single(longHit.Paths).Steps);
        Assert.Equal(new(At.AddMinutes(90), At.AddMinutes(105)), step.CompetitionWindow);
        Assert.Equal(2, step.CapacityUnits);
        Assert.Equal(baseline.Assignments.Single(x => x.OrderId == "short"), result.Candidate.Assignments.Single(x => x.OrderId == "short"));
    }

    [Theory]
    [InlineData(1, true, false)]
    [InlineData(2, false, false)]
    [InlineData(2, true, true)]
    public void Parallel_capacity_and_segment_gaps_determine_the_actual_affected_set(int capacity, bool segmented, bool concurrent)
    {
        var existing = Order("existing", Operation("op", "R1", 120));
        var other = Order("parallel", Operation("op", "R1", 120));
        var rush = Order("rush", Operation("op", "R1", 30, earliest: 60, rush: true));
        var problem = Problem(existing, rush) with { Resources = Problem().Resources.Select(x => x.ResourceId == "R1" ? x with { CapacityUnits = capacity } : x).ToArray() };
        var baseline = Baseline(problem, existing);
        if (segmented)
        {
            baseline = baseline with { Assignments = baseline.Assignments.Select(x => x with
            { EndUtc = At.AddMinutes(180), Segments = [new(At, At.AddMinutes(60)), new(At.AddMinutes(120), At.AddMinutes(180))] }).ToArray() };
        }
        if (concurrent)
        {
            problem = problem with { Orders = [existing, other, rush] };
            baseline = baseline with { Assignments = [.. baseline.Assignments, Baseline(problem, other).Assignments.Single()] };
        }
        var result = Calculate(problem, baseline);
        Assert.Equal(At.AddMinutes(90), result.PromiseUtc);
        Assert.DoesNotContain(result.Impact.AffectedOperations, x => x.Assignment.OrderId != "rush");
        foreach (var assignment in baseline.Assignments)
            Assert.Equal(assignment, result.Candidate.Assignments.Single(x => x.OrderId == assignment.OrderId));
    }

    [Fact]
    public void Saturated_parallel_capacity_moves_existing_queue()
    {
        var a = Order("a", Operation("op", "R1", 60));
        var b = Order("b", Operation("op", "R1", 60));
        var problem = Problem(a, b, Order("rush", Operation("op", "R1", 30, rush: true))) with
        { Resources = Problem().Resources.Select(x => x with { CapacityUnits = 2 }).ToArray() };
        var result = Calculate(problem, Baseline(problem, a, b));
        Assert.Equal(At.AddMinutes(30), result.PromiseUtc);
        Assert.Equal(2, result.Impact.RecalculateAssignments.Count);
        Assert.Contains(result.Candidate.Assignments, x => x.OrderId != "rush" && x.EndUtc == At.AddMinutes(90));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void Preserved_capacity_conflicts_use_segment_windows_and_half_open_boundaries(int capacity)
    {
        var a = Order("a", Operation("op", "R1", 60));
        var b = Order("b", Operation("op", "R1", 60));
        var c = Order("c", Operation("op", "R1", 30));
        var d = Order("d", Operation("op", "R1", 30));
        var problem = Problem(a, b, c, d, Order("rush", Operation("op", "R2", 30, rush: true))) with
        { Resources = Problem().Resources.Select(x => x.ResourceId == "R1" ? x with { CapacityUnits = capacity } : x).ToArray() };
        var baseline = Baseline(problem, a, b, c, d);
        baseline = baseline with { Assignments = baseline.Assignments.Select(x => x.OrderId switch
        {
            "a" => x with { StartUtc = At, EndUtc = At.AddMinutes(120), Segments = [new(At, At.AddMinutes(30)), new(At.AddMinutes(90), At.AddMinutes(120))] },
            "b" => x with { StartUtc = At.AddMinutes(30), EndUtc = At.AddMinutes(90) },
            "c" => x with { StartUtc = At.AddMinutes(100), EndUtc = At.AddMinutes(130) },
            _ => x with { StartUtc = At.AddMinutes(130), EndUtc = At.AddMinutes(160) },
        }).ToArray() };
        var result = Calculate(problem, baseline, locks: [("a", "op"), ("b", "op"), ("c", "op"), ("d", "op")]);
        foreach (var assignment in baseline.Assignments)
            Assert.Equal(assignment, result.Candidate.Assignments.Single(x => x.OrderId == assignment.OrderId));
        var conflicts = result.Candidate.Conflicts.Where(x => x.ReasonCode == ScheduleConflictReasonCodeContract.InvalidLockedAssignment).ToArray();
        Assert.Equal(capacity == 1 ? ["a", "c"] : Array.Empty<string>(), conflicts.Select(x => x.OrderId).ToArray());
        Assert.Equal(capacity == 1, result.PromiseUtc is null);
    }

    [Fact]
    public void Frozen_segments_remain_identical_and_new_unavailability_is_a_visible_blocking_conflict()
    {
        var existing = Order("existing", Operation("op", "R1", 120));
        var problem = Problem(existing, Order("rush", Operation("op", "R2", 30, rush: true)));
        var baseline = Baseline(problem, existing);
        var frozen = baseline.Assignments.Single() with { EndUtc = At.AddMinutes(180), Segments = [new(At, At.AddMinutes(60)), new(At.AddMinutes(120), At.AddMinutes(180))] };
        baseline = baseline with { Assignments = [frozen] };
        problem = problem with { UnavailabilityWindows = [new("R1", null, At.AddMinutes(130), At.AddMinutes(140), "outage")] };
        var result = Calculate(problem, baseline, locks: [("existing", "op")]);
        Assert.Equal(frozen, result.Candidate.Assignments.Single(x => x.OrderId == "existing"));
        Assert.Contains(result.Candidate.Conflicts, x => x.OrderId == "existing" && x.Severity == ScheduleConflictSeverityContract.Error);
        Assert.Null(result.PromiseUtc);
        Assert.Contains(SchedulingInsertionFailure.BlockingConflict, result.Failures);
    }

    [Theory]
    [InlineData("operation", "op")]
    [InlineData("resource", "R1")]
    public void Frozen_quality_block_is_visible_without_moving_the_assignment(string scopeType, string scopeId)
    {
        var existing = Order("existing", Operation("op", "R1", 60));
        var problem = Problem(existing, Order("rush", Operation("rush-op", "R2", 30, rush: true)));
        var baseline = Baseline(problem, existing);
        problem = problem with { QualityBlocks = [new(scopeType, scopeId, "hold", null)] };
        var result = Calculate(problem, baseline, locks: [("existing", "op")]);
        Assert.Equal(baseline.Assignments.Single(), result.Candidate.Assignments.Single(x => x.OrderId == "existing"));
        Assert.Null(result.PromiseUtc);
        Assert.Contains(result.Candidate.Conflicts, x => x.OrderId == "existing" && x.ReasonCode == ScheduleConflictReasonCodeContract.Quality);
    }

    [Theory]
    [InlineData("qualification")]
    [InlineData("calendar")]
    [InlineData("quality")]
    public void Unscheduled_chain_never_reports_a_partial_completion(string blockedBy)
    {
        var last = Operation("last", "R2", 30, predecessor: "first", rush: true);
        var problem = Problem(Order("rush", Operation("first", "R1", 30, rush: true), last));
        problem = blockedBy switch
        {
            "qualification" => problem with { Resources = problem.Resources.Select(x => x.ResourceId == "R2" ? x with { CapabilityCodes = ["other"] } : x).ToArray() },
            "calendar" => problem with { HorizonEndUtc = At.AddMinutes(40) },
            _ => problem with { QualityBlocks = [new("operation", "last", "hold", null)] },
        };
        var result = Calculate(problem, Baseline(problem));
        Assert.Single(result.Candidate.Assignments);
        Assert.Contains(result.Candidate.UnscheduledOperations, x => x.OperationId == "last");
        Assert.Null(result.PromiseUtc);
        Assert.Contains(SchedulingInsertionFailure.IncompleteChain, result.Failures);
    }

    [Fact]
    public void Calendar_setup_and_qualified_resources_constrain_the_same_candidate_and_promise()
    {
        var existing = Order("existing", Operation("op", "R1", 60));
        var rush = Order("rush", Operation("op", "R1", 30, rush: true) with { SetupMinutes = 15, EligibleResourceIds = ["R1", "R2"] });
        var problem = Problem(existing, rush) with
        {
            Resources = Problem().Resources.Select(x => x.ResourceId == "R2" ? x with { CapabilityCodes = ["other"] } : x).ToArray(),
            Calendars = [new("CAL", [new(At, At.AddMinutes(60), "shift"), new(At.AddMinutes(120), At.AddDays(2), "shift")])],
        };
        var result = Calculate(problem, Baseline(problem, existing), locks: [("existing", "op")]);
        var inserted = result.Candidate.Assignments.Single(x => x.OrderId == "rush");
        Assert.Equal("R1", inserted.ResourceId);
        Assert.Equal(At.AddMinutes(135), inserted.StartUtc);
        Assert.Equal(At.AddMinutes(165), result.PromiseUtc);
    }

    [Fact]
    public void Normalized_inputs_are_deterministic_and_fingerprint_covers_baseline_and_freeze()
    {
        var existing = Order("existing", Operation("op", "R1", 60));
        var problem = Problem(existing, Order("rush", Operation("op", "R1", 30, rush: true)));
        var baseline = Baseline(problem, existing);
        var first = Calculate(problem, baseline);
        Assert.Equal(JsonSerializer.Serialize(first), JsonSerializer.Serialize(Calculate(problem, baseline)));
        var reordered = problem with { Orders = problem.Orders.Reverse().ToArray(), Resources = problem.Resources.Reverse().ToArray() };
        Assert.Equal(first.InputFingerprint, first.Candidate.ProblemFingerprint);
        Assert.Equal(first.InputFingerprint, Calculate(reordered, baseline).InputFingerprint);
        Assert.NotEqual(first.InputFingerprint, Calculate(problem, baseline, locks: [("existing", "op")]).InputFingerprint);
        Assert.NotEqual(first.InputFingerprint, Calculate(problem with { HorizonEndUtc = At.AddDays(3) }, baseline).InputFingerprint);
    }

    [Fact]
    public void Fingerprint_normalizes_nested_sets_and_equivalent_utc_offsets_without_rewriting_baseline_segments()
    {
        var existing = Order("existing", Operation("op", "R1", 60));
        var problem = Problem(existing, Order("rush", Operation("op", "R2", 30, rush: true))) with
        {
            EquipmentDataRisks = [new("R1", "WC1", "unknown", At, At.AddDays(2)), new("R2", "WC2", "stale", At, At.AddDays(2))],
            MaterialReadiness = [new("order", "rush", At.AddMinutes(20), false, ["eta"],
                [new("M1", null, 1, 0, 1), new("M2", null, 1, 0, 1)])],
        };
        var baseline = Baseline(problem, existing);
        var segmented = baseline.Assignments.Single() with { EndUtc = At.AddMinutes(120), Segments = [new(At, At.AddMinutes(30)), new(At.AddMinutes(90), At.AddMinutes(120))] };
        baseline = baseline with { Assignments = [segmented] };
        var first = Calculate(problem, baseline);
        var shuffled = problem with
        {
            HorizonStartUtc = At.ToOffset(TimeSpan.FromHours(8)),
            EquipmentDataRisks = problem.EquipmentDataRisks!.Reverse().ToArray(),
            MaterialReadiness = problem.MaterialReadiness.Select(x => x with { Shortages = x.Shortages!.Reverse().ToArray() }).ToArray(),
        };
        var reversed = segmented with { Segments = segmented.Segments!.Reverse().ToArray() };
        var second = Calculate(shuffled, baseline with { Assignments = [reversed] });
        Assert.Equal(first.InputFingerprint, second.InputFingerprint);
        Assert.Equal(reversed, second.Candidate.Assignments.Single(x => x.OrderId == "existing"));
    }

    [Fact]
    public void Fixed_500_order_2000_operation_24_resource_sample_measures_pure_local_and_full_calculation()
    {
        var orders = Enumerable.Range(0, 500).Select(index => Order(index == 499 ? "rush" : $"WO-{index:D3}",
            Enumerable.Range(0, 4).Select(op => Operation($"OP-{op}", $"R{index % 24 + 1}", 15,
                predecessor: op == 0 ? null : $"OP-{op - 1}", rush: index == 499)).ToArray())).ToArray();
        var problem = Problem(orders) with { Resources = Enumerable.Range(1, 24).Select(index => new SchedulingResourceContract($"R{index}", $"WC{index}", ["cut"], 1, "CAL", $"{index:D2}")).ToArray() };
        var baseline = Baseline(problem, orders[..499]);
        // 固定等量预热，让后台 tiered JIT 优化发生在正式采样前；保留全部七次采样。
        for (var warmup = 0; warmup < 100; warmup++)
        {
            Calculate(problem, baseline);
            new FiniteCapacityScheduler().Schedule(problem, "full", At);
        }
        var local = new List<double>();
        var full = new List<double>();
        SchedulingInsertionResult? result = null;
        for (var run = 0; run < 7; run++)
        {
            var timer = Stopwatch.StartNew();
            result = Calculate(problem, baseline);
            timer.Stop();
            local.Add(timer.Elapsed.TotalMilliseconds);
            timer.Restart();
            new FiniteCapacityScheduler().Schedule(problem, "full", At);
            timer.Stop();
            full.Add(timer.Elapsed.TotalMilliseconds);
        }
        var localMedian = local.Order().ElementAt(3);
        var fullMedian = full.Order().ElementAt(3);
        output.WriteLine($"sample=500 orders/2000 operations/24 resources; warmupPerPath=100; samplesPerPath=7; .NET={RuntimeInformation.FrameworkDescription}; OS={RuntimeInformation.OSDescription}; arch={RuntimeInformation.ProcessArchitecture}; cpu={Environment.ProcessorCount}; localMs=[{string.Join(",", local)}]; fullMs=[{string.Join(",", full)}]; localMedianMs={localMedian:F3}; fullMedianMs={fullMedian:F3}; ratio={localMedian / fullMedian:P2}; affected={result!.Impact.AffectedOperations.Count}; recalculated={result.Impact.RecalculateAssignments.Count}");
        Assert.Equal(2000, result.Candidate.Assignments.Count);
        Assert.NotNull(result.PromiseUtc);
        Assert.True(localMedian <= 2000, $"local median={localMedian} ms");
        Assert.True(localMedian <= fullMedian * 0.5, $"local={localMedian} ms; full={fullMedian} ms");
    }

    private static SchedulingInsertionResult Calculate(SchedulingProblemContract problem, SchedulePlanContract baseline,
        (string OrderId, string OperationId)[]? locks = null) => SchedulingInsertionCalculator.Calculate(new FiniteCapacityScheduler(), problem,
            baseline, "rush", "candidate", [], locks ?? [], new(At, TimeSpan.Zero, new Dictionary<string, TimeSpan>()));

    private static SchedulePlanContract Baseline(SchedulingProblemContract problem, params SchedulingOrderContract[] orders) =>
        new FiniteCapacityScheduler().Schedule(problem with { Orders = orders }, "baseline", At);

    private static SchedulingOrderContract Order(string id, params SchedulingOperationContract[] operations) =>
        new(id, id == "rush" ? "RUSH-SKU" : "SKU", 10, At.AddDays(1), 0, id == "rush", operations);

    private static SchedulingOperationContract Operation(string id, string resource, int duration, string? predecessor = null, int earliest = 0, bool rush = false) =>
        new(id, predecessor is null ? 10 : 20, predecessor is null ? [] : [predecessor], duration, "cut", [resource], resource,
            At.AddMinutes(earliest), At.AddDays(1), 0, rush, ScheduleSplitPolicyContract.NonSplittable, null, null, null);

    private static SchedulingProblemContract Problem(params SchedulingOrderContract[] orders) => new(1, "problem", "org", "env", At, At.AddDays(2), orders,
        [new("R1", "WC1", ["cut"], 1, "CAL", "1"), new("R2", "WC2", ["cut"], 1, "CAL", "2"), new("R3", "WC3", ["cut"], 1, "CAL", "3")],
        [new("CAL", [new(At, At.AddDays(2), "shift")])], [], [], [], []);
}
