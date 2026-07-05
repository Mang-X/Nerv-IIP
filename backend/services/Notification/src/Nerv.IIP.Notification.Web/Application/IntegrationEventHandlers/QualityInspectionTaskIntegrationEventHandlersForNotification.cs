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
        var payload = integrationEvent.Payload;
        if (!TryRequired(payload.InspectionTaskId, out var inspectionTaskId)
            || !TryRequired(payload.SourceDocumentId, out var sourceDocumentId)
            || !TryRequired(payload.SkuCode, out var skuCode))
        {
            await deadLetterStore.AddAsync(
                IntegrationEventDeadLetterMessage.Create(
                    ConsumerName,
                    integrationEvent,
                    "invalid-payload",
                    "Quality inspection task overdue payload is missing required task, source document, or SKU fields."),
                cancellationToken);
            return;
        }

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
            SourceService: integrationEvent.SourceService,
            SourceEventType: integrationEvent.EventType,
            SourceEventId: integrationEvent.EventId,
            IntentType: NotificationContractConstants.IntentTypeTask,
            Severity: NotificationContractConstants.SeverityWarning,
            DedupeKey: integrationEvent.IdempotencyKey,
            Resource: new NotificationResourceRef("inspection-task", inspectionTaskId, null),
            Title: $"Inspection task overdue: {skuCode}",
            Summary: $"Inspection task {inspectionTaskId} for {payload.SourceType}/{payload.SourceService} document {sourceDocumentId} is overdue since {payload.DueAtUtc:O}.",
            SuggestedRecipientRefs: recipientRefs);

        await sender.Send(new SubmitNotificationIntentCommand(integrationEvent.OrganizationId, integrationEvent.EnvironmentId, request, timeProvider.GetUtcNow()), cancellationToken);
    }

    private static bool TryRequired(string? value, out string required)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            required = string.Empty;
            return false;
        }

        required = value.Trim();
        return true;
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
