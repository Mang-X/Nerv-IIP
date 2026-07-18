using Nerv.IIP.Business.Scheduling.Domain.AggregatesModel.SchedulePlanAggregate;
using Nerv.IIP.Business.Scheduling.Domain.DomainEvents;

namespace Nerv.IIP.Business.Scheduling.Domain.Tests;

public sealed class SchedulePlanAggregateTests
{
    [Fact]
    public void Generated_plan_stores_metadata_and_generated_event()
    {
        var plan = CreatePlan();

        Assert.Equal("plan-001", plan.PlanId);
        Assert.Equal("problem-001", plan.ProblemId);
        Assert.Equal("fingerprint-001", plan.ProblemFingerprint);
        Assert.Equal("aps-lite-v1", plan.AlgorithmVersion);
        Assert.Equal(1, plan.ContractVersion);
        Assert.Equal(SchedulePlanLifecycleStatus.Generated, plan.Status);
        Assert.Contains(plan.GetDomainEvents(), x => x is SchedulePlanGeneratedDomainEvent);
        Assert.Contains(plan.GetDomainEvents(), x => x is ScheduleConflictDetectedDomainEvent);
    }

    [Fact]
    public void Generated_plan_can_be_released_once()
    {
        var plan = CreatePlan();
        plan.ClearDomainEvents();
        var releasedAtUtc = new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);

        plan.Release(releasedAtUtc);

