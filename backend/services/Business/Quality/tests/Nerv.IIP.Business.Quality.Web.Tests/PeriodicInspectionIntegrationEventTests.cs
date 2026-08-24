using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionPlanAggregate;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionTaskAggregate;
using Nerv.IIP.Business.Quality.Infrastructure;
using Nerv.IIP.Business.Quality.Web.Application.Commands.InspectionTasks;
using Nerv.IIP.Business.Quality.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Contracts.Mes;
using Nerv.IIP.Messaging.CAP;

namespace Nerv.IIP.Business.Quality.Web.Tests;

public sealed class PeriodicInspectionIntegrationEventTests
{
    [Fact]
    public async Task Fake_time_at_the_first_due_window_generates_one_assigned_operation_task_and_advances_watermark()
    {
        await using var dbContext = CreateDbContext();
        var plan = NewPeriodicPlan();
        dbContext.InspectionPlans.Add(plan);
        await dbContext.SaveChangesAsync();
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        var coordinator = new PeriodicInspectionOperationScopeCoordinator(dbContext);
        await new WorkOrderReleasedIntegrationEventHandlerForCreatePeriodicInspectionContexts(
            dbContext, coordinator, deadLetters).HandleAsync(WorkOrderReleased(), CancellationToken.None);
        await new ProductionReportRecordedIntegrationEventHandlerForTrackPeriodicInspection(
            dbContext, coordinator, deadLetters).HandleAsync(ProductionReport(), CancellationToken.None);
        var clock = new FakeTimeProvider(DateTimeOffset.Parse("2026-08-24T03:30:00Z"));
        var handler = new GeneratePeriodicInspectionTimeTasksCommandHandler(dbContext, coordinator, clock);

        var generated = await handler.Handle(
            new GeneratePeriodicInspectionTimeTasksCommand("org-001", "env-dev", 24, 100),
            CancellationToken.None);
        var replayed = await handler.Handle(
            new GeneratePeriodicInspectionTimeTasksCommand("org-001", "env-dev", 24, 100),
            CancellationToken.None);

        Assert.Equal(1, generated);
        Assert.Equal(0, replayed);
        var task = await dbContext.InspectionTasks.SingleAsync();
        Assert.Equal(plan.Id, task.InspectionPlanId);
        Assert.Equal("operation", task.SourceType);
        Assert.Equal("mes", task.SourceService);
        Assert.Equal("WO-001", task.SourceDocumentId);
        Assert.Equal("OP-001:periodic-time:1", task.SourceDocumentLineId);
        Assert.Equal("SKU-FG-1000", task.SkuCode);
        Assert.Equal(25m, task.Quantity);
        Assert.Equal("EA", task.UomCode);
        Assert.Equal("team-quality-001", task.AssignedTeamId);
        var operation = await dbContext.PeriodicInspectionOperations.Include(x => x.RuntimeContexts).SingleAsync();
        var runtimeContext = Assert.Single(operation.RuntimeContexts);
        Assert.Equal($"quality:periodic-time:{runtimeContext.Id}:1", task.TriggerIdempotencyKey);
        Assert.Equal(1, runtimeContext.LastGeneratedTimeWindowSequence);
        Assert.Equal(DateTime.Parse("2026-08-24T05:30:00Z").ToUniversalTime(), runtimeContext.NextTimeWindowAtUtc);

        var claimed = await new ClaimInspectionTaskCommandHandler(dbContext).Handle(
            new ClaimInspectionTaskCommand(
                task.Id,
                "org-001",
                "env-dev",
                "inspector-001",
                ["team-quality-001"],
                "claim-periodic-time-task",
                task.Version),
            CancellationToken.None);
        Assert.Equal(InspectionTaskStatuses.InProgress, claimed.Status);
        Assert.Equal("inspector-001", claimed.AssignedInspectorUserId);
    }

    [Fact]
    public async Task Out_of_order_mes_facts_reconcile_a_closed_periodic_context_without_generating_a_task()
    {
        await using var dbContext = CreateDbContext();
        var plan = NewPeriodicPlan();
        dbContext.InspectionPlans.Add(plan);
        await dbContext.SaveChangesAsync();
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        var scopeCoordinator = new PeriodicInspectionOperationScopeCoordinator(dbContext);

        await new ProductionReportRecordedIntegrationEventHandlerForTrackPeriodicInspection(
            dbContext, scopeCoordinator, deadLetters).HandleAsync(ProductionReport(), CancellationToken.None);
        await new MesOperationTaskCompletedIntegrationEventHandlerForClosePeriodicInspection(
            dbContext, scopeCoordinator, deadLetters).HandleAsync(OperationCompleted(), CancellationToken.None);
        await new WorkOrderReleasedIntegrationEventHandlerForCreatePeriodicInspectionContexts(
            dbContext, scopeCoordinator, deadLetters).HandleAsync(WorkOrderReleased(), CancellationToken.None);

        var generated = await new GeneratePeriodicInspectionTimeTasksCommandHandler(
            dbContext,
            scopeCoordinator,
            new FakeTimeProvider(DateTimeOffset.Parse("2026-08-25T01:00:00Z"))).Handle(
                new GeneratePeriodicInspectionTimeTasksCommand("org-001", "env-dev", 24, 100),
                CancellationToken.None);

        var operation = await dbContext.PeriodicInspectionOperations
            .Include(x => x.ProductionReports)
            .Include(x => x.RuntimeContexts)
            .SingleAsync();
        var context = Assert.Single(operation.RuntimeContexts);
        Assert.Equal(plan.Id, context.InspectionPlanId);
        Assert.Equal(1, context.InspectionPlanVersion);
        Assert.Equal(25m, context.CumulativeGoodQuantity);
        Assert.Equal(25m, context.QuantityHighWater);
        Assert.Equal("EA", context.UomCode);
        Assert.Equal("closed", context.Status);
        Assert.Equal(0, generated);
        Assert.Empty(await dbContext.InspectionTasks.ToListAsync());
        Assert.Empty(await deadLetters.ListAsync(null, null, CancellationToken.None));
    }

