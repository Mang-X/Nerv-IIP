using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.OperationTaskAggregate;
using Nerv.IIP.Business.Mes.Domain.DomainEvents;
using Nerv.IIP.Business.Mes.Web.Application.IntegrationEventConverters;
using Nerv.IIP.Contracts.Mes;
using NetCorePal.Extensions.Domain;

namespace Nerv.IIP.Business.Mes.Web.Tests;

public sealed class ManualDispatchSchedulingLockEventTests
{
    [Fact]
    public void Assign_with_real_device_raises_immutable_versioned_scheduling_lock_event()
    {
        var task = NewTask();

        task.Assign(null, "DEV-1", "SHIFT-1", At(1), "user:planner-1");
        var raised = Assert.IsType<OperationTaskManuallyDispatchedDomainEvent>(Assert.Single(task.GetDomainEvents()));
        task.Assign(null, "DEV-2", "SHIFT-2", At(2), "user:planner-2");

        Assert.Equal("DEV-1", raised.Dispatch.ResourceId);
        Assert.Equal(At(0), raised.Dispatch.StartUtc);
        Assert.Equal(1, raised.Dispatch.DispatchRevision);
        Assert.Equal("user:planner-1", raised.Actor);
        Assert.Equal(2, task.ManualDispatchRevision);
        Assert.True(task.HasActiveManualDispatch);
    }

    [Fact]
    public void Clearing_a_real_manual_device_raises_one_versioned_clear_event()
    {
        var task = NewTask();
        task.Assign(null, "DEV-1", null, At(1), "user:planner");
        task.ClearDomainEvents();

        task.Assign(null, null, null, At(2), "user:planner");

        var cleared = Assert.IsType<OperationTaskManualDispatchClearedDomainEvent>(
            Assert.Single(task.GetDomainEvents()));
        Assert.Equal(2, cleared.Dispatch.DispatchRevision);
        Assert.Equal("DEV-1", cleared.Dispatch.ResourceId);
        Assert.Equal("device-cleared", cleared.ReasonCode);
        Assert.Equal(At(2), cleared.ClearedAtUtc);
        Assert.Equal("user:planner", cleared.Actor);
        Assert.False(task.HasActiveManualDispatch);
    }

    [Fact]
    public void Equal_time_clear_and_reassign_have_distinct_monotonic_revisions()
    {
        var task = NewTask();
        task.Assign(null, "DEV-1", null, At(1), "user:planner");
        task.Assign(null, null, null, At(1), "user:planner");
        task.Assign(null, "DEV-2", null, At(1), "user:planner");

        Assert.Equal(3, task.ManualDispatchRevision);
        Assert.True(task.HasActiveManualDispatch);
        Assert.Equal([1L, 2L, 3L], task.GetDomainEvents().Select(GetRevision));
    }

    [Fact]
    public void Repeated_null_assignment_does_not_raise_manual_dispatch_event()
    {
        var task = NewTask();

        task.Assign("USER-1", null, "SHIFT-1", At(1), "user:planner");
        task.Assign(null, null, null, At(2), "user:planner");

        Assert.Empty(task.GetDomainEvents());
        Assert.Equal(0, task.ManualDispatchRevision);
        Assert.False(task.HasActiveManualDispatch);
    }

    [Fact]
    public void Cancelling_an_active_manual_dispatch_raises_operation_cancelled_event()
    {
        var task = NewTask();
        task.Assign(null, "DEV-1", null, At(1), "user:planner");
        task.ClearDomainEvents();

        task.Cancel(At(2), "user:planner");

        var cleared = Assert.IsType<OperationTaskManualDispatchClearedDomainEvent>(
            Assert.Single(task.GetDomainEvents()));
        Assert.Equal("operation-cancelled", cleared.ReasonCode);
        Assert.Equal("DEV-1", cleared.Dispatch.ResourceId);
        Assert.Equal(2, cleared.Dispatch.DispatchRevision);
        Assert.False(task.HasActiveManualDispatch);
    }

    [Fact]
    public void Released_schedule_assignment_does_not_become_a_manual_dispatch_fact()
    {
        var task = NewTask();

        task.ApplyScheduleAssignment("WC-2", "DEV-SCHEDULED", At(3), At(4), At(2));

        Assert.Empty(task.GetDomainEvents());
        Assert.Equal(0, task.ManualDispatchRevision);
        Assert.False(task.HasActiveManualDispatch);
    }

    [Fact]
    public void Released_schedule_assignment_preserves_an_active_manual_dispatch_lifecycle()
    {
        var task = NewTask();
        task.Assign(null, "DEV-MANUAL", null, At(1), "user:planner");
        task.ClearDomainEvents();

        task.ApplyScheduleAssignment("WC-2", "DEV-SCHEDULED", At(3), At(4), At(2));

        Assert.Empty(task.GetDomainEvents());
        Assert.Equal(1, task.ManualDispatchRevision);
        Assert.True(task.HasActiveManualDispatch);
    }

