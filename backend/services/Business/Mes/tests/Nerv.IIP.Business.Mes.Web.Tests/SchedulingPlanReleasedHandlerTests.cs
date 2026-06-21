using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.OperationTaskAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Business.Mes.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Contracts.Scheduling;
using Nerv.IIP.Messaging.CAP;

namespace Nerv.IIP.Business.Mes.Web.Tests;

public sealed class SchedulingPlanReleasedHandlerTests
{
    [Fact]
    public async Task SchedulePlanReleasedHandler_UpsertsAndAssignsMesOperationTasks()
    {
        await using var dbContext = CreateDbContext();
        dbContext.WorkOrders.Add(WorkOrder.Create(
            "org-001",
            "env-dev",
            "WO-APS-001",
            "FG-APS",
            "PV-001",
            1m,
            10,
            DateTimeOffset.Parse("2026-06-02T16:00:00Z"),
            "PCS",
            new SourcePlanReference("DemandPlanning", "PlanningSuggestion", "PS-001", null)));
        dbContext.OperationTasks.Add(OperationTask.Queue(
            "org-001",
            "env-dev",
            "WO-APS-001",
            "OP-10",
            10,
            "WC-OLD",
            [],
            DateTimeOffset.Parse("2026-06-01T08:00:00Z"),
            TimeSpan.FromMinutes(30)));
        await dbContext.SaveChangesAsync();
        var handler = new SchedulePlanReleasedIntegrationEventHandlerForDispatch(
            dbContext,
            new InMemoryIntegrationEventDeadLetterStore());

        await handler.HandleAsync(CreateReleasedEvent(), CancellationToken.None);
        await dbContext.SaveChangesAsync();

        var task = await dbContext.OperationTasks.SingleAsync(x => x.OperationTaskIdValue == "OP-10");
        Assert.Equal("WC-OIL", task.WorkCenterId);
        Assert.Equal("DEV-OIL-01", task.DeviceAssetId);
        Assert.Equal(DateTimeOffset.Parse("2026-06-01T12:00:00Z"), task.EarliestStartUtc);
        Assert.Equal(TimeSpan.FromMinutes(90), task.Duration);
        Assert.Equal(DateTimeOffset.Parse("2026-06-01T07:30:00Z"), task.AssignedAtUtc);
    }

    [Fact]
    public async Task SchedulePlanReleasedHandler_SkipsDuplicateReleaseEvent()
    {
        var databaseRoot = new InMemoryDatabaseRoot();
        var options = CreateDbContextOptions($"mes-scheduling-release-{Guid.CreateVersion7():N}", databaseRoot);
        await using (var dbContext = CreateDbContext(options))
        {
            dbContext.WorkOrders.Add(WorkOrder.Create(
                "org-001",
                "env-dev",
                "WO-APS-001",
                "FG-APS",
                "PV-001",
                1m,
                10,
                DateTimeOffset.Parse("2026-06-02T16:00:00Z"),
                "PCS",
                null));
            await dbContext.SaveChangesAsync();
        }

        await using (var dbContext = CreateDbContext(options))
        {
            var handler = new SchedulePlanReleasedIntegrationEventHandlerForDispatch(
                dbContext,
                new InMemoryIntegrationEventDeadLetterStore());
            await handler.HandleAsync(CreateReleasedEvent(), CancellationToken.None);
            await dbContext.SaveChangesAsync();
        }

        await using (var dbContext = CreateDbContext(options))
        {
            var handler = new SchedulePlanReleasedIntegrationEventHandlerForDispatch(
                dbContext,
                new InMemoryIntegrationEventDeadLetterStore());
            await handler.HandleAsync(CreateReleasedEvent(), CancellationToken.None);
            await dbContext.SaveChangesAsync();
        }

        await using var assertionDbContext = CreateDbContext(options);
        Assert.Single(assertionDbContext.OperationTasks);
        Assert.Equal(1, await assertionDbContext.ProcessedIntegrationEvents.CountAsync());
    }

    [Fact]
    public async Task SchedulePlanReleasedHandler_DeadLettersMissingWorkOrderWithoutThrowing()
    {
        await using var dbContext = CreateDbContext();
        var deadLetterStore = new InMemoryIntegrationEventDeadLetterStore();
        var handler = new SchedulePlanReleasedIntegrationEventHandlerForDispatch(
            dbContext,
            deadLetterStore);

        await handler.HandleAsync(CreateReleasedEvent(), CancellationToken.None);
        await dbContext.SaveChangesAsync();

        Assert.Empty(dbContext.OperationTasks);
        Assert.Equal(1, await dbContext.ProcessedIntegrationEvents.CountAsync());
        var deadLetter = Assert.Single(await deadLetterStore.ListAsync(
            SchedulePlanReleasedIntegrationEventHandlerForDispatch.ConsumerName,
            IntegrationEventDeadLetterStatus.Pending,
            CancellationToken.None));
        Assert.Equal("mes.schedulePlanReleased.workOrderNotFound", deadLetter.FailureCode);
        Assert.Contains("WO-APS-001", deadLetter.FailureMessage, StringComparison.Ordinal);
    }

    private static SchedulePlanReleasedIntegrationEvent CreateReleasedEvent()
    {
        return new SchedulePlanReleasedIntegrationEvent(
            "evt-scheduling-release-001",
            SchedulingIntegrationEventTypes.SchedulePlanReleased,
            SchedulingIntegrationEventVersions.V1,
            DateTimeOffset.Parse("2026-06-01T07:30:00Z"),
            SchedulingIntegrationEventSources.BusinessScheduling,
            "corr-001",
            "cause-001",
            "org-001",
            "env-dev",
            "scheduling",
            "scheduling:schedule-plan-released:org-001:env-dev:plan-001",
            new SchedulePlanLifecyclePayload(
                "plan-001",
                "problem-001",
                1,
                "aps-lite-v1",
                "fingerprint-001",
                "released",
                [
                    new SchedulePlanAffectedOperationPayload(
                        "WO-APS-001",
                        "OP-10",
                        10,
                        "DEV-OIL-01",
                        "WC-OIL",
                        DateTimeOffset.Parse("2026-06-01T12:00:00Z"),
                        DateTimeOffset.Parse("2026-06-01T13:30:00Z"))
                ]));
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = CreateDbContextOptions($"mes-scheduling-release-{Guid.CreateVersion7():N}", new InMemoryDatabaseRoot());
        return CreateDbContext(options);
    }

    private static ApplicationDbContext CreateDbContext(DbContextOptions<ApplicationDbContext> options)
    {
        return new ApplicationDbContext(options, new NoopMediator());
    }

    private static DbContextOptions<ApplicationDbContext> CreateDbContextOptions(
        string databaseName,
        InMemoryDatabaseRoot databaseRoot)
    {
        return new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName, databaseRoot)
            .Options;
    }

    private sealed class NoopMediator : IMediator
    {
        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification => Task.CompletedTask;

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("Noop mediator cannot send requests.");
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest
        {
            throw new NotSupportedException("Noop mediator cannot send requests.");
        }

        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("Noop mediator cannot send requests.");
        }

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("Noop mediator cannot stream requests.");
        }

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("Noop mediator cannot stream requests.");
        }
    }
}