        Assert.Equal(SchedulePlanLifecycleStatus.Released, plan.Status);
        Assert.Equal(releasedAtUtc, plan.ReleasedAtUtc);
        Assert.IsType<SchedulePlanReleasedDomainEvent>(Assert.Single(plan.GetDomainEvents()));
    }

    [Fact]
    public void Repeated_release_is_idempotent_without_duplicate_release_event()
    {
        var plan = CreatePlan();
        plan.ClearDomainEvents();
        var firstRelease = new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);

        plan.Release(firstRelease);
        plan.Release(firstRelease.AddHours(1));

        Assert.Equal(firstRelease, plan.ReleasedAtUtc);
        Assert.Single(plan.GetDomainEvents().OfType<SchedulePlanReleasedDomainEvent>());
    }

    [Fact]
    public void Released_plan_can_be_superseded_once()
    {
        var plan = CreatePlan();
        plan.Release(new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero), 1);
        plan.ClearDomainEvents();
        var supersededAtUtc = new DateTimeOffset(2026, 6, 1, 13, 0, 0, TimeSpan.Zero);

        plan.Supersede("plan-002", supersededAtUtc);
        plan.Supersede("plan-002", supersededAtUtc.AddMinutes(1));

        Assert.Equal(SchedulePlanLifecycleStatus.Superseded, plan.Status);
        Assert.Equal(1, plan.ReleaseRevision);
        Assert.Equal(supersededAtUtc, plan.RevokedAtUtc);
        Assert.Equal("plan-002", plan.SupersededByPlanId);
        Assert.Equal(SchedulePlanRevocationReason.Superseded, plan.RevocationReason);
        Assert.IsType<SchedulePlanRevokedDomainEvent>(Assert.Single(plan.GetDomainEvents()));
    }

    [Fact]
    public void Released_plan_can_be_explicitly_revoked_once()
    {
        var plan = CreatePlan();
        plan.Release(new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero), 4);
        plan.ClearDomainEvents();
        var revokedAtUtc = new DateTimeOffset(2026, 6, 1, 13, 0, 0, TimeSpan.Zero);

        plan.Revoke(revokedAtUtc);
        plan.Revoke(revokedAtUtc.AddMinutes(1));

        Assert.Equal(SchedulePlanLifecycleStatus.Revoked, plan.Status);
        Assert.Equal(4, plan.ReleaseRevision);
        Assert.Equal(revokedAtUtc, plan.RevokedAtUtc);
        Assert.Null(plan.SupersededByPlanId);
        Assert.Equal(SchedulePlanRevocationReason.Explicit, plan.RevocationReason);
        Assert.IsType<SchedulePlanRevokedDomainEvent>(Assert.Single(plan.GetDomainEvents()));
    }

    [Fact]
    public void Released_plan_cannot_be_mutated_or_regenerated()
    {
        var plan = CreatePlan();
        plan.Release(new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero));

        Assert.Throws<InvalidOperationException>(() => plan.AddAssignment(CreateAssignment("assign-002", "op-002")));
        Assert.Throws<InvalidOperationException>(() => plan.ReplaceGeneratedPlan(CreateContract("plan-001", "fingerprint-002")));
    }

    [Fact]
    public void Replace_generated_plan_rematerializes_all_detail_facts_from_new_contract()
    {
        var plan = CreatePlan();
        plan.ClearDomainEvents();
        var replacement = CreateContract("plan-001", "fingerprint-002") with
        {
            Assignments = [CreateAssignment("assign-002", "op-002")],
            ResourceLoads =
            [
                new GeneratedScheduleResourceLoadSnapshot(
                    ResourceId: "res-002",
                    WindowStartUtc: new DateTimeOffset(2026, 6, 2, 8, 0, 0, TimeSpan.Zero),
                    WindowEndUtc: new DateTimeOffset(2026, 6, 2, 16, 0, 0, TimeSpan.Zero),
                    AssignedMinutes: 120,
                    AvailableMinutes: 480,
                    Utilization: 0.25m)
            ],
            Conflicts =
            [
                new GeneratedScheduleConflictSnapshot(
                    ConflictId: "conflict-002",
                    ReasonCode: ScheduleConflictReasonCode.Material,
                    Severity: ScheduleConflictSeverity.Error,
                    OrderId: "wo-003",
                    OperationId: "op-030",
                    ResourceId: "res-002",
                    Message: "material unavailable")
            ],
            UnscheduledOperations =
            [
                new GeneratedUnscheduledOperationSnapshot(
                    OrderId: "wo-003",
                    OperationId: "op-030",
                    ReasonCode: ScheduleConflictReasonCode.Material,
                    Message: "material unavailable")
            ]
        };

        plan.ReplaceGeneratedPlan(replacement);

        Assert.DoesNotContain(plan.Assignments, x => x.AssignmentId == "assign-001");
        Assert.Contains(plan.Assignments, x => x.AssignmentId == "assign-002" && x.OperationId == "op-002");
        Assert.DoesNotContain(plan.ResourceLoads, x => x.ResourceId == "res-001");
        Assert.Contains(plan.ResourceLoads, x => x.ResourceId == "res-002" && x.AssignedMinutes == 120);
        Assert.DoesNotContain(plan.Conflicts, x => x.ConflictPublicId == "conflict-001");
        Assert.Contains(plan.Conflicts, x => x.ConflictPublicId == "conflict-002" && x.ReasonCode == ScheduleConflictReasonCode.Material);
        Assert.DoesNotContain(plan.UnscheduledOperations, x => x.WorkOrderId == "wo-002");
        Assert.Contains(plan.UnscheduledOperations, x => x.WorkOrderId == "wo-003" && x.OperationId == "op-030");
        Assert.Contains(plan.GetDomainEvents(), x => x is SchedulePlanGeneratedDomainEvent);
        Assert.Contains(plan.GetDomainEvents(), x => x is ScheduleConflictDetectedDomainEvent);
    }

    [Fact]
    public void Replace_generated_plan_rejects_released_contract()
    {
        var plan = CreatePlan();
        var replacement = CreateContract("plan-001", "fingerprint-002") with
        {
            Status = SchedulePlanInputStatus.Released,
        };

        Assert.Throws<InvalidOperationException>(() => plan.ReplaceGeneratedPlan(replacement));
    }

    [Fact]
    public void Replace_generated_plan_rejects_mismatched_plan_id()
    {
        var plan = CreatePlan();
        var replacement = CreateContract("plan-other", "fingerprint-002");

        Assert.Throws<InvalidOperationException>(() => plan.ReplaceGeneratedPlan(replacement));
        Assert.Equal("plan-001", plan.PlanId);
    }

    private static SchedulePlan CreatePlan()
    {
        return SchedulePlan.FromGeneratedPlan(
            organizationId: "org-001",
            environmentId: "env-dev",
            plan: CreateContract("plan-001", "fingerprint-001"));
    }

    private static GeneratedSchedulePlanSnapshot CreateContract(string planId, string fingerprint)
    {
        return new GeneratedSchedulePlanSnapshot(
            ContractVersion: 1,
            PlanId: planId,
            ProblemId: "problem-001",
            ProblemFingerprint: fingerprint,
            AlgorithmVersion: "aps-lite-v1",
            Status: SchedulePlanInputStatus.Generated,
            GeneratedAtUtc: new DateTimeOffset(2026, 6, 1, 8, 0, 0, TimeSpan.Zero),
            Metrics: new GeneratedSchedulePlanMetricsSnapshot(
                ScheduledOperationCount: 1,
                UnscheduledOperationCount: 1,
                AssignedMinutes: 60,
                MakespanMinutes: 60,
                TotalTardinessMinutes: 0,
                LateOperationCount: 0,
                OnTimeRate: 1m,
                AverageResourceUtilization: 0.125m),
            Assignments: [CreateAssignment("assign-001", "op-001")],
            ResourceLoads:
            [
                new GeneratedScheduleResourceLoadSnapshot(
                    ResourceId: "res-001",
                    WindowStartUtc: new DateTimeOffset(2026, 6, 1, 8, 0, 0, TimeSpan.Zero),
                    WindowEndUtc: new DateTimeOffset(2026, 6, 1, 16, 0, 0, TimeSpan.Zero),
                    AssignedMinutes: 60,
                    AvailableMinutes: 480,
                    Utilization: 0.125m)
            ],
            Conflicts:
            [
                new GeneratedScheduleConflictSnapshot(
                    ConflictId: "conflict-001",
                    ReasonCode: ScheduleConflictReasonCode.DueDate,
                    Severity: ScheduleConflictSeverity.Warning,
                    OrderId: "wo-001",
                    OperationId: "op-001",
                    ResourceId: "res-001",
                    Message: "late")
            ],
            UnscheduledOperations:
            [
                new GeneratedUnscheduledOperationSnapshot(
                    OrderId: "wo-002",
                    OperationId: "op-020",
                    ReasonCode: ScheduleConflictReasonCode.NoEligibleResource,
                    Message: "no resource")
            ]);
    }

    private static GeneratedScheduleAssignmentSnapshot CreateAssignment(string assignmentId, string operationId)
    {
        return new GeneratedScheduleAssignmentSnapshot(
            AssignmentId: assignmentId,
            OrderId: "wo-001",
            OperationId: operationId,
            OperationSequence: 10,
            ResourceId: "res-001",
            WorkCenterId: "wc-001",
            StartUtc: new DateTimeOffset(2026, 6, 1, 8, 0, 0, TimeSpan.Zero),
            EndUtc: new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero),
            IsLocked: false,
            ExplanationCode: "scheduled");
    }
}