    [Fact]
    public void Clear_after_schedule_release_uses_the_original_active_manual_dispatch_snapshot()
    {
        var task = NewTask();
        task.Assign(null, "DEV-MANUAL", null, At(1), "user:planner");
        task.ClearDomainEvents();
        task.ApplyScheduleAssignment("WC-SCHEDULED", "DEV-SCHEDULED", At(3), At(4), At(2));

        task.Assign(null, null, null, At(5), "user:planner");

        var cleared = Assert.IsType<OperationTaskManualDispatchClearedDomainEvent>(
            Assert.Single(task.GetDomainEvents()));
        Assert.Equal("DEV-MANUAL", cleared.Dispatch.ResourceId);
        Assert.Equal("WC-1", cleared.Dispatch.WorkCenterId);
        Assert.Equal(At(0), cleared.Dispatch.StartUtc);
        Assert.Equal(At(1), cleared.Dispatch.EndUtc);
        Assert.Equal(At(1), cleared.Dispatch.OccurredAtUtc);
        Assert.Equal(2, cleared.Dispatch.DispatchRevision);
    }

    [Fact]
    public void Manual_dispatch_event_uses_snapshot_actor_and_revision_in_contract()
    {
        var task = NewTask();
        task.Assign("USER-1", "DEV-1", "SHIFT-1", At(1), "user:planner-1");
        var domainEvent = Assert.IsType<OperationTaskManuallyDispatchedDomainEvent>(Assert.Single(task.GetDomainEvents()));

        var integrationEvent = new OperationTaskManuallyDispatchedIntegrationEventConverter().Convert(domainEvent);

        Assert.Equal("user:planner-1", integrationEvent.Actor);
        Assert.Equal("DEV-1", integrationEvent.Payload.ResourceId);
        Assert.Equal(1, integrationEvent.Payload.DispatchRevision);
    }

    [Fact]
    public void Cleared_converter_uses_request_context_and_revision_based_idempotency()
    {
        var task = NewTask();
        task.Assign(null, "DEV-1", null, At(1), "user:planner");
        task.ClearDomainEvents();
        task.Assign(null, null, null, At(2), "user:planner");
        var domainEvent = Assert.IsType<OperationTaskManualDispatchClearedDomainEvent>(Assert.Single(task.GetDomainEvents()));
        var converter = new OperationTaskManualDispatchClearedIntegrationEventConverter(
            new StubMesIntegrationEventContextAccessor(new MesIntegrationEventContext("corr-1", "cause-1")));

        var integrationEvent = converter.Convert(domainEvent);

        Assert.Equal(MesIntegrationEventTypes.OperationTaskManualDispatchCleared, integrationEvent.EventType);
        Assert.Equal("corr-1", integrationEvent.CorrelationId);
        Assert.Equal("cause-1", integrationEvent.CausationId);
        Assert.Equal("user:planner", integrationEvent.Actor);
        Assert.Equal("device-cleared", integrationEvent.Payload.ReasonCode);
        Assert.Equal(2, integrationEvent.Payload.DispatchRevision);
        Assert.Contains("operation-task-manual-dispatch-cleared", integrationEvent.IdempotencyKey, StringComparison.Ordinal);
    }

    [Fact]
    public void Http_context_accessor_prefers_correlation_and_causation_headers()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["X-Correlation-Id"] = " corr-http ";
        httpContext.Request.Headers["X-Causation-Id"] = " cause-http ";
        var accessor = new HttpMesIntegrationEventContextAccessor(new HttpContextAccessor
        {
            HttpContext = httpContext,
        });

        var context = accessor.GetContext();

        Assert.Equal("corr-http", context.CorrelationId);
        Assert.Equal("cause-http", context.CausationId);
    }

    [Fact]
    public void Positive_v1_payload_without_revision_still_deserializes()
    {
        const string legacyJson = """
            {
              "workOrderId": "WO-1",
              "operationTaskId": "OP-1",
              "operationSequence": 10,
              "resourceId": "DEV-1",
              "workCenterId": "WC-1",
              "startUtc": "2026-07-14T08:00:00Z",
              "endUtc": "2026-07-14T09:00:00Z",
              "assignedAtUtc": "2026-07-14T07:55:00Z"
            }
            """;

        var payload = JsonSerializer.Deserialize<OperationTaskManuallyDispatchedPayload>(legacyJson,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(payload);
        Assert.Equal(0, payload.DispatchRevision);
    }

    private static OperationTask NewTask() =>
        OperationTask.Queue("org-1", "env-1", "WO-1", "OP-1", 10,
            "WC-1", [], At(0), TimeSpan.FromHours(1));

    private static DateTimeOffset At(int hour) =>
        new(2026, 7, 14, 8 + hour, 0, 0, TimeSpan.Zero);

    private static long GetRevision(IDomainEvent domainEvent) => domainEvent switch
    {
        OperationTaskManuallyDispatchedDomainEvent dispatched => dispatched.Dispatch.DispatchRevision,
        OperationTaskManualDispatchClearedDomainEvent cleared => cleared.Dispatch.DispatchRevision,
        _ => throw new InvalidOperationException($"Unexpected event {domainEvent.GetType().Name}."),
    };

    private sealed class StubMesIntegrationEventContextAccessor(MesIntegrationEventContext context)
        : IMesIntegrationEventContextAccessor
    {
        public MesIntegrationEventContext GetContext() => context;
    }
}
