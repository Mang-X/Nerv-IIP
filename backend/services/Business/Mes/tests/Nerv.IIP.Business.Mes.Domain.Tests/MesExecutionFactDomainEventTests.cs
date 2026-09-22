using Nerv.IIP.Business.Mes.Domain.AggregatesModel.OperationTaskAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.ScheduleAggregate;
using Nerv.IIP.Business.Mes.Domain.DomainEvents;

namespace Nerv.IIP.Business.Mes.Domain.Tests;

public sealed class MesExecutionFactDomainEventTests
{
    [Fact]
    public void Operation_lifecycle_transitions_raise_one_fact_with_the_transition_time()
    {
        var startedAtUtc = DateTimeOffset.Parse("2026-09-22T01:00:00Z");
        var pausedAtUtc = startedAtUtc.AddMinutes(20);
        var resumedAtUtc = pausedAtUtc.AddMinutes(10);
        var task = OperationTask.Queue(
            "org-001", "env-dev", "WO-001", "OP-10", 10, "WC-01", [],
            startedAtUtc, TimeSpan.FromHours(1), "SKU-001");

        task.Start(startedAtUtc);
        var started = Assert.IsType<OperationTaskStartedDomainEvent>(Assert.Single(task.GetDomainEvents()));
        Assert.Same(task, started.OperationTask);
        Assert.Equal(startedAtUtc, started.StartedAtUtc);

        task.ClearDomainEvents();
        task.Pause(pausedAtUtc);
        var paused = Assert.IsType<OperationTaskPausedDomainEvent>(Assert.Single(task.GetDomainEvents()));
        Assert.Same(task, paused.OperationTask);
        Assert.Equal(pausedAtUtc, paused.PausedAtUtc);

        task.ClearDomainEvents();
        task.Resume(resumedAtUtc);
        var resumed = Assert.IsType<OperationTaskResumedDomainEvent>(Assert.Single(task.GetDomainEvents()));
        Assert.Same(task, resumed.OperationTask);
        Assert.Equal(resumedAtUtc, resumed.ResumedAtUtc);
    }

    [Fact]
    public void Downtime_open_and_recovery_raise_linked_facts_from_the_same_window()
    {
        var startedAtUtc = DateTimeOffset.Parse("2026-09-22T02:00:00Z");
        var recoveredAtUtc = startedAtUtc.AddMinutes(45);

        var downtime = WorkCenterUnavailability.Open(
            "org-001",
            "env-dev",
            "DT-001",
            "WC-01",
            startedAtUtc,
            null,
            "equipment-fault",
            "DEVICE-01",
            "WO-001",
            "OP-10");

        var started = Assert.IsType<DowntimeStartedDomainEvent>(Assert.Single(downtime.GetDomainEvents()));
        Assert.Same(downtime, started.Downtime);
        Assert.Equal("WO-001", downtime.WorkOrderId);
        Assert.Equal("OP-10", downtime.OperationTaskId);

        downtime.ClearDomainEvents();
        downtime.Close(recoveredAtUtc);
        var restored = Assert.IsType<DowntimeRestoredDomainEvent>(Assert.Single(downtime.GetDomainEvents()));
        Assert.Same(downtime, restored.Downtime);
        Assert.Equal(recoveredAtUtc, restored.RestoredAtUtc);
    }
}
