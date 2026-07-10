using DotNetCore.CAP;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Contracts.Notification;
using Nerv.IIP.Contracts.Wms;
using Nerv.IIP.Messaging.CAP;
using Nerv.IIP.Notification.Infrastructure;
using Nerv.IIP.Notification.Web.Application.Commands.Notifications;
using NetCorePal.Extensions.DistributedTransactions;

namespace Nerv.IIP.Notification.Web.Application.IntegrationEventHandlers;

[IntegrationEventConsumer("Nerv.IIP.Contracts.Wms.WmsIntegrationEvent", ConsumerName)]
public sealed class WcsRetryExhaustedIntegrationEventHandlerForNotification(
    ISender sender, ApplicationDbContext dbContext, IIntegrationEventDeadLetterStore deadLetterStore, TimeProvider timeProvider)
    : IIntegrationEventHandler<WmsIntegrationEvent>, ICapSubscribe
{
    public const string ConsumerName = "notification.wms-wcs-retry-exhausted";
    private readonly IntegrationEventConsumerGuard<WmsIntegrationEvent> consumerGuard = new(new IntegrationEventEnvelopeValidator(), deadLetterStore, new IntegrationEventConsumerOptions(ConsumerName, WmsIntegrationEventTypes.WcsTaskRetryExhausted, WmsIntegrationEventVersions.V1));

    public Task HandleAsync(WmsIntegrationEvent integrationEvent, CancellationToken cancellationToken) => consumerGuard.HandleAsync(integrationEvent, HandleValidAsync, cancellationToken);

    [CapSubscribe("Nerv.IIP.Contracts.Wms.WmsIntegrationEvent", Group = ConsumerName)]
    public Task HandleCapAsync(WmsIntegrationEvent integrationEvent, CancellationToken cancellationToken) => HandleAsync(integrationEvent, cancellationToken);

    private async Task HandleValidAsync(WmsIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(integrationEvent.Payload.PublicReference))
        {
            return;
        }
        if (!await NotificationProcessedIntegrationEventInbox.TryRecordAsync(dbContext, ConsumerName, integrationEvent, timeProvider.GetUtcNow(), cancellationToken)) return;
        var request = new SubmitNotificationIntentRequest(integrationEvent.SourceService, integrationEvent.EventType, integrationEvent.EventId, NotificationContractConstants.IntentTypeTask, NotificationContractConstants.SeverityCritical, integrationEvent.IdempotencyKey, new NotificationResourceRef("wcs-task", integrationEvent.Payload.PublicReference, null), "WCS retry attempts exhausted", $"WCS task {integrationEvent.Payload.PublicReference} exhausted retry attempts: {integrationEvent.Payload.DiagnosticCode} {integrationEvent.Payload.DiagnosticMessage}", ["role:wms-operator"]);
        await sender.Send(new SubmitNotificationIntentCommand(integrationEvent.OrganizationId, integrationEvent.EnvironmentId, request, timeProvider.GetUtcNow()), cancellationToken);
    }
}
