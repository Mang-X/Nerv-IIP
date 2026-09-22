using System.Text.Json;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.OperationTaskAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.ScheduleAggregate;
using Nerv.IIP.Business.Mes.Domain.DomainEvents;
using Nerv.IIP.Business.Mes.Web.Application.IntegrationEventConverters;
using Nerv.IIP.Contracts.Mes;

namespace Nerv.IIP.Business.Mes.Web.Tests;

public sealed class MesExecutionFactIntegrationEventTests
{
    private static readonly IMesIntegrationEventContextAccessor Context =
        new StubContextAccessor("corr-3720", "cause-3720");

    [Fact]
    public void Operation_lifecycle_converters_preserve_exact_operation_and_transition_time()
    {
        var startedAtUtc = DateTimeOffset.Parse("2026-09-22T01:00:00Z");
        var pausedAtUtc = startedAtUtc.AddMinutes(20);
        var resumedAtUtc = pausedAtUtc.AddMinutes(10);
        var task = OperationTask.Queue(
            "org-001", "env-dev", "WO-001", "OP-10", 10, "WC-01", [],
            startedAtUtc, TimeSpan.FromHours(1), "SKU-001");

        var started = new OperationTaskStartedIntegrationEventConverter(Context).Convert(
            new OperationTaskStartedDomainEvent(task, startedAtUtc));
        var paused = new OperationTaskPausedIntegrationEventConverter(Context).Convert(
            new OperationTaskPausedDomainEvent(task, pausedAtUtc));
        var resumed = new OperationTaskResumedIntegrationEventConverter(Context).Convert(
            new OperationTaskResumedDomainEvent(task, resumedAtUtc));

        Assert.Equal(MesIntegrationEventTypes.OperationTaskStarted, started.EventType);
        Assert.Equal(MesIntegrationEventTypes.OperationTaskPaused, paused.EventType);
        Assert.Equal(MesIntegrationEventTypes.OperationTaskResumed, resumed.EventType);
        Assert.All(
            new[] { started.Payload, paused.Payload, resumed.Payload },
            payload =>
            {
                Assert.Equal("WO-001", payload.WorkOrderId);
                Assert.Equal("OP-10", payload.OperationTaskId);
                Assert.Equal(10, payload.OperationSequence);
                Assert.Equal("WC-01", payload.WorkCenterId);
            });
        Assert.Equal(startedAtUtc, started.Payload.ChangedAtUtc);
        Assert.Equal(pausedAtUtc, paused.Payload.ChangedAtUtc);
        Assert.Equal(resumedAtUtc, resumed.Payload.ChangedAtUtc);
        Assert.All(new[] { started.CorrelationId, paused.CorrelationId, resumed.CorrelationId },
            value => Assert.Equal("corr-3720", value));
        Assert.All(new[] { started.CausationId, paused.CausationId, resumed.CausationId },
            value => Assert.Equal("cause-3720", value));
        Assert.Equal(
            started.IdempotencyKey,
            new OperationTaskStartedIntegrationEventConverter(Context).Convert(
                new OperationTaskStartedDomainEvent(task, startedAtUtc)).IdempotencyKey);
    }

    [Fact]
    public void Downtime_converters_preserve_window_reason_and_available_execution_references()
    {
        var startedAtUtc = DateTimeOffset.Parse("2026-09-22T02:00:00Z");
        var recoveredAtUtc = startedAtUtc.AddMinutes(45);
        var downtime = WorkCenterUnavailability.Open(
            "org-001", "env-dev", "DT-001", "WC-01", startedAtUtc, null,
            "equipment-fault", "DEVICE-01", "WO-001", "OP-10");

        var started = new DowntimeStartedIntegrationEventConverter(Context).Convert(
            new DowntimeStartedDomainEvent(downtime));
        downtime.ClearDomainEvents();
        downtime.Close(recoveredAtUtc);
        var restored = new DowntimeRestoredIntegrationEventConverter(Context).Convert(
            new DowntimeRestoredDomainEvent(downtime, recoveredAtUtc));

        Assert.Equal(MesIntegrationEventTypes.DowntimeStarted, started.EventType);
        Assert.Equal(MesIntegrationEventTypes.DowntimeRestored, restored.EventType);
        Assert.Equal("DT-001", started.Payload.DowntimeEventNo);
        Assert.Equal("WO-001", started.Payload.WorkOrderId);
        Assert.Equal("OP-10", started.Payload.OperationTaskId);
        Assert.Equal("WC-01", started.Payload.WorkCenterId);
        Assert.Equal("DEVICE-01", started.Payload.DeviceAssetId);
        Assert.Equal("equipment-fault", started.Payload.Reason);
        Assert.Equal(startedAtUtc, started.Payload.StartedAtUtc);
        Assert.Null(started.Payload.EndedAtUtc);
        Assert.Equal("DT-001", restored.Payload.DowntimeEventNo);
        Assert.Equal("WO-001", restored.Payload.WorkOrderId);
        Assert.Equal("OP-10", restored.Payload.OperationTaskId);
        Assert.Equal(startedAtUtc, restored.Payload.StartedAtUtc);
        Assert.Equal(recoveredAtUtc, restored.Payload.RestoredAtUtc);
        Assert.Equal("corr-3720", started.CorrelationId);
        Assert.Equal("cause-3720", started.CausationId);
        Assert.Equal("corr-3720", restored.CorrelationId);
        Assert.Equal("cause-3720", restored.CausationId);
        Assert.Equal(
            restored.IdempotencyKey,
            new DowntimeRestoredIntegrationEventConverter(Context).Convert(
                new DowntimeRestoredDomainEvent(downtime, recoveredAtUtc)).IdempotencyKey);
    }

