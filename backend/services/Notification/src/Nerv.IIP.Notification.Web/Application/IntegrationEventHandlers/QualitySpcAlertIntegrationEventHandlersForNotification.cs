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

[IntegrationEventConsumer("Nerv.IIP.Contracts.Quality.SpcAlertRaisedIntegrationEvent", ConsumerName)]
public sealed class SpcAlertRaisedIntegrationEventHandlerForNotification(
    ISender sender,
    ApplicationDbContext dbContext,
    IIntegrationEventDeadLetterStore deadLetterStore,
    IConfiguration configuration,
    TimeProvider timeProvider)
    : IIntegrationEventHandler<SpcAlertRaisedIntegrationEvent>, ICapSubscribe
{
    public const string ConsumerName = "notification.quality-spc-alert-raised";

    private readonly IntegrationEventConsumerGuard<SpcAlertRaisedIntegrationEvent> consumerGuard = new(
        new IntegrationEventEnvelopeValidator(),
        deadLetterStore,
        new IntegrationEventConsumerOptions(
            ConsumerName,
            QualityIntegrationEventTypes.SpcAlertRaised,
            QualityIntegrationEventVersions.V1));

    public async Task HandleAsync(
        SpcAlertRaisedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        await consumerGuard.HandleAsync(integrationEvent, HandleValidEventAsync, cancellationToken);
    }

    [CapSubscribe(nameof(SpcAlertRaisedIntegrationEvent), Group = ConsumerName)]
    public Task HandleCapAsync(
        SpcAlertRaisedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        return HandleAsync(integrationEvent, cancellationToken);
    }

    private async Task HandleValidEventAsync(
        SpcAlertRaisedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        var payload = integrationEvent.Payload;
        if (!TryRequired(payload.AlertKey, out var alertKey)
            || !TryRequired(payload.SkuCode, out var skuCode)
            || !TryRequired(payload.CharacteristicCode, out var characteristicCode)
            || !TryRequired(payload.WorkCenterId, out var workCenterId))
        {
            await deadLetterStore.AddAsync(
                IntegrationEventDeadLetterMessage.Create(
                    ConsumerName,
                    integrationEvent,
                    "invalid-payload",
                    "Quality SPC alert payload is missing alert key, SKU, characteristic, or work center."),
                cancellationToken);
            return;
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

        var ruleSummary = payload.RuleCodes.Count == 0
            ? "SPC rule violation"
            : string.Join(", ", payload.RuleCodes);
        var request = new SubmitNotificationIntentRequest(
            SourceService: integrationEvent.SourceService,
            SourceEventType: integrationEvent.EventType,
            SourceEventId: integrationEvent.EventId,
            IntentType: NotificationContractConstants.IntentTypeTask,
            Severity: NormalizeSeverity(payload.Severity),
            DedupeKey: integrationEvent.IdempotencyKey,
            Resource: new NotificationResourceRef(payload.ResourceType, alertKey, null),
            Title: $"Quality SPC alert: {skuCode}/{characteristicCode}",
            Summary: string.IsNullOrWhiteSpace(payload.Summary)
                ? $"Quality SPC alert for {skuCode}/{characteristicCode} at work center {workCenterId}: {ruleSummary}."
                : $"{payload.Summary} Rules: {ruleSummary}.",
            SuggestedRecipientRefs: GetRecipientRefs(configuration));

        await sender.Send(new SubmitNotificationIntentCommand(integrationEvent.OrganizationId, integrationEvent.EnvironmentId, request, timeProvider.GetUtcNow()), cancellationToken);
    }

    private static string NormalizeSeverity(string severity)
    {
        return severity.Trim().ToLowerInvariant() switch
        {
            NotificationContractConstants.SeverityCritical => NotificationContractConstants.SeverityCritical,
            NotificationContractConstants.SeverityInfo => NotificationContractConstants.SeverityInfo,
            _ => NotificationContractConstants.SeverityWarning,
        };
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
        var recipientRefs = configuration.GetSection("Quality:SpcAlert:RecipientRefs").Get<string[]>() ?? [];
        recipientRefs = recipientRefs
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return recipientRefs.Length == 0 ? ["role:quality-engineer"] : recipientRefs;
    }
}
