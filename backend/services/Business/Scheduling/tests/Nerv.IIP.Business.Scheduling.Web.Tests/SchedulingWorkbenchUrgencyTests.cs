using System.Text.Json;
using Nerv.IIP.Business.Scheduling.Domain.AggregatesModel.OrderUrgencyAggregate;
using Nerv.IIP.Business.Scheduling.Domain.Services;
using Nerv.IIP.Business.Scheduling.Infrastructure;
using Nerv.IIP.Business.Scheduling.Web.Application.Scheduling;
using Nerv.IIP.Business.Scheduling.Web.Application.Urgency;
using Nerv.IIP.Contracts.Scheduling;

namespace Nerv.IIP.Business.Scheduling.Web.Tests;

public sealed partial class SchedulingWorkbenchTests
{
    // DomainInvariant: #4035 acceptance, ADR 0022 §D.13 and ADR 0014 §13.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Workbench_orders_by_saved_rush_urgency_priority_due_and_identity(bool reverseInput)
    {
        var start = new DateTimeOffset(2026, 9, 30, 8, 0, 0, TimeSpan.Zero);
        var due = start.AddDays(2);
        SavedWorkbenchOrder[] saved =
        [
            new("RUSH", int.MinValue, true, due.AddDays(1), BusinessPriorityLevel.P3),
            new("CRITICAL", int.MinValue, false, due.AddDays(1), BusinessPriorityLevel.P0),
            new("URGENT", int.MinValue, false, due.AddDays(1), BusinessPriorityLevel.P1),
            new("MAX", int.MaxValue, false, due.AddDays(1), BusinessPriorityLevel.P3),
            new("Z-EARLY", 12345, false, due, BusinessPriorityLevel.P3),
            new("TIE-A", 12345, false, due.AddDays(1), BusinessPriorityLevel.P3),
            new("TIE-B", 12345, false, due.AddDays(1), BusinessPriorityLevel.P3),
            new("MIN", int.MinValue, false, due, BusinessPriorityLevel.P3),
        ];
        await using var db = CreateDbContext();
        await SaveUrgenciesAsync(db, saved, start);
        var snapshotsBefore = db.OrderUrgencySnapshots.Select(x => x.ResultJson).ToArray();
        var assembler = CreateUrgencyWorkbenchAssembler(db, () => saved, start);
        var selections = saved.Select(x => new SchedulingWorkbenchOrderSelection(x.Id, 9999, !x.IsRush)).ToArray();
        if (reverseInput) Array.Reverse(selections);

        var input = await assembler.AssembleAsync("org-001", "env-dev", start, start.AddHours(8), selections, CancellationToken.None);
        var scheduler = new FiniteCapacityScheduler();
        var plan = scheduler.Schedule(input.Problem, "plan-urgency", start);
        string[] expected = ["RUSH", "CRITICAL", "URGENT", "MAX", "Z-EARLY", "TIE-A", "TIE-B", "MIN"];
        Assert.Equal(expected, plan.Assignments.OrderBy(x => x.StartUtc).Select(x => x.OrderId));
        Assert.Empty(plan.UnscheduledOperations);
        Assert.All(input.Problem.Orders, order =>
        {
            var source = saved.Single(x => x.Id == order.OrderId);
            Assert.Equal(source.Priority, order.Priority);
            Assert.Equal(source.IsRush, order.IsRush);
        });
        var repeated = scheduler.Schedule(input.Problem with { Orders = input.Problem.Orders.Reverse().ToArray() }, "plan-urgency", start);
        Assert.Equal(JsonSerializer.Serialize(plan, SchedulingJson.Options), JsonSerializer.Serialize(repeated, SchedulingJson.Options));
        Assert.Equal(snapshotsBefore, db.OrderUrgencySnapshots.Select(x => x.ResultJson).ToArray());

        var locked = new SchedulingLockedAssignmentContract("locked-min", "MIN", "MIN-TASK", 10, "WC-001", "WC-001", start, start.AddMinutes(30), "manual");
        var lockedPlan = scheduler.Schedule(input.Problem with { LockedAssignments = [locked] }, "plan-locked", start);
        var assignment = Assert.Single(lockedPlan.Assignments, x => x.OrderId == "MIN");
        Assert.True(assignment.IsLocked);
        Assert.Equal(start, assignment.StartUtc);
        Assert.Equal(start.AddMinutes(30), assignment.EndUtc);
        Assert.Equal(expected.Take(7), lockedPlan.Assignments.Where(x => !x.IsLocked).OrderBy(x => x.StartUtc).Select(x => x.OrderId));
    }

