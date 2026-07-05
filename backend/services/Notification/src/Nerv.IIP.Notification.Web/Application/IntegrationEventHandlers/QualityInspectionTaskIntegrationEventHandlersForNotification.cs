using DotNetCore.CAP;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Nerv.IIP.Contracts.Notification;
using Nerv.IIP.Contracts.Quality;
using Nerv.IIP.Messaging.CAP;
using Nerv.IIP.Notification.Infrastructure;
using Nerv.IIP.Notification.Web.Application.Commands.Notifications;
using NetCorePal.Extensions.DistributedTransactions;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Notification.Web.Application.IntegrationEventHandlers;

[IntegrationEventConsumer("Nerv.IIP.Contracts.Quality.InspectionTaskOverdueIntegrationEvent", ConsumerName)]
public sealed class InspectionTaskOverdueIntegrationEventHandlerForNotification(
    ISender sender,
    ApplicationDbContext dbContext,
    IIntegrationEventDeadLetterStore deadLetterStore,
    IConfiguration configuration,
    TimeProvider timeProvider)
    : IIntegrationEventHandler<InspectionTaskOverdueIntegrationEvent>, ICapSubscribe
{
    public const string ConsumerName = "notification.quality-inspection-task-overdue";

    private readonly IntegrationEventConsumerGuard<InspectionTaskOverdueIntegrationEvent> consumerGuard = new(
        new IntegrationEventEnvelopeValidator(),
        deadLetterStore,
        new IntegrationEventConsumerOptions(
            ConsumerName,
            QualityIntegrationEventTypes.InspectionTaskOverdue,
            QualityIntegrationEventVersions.V1));

    public async Task HandleAsync(
        InspectionTaskOverdueIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        await consumerGuard.HandleAsync(integrationEvent, HandleValidEventAsync, cancellationToken);
    }

    [CapSubscribe("Nerv.IIP.Contracts.Quality.InspectionTaskOverdueIntegrationEvent", Group = ConsumerName)]
    public Task HandleCapAsync(
        InspectionTaskOverdueIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        return HandleAsync(integrationEvent, cancellationToken);
    }

    private async Task HandleValidEventAsync(
        InspectionTaskOverdueIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        var payload = integrationEvent.Payload
            ?? throw new KnownException("Quality inspection task overdue payload is required.");
        var eventId = Required(integrationEvent.EventId, "Integration event id is required.");
        var eventType = Required(integrationEvent.EventType, "Integration event type is required.");
        var sourceService = Required(integrationEvent.SourceService, "Integration event source service is required.");
        var organizationId = Required(integrationEvent.OrganizationId, "Integration event organization is required.");
        var environmentId = Required(integrationEvent.EnvironmentId, "Integration event environment is required.");
        var dedupeKey = Required(integrationEvent.IdempotencyKey, "Integration event idempotency key is required.");
        var inspectionTaskId = Required(payload.InspectionTaskId, "Inspection task id is required.");
        var sourceDocumentId = Required(payload.SourceDocumentId, "Inspection task source document id is required.");
        var skuCode = Required(payload.SkuCode, "Inspection task SKU code is required.");
        var recipientRefs = GetRecipientRefs(configuration);

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
            Severity: NotificationContractConstants.SeverityWarning,
            DedupeKey: dedupeKey,
            Resource: new NotificationResourceRef("inspection-task", inspectionTaskId, null),
            Title: $"Inspection task overdue: {skuCode}",
            Summary: $"Inspection task {inspectionTaskId} for {payload.SourceType}/{payload.SourceService} document {sourceDocumentId} is overdue since {payload.DueAtUtc:O}.",
            SuggestedRecipientRefs: recipientRefs);

        await sender.Send(new SubmitNotificationIntentCommand(organizationId, environmentId, request, timeProvider.GetUtcNow()), cancellationToken);
    }

    private static string Required(string? value, string message)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new KnownException(message);
        }

        return value.Trim();
    }

    private static string[] GetRecipientRefs(IConfiguration configuration)
    {
        var recipientRefs = configuration.GetSection("Quality:InspectionTaskOverdue:RecipientRefs").Get<string[]>() ?? [];
        recipientRefs = recipientRefs
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return recipientRefs.Length == 0 ? ["role:quality-inspector"] : recipientRefs;
    }
}
