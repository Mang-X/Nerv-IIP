using Nerv.IIP.Business.Mes.Domain.AggregatesModel.OperationTaskAggregate;
using Nerv.IIP.Business.Mes.Domain.DomainEvents;

namespace Nerv.IIP.Business.Mes.Web.Tests;

public sealed class ManualDispatchSchedulingLockEventTests
{
    [Fact]
    public void Assign_with_real_device_raises_scheduling_lock_event()
    {
        var start = new DateTimeOffset(2026, 7, 14, 8, 0, 0, TimeSpan.Zero);
        var task = OperationTask.Queue("org-1", "env-1", "WO-1", "OP-1", 10,
            "WC-1", [], start, TimeSpan.FromHours(1));

        task.Assign(null, "DEV-1", "SHIFT-1", start.AddMinutes(-5));

        var raised = Assert.IsType<OperationTaskManuallyDispatchedDomainEvent>(task.GetDomainEvents().Single());
        Assert.Equal("DEV-1", raised.OperationTask.DeviceAssetId);
        Assert.Equal(start, raised.OperationTask.EarliestStartUtc);
    }

    [Fact]
    public void Assign_without_device_does_not_raise_scheduling_lock_event()
    {
        var start = new DateTimeOffset(2026, 7, 14, 8, 0, 0, TimeSpan.Zero);
        var task = OperationTask.Queue("org-1", "env-1", "WO-1", "OP-1", 10,
            "WC-1", [], start, TimeSpan.FromHours(1));

        task.Assign("USER-1", null, "SHIFT-1", start.AddMinutes(-5));

        Assert.Empty(task.GetDomainEvents());
    }
}
