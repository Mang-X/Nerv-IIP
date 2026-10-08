using System.Text.Json;
using Nerv.IIP.Business.Scheduling.Web.Application.Scheduling;
using Nerv.IIP.Contracts.EquipmentRuntime;
using Nerv.IIP.Contracts.Scheduling;
using static Nerv.IIP.Business.Scheduling.Web.Tests.RightShiftCandidateGeneratorTests;

namespace Nerv.IIP.Business.Scheduling.Web.Tests;

// DomainInvariant: #4246 / #3629 approved spec r1: exact overlap, persisted qualification, confirmed availability, read-only.
public sealed class SchedulingDowntimeImpactTests
{
    private static readonly DateTimeOffset At = Assignment("A", "a", "R1", 0, 60).StartUtc;

    [Fact]
    public void Raw_time_and_exact_segment_overlap_are_preserved_without_moving_assignments()
    {
        var problem = Problem(Order("A", Operation("same", "R1") with { EligibleResourceIds = ["R1", "R2", "R3"] }),
            Order("B", Operation("same", "R1")), Order("C", Operation("gap", "R1")));
        var assignments = new[] { Assignment("A", "same", "R1", 0, 60), Assignment("B", "same", "R1", 120, 180),
            Assignment("C", "gap", "R1", 0, 120) with { Segments = [new(At, At.AddMinutes(10)), new(At.AddMinutes(90), At.AddMinutes(120))] } };
        var plan = Input(problem, assignments, []).Baseline;
        var facts = new[] { new SchedulingDowntimeFactContract("maintenance", "mw", "R1", null,
            At.AddDays(-2), At.AddMinutes(60), At.AddMinutes(30)),
            new SchedulingDowntimeFactContract("mes", "mes-gap", "R1", null, At.AddMinutes(20), At.AddMinutes(40), null) };
        var before = JsonSerializer.Serialize(plan);
        var result = SchedulingDowntimeImpactProjector.Project(problem, plan, facts, Availability(Window("R2")), At);
        var maintenance = result.Items.Single(x => x.Fact.SourceReferenceId == "mw");
        Assert.Equal(At.AddDays(-2), maintenance.Fact.StartedAtUtc);
        Assert.Equal(At.AddMinutes(60), maintenance.Fact.RecoveredAtUtc);
        Assert.Equal(At.AddMinutes(30), maintenance.Fact.ExpectedRestoreAtUtc);
        Assert.Equal(new[] { ("A", "same"), ("C", "gap") }, maintenance.AffectedOperations.Select(x => (x.WorkOrderId, x.OperationId)));
        var mes = result.Items.Single(x => x.Fact.SourceReferenceId == "mes-gap");
        Assert.Equal(("A", "same"), (Assert.Single(mes.AffectedOperations).WorkOrderId, mes.AffectedOperations[0].OperationId));
        Assert.Equal(before, JsonSerializer.Serialize(plan));
    }

    [Fact]
    public void Alternatives_require_snapshot_eligibility_capabilities_and_confirmed_current_availability_and_count_operations_once()
    {
        var problem = Problem(Order("A", Operation("a", "R1") with { EligibleResourceIds = ["R1", "R2", "R2", "R3", "R4", "R5", "R6", "R7"] }),
            Order("B", Operation("b", "R1")));
        problem = problem with { Resources = problem.Resources.Concat(new[] {
            new SchedulingResourceContract("R4", "WC-R4", ["WRONG"], 1, "CAL", "4"),
            new SchedulingResourceContract("R5", "WC-R5", ["CAP"], 1, "CAL", "5"),
            new SchedulingResourceContract("R6", "WC-R6", ["CAP"], 1, "CAL", "6"),
            new SchedulingResourceContract("R7", "WC-R7", ["CAP"], 1, "CAL", "7") }).ToArray() };
        var plan = Input(problem, [Assignment("A", "a", "R1", 0, 60), Assignment("B", "b", "R1", 0, 60)], []).Baseline;
        var facts = new[] { new SchedulingDowntimeFactContract("mes", "outage", "R1", null, At.AddHours(-1), null, At.AddMinutes(-1)) };
        var result = SchedulingDowntimeImpactProjector.Project(problem, plan, facts, Availability(
            Window("R2"), Window("R3", EquipmentRuntimeAvailabilityStatus.Unknown), Window("R4"), Window("R5"),
            Window("R5", EquipmentRuntimeAvailabilityStatus.Unavailable), Window("R6") with { EndUtc = At }, Window("R7")), At);
        var item = Assert.Single(result.Items);
        Assert.Null(item.Fact.RecoveredAtUtc); // expired ETR does not recover an asset
        Assert.Equal(1, item.OperationsWithAlternativesCount);
        Assert.Equal(new[] { "R2", "R7" }, item.AffectedOperations.Single(x => x.WorkOrderId == "A").AvailableAlternativeResourceIds);
        Assert.Empty(item.AffectedOperations.Single(x => x.WorkOrderId == "B").AvailableAlternativeResourceIds);
    }

    [Fact]
    public void Work_center_downtime_matches_only_its_center_and_raw_current_downtime_excludes_reported_available_alternative()
    {
        var problem = Problem(Order("A", Operation("a", "R1") with { EligibleResourceIds = ["R1", "R2"] }),
            Order("B", Operation("b", "R3")));
        var plan = Input(problem, [Assignment("A", "a", "R1", 0, 60), Assignment("B", "b", "R3", 0, 60)], []).Baseline;
        var facts = new[] { new SchedulingDowntimeFactContract("mes", "center", null, "WC-R1", At.AddHours(-1), null, null),
            new SchedulingDowntimeFactContract("mes", "alternative-down", "R2", "WC-R2", At.AddHours(-1), null, null) };
        var result = SchedulingDowntimeImpactProjector.Project(problem, plan, facts, Availability(Window("R2")), At);
        var center = result.Items.Single(x => x.Fact.SourceReferenceId == "center");
        Assert.Equal("A", Assert.Single(center.AffectedOperations).WorkOrderId);
        Assert.Equal(0, center.OperationsWithAlternativesCount);
        Assert.Empty(center.AffectedOperations[0].AvailableAlternativeResourceIds);
        Assert.Empty(result.Items.Single(x => x.Fact.SourceReferenceId == "alternative-down").AffectedOperations);
    }

    private static EquipmentRuntimeAvailabilityResponse Availability(params EquipmentRuntimeAvailabilityWindowContract[] windows) =>
        new(1, "org", "env", At, At.AddMinutes(1), windows);
    private static EquipmentRuntimeAvailabilityWindowContract Window(string id, EquipmentRuntimeAvailabilityStatus status = EquipmentRuntimeAvailabilityStatus.Available) =>
        new(id, null, status, "state", EquipmentRuntimeSeverity.Blocked, At.AddMinutes(-1), At.AddMinutes(1),
            EquipmentRuntimeSourceType.StaleSource, "source", "state", []);
}
