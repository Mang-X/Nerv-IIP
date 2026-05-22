using Nerv.IIP.Business.Mes.Web.Application.Commands.Schedules;
using Nerv.IIP.Business.Mes.Web.Application.Commands.WorkOrders;
using Nerv.IIP.Business.Mes.Web.Application.Planning;
using Nerv.IIP.Business.Mes.Web.Application.Scheduling;

namespace Nerv.IIP.Business.Mes.Web.Tests;

public sealed class RushWorkOrderCommandTests
{
    [Fact]
    public async Task CreateRushWorkOrderCommand_CreatesHighPriorityWorkOrderAndReturnsDelayedOrders()
    {
        var store = new InMemoryMesPlanningStore();
        var now = DateTimeOffset.Parse("2026-05-22T08:00:00Z");
        store.AddWorkOrder(new PlannedWorkOrder("org-001", "env-dev", "WO-NORMAL", "SKU-N", null, 1m, 10, now.AddDays(1)));
        store.AddOperationTask(new PlannedOperationTask("WO-NORMAL", "OP-10", OperationTaskStatus.Queued, 10, "WC-A", [], now, TimeSpan.FromHours(2)));

        var handler = new CreateRushWorkOrderCommandHandler(store, new RuleScheduler());

        var response = await handler.Handle(
            new CreateRushWorkOrderCommand(
                "org-001",
                "env-dev",
                "WO-RUSH",
                "SKU-R",
                "production-version-from-issue-95",
                1m,
                now.AddHours(4),
                "WC-A",
                "OP-RUSH-20",
                20,
                TimeSpan.FromHours(1),
                now),
            CancellationToken.None);

        Assert.Equal("WO-RUSH", response.WorkOrderId);
        Assert.Equal(1000, store.WorkOrders.Single(x => x.WorkOrderId == "WO-RUSH").Priority);
        var operation = Assert.Single(store.OperationTasks, x => x.WorkOrderId == "WO-RUSH");
        Assert.Equal("OP-RUSH-20", operation.OperationTaskId);
        Assert.Equal(20, operation.OperationSequence);
        Assert.Equal(RescheduleTrigger.RushOrder, response.Schedule.Trigger);
        Assert.Contains("WO-NORMAL", response.AffectedWorkOrderIds);
    }
}
