using MediatR;
using Microsoft.Data.Sqlite;
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
    [Theory]
    [InlineData(typeof(SchedulePlanReleasedIntegrationEventHandlerForDispatch))]
    [InlineData(typeof(SchedulePlanRevokedIntegrationEventHandlerForWithdrawDispatch))]
    public void ScheduleLifecycleHandlers_RequireScopeCoordinator(Type handlerType)
    {
        var coordinator = Assert.Single(handlerType.GetConstructors())
            .GetParameters()
            .Single(x => x.ParameterType == typeof(IMesScheduleReleaseScopeCoordinator));

        Assert.False(coordinator.IsOptional);
    }

    [Fact]
    public async Task SchedulePlanReleasedHandler_DeadLettersMissingReleaseRevisionWithoutPersistingProvenance()
    {
        await using var dbContext = CreateDbContext();
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        var handler = CreateReleasedHandler(
            dbContext,
            deadLetters);

        var releasedEvent = CreateReleasedEvent();
        var missingRevisionEvent = releasedEvent with
        {
            Payload = releasedEvent.Payload with { ReleaseRevision = null }
        };

        await handler.HandleAsync(missingRevisionEvent, CancellationToken.None);
        await dbContext.SaveChangesAsync();

        Assert.Empty(await dbContext.OperationTasks.ToArrayAsync());
        Assert.Empty(await dbContext.ProcessedIntegrationEvents.ToArrayAsync());
        var deadLetter = Assert.Single(await deadLetters.ListAsync(
            SchedulePlanReleasedIntegrationEventHandlerForDispatch.ConsumerName,
            IntegrationEventDeadLetterStatus.Pending,
            CancellationToken.None));
        Assert.Equal("mes.schedulePlanReleased.invalidPayload", deadLetter.FailureCode);
    }

    [Theory]
    [InlineData("", "WC-OIL")]
    [InlineData("OP-10", "")]
    public async Task SchedulePlanReleasedHandler_DeadLettersMissingOperationIdentityWithoutThrowing(
        string operationId,
        string workCenterId)
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
        await dbContext.SaveChangesAsync();
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        var handler = CreateReleasedHandler(dbContext, deadLetters);

        await handler.HandleAsync(
            CreateReleasedEvent(new SchedulePlanAffectedOperationPayload(
                "WO-APS-001",
                operationId,
                10,
                "DEV-OIL-01",
                workCenterId,
                DateTimeOffset.Parse("2026-06-01T12:00:00Z"),
                DateTimeOffset.Parse("2026-06-01T13:30:00Z"))),
            CancellationToken.None);

        Assert.Empty(await dbContext.OperationTasks.ToArrayAsync());
        Assert.Single(await dbContext.ProcessedIntegrationEvents.ToArrayAsync());
        var deadLetter = Assert.Single(await deadLetters.ListAsync(
            SchedulePlanReleasedIntegrationEventHandlerForDispatch.ConsumerName,
            IntegrationEventDeadLetterStatus.Pending,
            CancellationToken.None));
        Assert.Equal("mes.schedulePlanReleased.invalidOperationIdentity", deadLetter.FailureCode);
    }

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
        var handler = CreateReleasedHandler(
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
        Assert.Equal(DateTimeOffset.Parse("2026-06-01T07:30:00Z"), task.ScheduledAtUtc);
        Assert.Equal("plan-001", task.SchedulePlanId);
        Assert.Equal(1, task.ScheduleReleaseRevision);
        Assert.Equal("STD-OIL", task.OperationCode);
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
        var handler = CreateReleasedHandler(
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
        var handler = CreateReleasedHandler(
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
                    DateTimeOffset.Parse("2026-06-01T13:30:00Z"),
                    "STD-OIL"),
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
    public async Task SchedulePlanReleasedHandler_DeadLettersInvalidOperationWithoutThrowing()
    {
        await using var connection = await CreateOpenSqliteConnectionAsync();
        await using (var dbContext = await CreateInitializedSqliteDbContextAsync(connection))
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

        await using (var dbContext = CreateSqliteDbContext(connection))
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync();
            var deadLetterStore = new SaveChangesDeadLetterStore(dbContext);
            var handler = CreateReleasedHandler(
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
                        DateTimeOffset.Parse("2026-06-01T14:45:00Z"),
                        DateTimeOffset.Parse("2026-06-01T14:00:00Z"))),
                CancellationToken.None);
            Assert.Equal(2, (await deadLetterStore.ListAsync(
                SchedulePlanReleasedIntegrationEventHandlerForDispatch.ConsumerName,
                IntegrationEventDeadLetterStatus.Pending,
                CancellationToken.None)).Count);
            await transaction.CommitAsync();
        }

        await using var assertionDbContext = CreateSqliteDbContext(connection);
        Assert.Single(assertionDbContext.ProcessedIntegrationEvents);
    }

    [Fact]
    public async Task SchedulePlanReleasedHandler_DoesNotPersistInboxWhenDeadLetterBatchWriteFails()
    {
        await using var connection = await CreateOpenSqliteConnectionAsync();
        await using (var dbContext = await CreateInitializedSqliteDbContextAsync(connection))
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

        await using (var dbContext = CreateSqliteDbContext(connection))
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync();
            var handler = CreateReleasedHandler(
                dbContext,
                new SaveThenThrowDeadLetterStore(dbContext));

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
            await transaction.RollbackAsync();
        }

        await using var assertionDbContext = CreateSqliteDbContext(connection);
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
            var handler = CreateReleasedHandler(
                dbContext,
                new InMemoryIntegrationEventDeadLetterStore());
            await handler.HandleAsync(CreateReleasedEvent(), CancellationToken.None);
            await dbContext.SaveChangesAsync();
        }

        await using (var dbContext = CreateDbContext(options))
        {
            var handler = CreateReleasedHandler(
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
        var handler = CreateReleasedHandler(
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

    [Fact]
    public async Task SchedulePlanInvalidatedHandler_MarksAffectedQueuedOperationTaskAsScheduleInvalidatedOnce()
    {
        var databaseRoot = new InMemoryDatabaseRoot();
        var options = CreateDbContextOptions($"mes-scheduling-invalidated-{Guid.CreateVersion7():N}", databaseRoot);
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
            dbContext.OperationTasks.Add(OperationTask.Queue(
                "org-001",
                "env-dev",
                "WO-APS-001",
                "OP-10",
                10,
                "WC-OIL",
                [],
                DateTimeOffset.Parse("2026-06-01T12:00:00Z"),
                TimeSpan.FromMinutes(90)));
            await dbContext.SaveChangesAsync();
        }

        await using (var dbContext = CreateDbContext(options))
        {
            var handler = new SchedulePlanInvalidatedIntegrationEventHandlerForMarkInvalidated(
                dbContext,
                new InMemoryIntegrationEventDeadLetterStore());
            var integrationEvent = CreateInvalidatedEvent();

            await handler.HandleAsync(integrationEvent, CancellationToken.None);
            await handler.HandleAsync(integrationEvent, CancellationToken.None);
        }

        await using var assertionDbContext = CreateDbContext(options);
        var task = await assertionDbContext.OperationTasks.SingleAsync(x => x.OperationTaskIdValue == "OP-10");
        Assert.Equal(OperationTaskLifecycleStatus.ScheduleInvalidated, task.Status);
        Assert.Equal("equipmentUnavailable", task.ScheduleInvalidationReasonCode);
        Assert.Equal(1, await assertionDbContext.ProcessedIntegrationEvents.CountAsync());
    }

    [Fact]
    public async Task SchedulePlanReleasedHandler_ClearsScheduleInvalidationReasonWhenRescheduled()
    {
        var databaseRoot = new InMemoryDatabaseRoot();
        var options = CreateDbContextOptions($"mes-scheduling-reschedule-clears-reason-{Guid.CreateVersion7():N}", databaseRoot);
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
            var task = OperationTask.Queue(
                "org-001",
                "env-dev",
                "WO-APS-001",
                "OP-10",
                10,
                "WC-OIL",
                [],
                DateTimeOffset.Parse("2026-06-01T12:00:00Z"),
                TimeSpan.FromMinutes(90));
            task.MarkScheduleInvalidated("equipmentUnavailable");
            dbContext.OperationTasks.Add(task);
            await dbContext.SaveChangesAsync();
        }

        await using (var dbContext = CreateDbContext(options))
        {
            var handler = CreateReleasedHandler(
                dbContext,
                new InMemoryIntegrationEventDeadLetterStore());
            await handler.HandleAsync(CreateReleasedEvent(), CancellationToken.None);
            await dbContext.SaveChangesAsync();
        }

        await using var assertionDbContext = CreateDbContext(options);
        var rescheduled = await assertionDbContext.OperationTasks.SingleAsync(x => x.OperationTaskIdValue == "OP-10");
        Assert.Equal(OperationTaskLifecycleStatus.Queued, rescheduled.Status);
        Assert.Null(rescheduled.ScheduleInvalidationReasonCode);
        Assert.Equal("DEV-OIL-01", rescheduled.DeviceAssetId);
    }

    [Theory]
    [InlineData(OperationTaskLifecycleStatus.InProgress)]
    [InlineData(OperationTaskLifecycleStatus.Paused)]
    public async Task SchedulePlanInvalidatedHandler_DoesNotOverrideActiveOrPausedOperationTaskStatus(
        OperationTaskLifecycleStatus status)
    {
        var options = CreateDbContextOptions($"mes-scheduling-invalidated-active-{Guid.CreateVersion7():N}", new InMemoryDatabaseRoot());
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
            var task = OperationTask.Queue(
                "org-001",
                "env-dev",
                "WO-APS-001",
                "OP-10",
                10,
                "WC-OIL",
                [],
                DateTimeOffset.Parse("2026-06-01T12:00:00Z"),
                TimeSpan.FromMinutes(90));
            task.Start(DateTimeOffset.Parse("2026-06-01T12:05:00Z"));
            if (status == OperationTaskLifecycleStatus.Paused)
            {
                task.Pause(DateTimeOffset.Parse("2026-06-01T12:10:00Z"));
            }

            dbContext.OperationTasks.Add(task);
            await dbContext.SaveChangesAsync();
        }

        await using (var dbContext = CreateDbContext(options))
        {
            var handler = new SchedulePlanInvalidatedIntegrationEventHandlerForMarkInvalidated(
                dbContext,
                new InMemoryIntegrationEventDeadLetterStore());

            await handler.HandleAsync(CreateInvalidatedEvent(), CancellationToken.None);
        }

        await using var assertionDbContext = CreateDbContext(options);
        var taskAfterInvalidation = await assertionDbContext.OperationTasks.SingleAsync(x => x.OperationTaskIdValue == "OP-10");
        Assert.Equal(status, taskAfterInvalidation.Status);
        Assert.Equal(1, await assertionDbContext.ProcessedIntegrationEvents.CountAsync());
    }

    [Fact]
    public async Task SchedulePlanReleasedHandler_ClosedTaskDoesNotPoisonQueuedAssignments()
    {
        await using var dbContext = CreateDbContext();
        dbContext.WorkOrders.Add(WorkOrder.Create(
            "org-001", "env-dev", "WO-APS-001", "FG-APS", "PV-001", 1m, 10,
            DateTimeOffset.Parse("2026-06-02T16:00:00Z"), "PCS", null));
        dbContext.OperationTasks.Add(OperationTask.Create(
            "org-001", "env-dev", "WO-APS-001", "OP-10", OperationTaskLifecycleStatus.Completed,
            10, "WC-DONE", [], DateTimeOffset.Parse("2026-06-01T08:00:00Z"), TimeSpan.FromMinutes(30),
            DateTimeOffset.Parse("2026-06-01T08:00:00Z"), DateTimeOffset.Parse("2026-06-01T08:30:00Z")));
        dbContext.OperationTasks.Add(OperationTask.Queue(
            "org-001", "env-dev", "WO-APS-001", "OP-20", 20, "WC-OLD", [],
            DateTimeOffset.Parse("2026-06-01T09:00:00Z"), TimeSpan.FromMinutes(30)));
        await dbContext.SaveChangesAsync();
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        var handler = CreateReleasedHandler(dbContext, deadLetters);

        await handler.HandleAsync(CreateReleasedEvent(
            new SchedulePlanAffectedOperationPayload("WO-APS-001", "OP-10", 10, "DEV-NEW-10", "WC-NEW", DateTimeOffset.Parse("2026-06-01T12:00:00Z"), DateTimeOffset.Parse("2026-06-01T13:00:00Z")),
            new SchedulePlanAffectedOperationPayload("WO-APS-001", "OP-20", 20, "DEV-NEW-20", "WC-NEW", DateTimeOffset.Parse("2026-06-01T13:00:00Z"), DateTimeOffset.Parse("2026-06-01T14:00:00Z"))), CancellationToken.None);
        await dbContext.SaveChangesAsync();

        Assert.Equal(OperationTaskLifecycleStatus.Completed, (await dbContext.OperationTasks.SingleAsync(x => x.OperationTaskIdValue == "OP-10")).Status);
        Assert.Equal("plan-001", (await dbContext.OperationTasks.SingleAsync(x => x.OperationTaskIdValue == "OP-20")).SchedulePlanId);
        Assert.Equal("mes.schedulePlanReleased.operationTaskClosed", Assert.Single(await deadLetters.ListAsync(
            SchedulePlanReleasedIntegrationEventHandlerForDispatch.ConsumerName,
            IntegrationEventDeadLetterStatus.Pending,
            CancellationToken.None)).FailureCode);
    }

    [Fact]
    public async Task FirstGovernedReleaseInvalidatesOmittedLegacyApsAssignments()
    {
        await using var dbContext = CreateDbContext();
        dbContext.WorkOrders.Add(WorkOrder.Create(
            "org-001", "env-dev", "WO-APS-001", "FG-APS", "PV-001", 1m, 10,
            DateTimeOffset.Parse("2026-06-02T16:00:00Z"), "PCS", null));
        var omittedLegacy = OperationTask.Queue(
            "org-001", "env-dev", "WO-APS-001", "OP-10", 10, "WC-OLD", [],
            DateTimeOffset.Parse("2026-06-01T08:00:00Z"), TimeSpan.FromMinutes(30));
        omittedLegacy.ApplyScheduleAssignment(
            "WC-LEGACY", "DEV-LEGACY", DateTimeOffset.Parse("2026-06-01T08:00:00Z"),
            DateTimeOffset.Parse("2026-06-01T08:30:00Z"), DateTimeOffset.Parse("2026-06-01T07:00:00Z"));
        dbContext.OperationTasks.Add(omittedLegacy);
        dbContext.OperationTasks.Add(OperationTask.Queue(
            "org-001", "env-dev", "WO-APS-001", "OP-20", 20, "WC-OLD", [],
            DateTimeOffset.Parse("2026-06-01T09:00:00Z"), TimeSpan.FromMinutes(30)));
        await dbContext.SaveChangesAsync();

        await CreateReleasedHandler(
            dbContext, new InMemoryIntegrationEventDeadLetterStore()).HandleAsync(
            CreateReleasedEvent(new SchedulePlanAffectedOperationPayload(
                "WO-APS-001", "OP-20", 20, "DEV-NEW", "WC-NEW",
                DateTimeOffset.Parse("2026-06-01T09:00:00Z"), DateTimeOffset.Parse("2026-06-01T09:30:00Z"))),
            CancellationToken.None);
        await dbContext.SaveChangesAsync();

        var reconciled = await dbContext.OperationTasks.SingleAsync(x => x.OperationTaskIdValue == "OP-10");
        Assert.Equal(OperationTaskLifecycleStatus.ScheduleInvalidated, reconciled.Status);
        Assert.Null(reconciled.ScheduledAtUtc);
        Assert.Null(reconciled.DeviceAssetId);
        Assert.Equal("plan-001", (await dbContext.OperationTasks.SingleAsync(x => x.OperationTaskIdValue == "OP-20")).SchedulePlanId);
    }

    private static SchedulePlanReleasedIntegrationEventHandlerForDispatch CreateReleasedHandler(
        ApplicationDbContext dbContext,
        IIntegrationEventDeadLetterStore deadLetterStore)
    {
        return new SchedulePlanReleasedIntegrationEventHandlerForDispatch(
            dbContext,
            deadLetterStore,
            new PostgreSqlMesScheduleReleaseScopeCoordinator(dbContext));
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
                    DateTimeOffset.Parse("2026-06-01T13:30:00Z"),
                    "STD-OIL")
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
                affectedOperations,
                1));
    }

    private static SchedulePlanInvalidatedIntegrationEvent CreateInvalidatedEvent()
    {
        return new SchedulePlanInvalidatedIntegrationEvent(
            "evt-scheduling-invalidated-001",
            SchedulingIntegrationEventTypes.SchedulePlanInvalidated,
            SchedulingIntegrationEventVersions.V1,
            DateTimeOffset.Parse("2026-06-01T09:00:00Z"),
            SchedulingIntegrationEventSources.BusinessScheduling,
            "corr-001",
            "maintenance-event-001",
            "org-001",
            "env-dev",
            "scheduling",
            "scheduling:schedule-plan-invalidated:org-001:env-dev:plan-001:maintenance-event-001",
            new SchedulePlanInvalidatedPayload(
                "plan-001",
                "problem-001",
                1,
                "aps-lite-v1",
                "fingerprint-001",
                "generated",
                "equipmentUnavailable",
                "maintenance.AssetUnavailable",
                "maintenance-event-001",
                ["DEV-OIL-01"],
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

    private static async Task<SqliteConnection> CreateOpenSqliteConnectionAsync()
    {
        var connection = new SqliteConnection("Filename=:memory:");
        await connection.OpenAsync();
        return connection;
    }

    private static async Task<ApplicationDbContext> CreateInitializedSqliteDbContextAsync(SqliteConnection connection)
    {
        var dbContext = CreateSqliteDbContext(connection);
        await dbContext.Database.EnsureCreatedAsync();
        return dbContext;
    }

    private static ApplicationDbContext CreateSqliteDbContext(SqliteConnection connection)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;
        return CreateDbContext(options);
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

        public Task<IReadOnlyList<IntegrationEventDeadLetterMessage>> ListAsync(
            IntegrationEventDeadLetterQuery query,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<IntegrationEventDeadLetterMessage>>(
                messages
                    .Where(message => query.ConsumerName is null || message.ConsumerName == query.ConsumerName)
                    .Where(message => query.Status is null || message.Status == query.Status)
                    .Where(message => query.EventType is null || message.EventType == query.EventType)
                    .Skip(query.Skip)
                    .Take(query.Take)
                    .ToArray());
        }

        public Task<IntegrationEventDeadLetterMetrics> GetMetricsAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(IntegrationEventDeadLetterMetrics.FromMessages(messages));
        }

        public Task<IntegrationEventDeadLetterMessage?> GetAsync(
            Guid id,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(messages.FirstOrDefault(message => message.Id == id));
        }

        public Task MarkReplayedAsync(
            Guid id,
            DateTimeOffset replayedAtUtc,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public Task MarkFailedAsync(
            Guid id,
            string failureCode,
            string failureMessage,
            DateTimeOffset failedAtUtc,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public Task MarkIgnoredAsync(
            Guid id,
            string reason,
            DateTimeOffset ignoredAtUtc,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }

    private sealed class SaveThenThrowDeadLetterStore(ApplicationDbContext dbContext) : IIntegrationEventDeadLetterStore
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
            return SaveThenThrowAsync(messages, cancellationToken);
        }

        private async Task<IReadOnlyList<IntegrationEventDeadLetterMessage>> SaveThenThrowAsync(
            IReadOnlyCollection<IntegrationEventDeadLetterMessage> messages,
            CancellationToken cancellationToken)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
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

        public Task<IReadOnlyList<IntegrationEventDeadLetterMessage>> ListAsync(
            IntegrationEventDeadLetterQuery query,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<IntegrationEventDeadLetterMessage>>([]);
        }

        public Task<IntegrationEventDeadLetterMetrics> GetMetricsAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(IntegrationEventDeadLetterMetrics.FromMessages([]));
        }

        public Task<IntegrationEventDeadLetterMessage?> GetAsync(
            Guid id,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<IntegrationEventDeadLetterMessage?>(null);
        }

        public Task MarkReplayedAsync(
            Guid id,
            DateTimeOffset replayedAtUtc,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public Task MarkFailedAsync(
            Guid id,
            string failureCode,
            string failureMessage,
            DateTimeOffset failedAtUtc,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public Task MarkIgnoredAsync(
            Guid id,
            string reason,
            DateTimeOffset ignoredAtUtc,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }
}
