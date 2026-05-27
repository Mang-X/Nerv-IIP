using DotNetCore.CAP;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Contracts.IntegrationEvents;
using Nerv.IIP.Contracts.Notification;
using Nerv.IIP.Contracts.Ops;
using Nerv.IIP.Messaging.CAP;
using Nerv.IIP.Notification.Infrastructure;
using Nerv.IIP.Notification.Infrastructure.IntegrationEvents;
using Nerv.IIP.Notification.Web.Application.Commands.Notifications;
using NetCorePal.Extensions.DistributedTransactions;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Notification.Web.Application.IntegrationEventHandlers;

[IntegrationEventConsumer("Nerv.IIP.Contracts.Ops.OperationTaskCompletedIntegrationEvent", ConsumerName)]
public sealed class OperationTaskCompletedIntegrationEventHandlerForNotification(
    ISender sender,
    ApplicationDbContext dbContext,
    IIntegrationEventDeadLetterStore deadLetterStore)
    : IIntegrationEventHandler<OperationTaskCompletedIntegrationEvent>, ICapSubscribe
{
    public const string ConsumerName = "notification.operation-task-completed";
    private readonly IntegrationEventConsumerGuard<OperationTaskCompletedIntegrationEvent> consumerGuard = new(
        new IntegrationEventEnvelopeValidator(),
        deadLetterStore,
        new IntegrationEventConsumerOptions(ConsumerName, "ops.OperationTaskCompleted", 1));

    public async Task HandleAsync(OperationTaskCompletedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        await consumerGuard.HandleAsync(integrationEvent, HandleValidEventAsync, cancellationToken);
    }

    [CapSubscribe("Nerv.IIP.Contracts.Ops.OperationTaskCompletedIntegrationEvent", Group = ConsumerName)]
    public Task HandleCapAsync(OperationTaskCompletedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        return HandleAsync(integrationEvent, cancellationToken);
    }

    private async Task HandleValidEventAsync(OperationTaskCompletedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        var payload = integrationEvent.Payload ?? throw new KnownException("Operation task completed payload is required.");
        await OpsNotificationConsumer.SubmitOnceAsync(
            sender,
            dbContext,
            ConsumerName,
            integrationEvent,
            payload.OperationTaskId,
            NotificationContractConstants.IntentTypeMessage,
            NotificationContractConstants.SeverityInfo,
            "Operation completed",
            $"Operation {payload.OperationCode} completed for {payload.InstanceKey}.",
            cancellationToken);
    }
}

[IntegrationEventConsumer("Nerv.IIP.Contracts.Ops.OperationApprovalRequestedIntegrationEvent", ConsumerName)]
public sealed class OperationApprovalRequestedIntegrationEventHandlerForNotification(
    ISender sender,
    ApplicationDbContext dbContext,
    IIntegrationEventDeadLetterStore deadLetterStore)
    : IIntegrationEventHandler<OperationApprovalRequestedIntegrationEvent>, ICapSubscribe
{
    public const string ConsumerName = "notification.operation-approval-requested";
    private readonly IntegrationEventConsumerGuard<OperationApprovalRequestedIntegrationEvent> consumerGuard = new(
        new IntegrationEventEnvelopeValidator(),
        deadLetterStore,
        new IntegrationEventConsumerOptions(ConsumerName, "ops.OperationApprovalRequested", 1));

    public async Task HandleAsync(OperationApprovalRequestedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        await consumerGuard.HandleAsync(integrationEvent, HandleValidEventAsync, cancellationToken);
    }

    [CapSubscribe("Nerv.IIP.Contracts.Ops.OperationApprovalRequestedIntegrationEvent", Group = ConsumerName)]
    public Task HandleCapAsync(OperationApprovalRequestedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        return HandleAsync(integrationEvent, cancellationToken);
    }

    private async Task HandleValidEventAsync(OperationApprovalRequestedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        var payload = integrationEvent.Payload ?? throw new KnownException("Operation approval requested payload is required.");
        // TODO: Resolve approver recipients from ApprovalPolicy or OperationTemplate instead of the MVP ops-admin fallback.
        await OpsNotificationConsumer.SubmitOnceAsync(
            sender,
            dbContext,
            ConsumerName,
            integrationEvent,
            payload.OperationTaskId,
            NotificationContractConstants.IntentTypeTask,
            NotificationContractConstants.SeverityWarning,
            "Operation approval required",
            $"Operation {payload.OperationCode} for {payload.InstanceKey} requires approval.",
            cancellationToken);
    }
}

