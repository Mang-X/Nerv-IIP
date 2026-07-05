using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.OperationTaskAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;
using Nerv.IIP.Business.Mes.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Business.Scheduling.Domain.AggregatesModel.SchedulePlanAggregate;
using Nerv.IIP.Business.Scheduling.Domain.DomainEvents;
using Nerv.IIP.Business.Scheduling.Web.Application.IntegrationEventConverters;
using Nerv.IIP.Business.Scheduling.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Business.Scheduling.Web.Application.Queries;
using Nerv.IIP.Contracts.Maintenance;
using Nerv.IIP.Contracts.Notification;
using Nerv.IIP.Contracts.Scheduling;
using Nerv.IIP.Messaging.CAP;
using Nerv.IIP.Notification.Infrastructure.Repositories;
using Nerv.IIP.Notification.Web.Application.Commands.Notifications;
using Nerv.IIP.Notification.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Notification.Web.Application.Notifications;
using NetCorePal.Extensions.Primitives;
using MesDbContext = Nerv.IIP.Business.Mes.Infrastructure.ApplicationDbContext;
using NotificationDbContext = Nerv.IIP.Notification.Infrastructure.ApplicationDbContext;
using SchedulingDbContext = Nerv.IIP.Business.Scheduling.Infrastructure.ApplicationDbContext;

namespace Nerv.IIP.Business.Acceptance.Tests;

