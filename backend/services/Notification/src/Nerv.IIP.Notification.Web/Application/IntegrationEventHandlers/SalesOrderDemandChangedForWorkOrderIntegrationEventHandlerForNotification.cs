using DotNetCore.CAP;
using MediatR;
using Nerv.IIP.Contracts.DemandPlanning;
using Nerv.IIP.Contracts.Notification;
using Nerv.IIP.Messaging.CAP;
using Nerv.IIP.Notification.Infrastructure;
using Nerv.IIP.Notification.Web.Application.Commands.Notifications;
using Nerv.IIP.Notification.Web.Application.Notifications;
using NetCorePal.Extensions.DistributedTransactions;

namespace Nerv.IIP.Notification.Web.Application.IntegrationEventHandlers;

[IntegrationEventConsumer(TopicName, ConsumerName)]
public sealed class SalesOrderDemandChangedForWorkOrderIntegrationEventHandlerForNotification(
    ISender sender,
    ApplicationDbContext dbContext,
    IIntegrationEventDeadLetterStore deadLetterStore,
    IProductionPlannerMemberDirectory plannerMembers,
    TimeProvider timeProvider,
    NotificationSummaryBudget summaryBudget)
    : IIntegrationEventHandler<SalesOrderDemandChangedForWorkOrderIntegrationEvent>, ICapSubscribe
{
    public const string TopicName = nameof(SalesOrderDemandChangedForWorkOrderIntegrationEvent);
    public const string ConsumerName = "notification.demand-planning-sales-order-demand-changed";

    private readonly IntegrationEventConsumerGuard<SalesOrderDemandChangedForWorkOrderIntegrationEvent> consumerGuard = new(
        new IntegrationEventEnvelopeValidator(),
        deadLetterStore,
        new IntegrationEventConsumerOptions(
            ConsumerName,
            DemandPlanningIntegrationEventTypes.SalesOrderDemandChangedForWorkOrder,
            DemandPlanningIntegrationEventVersions.V1));

    public Task HandleAsync(
        SalesOrderDemandChangedForWorkOrderIntegrationEvent integrationEvent,
        CancellationToken cancellationToken) =>
        consumerGuard.HandleAsync(integrationEvent, HandleValidEventAsync, cancellationToken);

    [CapSubscribe(TopicName, Group = ConsumerName)]
    public Task HandleCapAsync(
        SalesOrderDemandChangedForWorkOrderIntegrationEvent integrationEvent,
        CancellationToken cancellationToken) => HandleAsync(integrationEvent, cancellationToken);

    private async Task HandleValidEventAsync(
        SalesOrderDemandChangedForWorkOrderIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        if (!await NotificationProcessedIntegrationEventInbox.TryRecordAsync(
            dbContext, ConsumerName, integrationEvent, timeProvider.GetUtcNow(), cancellationToken))
        {
            return;
        }

        var members = await plannerMembers.ListMemberIdsAsync(
            integrationEvent.OrganizationId, integrationEvent.EnvironmentId, cancellationToken);
        if (members.Count == 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        var payload = integrationEvent.Payload;
        var status = payload.Cancelled ? "cancelled" : "changed";
        var request = new SubmitNotificationIntentRequest(
            SourceService: integrationEvent.SourceService,
            SourceEventType: integrationEvent.EventType,
            SourceEventId: integrationEvent.EventId,
            IntentType: NotificationContractConstants.IntentTypeTask,
            Severity: NotificationContractConstants.SeverityWarning,
            DedupeKey: integrationEvent.IdempotencyKey,
            Resource: new NotificationResourceRef("mes-work-order", payload.WorkOrderId, null),
            Title: payload.Cancelled ? "Sales order demand cancelled" : "Sales order demand changed",
            Summary: $"Sales order {payload.SalesOrderId} demand for work order {payload.WorkOrderId} was {status} (order version {payload.OrderVersion}). Review the work order demand.",
            SuggestedRecipientRefs: members.Select(id => $"user:{id}").ToArray());

        await sender.Send(new SubmitNotificationIntentCommand(
            integrationEvent.OrganizationId,
            integrationEvent.EnvironmentId,
            request,
            NotificationSummary.Render(request.Summary, summaryBudget),
            timeProvider.GetUtcNow()), cancellationToken);
    }
}
