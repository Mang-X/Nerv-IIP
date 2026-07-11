using DotNetCore.CAP;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Nerv.IIP.Contracts.Approval;
using Nerv.IIP.Contracts.Notification;
using Nerv.IIP.Messaging.CAP;
using Nerv.IIP.Notification.Infrastructure;
using Nerv.IIP.Notification.Infrastructure.IntegrationEvents;
using Nerv.IIP.Notification.Web.Application.Commands.Notifications;
using NetCorePal.Extensions.DistributedTransactions;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Notification.Web.Application.IntegrationEventHandlers;

[IntegrationEventConsumer("Nerv.IIP.Contracts.Approval.ApprovalStepOverdueIntegrationEvent", ConsumerName)]
public sealed class ApprovalStepOverdueIntegrationEventHandlerForNotification(
    ISender sender,
    ApplicationDbContext dbContext,
    IIntegrationEventDeadLetterStore deadLetterStore,
    IConfiguration configuration,
    TimeProvider timeProvider)
    : IIntegrationEventHandler<ApprovalStepOverdueIntegrationEvent>, ICapSubscribe
{
    public const string ConsumerName = "notification.approval-step-overdue";

    private readonly IntegrationEventConsumerGuard<ApprovalStepOverdueIntegrationEvent> consumerGuard = new(
        new IntegrationEventEnvelopeValidator(),
        deadLetterStore,
        new IntegrationEventConsumerOptions(
            ConsumerName,
            ApprovalIntegrationEventTypes.StepOverdue,
            ApprovalIntegrationEventVersions.V1));

    public async Task HandleAsync(ApprovalStepOverdueIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        await consumerGuard.HandleAsync(integrationEvent, HandleValidEventAsync, cancellationToken);
    }

    [CapSubscribe(nameof(ApprovalStepOverdueIntegrationEvent), Group = ConsumerName)]
    public Task HandleCapAsync(ApprovalStepOverdueIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        return HandleAsync(integrationEvent, cancellationToken);
    }

    private async Task HandleValidEventAsync(ApprovalStepOverdueIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        var payload = integrationEvent.Payload
            ?? throw new KnownException("Approval step overdue payload is required.");
        var eventId = NotificationIntegrationEventRequired.Value(integrationEvent.EventId, "Integration event id is required.");
        var eventType = NotificationIntegrationEventRequired.Value(integrationEvent.EventType, "Integration event type is required.");
        var sourceService = NotificationIntegrationEventRequired.Value(integrationEvent.SourceService, "Integration event source service is required.");
        var organizationId = NotificationIntegrationEventRequired.Value(integrationEvent.OrganizationId, "Integration event organization is required.");
        var environmentId = NotificationIntegrationEventRequired.Value(integrationEvent.EnvironmentId, "Integration event environment is required.");
        var dedupeKey = NotificationIntegrationEventRequired.Value(integrationEvent.IdempotencyKey, "Integration event idempotency key is required.");
        var chainId = NotificationIntegrationEventRequired.Value(payload.ChainId, "Approval chain id is required.");
        var stepName = NotificationIntegrationEventRequired.Value(payload.StepName, "Approval step name is required.");
        var recipientRef = NotificationIntegrationEventRequired.Actor(payload.ApproverType, payload.ApproverRef);
        var recipientRefs = NotificationIntegrationEventRequired.MergeRecipients(
            recipientRef,
            configuration.GetSection("Approval:OverdueEscalation:RecipientRefs").Get<string[]>() ?? []);

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
            Resource: new NotificationResourceRef("approval-chain", chainId, null),
            Title: "Approval step overdue",
            Summary: $"Approval step {stepName} is overdue for {payload.DocumentReference.DocumentType} {payload.DocumentReference.DocumentId}.",
            SuggestedRecipientRefs: recipientRefs);

        await sender.Send(new SubmitNotificationIntentCommand(organizationId, environmentId, request, timeProvider.GetUtcNow()), cancellationToken);
    }
}

[IntegrationEventConsumer("Nerv.IIP.Contracts.Approval.ApprovalStepResolvedIntegrationEvent", ConsumerName)]
public sealed class ApprovalStepResolvedIntegrationEventHandlerForNotification(
    ISender sender,
    ApplicationDbContext dbContext,
    IIntegrationEventDeadLetterStore deadLetterStore,
    TimeProvider timeProvider)
    : IIntegrationEventHandler<ApprovalStepResolvedIntegrationEvent>, ICapSubscribe
{
    public const string ConsumerName = "notification.approval-step-resolved";

    private readonly IntegrationEventConsumerGuard<ApprovalStepResolvedIntegrationEvent> consumerGuard = new(
        new IntegrationEventEnvelopeValidator(),
        deadLetterStore,
        new IntegrationEventConsumerOptions(
            ConsumerName,
            ApprovalIntegrationEventTypes.StepResolved,
            ApprovalIntegrationEventVersions.V1));

    public async Task HandleAsync(ApprovalStepResolvedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        await consumerGuard.HandleAsync(integrationEvent, HandleValidEventAsync, cancellationToken);
    }

    [CapSubscribe(nameof(ApprovalStepResolvedIntegrationEvent), Group = ConsumerName)]
    public Task HandleCapAsync(ApprovalStepResolvedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        return HandleAsync(integrationEvent, cancellationToken);
    }

    private async Task HandleValidEventAsync(ApprovalStepResolvedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        var payload = integrationEvent.Payload
            ?? throw new KnownException("Approval step resolved payload is required.");
        var eventId = NotificationIntegrationEventRequired.Value(integrationEvent.EventId, "Integration event id is required.");
        var eventType = NotificationIntegrationEventRequired.Value(integrationEvent.EventType, "Integration event type is required.");
        var sourceService = NotificationIntegrationEventRequired.Value(integrationEvent.SourceService, "Integration event source service is required.");
        var organizationId = NotificationIntegrationEventRequired.Value(integrationEvent.OrganizationId, "Integration event organization is required.");
        var environmentId = NotificationIntegrationEventRequired.Value(integrationEvent.EnvironmentId, "Integration event environment is required.");
        var dedupeKey = NotificationIntegrationEventRequired.Value(integrationEvent.IdempotencyKey, "Integration event idempotency key is required.");
        var chainId = NotificationIntegrationEventRequired.Value(payload.ChainId, "Approval chain id is required.");
        var recipientRef = NotificationIntegrationEventRequired.Actor(payload.ActorType, payload.ActorRef);

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
            IntentType: NotificationContractConstants.IntentTypeMessage,
            Severity: NotificationContractConstants.SeverityInfo,
            DedupeKey: dedupeKey,
            Resource: new NotificationResourceRef("approval-chain", chainId, null),
            Title: "Approval step resolved",
            Summary: $"Approval step {payload.StepNo} was {payload.Decision} for {payload.DocumentReference.DocumentType} {payload.DocumentReference.DocumentId}.",
            SuggestedRecipientRefs: [recipientRef]);

        await sender.Send(new SubmitNotificationIntentCommand(organizationId, environmentId, request, timeProvider.GetUtcNow()), cancellationToken);
    }
}

