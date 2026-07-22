using Nerv.IIP.Business.Scheduling.Domain.Services;
using Nerv.IIP.Business.Scheduling.Web.Application.Urgency;
using Nerv.IIP.Contracts.Scheduling;

namespace Nerv.IIP.Business.Scheduling.Web.Tests;

public sealed class OrderUrgencyFactAssemblerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 22, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Uses_business_reference_and_keeps_all_execution_risk_sources()
    {
        var result = OrderUrgencyFactAssembler.Calculate(
            Problem(toolingAvailable: false),
            Plan(
                conflicts:
                [
                    new ScheduleConflictContract("c-cap", ScheduleConflictReasonCodeContract.Capacity,
                        ScheduleConflictSeverityContract.Error, "WO-1", "OP-1", "DEV-1", "capacity"),
                ]),
            "WO-1",
            new BusinessPriorityFact(BusinessPriorityLevel.P2, "default", "standard", Now, null, 0),
            Now,
            "fingerprint");

        Assert.Equal("SO-1001", result.BusinessReference);
        Assert.Contains("material.shortage", result.ExecutionRisk.ReasonCodes);
        Assert.Contains("equipment.unavailable", result.ExecutionRisk.ReasonCodes);
        Assert.Contains("quality.hold", result.ExecutionRisk.ReasonCodes);
        Assert.Contains("tooling.unavailable", result.ExecutionRisk.ReasonCodes);
        Assert.Contains("capacity.insufficient", result.ExecutionRisk.ReasonCodes);
        Assert.Equal(OrderUrgencyLevel.HighRisk, result.ExecutionRisk.Level);
    }

    [Fact]
    public void Uses_latest_assignment_end_as_estimated_completion_for_cr_and_slack()
    {
        var result = OrderUrgencyFactAssembler.Calculate(
            Problem(toolingAvailable: true),
            Plan(assignments:
            [
                new ScheduleAssignmentContract("a-1", "WO-1", "OP-1", 1, "DEV-1", "WC-1",
                    Now, Now.AddHours(10), false, "scheduled"),
            ]),
            "WO-1",
            new BusinessPriorityFact(BusinessPriorityLevel.P2, "default", "standard", Now, null, 0),
            Now,
            "fingerprint");

        Assert.Equal(0.8m, result.CriticalRatio);
        Assert.Equal(-2m, result.SlackHours);
        Assert.Equal(Now.AddHours(10), result.TimeCriticality.EstimatedCompletionUtc);
    }

    private static SchedulingProblemContract Problem(bool toolingAvailable)
    {
        return new SchedulingProblemContract(
            1, "problem-1", "org", "env", Now, Now.AddDays(2),
            [new SchedulingOrderContract(
                "WO-1", "SKU-1", 1m, Now.AddHours(8), 100, false,
                [new SchedulingOperationContract(
                    "OP-1", 1, [], 600, "CAP-1", ["DEV-1"], "DEV-1", Now,
                    Now.AddHours(8), 100, false, ScheduleSplitPolicyContract.NonSplittable,
                    null, null, null, 0, [], ["TOOL-1"], toolingAvailable)],
                "SO-1001")],
            [new SchedulingResourceContract("DEV-1", "WC-1", ["CAP-1"], 1, "CAL-1", "DEV-1")],
            [new SchedulingCalendarContract("CAL-1", [new SchedulingTimeWindowContract(Now, Now.AddDays(2), "shift")])],
            [new SchedulingUnavailabilityWindowContract("DEV-1", null, Now, Now.AddHours(12), "equipment.unavailable")],
            [new SchedulingMaterialReadinessContract("order", "WO-1", null, false, ["material.shortage"])],
            [new SchedulingQualityBlockContract("order", "WO-1", "quality.hold", null)],
            []);
    }

    private static SchedulePlanContract Plan(
        IReadOnlyCollection<ScheduleAssignmentContract>? assignments = null,
        IReadOnlyCollection<ScheduleConflictContract>? conflicts = null)
    {
        return new SchedulePlanContract(
            1, "plan-1", "problem-1", "fingerprint", "aps-lite-v1",
            SchedulePlanStatusContract.Generated, Now,
            new SchedulePlanMetricsContract(1, 0, 600, 600, 0, 0, 1m, 0.5m),
            assignments ?? [], [], conflicts ?? [], [], [], []);
    }
}