    [Fact]
    public async Task Released_periodic_context_without_a_production_report_does_not_generate_a_time_task()
    {
        await using var dbContext = CreateDbContext();
        dbContext.InspectionPlans.Add(NewPeriodicPlan());
        await dbContext.SaveChangesAsync();
        var coordinator = new PeriodicInspectionOperationScopeCoordinator(dbContext);
        await new WorkOrderReleasedIntegrationEventHandlerForCreatePeriodicInspectionContexts(
            dbContext,
            coordinator,
            new InMemoryIntegrationEventDeadLetterStore()).HandleAsync(WorkOrderReleased(), CancellationToken.None);

        var generated = await new GeneratePeriodicInspectionTimeTasksCommandHandler(
            dbContext,
            coordinator,
            new FakeTimeProvider(DateTimeOffset.Parse("2026-08-25T01:00:00Z"))).Handle(
                new GeneratePeriodicInspectionTimeTasksCommand("org-001", "env-dev", 24, 100),
                CancellationToken.None);

        Assert.Equal(0, generated);
        Assert.Empty(await dbContext.InspectionTasks.ToListAsync());
    }

    [Fact]
    public async Task Context_batch_limit_does_not_allow_an_earlier_not_due_context_to_hide_a_due_context()
    {
        await using var dbContext = CreateDbContext();
        dbContext.InspectionPlans.Add(NewPeriodicPlan());
        await dbContext.SaveChangesAsync();
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        var coordinator = new PeriodicInspectionOperationScopeCoordinator(dbContext);
        var releaseHandler = new WorkOrderReleasedIntegrationEventHandlerForCreatePeriodicInspectionContexts(
            dbContext, coordinator, deadLetters);
        var reportHandler = new ProductionReportRecordedIntegrationEventHandlerForTrackPeriodicInspection(
            dbContext, coordinator, deadLetters);

        await releaseHandler.HandleAsync(WorkOrderReleased("WO-001", "OP-001"), CancellationToken.None);
        await reportHandler.HandleAsync(
            ProductionReport("RPT-001", "WO-001", "OP-001", "2026-08-24T03:00:00Z"),
            CancellationToken.None);
        await releaseHandler.HandleAsync(WorkOrderReleased("WO-002", "OP-002"), CancellationToken.None);
        await reportHandler.HandleAsync(
            ProductionReport("RPT-002", "WO-002", "OP-002", "2026-08-24T01:00:00Z"),
            CancellationToken.None);

        var generated = await new GeneratePeriodicInspectionTimeTasksCommandHandler(
            dbContext,
            coordinator,
            new FakeTimeProvider(DateTimeOffset.Parse("2026-08-24T03:30:00Z"))).Handle(
                new GeneratePeriodicInspectionTimeTasksCommand("org-001", "env-dev", 24, 1),
                CancellationToken.None);

        Assert.Equal(1, generated);
        Assert.Equal("WO-002", (await dbContext.InspectionTasks.SingleAsync()).SourceDocumentId);
    }

    [Fact]
    public async Task Release_without_a_matching_periodic_plan_preserves_source_facts_without_creating_a_context()
    {
        await using var dbContext = CreateDbContext();
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();

        await new WorkOrderReleasedIntegrationEventHandlerForCreatePeriodicInspectionContexts(
            dbContext,
            new PeriodicInspectionOperationScopeCoordinator(dbContext),
            deadLetters).HandleAsync(WorkOrderReleased(), CancellationToken.None);

        var operation = await dbContext.PeriodicInspectionOperations
            .Include(x => x.RuntimeContexts)
            .SingleAsync();
        Assert.Equal("SKU-FG-1000", operation.SkuCode);
        Assert.Empty(operation.RuntimeContexts);
        Assert.Empty(await dbContext.InspectionTasks.ToListAsync());
        Assert.Empty(await deadLetters.ListAsync(null, null, CancellationToken.None));
    }

