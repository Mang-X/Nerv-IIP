using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.OperationTaskAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Business.Mes.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Contracts.Scheduling;
using Nerv.IIP.Messaging.CAP;
using NetCorePal.Extensions.Primitives;

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

    [Theory]
    [InlineData(OperationTaskLifecycleStatus.InProgress)]
    [InlineData(OperationTaskLifecycleStatus.Paused)]
    public async Task SchedulePlanReleasedHandler_DeadLettersRescheduleForActiveOperationTaskWithoutOverwritingAssignment(
        OperationTaskLifecycleStatus status)
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
            null));
        var task = OperationTask.Create(
            "org-001",
            "env-dev",
            "WO-APS-001",
            "OP-10",
            status,
            10,
            "WC-OLD",
            [],
            DateTimeOffset.Parse("2026-06-01T08:00:00Z"),
            TimeSpan.FromMinutes(30),
            DateTimeOffset.Parse("2026-06-01T08:05:00Z"),
            null);
        task.Assign(
            "operator-001",
            "DEV-OLD-01",
            "shift-a",
            DateTimeOffset.Parse("2026-06-01T07:45:00Z"));
        dbContext.OperationTasks.Add(task);
        await dbContext.SaveChangesAsync();
        var deadLetterStore = new InMemoryIntegrationEventDeadLetterStore();
        var handler = new SchedulePlanReleasedIntegrationEventHandlerForDispatch(
            dbContext,
            deadLetterStore);

        await handler.HandleAsync(CreateReleasedEvent(), CancellationToken.None);
        await dbContext.SaveChangesAsync();

        var persisted = await dbContext.OperationTasks.SingleAsync(x => x.OperationTaskIdValue == "OP-10");
        Assert.Equal(status, persisted.Status);
        Assert.Equal("WC-OLD", persisted.WorkCenterId);
        Assert.Equal("DEV-OLD-01", persisted.DeviceAssetId);
        Assert.Equal(DateTimeOffset.Parse("2026-06-01T08:00:00Z"), persisted.EarliestStartUtc);
        Assert.Equal(TimeSpan.FromMinutes(30), persisted.Duration);
        Assert.Equal(DateTimeOffset.Parse("2026-06-01T07:45:00Z"), persisted.AssignedAtUtc);
        Assert.Equal(1, await dbContext.ProcessedIntegrationEvents.CountAsync());

        var deadLetter = Assert.Single(await deadLetterStore.ListAsync(
            SchedulePlanReleasedIntegrationEventHandlerForDispatch.ConsumerName,
            IntegrationEventDeadLetterStatus.Pending,
            CancellationToken.None));
        Assert.Equal("mes.schedulePlanReleased.operationTaskInExecution", deadLetter.FailureCode);
        Assert.Contains("OP-10", deadLetter.FailureMessage, StringComparison.Ordinal);
        Assert.Contains(status.ToString(), deadLetter.FailureMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SchedulePlanReleasedHandler_DeadLettersActiveOperationTaskAndStillAssignsQueuedOperations()
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
            null));
        var activeTask = OperationTask.Create(
            "org-001",
            "env-dev",
            "WO-APS-001",
            "OP-10",
            OperationTaskLifecycleStatus.InProgress,
            10,
            "WC-OLD",
            [],
            DateTimeOffset.Parse("2026-06-01T08:00:00Z"),
            TimeSpan.FromMinutes(30),
            DateTimeOffset.Parse("2026-06-01T08:05:00Z"),
            null);
        activeTask.Assign(
            "operator-001",
            "DEV-OLD-01",
            "shift-a",
            DateTimeOffset.Parse("2026-06-01T07:45:00Z"));
        dbContext.OperationTasks.Add(activeTask);
        dbContext.OperationTasks.Add(OperationTask.Queue(
            "org-001",
            "env-dev",
            "WO-APS-001",
            "OP-20",
            20,
            "WC-PACK-OLD",
            [],
            DateTimeOffset.Parse("2026-06-01T14:00:00Z"),
            TimeSpan.FromMinutes(45)));
        await dbContext.SaveChangesAsync();
        var deadLetterStore = new InMemoryIntegrationEventDeadLetterStore();
        var handler = new SchedulePlanReleasedIntegrationEventHandlerForDispatch(
            dbContext,
            deadLetterStore);

        await handler.HandleAsync(
            CreateReleasedEvent(
                new SchedulePlanAffectedOperationPayload(
                    "WO-APS-001",
                    "OP-10",
                    10,
                    "DEV-OIL-01",
                    "WC-OIL",
                    DateTimeOffset.Parse("2026-06-01T12:00:00Z"),
                    DateTimeOffset.Parse("2026-06-01T13:30:00Z")),
                new SchedulePlanAffectedOperationPayload(
                    "WO-APS-001",
                    "OP-20",
                    20,
                    "DEV-PACK-01",
                    "WC-PACK",
                    DateTimeOffset.Parse("2026-06-01T14:00:00Z"),
                    DateTimeOffset.Parse("2026-06-01T14:45:00Z"))),
            CancellationToken.None);
        await dbContext.SaveChangesAsync();

        var persistedActiveTask = await dbContext.OperationTasks.SingleAsync(x => x.OperationTaskIdValue == "OP-10");
        Assert.Equal("WC-OLD", persistedActiveTask.WorkCenterId);
        Assert.Equal("DEV-OLD-01", persistedActiveTask.DeviceAssetId);
        Assert.Equal(DateTimeOffset.Parse("2026-06-01T08:00:00Z"), persistedActiveTask.EarliestStartUtc);
        Assert.Equal(TimeSpan.FromMinutes(30), persistedActiveTask.Duration);
        var persistedQueuedTask = await dbContext.OperationTasks.SingleAsync(x => x.OperationTaskIdValue == "OP-20");
        Assert.Equal("WC-PACK", persistedQueuedTask.WorkCenterId);
        Assert.Equal("DEV-PACK-01", persistedQueuedTask.DeviceAssetId);
        Assert.Equal(DateTimeOffset.Parse("2026-06-01T14:00:00Z"), persistedQueuedTask.EarliestStartUtc);
        Assert.Equal(TimeSpan.FromMinutes(45), persistedQueuedTask.Duration);

        var deadLetter = Assert.Single(await deadLetterStore.ListAsync(
            SchedulePlanReleasedIntegrationEventHandlerForDispatch.ConsumerName,
            IntegrationEventDeadLetterStatus.Pending,
            CancellationToken.None));
        Assert.Equal("mes.schedulePlanReleased.operationTaskInExecution", deadLetter.FailureCode);
        Assert.Contains("OperationId = OP-10", deadLetter.FailureMessage, StringComparison.Ordinal);
        Assert.Contains("TargetWorkCenterId = WC-OIL", deadLetter.FailureMessage, StringComparison.Ordinal);
        Assert.Contains("TargetResourceId = DEV-OIL-01", deadLetter.FailureMessage, StringComparison.Ordinal);
        Assert.Contains("TargetStartUtc = 2026-06-01T12:00:00.0000000+00:00", deadLetter.FailureMessage, StringComparison.Ordinal);
        Assert.Contains("TargetEndUtc = 2026-06-01T13:30:00.0000000+00:00", deadLetter.FailureMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SchedulePlanReleasedHandler_DoesNotPersistInboxWhenRejectedOperationPrecedesInvalidOperation()
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
            dbContext.OperationTasks.Add(OperationTask.Create(
                "org-001",
                "env-dev",
                "WO-APS-001",
                "OP-10",
                OperationTaskLifecycleStatus.InProgress,
                10,
                "WC-OLD",
                [],
                DateTimeOffset.Parse("2026-06-01T08:00:00Z"),
                TimeSpan.FromMinutes(30),
                DateTimeOffset.Parse("2026-06-01T08:05:00Z"),
                null));
            await dbContext.SaveChangesAsync();
        }

        await using (var dbContext = CreateDbContext(options))
        {
            var handler = new SchedulePlanReleasedIntegrationEventHandlerForDispatch(
                dbContext,
                new SaveChangesDeadLetterStore(dbContext));

            await Assert.ThrowsAsync<KnownException>(() => handler.HandleAsync(
                CreateReleasedEvent(
                    new SchedulePlanAffectedOperationPayload(
                        "WO-APS-001",
                        "OP-10",
                        10,
                        "DEV-OIL-01",
                        "WC-OIL",
                        DateTimeOffset.Parse("2026-06-01T12:00:00Z"),
                        DateTimeOffset.Parse("2026-06-01T13:30:00Z")),
                    new SchedulePlanAffectedOperationPayload(
                        "WO-APS-001",
                        "OP-20",
                        20,
                        "DEV-PACK-01",
                        "WC-PACK",
                        DateTimeOffset.Parse("2026-06-01T14:45:00Z"),
                        DateTimeOffset.Parse("2026-06-01T14:00:00Z"))),
                CancellationToken.None));
        }

        await using var assertionDbContext = CreateDbContext(options);
        Assert.Empty(assertionDbContext.ProcessedIntegrationEvents);
    }

    [Fact]
    public async Task SchedulePlanReleasedHandler_DoesNotPersistInboxWhenDeadLetterBatchWriteFails()
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
            dbContext.OperationTasks.Add(OperationTask.Create(
                "org-001",
                "env-dev",
                "WO-APS-001",
                "OP-10",
                OperationTaskLifecycleStatus.InProgress,
                10,
                "WC-OLD",
                [],
                DateTimeOffset.Parse("2026-06-01T08:00:00Z"),
                TimeSpan.FromMinutes(30),
                DateTimeOffset.Parse("2026-06-01T08:05:00Z"),
                null));
            dbContext.OperationTasks.Add(OperationTask.Create(
                "org-001",
                "env-dev",
                "WO-APS-001",
                "OP-20",
                OperationTaskLifecycleStatus.Paused,
                20,
                "WC-PACK-OLD",
                [],
                DateTimeOffset.Parse("2026-06-01T14:00:00Z"),
                TimeSpan.FromMinutes(45),
                DateTimeOffset.Parse("2026-06-01T14:05:00Z"),
                null));
            await dbContext.SaveChangesAsync();
        }

        await using (var dbContext = CreateDbContext(options))
        {
            var handler = new SchedulePlanReleasedIntegrationEventHandlerForDispatch(
                dbContext,
                new ThrowingAddRangeDeadLetterStore());

            await Assert.ThrowsAsync<InvalidOperationException>(() => handler.HandleAsync(
                CreateReleasedEvent(
                    new SchedulePlanAffectedOperationPayload(
                        "WO-APS-001",
                        "OP-10",
                        10,
                        "DEV-OIL-01",
                        "WC-OIL",
                        DateTimeOffset.Parse("2026-06-01T12:00:00Z"),
                        DateTimeOffset.Parse("2026-06-01T13:30:00Z")),
                    new SchedulePlanAffectedOperationPayload(
                        "WO-APS-001",
                        "OP-20",
                        20,
                        "DEV-PACK-01",
                        "WC-PACK",
                        DateTimeOffset.Parse("2026-06-01T14:00:00Z"),
                        DateTimeOffset.Parse("2026-06-01T14:45:00Z"))),
                CancellationToken.None));
        }

        await using var assertionDbContext = CreateDbContext(options);
        Assert.Empty(assertionDbContext.ProcessedIntegrationEvents);
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

    private static SchedulePlanReleasedIntegrationEvent CreateReleasedEvent(
        params SchedulePlanAffectedOperationPayload[] affectedOperations)
    {
        if (affectedOperations.Length == 0)
        {
            affectedOperations =
            [
                new SchedulePlanAffectedOperationPayload(
                    "WO-APS-001",
                    "OP-10",
                    10,
                    "DEV-OIL-01",
                    "WC-OIL",
                    DateTimeOffset.Parse("2026-06-01T12:00:00Z"),
                    DateTimeOffset.Parse("2026-06-01T13:30:00Z"))
            ];
        }

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
                affectedOperations));
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

    private sealed class SaveChangesDeadLetterStore(ApplicationDbContext dbContext) : IIntegrationEventDeadLetterStore
    {
        private readonly List<IntegrationEventDeadLetterMessage> messages = [];

        public async Task<IntegrationEventDeadLetterMessage> AddAsync(
            IntegrationEventDeadLetterMessage message,
            CancellationToken cancellationToken)
        {
            messages.Add(message);
            await dbContext.SaveChangesAsync(cancellationToken);
            return message;
        }

        public async Task<IReadOnlyList<IntegrationEventDeadLetterMessage>> AddRangeAsync(
            IReadOnlyCollection<IntegrationEventDeadLetterMessage> messages,
            CancellationToken cancellationToken)
        {
            this.messages.AddRange(messages);
            await dbContext.SaveChangesAsync(cancellationToken);
            return messages.ToArray();
        }

        public Task<IReadOnlyList<IntegrationEventDeadLetterMessage>> ListAsync(
            string? consumerName,
            IntegrationEventDeadLetterStatus? status,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<IntegrationEventDeadLetterMessage>>(
                messages
                    .Where(message => consumerName is null || message.ConsumerName == consumerName)
                    .Where(message => status is null || message.Status == status)
                    .ToArray());
        }

        public Task MarkReplayedAsync(
            Guid id,
            DateTimeOffset replayedAtUtc,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingAddRangeDeadLetterStore : IIntegrationEventDeadLetterStore
    {
        public Task<IntegrationEventDeadLetterMessage> AddAsync(
            IntegrationEventDeadLetterMessage message,
            CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Single dead-letter writes should not be used for schedule release batches.");
        }

        public Task<IReadOnlyList<IntegrationEventDeadLetterMessage>> AddRangeAsync(
            IReadOnlyCollection<IntegrationEventDeadLetterMessage> messages,
            CancellationToken cancellationToken)
        {
            Assert.Equal(2, messages.Count);
            throw new InvalidOperationException("Simulated batch dead-letter persistence failure.");
        }

        public Task<IReadOnlyList<IntegrationEventDeadLetterMessage>> ListAsync(
            string? consumerName,
            IntegrationEventDeadLetterStatus? status,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<IntegrationEventDeadLetterMessage>>([]);
        }

        public Task MarkReplayedAsync(
            Guid id,
            DateTimeOffset replayedAtUtc,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }
}
