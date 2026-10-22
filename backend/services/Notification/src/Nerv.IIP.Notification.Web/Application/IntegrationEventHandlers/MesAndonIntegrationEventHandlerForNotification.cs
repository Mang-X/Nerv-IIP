using DotNetCore.CAP;
using MediatR;
using Nerv.IIP.Contracts.Mes;
using Nerv.IIP.Contracts.Notification;
using Nerv.IIP.Messaging.CAP;
using Nerv.IIP.Notification.Infrastructure;
using Nerv.IIP.Notification.Web.Application.Commands.Notifications;
using Nerv.IIP.Notification.Web.Application.Notifications;
using NetCorePal.Extensions.DistributedTransactions;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Notification.Web.Application.IntegrationEventHandlers;

[IntegrationEventConsumer("Nerv.IIP.Contracts.Mes.AndonCallEscalatedIntegrationEvent", ConsumerName)]
public sealed class AndonCallEscalatedIntegrationEventHandlerForNotification(
    ISender sender,
    ApplicationDbContext dbContext,
    IIntegrationEventDeadLetterStore deadLetterStore,
    TimeProvider timeProvider,
    NotificationSummaryBudget summaryBudget)
    : IIntegrationEventHandler<AndonCallEscalatedIntegrationEvent>, ICapSubscribe
{
    public const string ConsumerName = "notification.mes-andon-call-escalated";

    private readonly IntegrationEventConsumerGuard<AndonCallEscalatedIntegrationEvent> consumerGuard = new(
        new IntegrationEventEnvelopeValidator(),
        deadLetterStore,
        new IntegrationEventConsumerOptions(
            ConsumerName,
            AndonCallEscalatedIntegrationEvent.Type,
            AndonCallEscalatedIntegrationEvent.Version));

    public async Task HandleAsync(
        AndonCallEscalatedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        await consumerGuard.HandleAsync(integrationEvent, HandleValidEventAsync, cancellationToken);
    }

    [CapSubscribe(AndonCallEscalatedIntegrationEvent.TopicTemplate, Group = ConsumerName)]
    public Task HandleCapAsync(
        AndonCallEscalatedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        return HandleAsync(integrationEvent, cancellationToken);
    }

    private async Task HandleValidEventAsync(
        AndonCallEscalatedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        var payload = integrationEvent.Payload
            ?? throw new KnownException("MES Andon call escalation payload is required.");
        var eventId = Required(integrationEvent.EventId, "Integration event id is required.");
        var eventType = Required(integrationEvent.EventType, "Integration event type is required.");
        var sourceService = Required(integrationEvent.SourceService, "Integration event source service is required.");
        var organizationId = Required(integrationEvent.OrganizationId, "Integration event organization is required.");
        var environmentId = Required(integrationEvent.EnvironmentId, "Integration event environment is required.");
        var dedupeKey = Required(integrationEvent.IdempotencyKey, "Integration event idempotency key is required.");
        var callId = Required(payload.CallId, "MES Andon call id is required.");
        var recipientId = Required(payload.RecipientId, "MES Andon escalation recipient id is required.");

        if (!await NotificationProcessedIntegrationEventInbox.TryRecordAsync(
            dbContext,
            ConsumerName,
            integrationEvent,
            timeProvider.GetUtcNow(),
            cancellationToken))
        {
            return;
        }

        var request = new SubmitNotificationIntentRequest(
            SourceService: sourceService,
            SourceEventType: eventType,
            SourceEventId: eventId,
            IntentType: NotificationContractConstants.IntentTypeTask,
            Severity: NotificationContractConstants.SeverityCritical,
            DedupeKey: dedupeKey,
            Resource: new NotificationResourceRef("mes-andon-call", callId, null),
            Title: $"MES Andon call escalated: {payload.Category}",
            Summary: $"Andon call {callId} ({payload.Category}) from work order {payload.WorkOrderId}, operation {payload.OperationTaskId}, work center {payload.WorkCenterId}, raised by {payload.CallerId} at {payload.RaisedAtUtc:O}, remained unclaimed for {payload.UnclaimedTimeoutSeconds:0.###} seconds. Handle at /mes/andon.",
            SuggestedRecipientRefs: [$"user:{recipientId}"]);

        await sender.Send(
            new SubmitNotificationIntentCommand(
                organizationId,
                environmentId,
                request,
                NotificationSummary.Render(request.Summary, summaryBudget),
                timeProvider.GetUtcNow()),
            cancellationToken);
    }

    private static string Required(string? value, string message)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new KnownException(message);
        }

        return value.Trim();
    }
}
