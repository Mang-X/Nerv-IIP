using Nerv.IIP.Business.Mes.Web.Application.Commands.Schedules;
using Nerv.IIP.Business.Mes.Web.Application.Planning;
using Nerv.IIP.Business.Mes.Web.Application.Scheduling;

namespace Nerv.IIP.Business.Mes.Web.Tests;

public sealed class RescheduleCommandTests
{
    [Fact]
    public async Task RescheduleCommand_CreatesNewScheduleVersionAndReportsDelayedWorkOrders()
    {
        var store = new InMemoryMesPlanningStore();
        var now = DateTimeOffset.Parse("2026-05-22T08:00:00Z");
        store.AddWorkOrder(new PlannedWorkOrder("org-001", "env-dev", "WO-001", "SKU-1", null, 1m, 10, now.AddHours(12)));
        store.AddOperationTask(new PlannedOperationTask("WO-001", "OP-10", OperationTaskStatus.Queued, 10, "WC-A", [], now, TimeSpan.FromHours(2)));

        var handler = new RescheduleCommandHandler(store, new RuleScheduler());
        var first = await handler.Handle(new RescheduleCommand("org-001", "env-dev", RescheduleTrigger.Manual, now), CancellationToken.None);

        store.AddUnavailability(new WorkCenterUnavailability("WC-A", now, now.AddHours(4), "breakdown"));
        var second = await handler.Handle(new RescheduleCommand("org-001", "env-dev", RescheduleTrigger.AssetUnavailable, now.AddMinutes(5)), CancellationToken.None);

        Assert.Equal(1, first.ScheduleVersion);
        Assert.Equal(2, second.ScheduleVersion);
        Assert.Equal(RescheduleTrigger.AssetUnavailable, second.Trigger);
        Assert.Contains("WO-001", second.AffectedWorkOrderIds);
        Assert.Equal(2, store.ScheduleResults.Count);
    }
}
