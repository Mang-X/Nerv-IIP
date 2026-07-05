using DotNetCore.CAP;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Nerv.IIP.Contracts.IndustrialTelemetry;
using Nerv.IIP.Contracts.Notification;
using Nerv.IIP.Messaging.CAP;
using Nerv.IIP.Notification.Infrastructure;
using Nerv.IIP.Notification.Web.Application.Commands.Notifications;
using NetCorePal.Extensions.DistributedTransactions;
using NetCorePal.Extensions.Primitives;
using System.Globalization;

namespace Nerv.IIP.Notification.Web.Application.IntegrationEventHandlers;

[IntegrationEventConsumer("Nerv.IIP.Contracts.IndustrialTelemetry.AlarmRaisedIntegrationEvent", ConsumerName)]
public sealed class AlarmRaisedIntegrationEventHandlerForNotification(
    ISender sender,
    ApplicationDbContext dbContext,
    IIntegrationEventDeadLetterStore deadLetterStore,
    IConfiguration configuration,
    TimeProvider timeProvider)
    : IIntegrationEventHandler<AlarmRaisedIntegrationEvent>, ICapSubscribe
{
    public const string ConsumerName = "notification.industrial-telemetry-alarm-raised";

    private readonly IntegrationEventConsumerGuard<AlarmRaisedIntegrationEvent> consumerGuard = new(
        new IntegrationEventEnvelopeValidator(),
        deadLetterStore,
        new IntegrationEventConsumerOptions(
            ConsumerName,
            IndustrialTelemetryIntegrationEventTypes.AlarmRaised,
            IndustrialTelemetryIntegrationEventVersions.V1));

    public async Task HandleAsync(
        AlarmRaisedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        await consumerGuard.HandleAsync(integrationEvent, HandleValidEventAsync, cancellationToken);
    }

    [CapSubscribe("Nerv.IIP.Contracts.IndustrialTelemetry.AlarmRaisedIntegrationEvent", Group = ConsumerName)]
    public Task HandleCapAsync(
        AlarmRaisedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        return HandleAsync(integrationEvent, cancellationToken);
    }

    private async Task HandleValidEventAsync(
        AlarmRaisedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        var payload = integrationEvent.Payload
            ?? throw new KnownException("Industrial telemetry alarm payload is required.");
        var eventId = IndustrialTelemetryAlarmNotification.Required(integrationEvent.EventId, "Integration event id is required.");
        var eventType = IndustrialTelemetryAlarmNotification.Required(integrationEvent.EventType, "Integration event type is required.");
        var sourceService = IndustrialTelemetryAlarmNotification.Required(integrationEvent.SourceService, "Integration event source service is required.");
        var organizationId = IndustrialTelemetryAlarmNotification.Required(integrationEvent.OrganizationId, "Integration event organization is required.");
        var environmentId = IndustrialTelemetryAlarmNotification.Required(integrationEvent.EnvironmentId, "Integration event environment is required.");
        IndustrialTelemetryAlarmNotification.Required(integrationEvent.IdempotencyKey, "Integration event idempotency key is required.");
        var externalAlarmId = IndustrialTelemetryAlarmNotification.Required(payload.ExternalAlarmId, "Industrial telemetry external alarm id is required.");
        var alarmEventId = IndustrialTelemetryAlarmNotification.Required(payload.AlarmEventId, "Industrial telemetry alarm event id is required.");
        var deviceAssetId = IndustrialTelemetryAlarmNotification.Required(payload.DeviceAssetId, "Industrial telemetry alarm device asset id is required.");
        var alarmCode = IndustrialTelemetryAlarmNotification.Required(payload.AlarmCode, "Industrial telemetry alarm code is required.");
        var severity = MapAlarmSeverity(payload.Priority, payload.Severity);
        var recipientRefs = IndustrialTelemetryAlarmNotification.GetRecipientRefs(configuration);

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
            DedupeKey: $"industrial-telemetry-alarm-raised:{externalAlarmId}:{alarmEventId}",
            Resource: new NotificationResourceRef("industrial-telemetry-alarm", externalAlarmId, null),
            Title: $"Industrial telemetry alarm raised: {alarmCode}",
            Summary: BuildRaisedSummary(payload, deviceAssetId, alarmCode),
            SuggestedRecipientRefs: recipientRefs);

        await sender.Send(new SubmitNotificationIntentCommand(organizationId, environmentId, request, timeProvider.GetUtcNow()), cancellationToken);
    }

    private static string BuildRaisedSummary(AlarmRaisedPayload payload, string deviceAssetId, string alarmCode)
    {
        var parts = new List<string>
        {
            $"Alarm {alarmCode} raised on device {deviceAssetId}",
        };

        if (!string.IsNullOrWhiteSpace(payload.TagKey))
        {
            parts.Add($"tag {payload.TagKey.Trim()}");
        }

        var unit = string.IsNullOrWhiteSpace(payload.UnitCode) ? string.Empty : $" {payload.UnitCode.Trim()}";
        if (payload.ObservedValue is not null)
        {
            parts.Add($"observed {FormatDecimal(payload.ObservedValue.Value)}{unit}");
        }

        if (payload.ThresholdValue is not null)
        {
            parts.Add($"threshold {FormatDecimal(payload.ThresholdValue.Value)}{unit}");
        }

        parts.Add($"raised at {payload.RaisedAtUtc:O}");
        return string.Join("; ", parts) + ".";
    }

    private static string FormatDecimal(decimal value)
    {
        return value.ToString("0.######", CultureInfo.InvariantCulture);
    }

    private static string MapAlarmSeverity(string? priority, string? severity)
    {
        var mappedPriority = TryMapAlarmSeverity(priority);
        if (mappedPriority is not null)
        {
            return mappedPriority;
        }

        var requiredSeverity = IndustrialTelemetryAlarmNotification.Required(severity, "Industrial telemetry alarm severity is required.");
        return TryMapAlarmSeverity(requiredSeverity) ?? NotificationContractConstants.SeverityWarning;
    }

    private static string? TryMapAlarmSeverity(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "critical" or "high" or "urgent" or "emergency" => NotificationContractConstants.SeverityCritical,
            "info" or "normal" => NotificationContractConstants.SeverityInfo,
            "warning" or "warn" or "medium" or "low" => NotificationContractConstants.SeverityWarning,
            _ => null
        };
    }
}

