using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Data.Common;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Business.Mes.Infrastructure.Repositories;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Schedules;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Workbench;
using Nerv.IIP.Business.Mes.Web.Application.Commands.WorkOrders;
using Nerv.IIP.Business.Mes.Web.Application.Planning;
using Nerv.IIP.Business.Mes.Web.Application.Scheduling;

namespace Nerv.IIP.Business.Mes.Web.Tests;

public sealed class RushWorkOrderCommandTests
{
    [Fact]
    public async Task Rush_idempotent_replay_uses_scope_and_work_order_predicate_query()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var interceptor = new RecordingCommandInterceptor();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .AddInterceptors(interceptor)
            .Options;
        await using var dbContext = new ApplicationDbContext(options, new NoopMediator());
        await dbContext.Database.EnsureCreatedAsync();
        var coding = new MesCodingService();
        var now = DateTimeOffset.Parse("2026-07-18T08:00:00Z");
        var command = new CreateRushWorkOrderCommand(
            "org-001", "env-dev", "WO-PREDICATE", "SKU-1", "PV-1", 1m,
            now.AddDays(1), "WC-1", "OP-10", 10, TimeSpan.FromMinutes(30), now,
            "rush-predicate-replay");

        await new CreateRushWorkOrderCommandHandler(new InMemoryMesPlanningStore(), new RuleScheduler(), coding)
            .Handle(command, CancellationToken.None);
        var persistedWorkOrder = WorkOrder.Create(
            "org-001", "env-dev", "WO-PREDICATE", "SKU-1", "PV-1", 1m, 1000, now.AddDays(1));
        persistedWorkOrder.ClearDomainEvents();
        dbContext.WorkOrders.Add(persistedWorkOrder);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        dbContext.ChangeTracker.Clear();
        interceptor.Clear();

        var store = new PersistentMesPlanningStore(dbContext, new OperationTaskRepository(dbContext));
        var handler = new CreateRushWorkOrderCommandHandler(store, new RuleScheduler(), coding, dbContext);
        await handler.Handle(command, CancellationToken.None);

        var replayQuery = Assert.Single(interceptor.Commands, sql =>
            sql.Contains("work_orders", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("organization_id", replayQuery, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("environment_id", replayQuery, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("work_order_id", replayQuery, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("WHERE", replayQuery, StringComparison.OrdinalIgnoreCase);
    }
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

    private sealed class RecordingCommandInterceptor : DbCommandInterceptor
    {
        public List<string> Commands { get; } = [];

        public void Clear() => Commands.Clear();

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result)
        {
            Commands.Add(command.CommandText);
            return result;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Commands.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }
    }

    [Fact]
    public async Task CreateRushWorkOrderCommand_GeneratesWorkOrderAndOperationIdsWhenNotProvided()
    {
        var store = new InMemoryMesPlanningStore();
        var numbering = new MesCodingService();
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
        var numbering = new MesCodingService();
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
        var numbering = new MesCodingService();
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
        await using var provider = MesTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Infrastructure.ApplicationDbContext>();
        var numbering = new MesCodingService();
        var now = DateTimeOffset.Parse("2026-05-22T08:00:00Z");
        var handler = new ConvertPlanToWorkOrderCommandHandler(dbContext, numbering);
        var command = new ConvertPlanToWorkOrderCommand(
            "org-001",
            "env-dev",
            "PLAN-001",
            null,
            now,
            "SKU-FG-1000",
            "PV-001",
            10m,
            "PCS",
            now.AddDays(2),
            "WC-A",
            IdempotencyKey:
            "convert-plan-001");

        var first = await handler.Handle(command, CancellationToken.None);
        var second = await handler.Handle(command, CancellationToken.None);

        Assert.Equal(first.ReferenceId, second.ReferenceId);
        Assert.Matches("^WO-[0-9]{8}-[0-9]{6}$", first.ReferenceId);
    }

    [Fact]
    public async Task ConvertPlanToWorkOrderCommand_SchedulesCreatedOperationAroundWorkCenterUnavailability()
    {
        await using var provider = MesTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Infrastructure.ApplicationDbContext>();
        var numbering = new MesCodingService();
        var now = DateTimeOffset.Parse("2026-05-22T08:00:00Z");
        dbContext.WorkCenterUnavailabilities.Add(Domain.AggregatesModel.ScheduleAggregate.WorkCenterUnavailability.Open(
            "org-001",
            "env-dev",
            "UNAV-WC-A",
            "WC-A",
            now,
            now.AddHours(2),
            "maintenance",
            "DEV-001"));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new ConvertPlanToWorkOrderCommandHandler(dbContext, numbering);

        var response = await handler.Handle(
            new ConvertPlanToWorkOrderCommand(
                "org-001",
                "env-dev",
                "PLAN-001",
                "WO-PLAN-001",
                now,
                "SKU-FG-1000",
                "PV-001",
                10m,
                "PCS",
                now.AddDays(2),
                "WC-A",
                IdempotencyKey: "convert-plan-schedule-001"),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        Assert.Equal("WO-PLAN-001", response.ReferenceId);
        var schedule = await dbContext.ScheduleResults.AsNoTracking().SingleAsync(CancellationToken.None);
        var assignment = Assert.Single(schedule.Assignments);
        Assert.Equal("WO-PLAN-001", assignment.WorkOrderId);
        Assert.Equal("WO-PLAN-001-OP-10", assignment.OperationTaskId);
        Assert.Equal(now.AddHours(2), assignment.StartUtc);
    }

    [Fact]
    public async Task MesCodingService_PersistsCounterAndIdempotencyKey()
    {
        await using var provider = MesTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Infrastructure.ApplicationDbContext>();
        var coding = new MesCodingService(dbContext, scope.ServiceProvider.GetRequiredService<IServiceScopeFactory>());

        var allocation = await coding.AllocateAsync(
            "org-001",
            "env-dev",
            "work-order",
            null,
            "mes-persisted-numbering",
            "payload",
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        Assert.Matches("^WO-[0-9]{8}-[0-9]{6}$", allocation.Code);
        using var observerScope = provider.CreateScope();
        var observerContext = observerScope.ServiceProvider.GetRequiredService<Infrastructure.ApplicationDbContext>();
        Assert.Single(observerContext.CodeCounters);
        var idempotency = Assert.Single(observerContext.CodeIdempotencyKeys);
        Assert.Equal(allocation.Code, idempotency.Code);
    }
}