[IntegrationEventConsumer("Nerv.IIP.Contracts.Ops.OperationApprovalApprovedIntegrationEvent", ConsumerName)]
public sealed class OperationApprovalApprovedIntegrationEventHandlerForNotification(
    ISender sender,
    ApplicationDbContext dbContext,
    IIntegrationEventDeadLetterStore deadLetterStore)
    : IIntegrationEventHandler<OperationApprovalApprovedIntegrationEvent>, ICapSubscribe
{
    public const string ConsumerName = "notification.operation-approval-approved";
    private readonly IntegrationEventConsumerGuard<OperationApprovalApprovedIntegrationEvent> consumerGuard = new(
        new IntegrationEventEnvelopeValidator(),
        deadLetterStore,
        new IntegrationEventConsumerOptions(ConsumerName, "ops.OperationApprovalApproved", 1));

    public async Task HandleAsync(OperationApprovalApprovedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        await consumerGuard.HandleAsync(integrationEvent, HandleValidEventAsync, cancellationToken);
    }

    [CapSubscribe("Nerv.IIP.Contracts.Ops.OperationApprovalApprovedIntegrationEvent", Group = ConsumerName)]
    public Task HandleCapAsync(OperationApprovalApprovedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        return HandleAsync(integrationEvent, cancellationToken);
    }

    private async Task HandleValidEventAsync(OperationApprovalApprovedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        var payload = integrationEvent.Payload ?? throw new KnownException("Operation approval approved payload is required.");
        await OpsNotificationConsumer.SubmitOnceAsync(
            sender,
            dbContext,
            ConsumerName,
            integrationEvent,
            payload.OperationTaskId,
            NotificationContractConstants.IntentTypeMessage,
            NotificationContractConstants.SeverityInfo,
            "Operation approved",
            $"Operation {payload.OperationCode} for {payload.InstanceKey} was approved by {payload.DecidedBy}.",
            cancellationToken);
    }
}

[IntegrationEventConsumer("Nerv.IIP.Contracts.Ops.OperationApprovalRejectedIntegrationEvent", ConsumerName)]
public sealed class OperationApprovalRejectedIntegrationEventHandlerForNotification(
    ISender sender,
    ApplicationDbContext dbContext,
    IIntegrationEventDeadLetterStore deadLetterStore)
    : IIntegrationEventHandler<OperationApprovalRejectedIntegrationEvent>, ICapSubscribe
{
    public const string ConsumerName = "notification.operation-approval-rejected";
    private readonly IntegrationEventConsumerGuard<OperationApprovalRejectedIntegrationEvent> consumerGuard = new(
        new IntegrationEventEnvelopeValidator(),
        deadLetterStore,
        new IntegrationEventConsumerOptions(ConsumerName, "ops.OperationApprovalRejected", 1));

    public async Task HandleAsync(OperationApprovalRejectedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        await consumerGuard.HandleAsync(integrationEvent, HandleValidEventAsync, cancellationToken);
    }

    [CapSubscribe("Nerv.IIP.Contracts.Ops.OperationApprovalRejectedIntegrationEvent", Group = ConsumerName)]
    public Task HandleCapAsync(OperationApprovalRejectedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        return HandleAsync(integrationEvent, cancellationToken);
    }

    private async Task HandleValidEventAsync(OperationApprovalRejectedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        var payload = integrationEvent.Payload ?? throw new KnownException("Operation approval rejected payload is required.");
        await OpsNotificationConsumer.SubmitOnceAsync(
            sender,
            dbContext,
            ConsumerName,
            integrationEvent,
            payload.OperationTaskId,
            NotificationContractConstants.IntentTypeMessage,
            NotificationContractConstants.SeverityWarning,
            "Operation rejected",
            $"Operation {payload.OperationCode} for {payload.InstanceKey} was rejected by {payload.DecidedBy}.",
            cancellationToken);
    }
}

internal static class OpsNotificationConsumer
{
    private const string DefaultRecipientRef = "role:ops-admin";

    public static async Task SubmitOnceAsync(
        ISender sender,
        ApplicationDbContext dbContext,
        string consumerName,
        IIntegrationEventEnvelope integrationEvent,
        string operationTaskId,
        string intentType,
        string severity,
        string title,
        string summary,
        CancellationToken cancellationToken)
    {
        var eventId = Required(integrationEvent.EventId, "Integration event id is required.");
        var eventType = Required(integrationEvent.EventType, "Integration event type is required.");
        var sourceService = Required(integrationEvent.SourceService, "Integration event source service is required.");
        var organizationId = Required(integrationEvent.OrganizationId, "Integration event organization is required.");
        var environmentId = Required(integrationEvent.EnvironmentId, "Integration event environment is required.");
        var dedupeKey = Required(integrationEvent.IdempotencyKey, "Integration event idempotency key is required.");
        operationTaskId = Required(operationTaskId, "Operation task id is required.");

        if (await dbContext.ProcessedIntegrationEvents.AnyAsync(
            x => x.ConsumerName == consumerName && x.EventId == eventId,
            cancellationToken))
        {
            return;
        }

        dbContext.ProcessedIntegrationEvents.Add(new ProcessedIntegrationEvent(
            consumerName,
            eventId,
            eventType,
            integrationEvent.EventVersion,
            sourceService,
            dedupeKey,
            DateTimeOffset.UtcNow));

        var request = new SubmitNotificationIntentRequest(
            SourceService: sourceService,
            SourceEventType: eventType,
            SourceEventId: eventId,
            IntentType: intentType,
            Severity: severity,
            DedupeKey: dedupeKey,
            Resource: new NotificationResourceRef("operation-task", operationTaskId, null),
            Title: title,
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