    [Fact]
    public void Execution_fact_topics_are_canonical_and_json_accepts_unknown_fields()
    {
        Assert.Equal(
            "nerv-iip.development.business-mes.mes.operation-task-started.v1",
            MesExecutionFactIntegrationEventTopics.OperationTaskStarted("Development"));
        Assert.Equal(
            "nerv-iip.development.business-mes.mes.operation-task-paused.v1",
            MesExecutionFactIntegrationEventTopics.OperationTaskPaused("Development"));
        Assert.Equal(
            "nerv-iip.development.business-mes.mes.operation-task-resumed.v1",
            MesExecutionFactIntegrationEventTopics.OperationTaskResumed("Development"));
        Assert.Equal(
            "nerv-iip.development.business-mes.mes.downtime-started.v1",
            MesExecutionFactIntegrationEventTopics.DowntimeStarted("Development"));
        Assert.Equal(
            "nerv-iip.development.business-mes.mes.downtime-restored.v1",
            MesExecutionFactIntegrationEventTopics.DowntimeRestored("Development"));

        var occurredAtUtc = DateTimeOffset.Parse("2026-09-22T01:00:00Z");
        var lifecycle = new OperationTaskLifecyclePayload("WO-001", "OP-10", 10, "WC-01", occurredAtUtc);
        (object Value, string PayloadField)[] contracts =
        [
            (new MesOperationTaskStartedIntegrationEvent(
                "evt-1", MesIntegrationEventTypes.OperationTaskStarted, 1, occurredAtUtc,
                MesIntegrationEventSources.BusinessMes, "corr-1", "cause-1", "org-001", "env-dev",
                "system:mes", "idem-1", lifecycle), "changedAtUtc"),
            (new MesOperationTaskPausedIntegrationEvent(
                "evt-2", MesIntegrationEventTypes.OperationTaskPaused, 1, occurredAtUtc,
                MesIntegrationEventSources.BusinessMes, "corr-1", "cause-1", "org-001", "env-dev",
                "system:mes", "idem-2", lifecycle), "changedAtUtc"),
            (new MesOperationTaskResumedIntegrationEvent(
                "evt-3", MesIntegrationEventTypes.OperationTaskResumed, 1, occurredAtUtc,
                MesIntegrationEventSources.BusinessMes, "corr-1", "cause-1", "org-001", "env-dev",
                "system:mes", "idem-3", lifecycle), "changedAtUtc"),
            (new MesDowntimeStartedIntegrationEvent(
                "evt-4", MesIntegrationEventTypes.DowntimeStarted, 1, occurredAtUtc,
                MesIntegrationEventSources.BusinessMes, "corr-1", "cause-1", "org-001", "env-dev",
                "system:mes", "idem-4",
                new DowntimeStartedPayload("DT-1", "WO-001", "OP-10", "WC-01", "DEVICE-01",
                    "fault", occurredAtUtc, null)), "endedAtUtc"),
            (new MesDowntimeRestoredIntegrationEvent(
                "evt-5", MesIntegrationEventTypes.DowntimeRestored, 1, occurredAtUtc,
                MesIntegrationEventSources.BusinessMes, "corr-1", "cause-1", "org-001", "env-dev",
                "system:mes", "idem-5",
                new DowntimeRestoredPayload("DT-1", "WO-001", "OP-10", "WC-01", "DEVICE-01",
                    "fault", occurredAtUtc.AddMinutes(-10), occurredAtUtc)), "restoredAtUtc"),
        ];

        foreach (var contract in contracts)
        {
            var json = JsonSerializer.Serialize(contract.Value, contract.Value.GetType(), JsonOptions);
            Assert.Contains("\"eventId\":", json, StringComparison.Ordinal);
            Assert.Contains($"\"{contract.PayloadField}\":", json, StringComparison.Ordinal);
            var withUnknownFields = json.Replace(
                "\"payload\":{", "\"unknownEnvelope\":true,\"payload\":{\"unknownPayload\":true,",
                StringComparison.Ordinal);
            var roundTrip = JsonSerializer.Deserialize(withUnknownFields, contract.Value.GetType(), JsonOptions);
            Assert.Equal(contract.Value, roundTrip);
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private sealed class StubContextAccessor(string correlationId, string causationId)
        : IMesIntegrationEventContextAccessor
    {
        public MesIntegrationEventContext GetContext() => new(correlationId, causationId);
    }
}
