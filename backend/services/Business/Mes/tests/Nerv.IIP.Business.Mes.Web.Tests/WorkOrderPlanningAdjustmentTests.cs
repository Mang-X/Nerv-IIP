using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Workbench;
using Nerv.IIP.Business.Mes.Web.Application.Commands.WorkOrders;
using Nerv.IIP.Business.Mes.Web.Application.Errors;

namespace Nerv.IIP.Business.Mes.Web.Tests;

public sealed class WorkOrderPlanningAdjustmentTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-29T08:00:00Z");

    [Theory]
    [InlineData(WorkOrder.CreatedStatus)]
    [InlineData(WorkOrder.ReleasedStatus)]
    [InlineData(WorkOrder.StartedStatus)]
    [InlineData(WorkOrder.HoldStatus)]
    public async Task Adjust_due_time_persists_and_replay_keeps_version(string status)
    {
        await using var provider = MesTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var workOrder = CreateWorkOrder(status);
        db.WorkOrders.Add(workOrder);
        await db.SaveChangesAsync();

        var command = new AdjustWorkOrderDueUtcCommand("org-001", "env-dev", workOrder.WorkOrderId, Now.AddDays(3), Now);
        var handler = new AdjustWorkOrderDueUtcCommandHandler(db);
        var initialVersion = workOrder.Version;
        await handler.Handle(command, CancellationToken.None);
        Assert.Equal(initialVersion + 1, workOrder.Version);
        var version = workOrder.Version;
        await handler.Handle(command, CancellationToken.None);
        await db.SaveChangesAsync();

        var persisted = await db.WorkOrders.AsNoTracking().SingleAsync();
        Assert.Equal(Now.AddDays(3), persisted.DueUtc);
        Assert.Equal(version, persisted.Version);
        Assert.Equal(status, persisted.Status);
    }

    [Theory]
    [InlineData(WorkOrder.CompletedStatus)]
    [InlineData(WorkOrder.ClosedStatus)]
    [InlineData(WorkOrder.CancelledStatus)]
    [InlineData(WorkOrder.SplitStatus)]
    [InlineData(WorkOrder.MergedStatus)]
    public async Task Terminal_work_order_rejects_due_time_adjustment(string status)
    {
        await using var provider = MesTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var workOrder = CreateWorkOrder();
        if (status == WorkOrder.CompletedStatus)
        {
            workOrder.MarkReleased();
            workOrder.Start(Now);
            workOrder.RecordProductionProgress(10m, 0m, Now);
        }
        else if (status == WorkOrder.ClosedStatus)
        {
            workOrder.MarkReleased();
            workOrder.Start(Now);
            workOrder.RecordProductionProgress(10m, 0m, Now);
            workOrder.Close(Now);
        }
        else if (status == WorkOrder.CancelledStatus)
        {
            workOrder.Cancel("计划取消", Now);
        }
        else if (status == WorkOrder.SplitStatus)
        {
            workOrder.MarkSplit();
        }
        else
        {
            workOrder.MarkMerged();
        }
        db.WorkOrders.Add(workOrder);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<MesLifecycleConflictException>(() =>
            new AdjustWorkOrderDueUtcCommandHandler(db).Handle(
                new AdjustWorkOrderDueUtcCommand("org-001", "env-dev", workOrder.WorkOrderId, Now.AddDays(3), Now),
                CancellationToken.None));
        Assert.Equal(Now.AddDays(1), workOrder.DueUtc);
    }

    [Fact]
    public async Task Cancel_command_persists_status_and_replay_keeps_original_reason()
    {
        await using var provider = MesTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var workOrder = CreateWorkOrder();
        db.WorkOrders.Add(workOrder);
        await db.SaveChangesAsync();

        var handler = new CancelWorkOrderCommandHandler(db);
        await handler.Handle(new CancelWorkOrderCommand("org-001", "env-dev", workOrder.WorkOrderId, "计划取消", Now), CancellationToken.None);
        await handler.Handle(new CancelWorkOrderCommand("org-001", "env-dev", workOrder.WorkOrderId, "重复请求", Now.AddMinutes(1)), CancellationToken.None);
        await db.SaveChangesAsync();

        var persisted = await db.WorkOrders.AsNoTracking().SingleAsync();
        Assert.Equal(WorkOrder.CancelledStatus, persisted.Status);
        Assert.Equal("计划取消", persisted.CancelReason);
    }

    private static WorkOrder CreateWorkOrder(string status = WorkOrder.CreatedStatus)
    {
        var workOrder = WorkOrder.Create("org-001", "env-dev", "WO-PLAN", "SKU-001", "PV-001", 10m, 1, Now.AddDays(1));
        if (status == WorkOrder.ReleasedStatus)
        {
            workOrder.MarkReleased();
        }
        else if (status == WorkOrder.StartedStatus)
        {
            workOrder.MarkReleased();
            workOrder.Start(Now);
        }
        else if (status == WorkOrder.HoldStatus)
        {
            workOrder.Hold("待料");
        }
        return workOrder;
    }
}
