using MediatR;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Contracts.Notification;
using Nerv.IIP.Contracts.Ops;
using Nerv.IIP.Notification.Infrastructure;
using Nerv.IIP.Notification.Infrastructure.IntegrationEvents;
using Nerv.IIP.Notification.Web.Application.Commands.Notifications;
using NetCorePal.Extensions.DistributedTransactions;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Notification.Web.Application.IntegrationEventHandlers;

[IntegrationEventConsumer("Nerv.IIP.Contracts.Ops.OperationTaskFailedIntegrationEvent", ConsumerName)]
public sealed class OperationTaskFailedIntegrationEventHandlerForNotification(
    ISender sender,
    ApplicationDbContext dbContext)
    : IIntegrationEventHandler<OperationTaskFailedIntegrationEvent>
{
    public const string ConsumerName = "notification.operation-task-failed";
    private const string DefaultRecipientRef = "role:ops-admin";

    public async Task HandleAsync(OperationTaskFailedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        var payload = integrationEvent.Payload
            ?? throw new KnownException("Operation task failed payload is required.");
        var eventId = Required(integrationEvent.EventId, "Integration event id is required.");
        var eventType = Required(integrationEvent.EventType, "Integration event type is required.");
        var sourceService = Required(integrationEvent.SourceService, "Integration event source service is required.");
        var organizationId = Required(integrationEvent.OrganizationId, "Integration event organization is required.");
        var environmentId = Required(integrationEvent.EnvironmentId, "Integration event environment is required.");
        var dedupeKey = Required(integrationEvent.IdempotencyKey, "Integration event idempotency key is required.");
        var operationTaskId = Required(payload.OperationTaskId, "Operation task id is required.");
        var instanceKey = Required(payload.InstanceKey, "Operation instance key is required.");
        var operationCode = Required(payload.OperationCode, "Operation code is required.");

        if (await dbContext.ProcessedIntegrationEvents.AnyAsync(
            x => x.ConsumerName == ConsumerName && x.EventId == eventId,
            cancellationToken))
        {
            return;
        }

        dbContext.ProcessedIntegrationEvents.Add(new ProcessedIntegrationEvent(
            ConsumerName,
            eventId,
            eventType,
            integrationEvent.EventVersion,
            sourceService,
            dedupeKey,
            DateTimeOffset.UtcNow));

        var summary = payload.FailureCode is null
            ? $"Operation {operationCode} failed for {instanceKey}."
            : $"Operation {operationCode} failed for {instanceKey}: {payload.FailureCode}.";
        var request = new SubmitNotificationIntentRequest(
            SourceService: sourceService,
            SourceEventType: eventType,
            SourceEventId: eventId,
            IntentType: NotificationContractConstants.IntentTypeTask,
            Severity: NotificationContractConstants.SeverityCritical,
            DedupeKey: dedupeKey,
            Resource: new NotificationResourceRef("operation-task", operationTaskId, null),
            Title: "Operation failed",
            Summary: summary,
            SuggestedRecipientRefs: [DefaultRecipientRef]);

        await sender.Send(new SubmitNotificationIntentCommand(
            organizationId,
            environmentId,
            request,
            DateTimeOffset.UtcNow), cancellationToken);
    }

    private static string Required(string? value, string message)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new KnownException(message);
        }

        return value;
    }
}
