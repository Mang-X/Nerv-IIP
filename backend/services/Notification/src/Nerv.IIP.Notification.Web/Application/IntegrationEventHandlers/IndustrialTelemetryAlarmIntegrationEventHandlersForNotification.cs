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
        var eventId = Required(integrationEvent.EventId, "Integration event id is required.");
        var eventType = Required(integrationEvent.EventType, "Integration event type is required.");
        var sourceService = Required(integrationEvent.SourceService, "Integration event source service is required.");
        var organizationId = Required(integrationEvent.OrganizationId, "Integration event organization is required.");
        var environmentId = Required(integrationEvent.EnvironmentId, "Integration event environment is required.");
        Required(integrationEvent.IdempotencyKey, "Integration event idempotency key is required.");
        var externalAlarmId = Required(payload.ExternalAlarmId, "Industrial telemetry external alarm id is required.");
        var deviceAssetId = Required(payload.DeviceAssetId, "Industrial telemetry alarm device asset id is required.");
        var alarmCode = Required(payload.AlarmCode, "Industrial telemetry alarm code is required.");
        var severity = MapAlarmSeverity(string.IsNullOrWhiteSpace(payload.Priority) ? payload.Severity : payload.Priority);
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
            Severity: severity,
            DedupeKey: $"industrial-telemetry-alarm-raised:{externalAlarmId}",
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

    private static string Required(string? value, string message)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new KnownException(message);
        }

        return value.Trim();
    }

    private static string MapAlarmSeverity(string? value)
    {
        return Required(value, "Industrial telemetry alarm severity is required.").ToLowerInvariant() switch
        {
            "critical" or "high" or "urgent" or "emergency" or "p0" => NotificationContractConstants.SeverityCritical,
            "info" or "normal" => NotificationContractConstants.SeverityInfo,
            _ => NotificationContractConstants.SeverityWarning
        };
    }

    private static string[] GetRecipientRefs(IConfiguration configuration)
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
        var eventId = Required(integrationEvent.EventId, "Integration event id is required.");
        var eventType = Required(integrationEvent.EventType, "Integration event type is required.");
        var sourceService = Required(integrationEvent.SourceService, "Integration event source service is required.");
        var organizationId = Required(integrationEvent.OrganizationId, "Integration event organization is required.");
        var environmentId = Required(integrationEvent.EnvironmentId, "Integration event environment is required.");
        Required(integrationEvent.IdempotencyKey, "Integration event idempotency key is required.");
        var externalAlarmId = Required(payload.ExternalAlarmId, "Industrial telemetry external alarm id is required.");
        var deviceAssetId = Required(payload.DeviceAssetId, "Industrial telemetry alarm device asset id is required.");
        var alarmCode = Required(payload.AlarmCode, "Industrial telemetry alarm code is required.");
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
            IntentType: NotificationContractConstants.IntentTypeMessage,
            Severity: NotificationContractConstants.SeverityInfo,
            DedupeKey: $"industrial-telemetry-alarm-cleared:{externalAlarmId}",
            Resource: new NotificationResourceRef("industrial-telemetry-alarm", externalAlarmId, null),
            Title: $"Industrial telemetry alarm cleared: {alarmCode}",
            Summary: $"Alarm {alarmCode} on device {deviceAssetId} cleared at {payload.ClearedAtUtc:O}.",
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
        var recipientRefs = configuration.GetSection("IndustrialTelemetry:AlarmNotification:RecipientRefs").Get<string[]>() ?? [];
        recipientRefs = recipientRefs
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return recipientRefs.Length == 0 ? ["role:maintenance"] : recipientRefs;
    }
}