public sealed class SchedulingInvalidationPropagationAcceptanceTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 6, 1, 10, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task AssetUnavailable_invalidates_scheduling_plan_marks_mes_operation_and_notifies_planner_idempotently()
    {
        await using var schedulingDb = CreateSchedulingDbContext();
        schedulingDb.SchedulePlans.Add(CreateSchedulePlan());
        await schedulingDb.SaveChangesAsync();
        var schedulingHandler = new AssetUnavailableIntegrationEventHandlerForInvalidateSchedulePlans(
            schedulingDb,
            new InMemoryIntegrationEventDeadLetterStore(),
            new FixedTimeProvider(FixedNow),
            NullLogger<AssetUnavailableIntegrationEventHandlerForInvalidateSchedulePlans>.Instance);

        await schedulingHandler.HandleAsync(CreateAssetUnavailableEvent(), CancellationToken.None);

        var invalidation = await schedulingDb.SchedulePlanInvalidations.SingleAsync();
        var invalidatedPlan = await schedulingDb.SchedulePlans
            .Include(x => x.Assignments)
            .SingleAsync(x => x.PlanId == invalidation.PlanId);
        var invalidatedEvent = new SchedulePlanInvalidatedIntegrationEventConverter(
                new FixedTimeProvider(FixedNow),
                new StubSchedulingIntegrationEventContextAccessor())
            .Convert(new SchedulePlanInvalidatedDomainEvent(
                invalidation,
                SchedulePlanInvalidatedSnapshot.FromPlan(invalidatedPlan, invalidatedPlan.Assignments)));

        await using var mesDb = CreateMesDbContext();
        mesDb.WorkOrders.Add(WorkOrder.Create(
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
        mesDb.OperationTasks.Add(OperationTask.Queue(
            "org-001",
            "env-dev",
            "WO-APS-001",
            "OP-10",
            10,
            "WC-OIL",
            [],
            DateTimeOffset.Parse("2026-06-01T12:00:00Z"),
            TimeSpan.FromMinutes(90)));
        await mesDb.SaveChangesAsync();
        var mesHandler = new SchedulePlanInvalidatedIntegrationEventHandlerForMarkInvalidated(
            mesDb,
            new InMemoryIntegrationEventDeadLetterStore());

        await mesHandler.HandleAsync(invalidatedEvent, CancellationToken.None);
        await mesHandler.HandleAsync(invalidatedEvent, CancellationToken.None);
        await mesDb.SaveChangesAsync();

        var task = await mesDb.OperationTasks.SingleAsync(x => x.OperationTaskIdValue == "OP-10");
        Assert.Equal(OperationTaskLifecycleStatus.ScheduleInvalidated, task.Status);
        Assert.Equal(1, await mesDb.ProcessedIntegrationEvents.CountAsync());

        await using var notificationDb = CreateNotificationDbContext();
        var notificationHandler = new SchedulePlanInvalidatedIntegrationEventHandlerForNotification(
            new NotificationSender(notificationDb),
            notificationDb,
            new InMemoryIntegrationEventDeadLetterStore(),
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Scheduling:InvalidationNotification:RecipientRefs:0"] = "role:scheduler",
            }).Build(),
            new FixedTimeProvider(FixedNow));

        await notificationHandler.HandleAsync(invalidatedEvent, CancellationToken.None);
        await notificationHandler.HandleAsync(invalidatedEvent, CancellationToken.None);

        var intent = await notificationDb.NotificationIntents
            .Include(x => x.Messages)
            .Include(x => x.Tasks)
            .SingleAsync();
        Assert.Equal(SchedulingIntegrationEventTypes.SchedulePlanInvalidated, intent.SourceEventType);
        Assert.Equal("plan-generated", intent.ResourceId);
        Assert.Equal("role:scheduler", Assert.Single(intent.Messages).RecipientRef);
        Assert.Single(intent.Tasks);
        Assert.Equal(1, await notificationDb.ProcessedIntegrationEvents.CountAsync());
    }

    private static SchedulingDbContext CreateSchedulingDbContext()
    {
        var options = new DbContextOptionsBuilder<SchedulingDbContext>()
            .UseInMemoryDatabase($"scheduling-invalidation-acceptance-{Guid.NewGuid():N}")
            .Options;
        return new SchedulingDbContext(options, new NoopMediator());
    }

    private static MesDbContext CreateMesDbContext()
    {
        var options = new DbContextOptionsBuilder<MesDbContext>()
            .UseInMemoryDatabase($"mes-invalidation-acceptance-{Guid.NewGuid():N}")
            .Options;
        return new MesDbContext(options, new NoopMediator());
    }

    private static NotificationDbContext CreateNotificationDbContext()
    {
        var options = new DbContextOptionsBuilder<NotificationDbContext>()
            .UseInMemoryDatabase($"notification-invalidation-acceptance-{Guid.NewGuid():N}")
            .Options;
        return new NotificationDbContext(options, new NoopMediator());
    }

    private static SchedulePlan CreateSchedulePlan()
    {
        return SchedulePlan.FromGeneratedPlan(
            "org-001",
            "env-dev",
            SchedulePlanContractMapper.ToDomainSnapshot(new SchedulePlanContract(
                ContractVersion: 1,
                PlanId: "plan-generated",
                ProblemId: "problem-001",
                ProblemFingerprint: "fingerprint-001",
                AlgorithmVersion: "aps-lite-v1",
                Status: SchedulePlanStatusContract.Generated,
                GeneratedAtUtc: DateTimeOffset.Parse("2026-06-01T08:00:00Z"),
                Metrics: new SchedulePlanMetricsContract(1, 0, 90, 90, 0, 0, 1m, 0m),
                Assignments:
                [
                    new ScheduleAssignmentContract(
                        "assign-001",
                        "WO-APS-001",
                        "OP-10",
                        10,
                        "DEV-OIL-01",
                        "WC-OIL",
                        DateTimeOffset.Parse("2026-06-01T12:00:00Z"),
                        DateTimeOffset.Parse("2026-06-01T13:30:00Z"),
                        false,
                        "scheduled")
                ],
                ResourceLoads: [],
                Conflicts: [],
                UnscheduledOperations: [],
                ChangeSummary: [],
                GanttItems: [])));
    }

    private static AssetUnavailableIntegrationEvent CreateAssetUnavailableEvent()
    {
        return new AssetUnavailableIntegrationEvent(
            "evt-maint-001",
            MaintenanceIntegrationEventTypes.AssetUnavailable,
            MaintenanceIntegrationEventVersions.V1,
            DateTimeOffset.Parse("2026-06-01T09:00:00Z"),
            MaintenanceIntegrationEventSources.Maintenance,
            "corr-maint-001",
            "wo-maint-001",
            "org-001",
            "env-dev",
            "system:maintenance",
            "maintenance:asset-unavailable:DEV-OIL-01",
            new AssetUnavailablePayload("DEV-OIL-01", "breakdown", DateTimeOffset.Parse("2026-06-01T09:00:00Z")));
    }

    private sealed class NotificationSender(NotificationDbContext dbContext) : ISender
    {
        public async Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            if (request is not SubmitNotificationIntentCommand command)
            {
                throw new NotSupportedException($"Unsupported notification command {request.GetType().Name}.");
            }

            var handler = new SubmitNotificationIntentCommandHandler(
                new NotificationIntentRepository(dbContext),
                dbContext,
                new NotificationDeliveryService(
                    dbContext,
                    [],
                    Options.Create(new NotificationDeliveryOptions()),
                    new NotificationChannelRateLimiter()));
            var response = await handler.Handle(command, cancellationToken);
            return (TResponse)(object)response;
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest
        {
            throw new NotSupportedException("Void notification commands are not used by this test.");
        }

        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("Object notification commands are not used by this test.");
        }

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("Notification streams are not used by this test.");
        }

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("Notification streams are not used by this test.");
        }
    }

    private sealed class StubSchedulingIntegrationEventContextAccessor : ISchedulingIntegrationEventContextAccessor
    {
        public SchedulingIntegrationEventContext GetContext()
        {
            return new SchedulingIntegrationEventContext("corr-maint-001", "evt-maint-001", "system:business-scheduling");
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
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
