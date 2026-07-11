using DotNetCore.CAP;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Contracts.Mes;
using Nerv.IIP.Contracts.Notification;
using Nerv.IIP.Messaging.CAP;
using Nerv.IIP.Notification.Infrastructure;
using Nerv.IIP.Notification.Web.Application.Commands.Notifications;
using NetCorePal.Extensions.DistributedTransactions;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Notification.Web.Application.IntegrationEventHandlers;

[IntegrationEventConsumer("Nerv.IIP.Contracts.Mes.WorkOrderEngineeringChangeImpactDetectedIntegrationEvent", ConsumerName)]
public sealed class WorkOrderEngineeringChangeImpactDetectedIntegrationEventHandlerForNotification(
    ISender sender,
    ApplicationDbContext dbContext,
    IIntegrationEventDeadLetterStore deadLetterStore,
    IConfiguration configuration,
    TimeProvider timeProvider)
    : IIntegrationEventHandler<WorkOrderEngineeringChangeImpactDetectedIntegrationEvent>, ICapSubscribe
{
    public const string ConsumerName = "notification.mes-engineering-change-work-order-impact";

    private readonly IntegrationEventConsumerGuard<WorkOrderEngineeringChangeImpactDetectedIntegrationEvent> consumerGuard = new(
        new IntegrationEventEnvelopeValidator(),
        deadLetterStore,
        new IntegrationEventConsumerOptions(
            ConsumerName,
            MesIntegrationEventTypes.WorkOrderEngineeringChangeImpactDetected,
            MesIntegrationEventVersions.V1));

    public async Task HandleAsync(
        WorkOrderEngineeringChangeImpactDetectedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        await consumerGuard.HandleAsync(integrationEvent, HandleValidEventAsync, cancellationToken);
    }

    [CapSubscribe(nameof(WorkOrderEngineeringChangeImpactDetectedIntegrationEvent), Group = ConsumerName)]
    public Task HandleCapAsync(
        WorkOrderEngineeringChangeImpactDetectedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        return HandleAsync(integrationEvent, cancellationToken);
    }

    private async Task HandleValidEventAsync(
        WorkOrderEngineeringChangeImpactDetectedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        var payload = integrationEvent.Payload
            ?? throw new KnownException("MES engineering change impact payload is required.");
        var eventId = Required(integrationEvent.EventId, "Integration event id is required.");
        var eventType = Required(integrationEvent.EventType, "Integration event type is required.");
        var sourceService = Required(integrationEvent.SourceService, "Integration event source service is required.");
        var organizationId = Required(integrationEvent.OrganizationId, "Integration event organization is required.");
        var environmentId = Required(integrationEvent.EnvironmentId, "Integration event environment is required.");
        var dedupeKey = Required(integrationEvent.IdempotencyKey, "Integration event idempotency key is required.");
        var workOrderId = Required(payload.WorkOrderId, "MES work order id is required.");
        var changeNumber = Required(payload.ChangeNumber, "Engineering change number is required.");
        var archivedProductionVersionId = Required(payload.ArchivedProductionVersionId, "Archived production version id is required.");
        var impactStatus = Required(payload.ImpactStatus, "MES engineering change impact status is required.");
        var recipientRefs = configuration.GetSection("Mes:EngineeringChangeImpact:RecipientRefs").Get<string[]>() ?? [];
        recipientRefs = recipientRefs
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (recipientRefs.Length == 0)
        {
            recipientRefs = ["role:process-engineer", "role:production-planner"];
        }

        if (!await NotificationProcessedIntegrationEventInbox.TryRecordAsync(
            dbContext,
            ConsumerName,
            integrationEvent,
            timeProvider.GetUtcNow(),
            cancellationToken))
        {
            return;
        }

        var successor = string.IsNullOrWhiteSpace(payload.SupersededByProductionVersionId)
            ? "no successor production version declared"
            : $"successor {payload.SupersededByProductionVersionId}";
        var request = new SubmitNotificationIntentRequest(
            SourceService: sourceService,
            SourceEventType: eventType,
            SourceEventId: eventId,
            IntentType: NotificationContractConstants.IntentTypeTask,
            Severity: NotificationContractConstants.SeverityWarning,
            DedupeKey: dedupeKey,
            Resource: new NotificationResourceRef("mes-work-order", workOrderId, null),
            Title: "MES work order affected by engineering change",
            Summary: $"Work order {workOrderId} is affected by {changeNumber}; archived production version {archivedProductionVersionId}, {successor}, status {impactStatus}.",
            SuggestedRecipientRefs: recipientRefs);

        await sender.Send(new SubmitNotificationIntentCommand(organizationId, environmentId, request, timeProvider.GetUtcNow()), cancellationToken);
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