    [Fact]
    public async Task Missing_mes_business_facts_are_dead_lettered_without_creating_a_context()
    {
        await using var dbContext = CreateDbContext();
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        var malformed = ProductionReport() with
        {
            Payload = ProductionReport().Payload with { WorkCenterId = " " },
        };

        await new ProductionReportRecordedIntegrationEventHandlerForTrackPeriodicInspection(
            dbContext,
            new PeriodicInspectionOperationScopeCoordinator(dbContext),
            deadLetters).HandleAsync(malformed, CancellationToken.None);

        Assert.Empty(await dbContext.PeriodicInspectionOperations.ToListAsync());
        var deadLetter = Assert.Single(await deadLetters.ListAsync(null, null, CancellationToken.None));
        Assert.Equal("invalid-business-facts", deadLetter.FailureCode);
    }

    [Fact]
    public async Task Conflicting_report_identity_is_dead_lettered_and_preserves_the_first_fact()
    {
        await using var dbContext = CreateDbContext();
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        var handler = new ProductionReportRecordedIntegrationEventHandlerForTrackPeriodicInspection(
            dbContext,
            new PeriodicInspectionOperationScopeCoordinator(dbContext),
            deadLetters);

        await handler.HandleAsync(ProductionReport(), CancellationToken.None);
        var conflicting = ProductionReport() with
        {
            Payload = ProductionReport().Payload with { GoodQuantity = 30m },
        };
        await handler.HandleAsync(conflicting, CancellationToken.None);

        var operation = await dbContext.PeriodicInspectionOperations
            .Include(x => x.ProductionReports)
            .SingleAsync();
        Assert.Equal(25m, Assert.Single(operation.ProductionReports).GoodQuantity);
        Assert.Equal(
            "invalid-business-facts",
            Assert.Single(await deadLetters.ListAsync(null, null, CancellationToken.None)).FailureCode);
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"periodic-inspection-{Guid.CreateVersion7()}")
            .Options;
        return new ApplicationDbContext(options, new NoopMediator());
    }

    private static InspectionPlan NewPeriodicPlan()
    {
        var plan = InspectionPlan.Create(
            "org-001", "env-dev", "IQP-PERIODIC-001", "operation", "SKU-FG-1000", null, "WC-001", null, "mes-operation",
            timeIntervalHours: 2m,
            quantityInterval: 100m,
            assignedTeamId: "team-quality-001");
        plan.AddCharacteristic("appearance", "Appearance", "visual", "critical", true, "zero-defect");
        plan.Activate();
        return plan;
    }

    private static WorkOrderReleasedIntegrationEvent WorkOrderReleased(
        string workOrderId = "WO-001",
        string operationId = "OP-001") => new(
        $"evt-release-{workOrderId}",
        MesIntegrationEventTypes.WorkOrderReleased,
        MesIntegrationEventVersions.V1,
        DateTimeOffset.Parse("2026-08-24T01:00:00Z"),
        MesIntegrationEventSources.BusinessMes,
        $"corr-release-{workOrderId}",
        workOrderId,
        "org-001",
        "env-dev",
        "system:mes",
        $"mes:work-order-released:org-001:env-dev:{workOrderId}",
        new WorkOrderReleasedPayload(
            workOrderId,
            "SKU-FG-1000",
            1000m,
            DateTimeOffset.Parse("2026-08-24T01:00:00Z"),
            [new ReleasedOperationPayload(operationId, 10, "WC-001")]));

    private static ProductionReportRecordedIntegrationEvent ProductionReport(
        string reportNo = "RPT-001",
        string workOrderId = "WO-001",
        string operationId = "OP-001",
        string reportedAtUtc = "2026-08-24T01:30:00Z") => new(
        $"evt-report-{reportNo}",
        MesIntegrationEventTypes.ProductionReportRecorded,
        MesIntegrationEventVersions.V1,
        DateTimeOffset.Parse(reportedAtUtc),
        MesIntegrationEventSources.BusinessMes,
        $"corr-report-{reportNo}",
        workOrderId,
        "org-001",
        "env-dev",
        "system:mes",
        $"mes:production-report-recorded:org-001:env-dev:{reportNo}",
        new ProductionReportRecordedPayload(
            reportNo, workOrderId, operationId, "WC-001", null, 25m, 0m, 0m, "EA", null,
            DateTimeOffset.Parse(reportedAtUtc), false));

    private static MesOperationTaskCompletedIntegrationEvent OperationCompleted() => new(
        "evt-complete-001",
        MesIntegrationEventTypes.OperationTaskCompleted,
        MesIntegrationEventVersions.V1,
        DateTimeOffset.Parse("2026-08-24T04:00:00Z"),
        MesIntegrationEventSources.BusinessMes,
        "corr-complete-001",
        "WO-001",
        "org-001",
        "env-dev",
        "system:mes",
        "mes:operation-completed:org-001:env-dev:WO-001:OP-001",
        new OperationTaskCompletedPayload(
            "WO-001", "OP-001", "SKU-FG-1000", 10, "WC-001", 1000m, "EA", false,
            DateTimeOffset.Parse("2026-08-24T04:00:00Z")));

    private sealed class NoopMediator : IMediator
    {
        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification => Task.CompletedTask;

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest => throw new NotSupportedException();

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
