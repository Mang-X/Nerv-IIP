using System.Diagnostics;
using System.Reflection;
using Microsoft.AspNetCore.Http;
using Nerv.IIP.Business.Scheduling.Domain.AggregatesModel.SchedulePlanAggregate;
using Nerv.IIP.Business.Scheduling.Domain.DomainEvents;
using Nerv.IIP.Business.Scheduling.Web.Application.Queries;
using Nerv.IIP.Business.Scheduling.Web.Application.IntegrationEventConverters;
using Nerv.IIP.Contracts.IntegrationEvents;
using Nerv.IIP.Contracts.Scheduling;

namespace Nerv.IIP.Business.Scheduling.Web.Tests;

public sealed class SchedulingIntegrationEventTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 6, 1, 10, 30, 0, TimeSpan.Zero);

    [Fact]
    public void Schedule_plan_generated_event_uses_required_name_and_public_payload()
    {
        var plan = CreatePlan();

        var integrationEvent = new SchedulePlanGeneratedIntegrationEventConverter(
                new FixedTimeProvider(FixedNow),
                new StubSchedulingIntegrationEventContextAccessor())
            .Convert(new SchedulePlanGeneratedDomainEvent(plan));

        Assert.Equal("scheduling.SchedulePlanGenerated", integrationEvent.EventType);
        Assert.Equal("business-scheduling", integrationEvent.SourceService);
        Assert.Equal(FixedNow, integrationEvent.OccurredAtUtc);
        Assert.Equal("plan-001", integrationEvent.Payload.PlanId);
        Assert.Equal("problem-001", integrationEvent.Payload.ProblemId);
        Assert.Equal(1, integrationEvent.Payload.ContractVersion);
        Assert.Equal("aps-lite-v1", integrationEvent.Payload.AlgorithmVersion);
        Assert.Equal("fingerprint-001", integrationEvent.Payload.ProblemFingerprint);
        Assert.Equal("generated", integrationEvent.Payload.PlanStatus);
        Assert.Contains(integrationEvent.Payload.AffectedOperations, x => x.WorkOrderId == "wo-001" && x.OperationId == "op-001" && x.StandardOperationCode == "STD-ASSY");
    }

    [Fact]
    public void Schedule_conflict_detected_event_uses_required_name_and_reason_code()
    {
        var plan = CreatePlan();
        var conflict = plan.Conflicts.Single();

        var integrationEvent = new ScheduleConflictDetectedIntegrationEventConverter(
                new FixedTimeProvider(FixedNow),
                new StubSchedulingIntegrationEventContextAccessor())
            .Convert(new ScheduleConflictDetectedDomainEvent(plan, conflict));

        Assert.Equal("scheduling.ScheduleConflictDetected", integrationEvent.EventType);
        Assert.Equal(FixedNow, integrationEvent.OccurredAtUtc);
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

        var integrationEvent = new SchedulePlanReleasedIntegrationEventConverter(
                new FixedTimeProvider(FixedNow),
                new StubSchedulingIntegrationEventContextAccessor())
            .Convert(new SchedulePlanReleasedDomainEvent(plan));

        Assert.Equal("scheduling.SchedulePlanReleased", integrationEvent.EventType);
        Assert.Equal("plan-001", integrationEvent.Payload.PlanId);
        Assert.Equal("problem-001", integrationEvent.Payload.ProblemId);
        Assert.Equal("released", integrationEvent.Payload.PlanStatus);
        Assert.Contains(integrationEvent.Payload.AffectedOperations, x => x.WorkOrderId == "wo-001" && x.OperationId == "op-001" && x.StandardOperationCode == "STD-ASSY");
    }

    [Fact]
    public void Schedule_plan_invalidated_event_uses_required_name_reason_and_affected_operations()
    {
        var plan = CreatePlan();
        var invalidation = SchedulePlanInvalidation.Create(
            "org-001",
            "env-dev",
            "plan-001",
            "maintenance-event-001",
            "maintenance.AssetUnavailable",
            "maintenance",
            SchedulingPlanInvalidationReasons.EquipmentUnavailable,
            "res-001",
            null,
            null,
            null,
            new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero),
            FixedNow);
        var snapshot = SchedulePlanInvalidatedSnapshot.FromPlan(
            plan,
            [plan.Assignments.Single()]);

        var integrationEvent = new SchedulePlanInvalidatedIntegrationEventConverter(
                new FixedTimeProvider(FixedNow),
                new StubSchedulingIntegrationEventContextAccessor())
            .Convert(new SchedulePlanInvalidatedDomainEvent(invalidation, snapshot));

        Assert.Equal(SchedulingIntegrationEventTypes.SchedulePlanInvalidated, integrationEvent.EventType);
        Assert.Equal("plan-001", integrationEvent.Payload.PlanId);
        Assert.Equal("problem-001", integrationEvent.Payload.ProblemId);
        Assert.Equal(SchedulingPlanInvalidationReasons.EquipmentUnavailable, integrationEvent.Payload.ReasonCode);
        Assert.Equal("maintenance.AssetUnavailable", integrationEvent.Payload.SourceEventType);
        Assert.Equal("maintenance-event-001", integrationEvent.Payload.SourceEventId);
        Assert.Contains("res-001", integrationEvent.Payload.AffectedResourceIds);
        Assert.Contains("wc-001", integrationEvent.Payload.AffectedResourceIds);
        Assert.Contains(integrationEvent.Payload.AffectedOperations, x => x.WorkOrderId == "wo-001" && x.OperationId == "op-001");
    }

    [Fact]
    public void Schedule_plan_event_propagates_correlation_causation_and_actor_context()
    {
        var plan = CreatePlan();
        var converter = new SchedulePlanGeneratedIntegrationEventConverter(
            new FixedTimeProvider(FixedNow),
            new StubSchedulingIntegrationEventContextAccessor(new SchedulingIntegrationEventContext(
                "corr-scheduling-001",
                "cmd-run-schedule-001",
                "user:planner-001")));

        var integrationEvent = converter.Convert(new SchedulePlanGeneratedDomainEvent(plan));

        Assert.Equal("corr-scheduling-001", integrationEvent.CorrelationId);
        Assert.Equal("cmd-run-schedule-001", integrationEvent.CausationId);
        Assert.Equal("user:planner-001", integrationEvent.Actor);
    }

    [Fact]
    public void Scheduling_events_populate_required_envelope_fields_for_all_event_types()
    {
        var plan = CreatePlan();
        var contextAccessor = new StubSchedulingIntegrationEventContextAccessor(new SchedulingIntegrationEventContext(
            "corr-envelope-001",
            "cause-envelope-001",
            "user:planner-001"));
        var timeProvider = new FixedTimeProvider(FixedNow);

        var generatedEvent = new SchedulePlanGeneratedIntegrationEventConverter(timeProvider, contextAccessor)
            .Convert(new SchedulePlanGeneratedDomainEvent(plan));
        var conflictEvent = new ScheduleConflictDetectedIntegrationEventConverter(timeProvider, contextAccessor)
            .Convert(new ScheduleConflictDetectedDomainEvent(plan, plan.Conflicts.Single()));
        plan.Release(new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero));
        var releasedEvent = new SchedulePlanReleasedIntegrationEventConverter(timeProvider, contextAccessor)
            .Convert(new SchedulePlanReleasedDomainEvent(plan));

        AssertSchedulingEnvelope(generatedEvent, "scheduling.SchedulePlanGenerated");
        AssertSchedulingEnvelope(conflictEvent, "scheduling.ScheduleConflictDetected");
        AssertSchedulingEnvelope(releasedEvent, "scheduling.SchedulePlanReleased");
    }

    [Fact]
    public void Http_context_accessor_reads_correlation_causation_and_actor_headers()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["X-Correlation-Id"] = "corr-http-001";
        httpContext.Request.Headers["X-Causation-Id"] = "cmd-http-001";
        httpContext.Request.Headers["X-Actor"] = "user:planner-001";
        var accessor = new HttpSchedulingIntegrationEventContextAccessor(new HttpContextAccessor
        {
            HttpContext = httpContext
        });

        var context = accessor.GetContext();

        Assert.Equal("corr-http-001", context.CorrelationId);
        Assert.Equal("cmd-http-001", context.CausationId);
        Assert.Equal("user:planner-001", context.Actor);
    }

    [Fact]
    public void Http_context_accessor_uses_activity_correlation_tag_before_generating_fallback()
    {
        using var activity = new Activity("scheduling-test").Start();
        activity.SetTag("correlationId", "corr-activity-001");
        var accessor = new HttpSchedulingIntegrationEventContextAccessor(new HttpContextAccessor());

        var context = accessor.GetContext();

        Assert.Equal("corr-activity-001", context.CorrelationId);
        Assert.NotEmpty(context.CausationId);
        Assert.Equal("system:business-scheduling", context.Actor);
    }

    [Fact]
    public void Schedule_plan_event_rejects_unknown_lifecycle_status()
    {
        var plan = CreatePlan();
        typeof(SchedulePlan)
            .GetProperty(nameof(SchedulePlan.Status), BindingFlags.Instance | BindingFlags.Public)!
            .SetValue(plan, (SchedulePlanLifecycleStatus)99);
        var converter = new SchedulePlanGeneratedIntegrationEventConverter(
            new FixedTimeProvider(FixedNow),
            new StubSchedulingIntegrationEventContextAccessor());

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            converter.Convert(new SchedulePlanGeneratedDomainEvent(plan)));
    }

    private static SchedulePlan CreatePlan()
    {
        return SchedulePlan.FromGeneratedPlan(
            "org-001",
            "env-dev",
            SchedulePlanContractMapper.ToDomainSnapshot(new SchedulePlanContract(
                ContractVersion: 1,
                PlanId: "plan-001",
                ProblemId: "problem-001",
                ProblemFingerprint: "fingerprint-001",
                AlgorithmVersion: "aps-lite-v1",
                Status: SchedulePlanStatusContract.Generated,
                GeneratedAtUtc: new DateTimeOffset(2026, 6, 1, 8, 0, 0, TimeSpan.Zero),
                Metrics: new SchedulePlanMetricsContract(
                    ScheduledOperationCount: 1,
                    UnscheduledOperationCount: 0,
                    AssignedMinutes: 60,
                    MakespanMinutes: 60,
                    TotalTardinessMinutes: 0,
                    LateOperationCount: 0,
                    OnTimeRate: 1m,
                    AverageResourceUtilization: 0m),
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
                        ExplanationCode: "scheduled",
                        StandardOperationCode: "STD-ASSY")
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
                GanttItems: [])));
    }

    private static void AssertSchedulingEnvelope(
        IIntegrationEventEnvelope integrationEvent,
        string expectedEventType)
    {
        Assert.Equal(expectedEventType, integrationEvent.EventType);
        Assert.Equal(1, integrationEvent.EventVersion);
        Assert.Equal(FixedNow, integrationEvent.OccurredAtUtc);
        Assert.Equal("business-scheduling", integrationEvent.SourceService);
        Assert.Equal("corr-envelope-001", integrationEvent.CorrelationId);
        Assert.Equal("cause-envelope-001", integrationEvent.CausationId);
        Assert.Equal("user:planner-001", integrationEvent.Actor);
    }

    private sealed class StubSchedulingIntegrationEventContextAccessor(
        SchedulingIntegrationEventContext? context = null)
        : ISchedulingIntegrationEventContextAccessor
    {
        public SchedulingIntegrationEventContext GetContext()
        {
            return context ?? new SchedulingIntegrationEventContext(
                "corr-test-001",
                "cause-test-001",
                "system:business-scheduling");
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
