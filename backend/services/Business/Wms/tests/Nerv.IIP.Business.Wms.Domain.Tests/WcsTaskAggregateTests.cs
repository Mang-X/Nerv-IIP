using Nerv.IIP.Business.Wms.Domain.AggregatesModel.WcsTaskAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.WarehouseTaskAggregate;
using Nerv.IIP.Business.Wms.Domain.DomainEvents;

namespace Nerv.IIP.Business.Wms.Domain.Tests;

public sealed class WcsTaskAggregateTests
{
    [Fact]
    public void Dispatch_is_idempotent_by_warehouse_task_and_adapter()
    {
        var warehouseTaskId = new WarehouseTaskId(Guid.CreateVersion7());
        var first = WcsTask.Dispatch("org-001", "env-dev", warehouseTaskId, "stacker-crane", "EXT-001", "{}");
        var second = WcsTask.Dispatch("org-001", "env-dev", warehouseTaskId, "stacker-crane", "EXT-002", "{}");

        Assert.True(first.IsSameDispatch(second));
        Assert.Equal("org-001", first.OrganizationId);
        Assert.Equal("env-dev", first.EnvironmentId);
        Assert.Equal("stacker-crane", first.AdapterType);
        Assert.Contains(first.GetDomainEvents(), x => x is WcsTaskDispatchedDomainEvent);
    }

    [Fact]
    public void Completed_task_cannot_later_fail()
    {
        var task = WcsTask.Dispatch("org-001", "env-dev", new WarehouseTaskId(Guid.CreateVersion7()), "agv", "EXT-001", "{}");
        task.Complete("done");

        var exception = Assert.Throws<InvalidOperationException>(() => task.Fail("E001", "blocked"));

        Assert.Equal(WcsTaskStatus.Completed, task.Status);
        Assert.Contains("completed", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Failed_task_stores_diagnostics_and_retry_keeps_warehouse_task_reference()
    {
        var warehouseTaskId = new WarehouseTaskId(Guid.CreateVersion7());
        var task = WcsTask.Dispatch("org-001", "env-dev", warehouseTaskId, "agv", "EXT-001", "{}");

        task.Fail("E001", "blocked aisle");
        task.Retry("EXT-002", "{\"retry\":true}", task.NextRetryAtUtc!.Value);

        Assert.Equal(WcsTaskStatus.Dispatched, task.Status);
        Assert.Equal(2, task.AttemptCount);
        Assert.Equal(warehouseTaskId, task.WarehouseTaskId);
        Assert.Equal("E001", task.FailureCode);
        Assert.Equal("blocked aisle", task.FailureMessage);
        Assert.Contains(task.GetDomainEvents(), x => x is WcsTaskFailedDomainEvent);
    }

    [Fact]
    public void Retry_is_rejected_until_exponential_backoff_has_elapsed()
    {
        var failedAtUtc = new DateTime(2026, 7, 10, 0, 0, 0, DateTimeKind.Utc);
        var task = WcsTask.Dispatch("org-001", "env-dev", new WarehouseTaskId(Guid.CreateVersion7()), "agv", "EXT-001", "{}", deviceId: "AGV-01");

        task.Fail("E001", "blocked aisle", failedAtUtc);

        Assert.Equal(failedAtUtc.AddMinutes(1), task.NextRetryAtUtc);
        var exception = Assert.Throws<InvalidOperationException>(() => task.Retry("EXT-002", "{\"retry\":true}", failedAtUtc.AddSeconds(59)));
        Assert.Contains("retry", exception.Message, StringComparison.OrdinalIgnoreCase);

        task.Retry("EXT-002", "{\"retry\":true}", failedAtUtc.AddMinutes(1));

        Assert.Equal(WcsTaskStatus.Dispatched, task.Status);
        Assert.Equal(2, task.AttemptCount);
        Assert.Null(task.NextRetryAtUtc);
    }

    [Fact]
    public void Circuit_opens_after_threshold_failures_and_manual_reset_closes_it()
    {
        var openedAtUtc = new DateTime(2026, 7, 10, 0, 0, 0, DateTimeKind.Utc);
        var circuit = WcsDispatchCircuit.Create("org-001", "env-dev", "agv", "AGV-01");

        circuit.RecordFailure(openedAtUtc, failureThreshold: 3);
        circuit.RecordFailure(openedAtUtc.AddMinutes(1), failureThreshold: 3);
        circuit.RecordFailure(openedAtUtc.AddMinutes(2), failureThreshold: 3);

        Assert.True(circuit.IsOpen);
        Assert.Equal(openedAtUtc.AddMinutes(2), circuit.OpenedAtUtc);
        Assert.Contains("open", circuit.RejectionReason!, StringComparison.OrdinalIgnoreCase);

        circuit.Reset(openedAtUtc.AddMinutes(3));

        Assert.False(circuit.IsOpen);
        Assert.Equal(0, circuit.ConsecutiveFailureCount);
        Assert.Null(circuit.OpenedAtUtc);
    }

    [Fact]
    public void Cancelled_task_raises_domain_event_for_adapter_compensation()
    {
        var task = WcsTask.Dispatch("org-001", "env-dev", new WarehouseTaskId(Guid.CreateVersion7()), "agv", "EXT-001", "{}");

        task.Cancel();

        Assert.Equal(WcsTaskStatus.Cancelled, task.Status);
        Assert.Contains(task.GetDomainEvents(), x => x is WcsTaskCancelledDomainEvent);
    }
}
