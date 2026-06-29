using DotNetCore.CAP;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Nerv.IIP.Contracts.Notification;
using Nerv.IIP.Contracts.Scheduling;
using Nerv.IIP.Messaging.CAP;
using Nerv.IIP.Notification.Infrastructure;
using Nerv.IIP.Notification.Web.Application.Commands.Notifications;
using NetCorePal.Extensions.DistributedTransactions;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Notification.Web.Application.IntegrationEventHandlers;

[IntegrationEventConsumer("Nerv.IIP.Contracts.Scheduling.ScheduleConflictDetectedIntegrationEvent", ConsumerName)]
public sealed class ScheduleConflictDetectedIntegrationEventHandlerForNotification(
    ISender sender,
    ApplicationDbContext dbContext,
    IIntegrationEventDeadLetterStore deadLetterStore,
    IConfiguration configuration,
    TimeProvider timeProvider)
    : IIntegrationEventHandler<ScheduleConflictDetectedIntegrationEvent>, ICapSubscribe
{
    public const string ConsumerName = "notification.scheduling-conflict-detected";

    private readonly IntegrationEventConsumerGuard<ScheduleConflictDetectedIntegrationEvent> consumerGuard = new(
        new IntegrationEventEnvelopeValidator(),
        deadLetterStore,
        new IntegrationEventConsumerOptions(
            ConsumerName,
            SchedulingIntegrationEventTypes.ScheduleConflictDetected,
            SchedulingIntegrationEventVersions.V1));

    public async Task HandleAsync(
        ScheduleConflictDetectedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        await consumerGuard.HandleAsync(integrationEvent, HandleValidEventAsync, cancellationToken);
    }

    [CapSubscribe("Nerv.IIP.Contracts.Scheduling.ScheduleConflictDetectedIntegrationEvent", Group = ConsumerName)]
    public Task HandleCapAsync(
        ScheduleConflictDetectedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        return HandleAsync(integrationEvent, cancellationToken);
    }

    private async Task HandleValidEventAsync(
        ScheduleConflictDetectedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        var payload = integrationEvent.Payload
            ?? throw new KnownException("Schedule conflict payload is required.");
        var eventId = Required(integrationEvent.EventId, "Integration event id is required.");
        var eventType = Required(integrationEvent.EventType, "Integration event type is required.");
        var sourceService = Required(integrationEvent.SourceService, "Integration event source service is required.");
        var organizationId = Required(integrationEvent.OrganizationId, "Integration event organization is required.");
        var environmentId = Required(integrationEvent.EnvironmentId, "Integration event environment is required.");
        var dedupeKey = Required(integrationEvent.IdempotencyKey, "Integration event idempotency key is required.");
        var planId = Required(payload.PlanId, "Schedule plan id is required.");
        var conflictId = Required(payload.ConflictId, "Schedule conflict id is required.");
        var reasonCode = Required(payload.ConflictReasonCode, "Schedule conflict reason is required.");
        var severityCode = Required(payload.ConflictSeverity, "Schedule conflict severity is required.");
        var severity = MapSeverity(severityCode);
        var workOrderId = string.IsNullOrWhiteSpace(payload.WorkOrderId)
            ? null
            : payload.WorkOrderId.Trim();
        var recipientRefs = configuration.GetSection("Scheduling:ConflictNotification:RecipientRefs").Get<string[]>() ?? [];
        recipientRefs = recipientRefs
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (recipientRefs.Length == 0)
        {
            recipientRefs = ["role:production-planner"];
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

        var request = new SubmitNotificationIntentRequest(
            SourceService: sourceService,
            SourceEventType: eventType,
            SourceEventId: eventId,
            IntentType: NotificationContractConstants.IntentTypeTask,
            Severity: severity,
            DedupeKey: dedupeKey,
            Resource: new NotificationResourceRef("schedule-plan", planId, null),
            Title: "Schedule conflict detected",
            Summary: workOrderId is null
                ? $"Schedule plan {planId} has conflict {conflictId} ({reasonCode})."
                : $"Schedule plan {planId} has conflict {conflictId} ({reasonCode}) for work order {workOrderId}.",
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

    private static string MapSeverity(string value)
    {
        return value.Trim().ToLowerInvariant() switch
        {
            "error" => NotificationContractConstants.SeverityCritical,
            _ => NotificationContractConstants.SeverityWarning
        };
    }
}
