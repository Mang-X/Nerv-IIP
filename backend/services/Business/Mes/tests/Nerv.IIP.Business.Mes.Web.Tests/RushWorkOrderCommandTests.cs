using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Schedules;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Workbench;
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

    [Fact]
    public async Task CreateRushWorkOrderCommand_GeneratesWorkOrderAndOperationIdsWhenNotProvided()
    {
        var store = new InMemoryMesPlanningStore();
        var numbering = new MesNumberingService();
        var now = DateTimeOffset.Parse("2026-05-22T08:00:00Z");
        var handler = new CreateRushWorkOrderCommandHandler(store, new RuleScheduler(), numbering);

        var response = await handler.Handle(
            new CreateRushWorkOrderCommand(
                "org-001",
                "env-dev",
                null,
                "SKU-R",
                "production-version-from-issue-188",
                1m,
                now.AddHours(4),
                "WC-A",
                null,
                10,
                TimeSpan.FromHours(1),
                now,
                "rush-create-001"),
            CancellationToken.None);

        Assert.Matches("^WO-[0-9]{8}-[0-9]{6}$", response.WorkOrderId);
        Assert.Equal(response.WorkOrderId, store.WorkOrders.Single().WorkOrderId);
        Assert.Equal($"{response.WorkOrderId}-OP-10", store.OperationTasks.Single().OperationTaskId);
    }

    [Fact]
    public async Task CreateRushWorkOrderCommand_ReusesExistingWorkOrderForSameIdempotencyKey()
    {
        var store = new InMemoryMesPlanningStore();
        var numbering = new MesNumberingService();
        var now = DateTimeOffset.Parse("2026-05-22T08:00:00Z");
        var handler = new CreateRushWorkOrderCommandHandler(store, new RuleScheduler(), numbering);
        var command = new CreateRushWorkOrderCommand(
            "org-001",
            "env-dev",
            null,
            "SKU-R",
            "production-version-from-issue-188",
            1m,
            now.AddHours(4),
            "WC-A",
            null,
            10,
            TimeSpan.FromHours(1),
            now,
            "rush-create-002");

        var first = await handler.Handle(command, CancellationToken.None);
        var second = await handler.Handle(command, CancellationToken.None);

        Assert.Equal(first.WorkOrderId, second.WorkOrderId);
        Assert.Single(store.WorkOrders);
        Assert.Single(store.OperationTasks);
    }

    [Fact]
    public async Task CreateRushWorkOrderCommand_GeneratesUniqueWorkOrdersForParallelRequests()
    {
        var numbering = new MesNumberingService();
        var now = DateTimeOffset.Parse("2026-05-22T08:00:00Z");

        var tasks = Enumerable.Range(1, 20)
            .Select(async index =>
            {
                var store = new InMemoryMesPlanningStore();
                var handler = new CreateRushWorkOrderCommandHandler(store, new RuleScheduler(), numbering);
                var response = await handler.Handle(
                    new CreateRushWorkOrderCommand(
                        "org-001",
                        "env-dev",
                        null,
                        $"SKU-R-{index}",
                        "production-version-from-issue-188",
                        1m,
                        now.AddHours(4),
                        "WC-A",
                        null,
                        10,
                        TimeSpan.FromHours(1),
                        now,
                        $"rush-parallel-{index}"),
                    CancellationToken.None);
                return response.WorkOrderId;
            });

        var workOrderIds = await Task.WhenAll(tasks);

        Assert.Equal(20, workOrderIds.Distinct(StringComparer.Ordinal).Count());
        Assert.All(workOrderIds, id => Assert.Matches("^WO-[0-9]{8}-[0-9]{6}$", id));
    }

    [Fact]
    public async Task ConvertPlanToWorkOrderCommand_GeneratesWorkOrderAndReplaysIdempotentResult()
    {
        var numbering = new MesNumberingService();
        var now = DateTimeOffset.Parse("2026-05-22T08:00:00Z");
        var handler = new ConvertPlanToWorkOrderCommandHandler(numbering);
        var command = new ConvertPlanToWorkOrderCommand(
            "org-001",
            "env-dev",
            "PLAN-001",
            null,
            now,
            "convert-plan-001");

        var first = await handler.Handle(command, CancellationToken.None);
        var second = await handler.Handle(command, CancellationToken.None);

        Assert.Equal(first.ReferenceId, second.ReferenceId);
        Assert.Matches("^WO-[0-9]{8}-[0-9]{6}$", first.ReferenceId);
    }

    [Fact]
    public async Task MesNumberingService_PersistsCounterAndIdempotencyKey()
    {
        await using var provider = MesTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Infrastructure.ApplicationDbContext>();
        var numbering = new MesNumberingService(dbContext, scope.ServiceProvider.GetRequiredService<IServiceScopeFactory>());

        var allocation = await numbering.AllocateAsync(
            "org-001",
            "env-dev",
            "work-order",
            "WO",
            null,
            "mes-persisted-numbering",
            "payload",
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        Assert.Matches("^WO-[0-9]{8}-[0-9]{6}$", allocation.Number);
        using var observerScope = provider.CreateScope();
        var observerContext = observerScope.ServiceProvider.GetRequiredService<Infrastructure.ApplicationDbContext>();
        Assert.Single(observerContext.NumberingCounters);
        var idempotency = Assert.Single(observerContext.NumberingIdempotencyKeys);
        Assert.Equal(allocation.Number, idempotency.Number);
    }
}
