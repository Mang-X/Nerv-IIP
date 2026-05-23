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
        var first = WcsTask.Dispatch(warehouseTaskId, "stacker-crane", "EXT-001", "{}");
        var second = WcsTask.Dispatch(warehouseTaskId, "stacker-crane", "EXT-002", "{}");

        Assert.True(first.IsSameDispatch(second));
        Assert.Equal("stacker-crane", first.AdapterType);
        Assert.Contains(first.GetDomainEvents(), x => x is WcsTaskDispatchedDomainEvent);
    }

    [Fact]
    public void Completed_task_cannot_later_fail()
    {
        var task = WcsTask.Dispatch(new WarehouseTaskId(Guid.CreateVersion7()), "agv", "EXT-001", "{}");
        task.Complete("done");

        var exception = Assert.Throws<InvalidOperationException>(() => task.Fail("E001", "blocked"));

        Assert.Equal(WcsTaskStatus.Completed, task.Status);
        Assert.Contains("completed", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Failed_task_stores_diagnostics_and_retry_keeps_warehouse_task_reference()
    {
        var warehouseTaskId = new WarehouseTaskId(Guid.CreateVersion7());
        var task = WcsTask.Dispatch(warehouseTaskId, "agv", "EXT-001", "{}");

        task.Fail("E001", "blocked aisle");
        task.Retry("EXT-002", "{\"retry\":true}");

        Assert.Equal(WcsTaskStatus.Dispatched, task.Status);
        Assert.Equal(2, task.AttemptCount);
        Assert.Equal(warehouseTaskId, task.WarehouseTaskId);
        Assert.Equal("E001", task.FailureCode);
        Assert.Equal("blocked aisle", task.FailureMessage);
        Assert.Contains(task.GetDomainEvents(), x => x is WcsTaskFailedDomainEvent);
    }
}