    [Fact]
    public async Task Workbench_uses_changed_saved_mes_values_on_the_next_solve()
    {
        var start = new DateTimeOffset(2026, 9, 30, 8, 0, 0, TimeSpan.Zero);
        SavedWorkbenchOrder[] saved =
        [
            new("A", 1, false, start.AddDays(2), BusinessPriorityLevel.P3),
            new("B", 2, false, start.AddDays(2), BusinessPriorityLevel.P3),
        ];
        await using var db = CreateDbContext();
        await SaveUrgenciesAsync(db, saved, start);
        var assembler = CreateUrgencyWorkbenchAssembler(db, () => saved, start);
        SchedulingWorkbenchOrderSelection[] selections = [new("A", 9999, true), new("B", 0, false)];
        async Task<string> FirstOrderAsync()
        {
            var input = await assembler.AssembleAsync("org-001", "env-dev", start, start.AddHours(8), selections, CancellationToken.None);
            return new FiniteCapacityScheduler().Schedule(input.Problem, "changed-saved", start)
                .Assignments.OrderBy(x => x.StartUtc).First().OrderId;
        }

        Assert.Equal("B", await FirstOrderAsync());
        saved[0] = saved[0] with { Priority = int.MaxValue };
        Assert.Equal("A", await FirstOrderAsync());
        saved[1] = saved[1] with { IsRush = true };
        Assert.Equal("B", await FirstOrderAsync());
    }

    private static SchedulingWorkbenchPlanAssembler CreateUrgencyWorkbenchAssembler(
        ApplicationDbContext db, Func<SavedWorkbenchOrder[]> readSaved, DateTimeOffset start)
    {
        var routing = new SchedulingProblemRoutingSnapshot("ROUTE-001", "A", "SKU-001",
            [new SchedulingProblemRoutingOperationSnapshot(10, "WC-001", "cutting", "Cutting", 0, 30, 0)]);
        var engineering = new StubProductEngineeringClient(routing);
        var mes = new HttpClient(new StubHandler(_ => Json(new
        {
            items = readSaved().Select(x => new
            {
                workOrderId = x.Id, skuId = "SKU-001", skuCode = "SKU-001", productionVersionId = "pv-001",
                quantity = 1, priority = x.Priority, isRush = x.IsRush, dueUtc = x.DueUtc, status = "released",
                workOrderNo = $"MO-{x.Id}",
                operationTasks = new[] { new { operationTaskId = $"{x.Id}-TASK", operationSequence = 10, earliestStartUtc = start } },
            }).ToArray(),
            total = readSaved().Length,
        }))) { BaseAddress = new Uri("http://mes") };
        return new SchedulingWorkbenchPlanAssembler(db, new HttpSchedulingWorkbenchSourceProvider(mes, engineering),
            new SchedulingProblemProducer(engineering, new StubMasterDataClient(start)),
            new OrderUrgencyService(db, new FixedUrgencyClock(start)));
    }

    private static async Task SaveUrgenciesAsync(ApplicationDbContext db, IEnumerable<SavedWorkbenchOrder> orders, DateTimeOffset start)
    {
        foreach (var order in orders)
        {
            var result = OrderUrgencyCalculator.Calculate(new OrderUrgencyCalculationInput(
                order.Id, $"MO-{order.Id}", start, order.DueUtc, TimeSpan.FromMinutes(30),
                new BusinessPriorityFact(order.UrgencyPriority, "manual", "acceptance", start, null, 1),
                [], false, false, start, $"facts-{order.Id}"));
            db.OrderUrgencySnapshots.Add(new OrderUrgencySnapshot("org-001", "env-dev", result.OrderId,
                result.BusinessReference, result.Level, result.ModelVersion, result.InputFingerprint, 1, start, start,
                OrderUrgencyContractMapper.Serialize(result)));
        }
        await db.SaveChangesAsync();
    }

    private sealed class FixedUrgencyClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed record SavedWorkbenchOrder(string Id, int Priority, bool IsRush, DateTimeOffset DueUtc, BusinessPriorityLevel UrgencyPriority);
}