[IntegrationEventConsumer("Nerv.IIP.Contracts.Approval.ApprovalActionRecordedIntegrationEvent", ConsumerName)]
public sealed class ApprovalActionRecordedIntegrationEventHandlerForNotification(
    ISender sender,
    ApplicationDbContext dbContext,
    IIntegrationEventDeadLetterStore deadLetterStore,
    TimeProvider timeProvider)
    : IIntegrationEventHandler<ApprovalActionRecordedIntegrationEvent>, ICapSubscribe
{
    public const string ConsumerName = "notification.approval-action-recorded";

    private readonly IntegrationEventConsumerGuard<ApprovalActionRecordedIntegrationEvent> consumerGuard = new(
        new IntegrationEventEnvelopeValidator(),
        deadLetterStore,
        new IntegrationEventConsumerOptions(
            ConsumerName,
            ApprovalIntegrationEventTypes.ActionRecorded,
            ApprovalIntegrationEventVersions.V1));

    public async Task HandleAsync(ApprovalActionRecordedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        await consumerGuard.HandleAsync(integrationEvent, HandleValidEventAsync, cancellationToken);
    }

    [CapSubscribe(nameof(ApprovalActionRecordedIntegrationEvent), Group = ConsumerName)]
    public Task HandleCapAsync(ApprovalActionRecordedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        return HandleAsync(integrationEvent, cancellationToken);
    }

    private async Task HandleValidEventAsync(ApprovalActionRecordedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        var payload = integrationEvent.Payload
            ?? throw new KnownException("Approval action recorded payload is required.");
        var eventId = NotificationIntegrationEventRequired.Value(integrationEvent.EventId, "Integration event id is required.");
        var eventType = NotificationIntegrationEventRequired.Value(integrationEvent.EventType, "Integration event type is required.");
        var sourceService = NotificationIntegrationEventRequired.Value(integrationEvent.SourceService, "Integration event source service is required.");
        var organizationId = NotificationIntegrationEventRequired.Value(integrationEvent.OrganizationId, "Integration event organization is required.");
        var environmentId = NotificationIntegrationEventRequired.Value(integrationEvent.EnvironmentId, "Integration event environment is required.");
        var dedupeKey = NotificationIntegrationEventRequired.Value(integrationEvent.IdempotencyKey, "Integration event idempotency key is required.");
        var chainId = NotificationIntegrationEventRequired.Value(payload.ChainId, "Approval chain id is required.");
        var action = NotificationIntegrationEventRequired.Value(payload.Action, "Approval action is required.");
        var recipientRefs = payload.SuggestedRecipientRefs
            .Select(x => NotificationIntegrationEventRequired.Value(x, "Approval action recipient is required."))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (recipientRefs.Length == 0)
        {
            throw new KnownException("Approval action recipient is required.");
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

        var isTask = action is not "withdraw";
        var request = new SubmitNotificationIntentRequest(
            SourceService: sourceService,
            SourceEventType: eventType,
            SourceEventId: eventId,
            IntentType: isTask ? NotificationContractConstants.IntentTypeTask : NotificationContractConstants.IntentTypeMessage,
            Severity: NotificationContractConstants.SeverityInfo,
            DedupeKey: dedupeKey,
            Resource: new NotificationResourceRef("approval-chain", chainId, null),
            Title: $"Approval {action}",
            Summary: $"Approval action {action} was recorded for {payload.DocumentReference.DocumentType} {payload.DocumentReference.DocumentId}.",
            SuggestedRecipientRefs: recipientRefs);

        await sender.Send(new SubmitNotificationIntentCommand(organizationId, environmentId, request, timeProvider.GetUtcNow()), cancellationToken);
    }
}

file static class NotificationIntegrationEventRequired
{
    public static string Value(string? value, string message)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new KnownException(message);
        }

        return value;
    }

    public static string Actor(string actorType, string actorRef)
    {
        return $"{Value(actorType, "Actor type is required.")}:{Value(actorRef, "Actor ref is required.")}";
    }

    public static string[] MergeRecipients(string primaryRecipientRef, IReadOnlyCollection<string> escalationRecipientRefs)
    {
        return new[] { primaryRecipientRef }
            .Concat(escalationRecipientRefs)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }
}