[IntegrationEventConsumer("Nerv.IIP.Contracts.IndustrialTelemetry.AlarmClearedIntegrationEvent", ConsumerName)]
public sealed class AlarmClearedIntegrationEventHandlerForNotification(
    ISender sender,
    ApplicationDbContext dbContext,
    IIntegrationEventDeadLetterStore deadLetterStore,
    IConfiguration configuration,
    TimeProvider timeProvider)
    : IIntegrationEventHandler<AlarmClearedIntegrationEvent>, ICapSubscribe
{
    public const string ConsumerName = "notification.industrial-telemetry-alarm-cleared";

    private readonly IntegrationEventConsumerGuard<AlarmClearedIntegrationEvent> consumerGuard = new(
        new IntegrationEventEnvelopeValidator(),
        deadLetterStore,
        new IntegrationEventConsumerOptions(
            ConsumerName,
            IndustrialTelemetryIntegrationEventTypes.AlarmCleared,
            IndustrialTelemetryIntegrationEventVersions.V1));

    public async Task HandleAsync(
        AlarmClearedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        await consumerGuard.HandleAsync(integrationEvent, HandleValidEventAsync, cancellationToken);
    }

    [CapSubscribe("Nerv.IIP.Contracts.IndustrialTelemetry.AlarmClearedIntegrationEvent", Group = ConsumerName)]
    public Task HandleCapAsync(
        AlarmClearedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        return HandleAsync(integrationEvent, cancellationToken);
    }

    private async Task HandleValidEventAsync(
        AlarmClearedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        var payload = integrationEvent.Payload
            ?? throw new KnownException("Industrial telemetry alarm payload is required.");
        var eventId = IndustrialTelemetryAlarmNotification.Required(integrationEvent.EventId, "Integration event id is required.");
        var eventType = IndustrialTelemetryAlarmNotification.Required(integrationEvent.EventType, "Integration event type is required.");
        var sourceService = IndustrialTelemetryAlarmNotification.Required(integrationEvent.SourceService, "Integration event source service is required.");
        var organizationId = IndustrialTelemetryAlarmNotification.Required(integrationEvent.OrganizationId, "Integration event organization is required.");
        var environmentId = IndustrialTelemetryAlarmNotification.Required(integrationEvent.EnvironmentId, "Integration event environment is required.");
        IndustrialTelemetryAlarmNotification.Required(integrationEvent.IdempotencyKey, "Integration event idempotency key is required.");
        var externalAlarmId = IndustrialTelemetryAlarmNotification.Required(payload.ExternalAlarmId, "Industrial telemetry external alarm id is required.");
        var alarmEventId = IndustrialTelemetryAlarmNotification.Required(payload.AlarmEventId, "Industrial telemetry alarm event id is required.");
        var deviceAssetId = IndustrialTelemetryAlarmNotification.Required(payload.DeviceAssetId, "Industrial telemetry alarm device asset id is required.");
        var alarmCode = IndustrialTelemetryAlarmNotification.Required(payload.AlarmCode, "Industrial telemetry alarm code is required.");
        var recipientRefs = IndustrialTelemetryAlarmNotification.GetRecipientRefs(configuration);

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
            DedupeKey: $"industrial-telemetry-alarm-cleared:{externalAlarmId}:{alarmEventId}",
            Resource: new NotificationResourceRef("industrial-telemetry-alarm", externalAlarmId, null),
            Title: $"Industrial telemetry alarm cleared: {alarmCode}",
            Summary: $"Alarm {alarmCode} on device {deviceAssetId} cleared at {payload.ClearedAtUtc:O}.",
            SuggestedRecipientRefs: recipientRefs);

        await sender.Send(new SubmitNotificationIntentCommand(organizationId, environmentId, request, timeProvider.GetUtcNow()), cancellationToken);
    }

}

internal static class IndustrialTelemetryAlarmNotification
{
    public static string Required(string? value, string message)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new KnownException(message);
        }

        return value.Trim();
    }

    public static string[] GetRecipientRefs(IConfiguration configuration)
    {
        var recipientRefs = configuration.GetSection("IndustrialTelemetry:AlarmNotification:RecipientRefs").Get<string[]>() ?? [];
        recipientRefs = recipientRefs
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return recipientRefs.Length == 0 ? ["role:maintenance"] : recipientRefs;
    }
}
