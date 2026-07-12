using DotNetCore.CAP;
using MediatR;
using Microsoft.Extensions.Configuration;
using Nerv.IIP.Contracts.AppHubQueries;
using Nerv.IIP.Contracts.Notification;
using Nerv.IIP.Messaging.CAP;
using Nerv.IIP.Notification.Infrastructure;
using Nerv.IIP.Notification.Web.Application.Commands.Notifications;
using NetCorePal.Extensions.DistributedTransactions;

namespace Nerv.IIP.Notification.Web.Application.IntegrationEventHandlers;

[IntegrationEventConsumer("Nerv.IIP.Contracts.AppHubQueries.ConnectorHostUnreachableIntegrationEvent", ConsumerName)]
public sealed class ConnectorHostUnreachableIntegrationEventHandlerForNotification(
    ISender sender,
    ApplicationDbContext dbContext,
    IIntegrationEventDeadLetterStore deadLetterStore,
    IConfiguration configuration,
    TimeProvider timeProvider)
    : IIntegrationEventHandler<ConnectorHostUnreachableIntegrationEvent>, ICapSubscribe
{
    public const string ConsumerName = "notification.apphub-connector-host-unreachable";

    private readonly IntegrationEventConsumerGuard<ConnectorHostUnreachableIntegrationEvent> consumerGuard = new(
        new IntegrationEventEnvelopeValidator(),
        deadLetterStore,
        new IntegrationEventConsumerOptions(
            ConsumerName,
            AppHubIntegrationEventTypes.ConnectorHostUnreachable,
            AppHubIntegrationEventVersions.V1));

    public async Task HandleAsync(
        ConnectorHostUnreachableIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        await consumerGuard.HandleAsync(integrationEvent, HandleValidEventAsync, cancellationToken);
    }

    [CapSubscribe(nameof(ConnectorHostUnreachableIntegrationEvent), Group = ConsumerName)]
    public Task HandleCapAsync(
        ConnectorHostUnreachableIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        return HandleAsync(integrationEvent, cancellationToken);
    }

    private async Task HandleValidEventAsync(
        ConnectorHostUnreachableIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        var payload = integrationEvent.Payload;
        if (payload is null
            || !AppHubConnectorHostNotification.TryRequired(payload.ConnectorHostId, out var connectorHostId)
            || !AppHubConnectorHostNotification.TryRequired(payload.InstanceKey, out var instanceKey))
        {
            await deadLetterStore.AddAsync(
                IntegrationEventDeadLetterMessage.Create(
                    ConsumerName,
                    integrationEvent,
                    "invalid-payload",
                    "AppHub connector host unreachable payload is missing required connector host or instance fields."),
                cancellationToken);
            return;
        }

        var recipientRefs = AppHubConnectorHostNotification.GetRecipientRefs(configuration);

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
            Severity: NotificationContractConstants.SeverityCritical,
            DedupeKey: integrationEvent.IdempotencyKey,
            Resource: new NotificationResourceRef("connector-host", connectorHostId, null),
            Title: $"Connector Host unreachable: {connectorHostId}",
            Summary: $"Connector Host {connectorHostId} for instance {instanceKey} is unreachable; last heartbeat {payload.LastHeartbeatAtUtc:O}, detected at {payload.DetectedAtUtc:O}, timeout {payload.HeartbeatTimeoutSeconds}s.",
            SuggestedRecipientRefs: recipientRefs);

        await sender.Send(new SubmitNotificationIntentCommand(integrationEvent.OrganizationId, integrationEvent.EnvironmentId, request, timeProvider.GetUtcNow()), cancellationToken);
    }
}

[IntegrationEventConsumer("Nerv.IIP.Contracts.AppHubQueries.ConnectorHostRestoredIntegrationEvent", ConsumerName)]
public sealed class ConnectorHostRestoredIntegrationEventHandlerForNotification(
    ISender sender,
    ApplicationDbContext dbContext,
    IIntegrationEventDeadLetterStore deadLetterStore,
    IConfiguration configuration,
    TimeProvider timeProvider)
    : IIntegrationEventHandler<ConnectorHostRestoredIntegrationEvent>, ICapSubscribe
{
    public const string ConsumerName = "notification.apphub-connector-host-restored";

    private readonly IntegrationEventConsumerGuard<ConnectorHostRestoredIntegrationEvent> consumerGuard = new(
        new IntegrationEventEnvelopeValidator(),
        deadLetterStore,
        new IntegrationEventConsumerOptions(
            ConsumerName,
            AppHubIntegrationEventTypes.ConnectorHostRestored,
            AppHubIntegrationEventVersions.V1));

    public async Task HandleAsync(
        ConnectorHostRestoredIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        await consumerGuard.HandleAsync(integrationEvent, HandleValidEventAsync, cancellationToken);
    }

    [CapSubscribe(nameof(ConnectorHostRestoredIntegrationEvent), Group = ConsumerName)]
    public Task HandleCapAsync(
        ConnectorHostRestoredIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        return HandleAsync(integrationEvent, cancellationToken);
    }

    private async Task HandleValidEventAsync(
        ConnectorHostRestoredIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        var payload = integrationEvent.Payload;
        if (payload is null
            || !AppHubConnectorHostNotification.TryRequired(payload.ConnectorHostId, out var connectorHostId)
            || !AppHubConnectorHostNotification.TryRequired(payload.InstanceKey, out var instanceKey))
        {
            await deadLetterStore.AddAsync(
                IntegrationEventDeadLetterMessage.Create(
                    ConsumerName,
                    integrationEvent,
                    "invalid-payload",
                    "AppHub connector host restored payload is missing required connector host or instance fields."),
                cancellationToken);
            return;
        }

        var recipientRefs = AppHubConnectorHostNotification.GetRecipientRefs(configuration);

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
            IntentType: NotificationContractConstants.IntentTypeMessage,
            Severity: NotificationContractConstants.SeverityInfo,
            DedupeKey: integrationEvent.IdempotencyKey,
            Resource: new NotificationResourceRef("connector-host", connectorHostId, null),
            Title: $"Connector Host restored: {connectorHostId}",
            Summary: $"Connector Host {connectorHostId} for instance {instanceKey} restored at {payload.RestoredAtUtc:O}.",
            SuggestedRecipientRefs: recipientRefs);

        await sender.Send(new SubmitNotificationIntentCommand(integrationEvent.OrganizationId, integrationEvent.EnvironmentId, request, timeProvider.GetUtcNow()), cancellationToken);
    }
}

internal static class AppHubConnectorHostNotification
{
    public static bool TryRequired(string? value, out string normalized)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            normalized = string.Empty;
            return false;
        }

        normalized = value.Trim();
        return true;
    }

    public static string[] GetRecipientRefs(IConfiguration configuration)
    {
        var recipientRefs = configuration.GetSection("AppHub:ConnectorHostNotification:RecipientRefs").Get<string[]>() ?? [];
        recipientRefs = recipientRefs
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return recipientRefs.Length == 0 ? ["role:ops-admin"] : recipientRefs;
    }
}
