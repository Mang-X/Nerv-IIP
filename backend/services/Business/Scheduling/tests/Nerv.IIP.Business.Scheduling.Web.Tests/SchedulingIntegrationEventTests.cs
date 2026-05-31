using Nerv.IIP.Business.Scheduling.Domain.AggregatesModel.SchedulePlanAggregate;
using Nerv.IIP.Business.Scheduling.Domain.DomainEvents;
using Nerv.IIP.Business.Scheduling.Web.Application.IntegrationEventConverters;
using Nerv.IIP.Business.Scheduling.Web.Application.IntegrationEvents;
using Nerv.IIP.Contracts.Scheduling;

namespace Nerv.IIP.Business.Scheduling.Web.Tests;

public sealed class SchedulingIntegrationEventTests
{
    [Fact]
    public void Schedule_plan_generated_event_uses_required_name_and_public_payload()
    {
        var plan = CreatePlan();

        var integrationEvent = new SchedulePlanGeneratedIntegrationEventConverter()
            .Convert(new SchedulePlanGeneratedDomainEvent(plan));

        Assert.Equal("scheduling.SchedulePlanGenerated", integrationEvent.EventType);
        Assert.Equal("business-scheduling", integrationEvent.SourceService);
        Assert.Equal("plan-001", integrationEvent.Payload.PlanId);
        Assert.Equal("problem-001", integrationEvent.Payload.ProblemId);
        Assert.Equal(1, integrationEvent.Payload.ContractVersion);
        Assert.Equal("aps-lite-v1", integrationEvent.Payload.AlgorithmVersion);
        Assert.Equal("fingerprint-001", integrationEvent.Payload.ProblemFingerprint);
        Assert.Equal("generated", integrationEvent.Payload.PlanStatus);
        Assert.Contains(integrationEvent.Payload.AffectedOperations, x => x.WorkOrderId == "wo-001" && x.OperationId == "op-001");
    }

    [Fact]
    public void Schedule_conflict_detected_event_uses_required_name_and_reason_code()
    {
        var plan = CreatePlan();
        var conflict = plan.Conflicts.Single();

        var integrationEvent = new ScheduleConflictDetectedIntegrationEventConverter()
            .Convert(new ScheduleConflictDetectedDomainEvent(plan, conflict));

        Assert.Equal("scheduling.ScheduleConflictDetected", integrationEvent.EventType);
        Assert.Equal("plan-001", integrationEvent.Payload.PlanId);
        Assert.Equal("problem-001", integrationEvent.Payload.ProblemId);
        Assert.Equal("dueDate", integrationEvent.Payload.ConflictReasonCode);
        Assert.Equal("wo-001", integrationEvent.Payload.WorkOrderId);
        Assert.Equal("op-001", integrationEvent.Payload.OperationId);
        Assert.Equal("generated", integrationEvent.Payload.PlanStatus);
    }

    [Fact]
    public void Schedule_plan_released_event_uses_required_name_and_released_status()
    {
        var plan = CreatePlan();
        plan.Release(new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero));

        var integrationEvent = new SchedulePlanReleasedIntegrationEventConverter()
            .Convert(new SchedulePlanReleasedDomainEvent(plan));

        Assert.Equal("scheduling.SchedulePlanReleased", integrationEvent.EventType);
        Assert.Equal("plan-001", integrationEvent.Payload.PlanId);
        Assert.Equal("problem-001", integrationEvent.Payload.ProblemId);
        Assert.Equal("released", integrationEvent.Payload.PlanStatus);
        Assert.Contains(integrationEvent.Payload.AffectedOperations, x => x.WorkOrderId == "wo-001" && x.OperationId == "op-001");
    }

    private static SchedulePlan CreatePlan()
    {
        return SchedulePlan.FromGeneratedContract(
            "org-001",
            "env-dev",
            new SchedulePlanContract(
                ContractVersion: 1,
                PlanId: "plan-001",
                ProblemId: "problem-001",
                ProblemFingerprint: "fingerprint-001",
                AlgorithmVersion: "aps-lite-v1",
                Status: SchedulePlanStatusContract.Generated,
                GeneratedAtUtc: new DateTimeOffset(2026, 6, 1, 8, 0, 0, TimeSpan.Zero),
                Assignments:
                [
                    new ScheduleAssignmentContract(
                        AssignmentId: "assign-001",
                        OrderId: "wo-001",
                        OperationId: "op-001",
                        OperationSequence: 10,
                        ResourceId: "res-001",
                        WorkCenterId: "wc-001",
                        StartUtc: new DateTimeOffset(2026, 6, 1, 8, 0, 0, TimeSpan.Zero),
                        EndUtc: new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero),
                        IsLocked: false,
                        ExplanationCode: "scheduled")
                ],
                ResourceLoads: [],
                Conflicts:
                [
                    new ScheduleConflictContract(
                        ConflictId: "conflict-001",
                        ReasonCode: ScheduleConflictReasonCodeContract.DueDate,
                        Severity: ScheduleConflictSeverityContract.Warning,
                        OrderId: "wo-001",
                        OperationId: "op-001",
                        ResourceId: "res-001",
                        Message: "late")
                ],
                UnscheduledOperations: [],
                ChangeSummary: [],
                GanttItems: []));
    }
}
