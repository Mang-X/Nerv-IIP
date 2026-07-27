using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NetCorePal.Extensions.Primitives;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.OperationTaskAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Workbench;
using Nerv.IIP.Business.Mes.Web.Application.Errors;

namespace Nerv.IIP.Business.Mes.Web.Tests;

public sealed class MesLifecycleConflictTests
{
    [Theory]
    [InlineData("release", WorkOrder.ReleasedStatus)]
    [InlineData("release", WorkOrder.CompletedStatus)]
    [InlineData("release", WorkOrder.ClosedStatus)]
    [InlineData("release", WorkOrder.CancelledStatus)]
    [InlineData("release", WorkOrder.ScrappedStatus)]
    [InlineData("hold", WorkOrder.CompletedStatus)]
    [InlineData("hold", WorkOrder.ClosedStatus)]
    [InlineData("hold", WorkOrder.CancelledStatus)]
    [InlineData("hold", WorkOrder.ScrappedStatus)]
    [InlineData("cancel", WorkOrder.CompletedStatus)]
    [InlineData("cancel", WorkOrder.ClosedStatus)]
    [InlineData("cancel", WorkOrder.ScrappedStatus)]
    public async Task Work_order_action_rejects_incompatible_persisted_status(
        string action,
        string status)
    {
        await using var provider = MesTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Infrastructure.ApplicationDbContext>();
        var workOrder = WorkOrder.Create(
            "org-001",
            "env-dev",
            $"WO-{action}-{status}",
            "SKU-FG",
            "PV-001",
            10m,
            1,
            Utc("2026-07-28T08:00:00Z"),
            "PCS");
        SetStatus(workOrder, status);
        dbContext.WorkOrders.Add(workOrder);
        await dbContext.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<MesLifecycleConflictException>(async () =>
        {
            switch (action)
            {
                case "release":
                    await new ReleaseWorkOrderCommandHandler(dbContext).Handle(
                        new ReleaseWorkOrderCommand(
                            "org-001",
                            "env-dev",
                            workOrder.WorkOrderId,
                            Utc("2026-07-27T08:00:00Z")),
                        CancellationToken.None);
                    break;
                case "hold":
                    await new HoldWorkOrderCommandHandler(dbContext).Handle(
                        new HoldWorkOrderCommand(
                            "org-001",
                            "env-dev",
                            workOrder.WorkOrderId,
                            "等待物料",
                            Utc("2026-07-27T08:00:00Z")),
                        CancellationToken.None);
                    break;
                case "cancel":
                    await new CancelWorkOrderCommandHandler(dbContext).Handle(
                        new CancelWorkOrderCommand(
                            "org-001",
                            "env-dev",
                            workOrder.WorkOrderId,
                            "计划取消",
                            Utc("2026-07-27T08:00:00Z")),
                        CancellationToken.None);
                    break;
            }
        });

        Assert.Equal(action, exception.Action);
        Assert.Equal(status, exception.CurrentStatus);
    }

    [Theory]
    [InlineData(WorkOrder.CreatedStatus)]
    [InlineData(WorkOrder.StartedStatus)]
    [InlineData(WorkOrder.HoldStatus)]
    public async Task Work_order_release_allows_current_domain_states(string status)
    {
        await using var provider = MesTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Infrastructure.ApplicationDbContext>();
        var workOrder = WorkOrder.Create(
            "org-001",
            "env-dev",
            $"WO-RELEASE-{status}",
            "SKU-FG",
            "PV-001",
            10m,
            1,
            Utc("2026-07-28T08:00:00Z"),
            "PCS");
        SetStatus(workOrder, status);
        dbContext.WorkOrders.Add(workOrder);
        dbContext.OperationTasks.Add(OperationTask.Create(
            "org-001",
            "env-dev",
            workOrder.WorkOrderId,
            $"OP-{status}",
            OperationTaskLifecycleStatus.Queued,
            10,
            "WC-10",
            [],
            Utc("2026-07-27T07:00:00Z"),
            TimeSpan.FromHours(1),
            null,
            null));
        await dbContext.SaveChangesAsync();

        var response = await new ReleaseWorkOrderCommandHandler(
            dbContext,
            NoRequirementSnapshotProvider.Instance).Handle(
            new ReleaseWorkOrderCommand(
                "org-001",
                "env-dev",
                workOrder.WorkOrderId,
                Utc("2026-07-27T08:00:00Z")),
            CancellationToken.None);

        Assert.Equal("Accepted", response.Status);
        Assert.Equal(WorkOrder.ReleasedStatus, workOrder.Status);
    }

    [Fact]
    public async Task Cancelled_work_order_cancel_is_a_legal_noop()
    {
        await using var provider = MesTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Infrastructure.ApplicationDbContext>();
        var workOrder = WorkOrder.Create(
            "org-001",
            "env-dev",
            "WO-CANCEL-NOOP",
            "SKU-FG",
            "PV-001",
            10m,
            1,
            Utc("2026-07-28T08:00:00Z"),
            "PCS");
        workOrder.Cancel("首次取消", Utc("2026-07-27T07:00:00Z"));
        dbContext.WorkOrders.Add(workOrder);
        await dbContext.SaveChangesAsync();

        var response = await new CancelWorkOrderCommandHandler(dbContext).Handle(
            new CancelWorkOrderCommand(
                "org-001",
                "env-dev",
                workOrder.WorkOrderId,
                "重复取消",
                Utc("2026-07-27T08:00:00Z")),
            CancellationToken.None);

        Assert.Equal("Accepted", response.Status);
        Assert.Equal("首次取消", workOrder.CancelReason);
    }

    [Fact]
    public async Task Release_readiness_failure_remains_a_regular_business_error()
    {
        await using var provider = MesTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Infrastructure.ApplicationDbContext>();
        var workOrder = WorkOrder.Create(
            "org-001",
            "env-dev",
            "WO-NO-PV",
            "SKU-FG",
            null,
            10m,
            1,
            Utc("2026-07-28T08:00:00Z"),
            "PCS");
        dbContext.WorkOrders.Add(workOrder);
        await dbContext.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<KnownException>(() =>
            new ReleaseWorkOrderCommandHandler(dbContext).Handle(
                new ReleaseWorkOrderCommand(
                    "org-001",
                    "env-dev",
                    workOrder.WorkOrderId,
                    Utc("2026-07-27T08:00:00Z")),
                CancellationToken.None));

        Assert.Contains("QUALITY_PLAN_MISSING", exception.Message, StringComparison.Ordinal);
        Assert.IsNotType<MesLifecycleConflictException>(exception);
    }

    private static void SetStatus(WorkOrder workOrder, string status)
    {
        typeof(WorkOrder)
            .GetProperty(nameof(WorkOrder.Status))!
            .SetValue(workOrder, status);
    }

    private static DateTimeOffset Utc(string value) => DateTimeOffset.Parse(value);
}
