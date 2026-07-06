using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using NetCorePal.Extensions.DistributedTransactions;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;
using Nerv.IIP.Business.Mes.Domain.DomainEvents;
using Nerv.IIP.Business.Mes.Web.Application.IntegrationEventConverters;
using Nerv.IIP.Business.Mes.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Business.Mes.Web.Application.ProductEngineering;
using Nerv.IIP.Contracts.Mes;
using Nerv.IIP.Contracts.Notification;
using Nerv.IIP.Contracts.ProductEngineering;
using Nerv.IIP.Messaging.CAP;
using Nerv.IIP.Notification.Domain.AggregatesModel.NotificationIntentAggregate;
using Nerv.IIP.Notification.Infrastructure.Repositories;
using Nerv.IIP.Notification.Web.Application.Commands.Notifications;
using Nerv.IIP.Notification.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Notification.Web.Application.Notifications;
using MesDbContext = Nerv.IIP.Business.Mes.Infrastructure.ApplicationDbContext;
using NotificationDbContext = Nerv.IIP.Notification.Infrastructure.ApplicationDbContext;

namespace Nerv.IIP.Business.Acceptance.Tests;

public sealed class MesEngineeringChangeNotificationAcceptanceTests
{
    [Fact]
    public async Task Mes_eco_wip_impact_event_flows_to_notification_task()
    {
        await using var mesDb = CreateMesContext();
        var started = WorkOrder.Create(
            "org-001",
            "env-dev",
            "WO-STARTED",
            "SKU-FG-1000",
            "PV-OLD",
            10m,
            10,
            DateTimeOffset.Parse("2026-07-06T16:00:00Z"),
            "PCS");
        started.MarkReleased();
        started.Start(DateTimeOffset.Parse("2026-07-06T08:00:00Z"));
        mesDb.WorkOrders.Add(started);
        await mesDb.SaveChangesAsync();

        var mesHandler = new EngineeringChangeReleasedIntegrationEventHandlerForMesWip(
            mesDb,
            new InMemoryIntegrationEventDeadLetterStore(),
            new MesEngineeringChangeOptions { NotStartedPolicy = MesEngineeringChangeNotStartedPolicy.AutoRebind });
        await mesHandler.HandleAsync(CreateEngineeringChangeReleasedEvent(), CancellationToken.None);
        await mesDb.SaveChangesAsync();

        var impact = await mesDb.EngineeringChangeWorkOrderImpacts
            .SingleAsync(x => x.Status == MesEngineeringChangeImpactContractStatuses.PendingDecision);
        var impactEvent = new WorkOrderEngineeringChangeImpactDetectedIntegrationEventConverter()
            .Convert(new MesEngineeringChangeWorkOrderImpactDetectedDomainEvent(impact));
        Assert.Equal(MesIntegrationEventTypes.WorkOrderEngineeringChangeImpactDetected, impactEvent.EventType);
        Assert.Equal("WO-STARTED", impactEvent.Payload.WorkOrderId);
        Assert.Equal(MesEngineeringChangeImpactContractStatuses.PendingDecision, impactEvent.Payload.ImpactStatus);

        await using var notificationDb = CreateNotificationContext();
        var notificationHandler = new WorkOrderEngineeringChangeImpactDetectedIntegrationEventHandlerForNotification(
            new NotificationCommandExecutingSender(notificationDb),
            notificationDb,
            new InMemoryIntegrationEventDeadLetterStore(),
            CreateNotificationConfiguration(),
            TimeProvider.System);
        await notificationHandler.HandleAsync(impactEvent, CancellationToken.None);

        var intent = await notificationDb.NotificationIntents
            .Include(x => x.Messages)
            .Include(x => x.Tasks)
            .SingleAsync();
        Assert.Equal(MesIntegrationEventTypes.WorkOrderEngineeringChangeImpactDetected, intent.SourceEventType);
        Assert.Equal(NotificationIntentTypes.Task, intent.IntentType);
        Assert.Equal(NotificationContractConstants.SeverityWarning, intent.Severity);
        Assert.Equal("mes-work-order", intent.ResourceType);
        Assert.Equal("WO-STARTED", intent.ResourceId);
        Assert.Equal(["role:process-engineer", "role:production-planner"], intent.Messages.Select(x => x.RecipientRef).Order(StringComparer.Ordinal));
        Assert.Equal(2, intent.Tasks.Count);
        Assert.Contains("ECO-721", intent.Summary, StringComparison.Ordinal);
    }

    private static MesDbContext CreateMesContext()
    {
        var options = new DbContextOptionsBuilder<MesDbContext>()
            .UseInMemoryDatabase($"mes-eco-notification-acceptance-mes-{Guid.NewGuid():N}")
            .Options;
        return new MesDbContext(options, new NoopMediator());
    }

    private static NotificationDbContext CreateNotificationContext()
    {
        var options = new DbContextOptionsBuilder<NotificationDbContext>()
            .UseInMemoryDatabase($"mes-eco-notification-acceptance-notification-{Guid.NewGuid():N}")
            .Options;
        return new NotificationDbContext(options, new NoopMediator());
    }

    private static IConfiguration CreateNotificationConfiguration()
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Mes:EngineeringChangeImpact:RecipientRefs:0"] = "role:process-engineer",
                ["Mes:EngineeringChangeImpact:RecipientRefs:1"] = "role:production-planner",
            })
            .Build();
    }

    private static EngineeringChangeReleasedIntegrationEvent CreateEngineeringChangeReleasedEvent()
    {
        return new EngineeringChangeReleasedIntegrationEvent(
            "evt-product-engineering-eco-721",
            ProductEngineeringIntegrationEventTypes.EngineeringChangeReleased,
            ProductEngineeringIntegrationEventVersions.V1,
            DateTimeOffset.Parse("2026-07-06T08:00:00Z"),
            ProductEngineeringIntegrationEventSources.BusinessProductEngineering,
            "corr-eco-721",
            "cause-eco-721",
            "org-001",
            "env-dev",
            "product-engineering",
            "product-engineering:engineering-change-released:org-001:env-dev:ECO-721",
            new EngineeringChangeReleasedPayload(
                "change-721",
                "ECO-721",
                ["PV-OLD"],
                new DateOnly(2026, 7, 6),
                [
                    new EngineeringChangeAffectedVersionPayload(
                        "production-version",
                        "PV-OLD",
                        "PV-NEW")
                ]));
    }

    private sealed class NotificationCommandExecutingSender(NotificationDbContext dbContext) : ISender
    {
        public async Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            if (request is SubmitNotificationIntentCommand command)
            {
                var handler = new SubmitNotificationIntentCommandHandler(
                    new NotificationIntentRepository(dbContext),
                    dbContext,
                    new NotificationDeliveryService(
                        dbContext,
                        [],
                        Options.Create(new NotificationDeliveryOptions { RetryWorkerEnabled = false }),
                        new NotificationChannelRateLimiter()));
                return (TResponse)(object)await handler.Handle(command, cancellationToken);
            }

            throw new NotSupportedException($"Request type is not supported by this test sender: {request.GetType().FullName}");
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest
        {
            throw new NotSupportedException($"Request type is not supported by this test sender: {request.GetType().FullName}");
        }

        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException($"Request type is not supported by this test sender: {request.GetType().FullName}");
        }

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("This test sender does not support streams.");
        }

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("This test sender does not support streams.");
        }
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
