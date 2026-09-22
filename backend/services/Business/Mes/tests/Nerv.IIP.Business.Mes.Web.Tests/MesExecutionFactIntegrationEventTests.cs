using Nerv.IIP.Business.Mes.Domain.AggregatesModel.OperationTaskAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.ScheduleAggregate;
using Nerv.IIP.Business.Mes.Domain.DomainEvents;
using Nerv.IIP.Business.Mes.Web.Application.IntegrationEventConverters;
using Nerv.IIP.Contracts.Mes;

namespace Nerv.IIP.Business.Mes.Web.Tests;

public sealed class MesExecutionFactIntegrationEventTests
{
    [Fact]
    public void Operation_lifecycle_converters_preserve_exact_operation_and_transition_time()
    {
        var startedAtUtc = DateTimeOffset.Parse("2026-09-22T01:00:00Z");
        var pausedAtUtc = startedAtUtc.AddMinutes(20);
        var resumedAtUtc = pausedAtUtc.AddMinutes(10);
        var task = OperationTask.Queue(
            "org-001", "env-dev", "WO-001", "OP-10", 10, "WC-01", [],
            startedAtUtc, TimeSpan.FromHours(1), "SKU-001");

        var started = new OperationTaskStartedIntegrationEventConverter().Convert(
            new OperationTaskStartedDomainEvent(task, startedAtUtc));
        var paused = new OperationTaskPausedIntegrationEventConverter().Convert(
            new OperationTaskPausedDomainEvent(task, pausedAtUtc));
        var resumed = new OperationTaskResumedIntegrationEventConverter().Convert(
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
        Assert.Equal(
            started.IdempotencyKey,
            new OperationTaskStartedIntegrationEventConverter().Convert(
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

        var started = new DowntimeStartedIntegrationEventConverter().Convert(
            new DowntimeStartedDomainEvent(downtime));
        downtime.ClearDomainEvents();
        downtime.Close(recoveredAtUtc);
        var restored = new DowntimeRestoredIntegrationEventConverter().Convert(
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
        Assert.Null(started.Payload.PlannedEndUtc);
        Assert.Equal("DT-001", restored.Payload.DowntimeEventNo);
        Assert.Equal("WO-001", restored.Payload.WorkOrderId);
        Assert.Equal("OP-10", restored.Payload.OperationTaskId);
        Assert.Equal(startedAtUtc, restored.Payload.StartedAtUtc);
        Assert.Equal(recoveredAtUtc, restored.Payload.RestoredAtUtc);
        Assert.Equal(
            restored.IdempotencyKey,
            new DowntimeRestoredIntegrationEventConverter().Convert(
                new DowntimeRestoredDomainEvent(downtime, recoveredAtUtc)).IdempotencyKey);
    }
}
