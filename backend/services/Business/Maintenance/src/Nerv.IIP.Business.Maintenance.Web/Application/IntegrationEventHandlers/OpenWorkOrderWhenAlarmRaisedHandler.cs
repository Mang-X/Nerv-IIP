using DotNetCore.CAP;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Maintenance.Infrastructure;
using Nerv.IIP.Business.Maintenance.Infrastructure.IntegrationEvents;
using Nerv.IIP.Business.Maintenance.Web.Application.Commands;
using Nerv.IIP.Contracts.IntegrationEvents;
using Nerv.IIP.Contracts.IndustrialTelemetry;
using Nerv.IIP.Messaging.CAP;
using NetCorePal.Extensions.DistributedTransactions;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Business.Maintenance.Web.Application.IntegrationEventHandlers;

[IntegrationEventConsumer("Nerv.IIP.Contracts.IndustrialTelemetry.AlarmRaisedIntegrationEvent", ConsumerName)]
public sealed class OpenWorkOrderWhenAlarmRaisedHandler(
    ISender sender,
    ApplicationDbContext dbContext,
    IIntegrationEventDeadLetterStore deadLetterStore)
    : IIntegrationEventHandler<AlarmRaisedIntegrationEvent>, ICapSubscribe
{
    public const string ConsumerName = "business-maintenance.alarm-raised";

    private readonly IntegrationEventConsumerGuard<AlarmRaisedIntegrationEvent> consumerGuard = new(
        new IntegrationEventEnvelopeValidator(),
        deadLetterStore,
        new IntegrationEventConsumerOptions(
            ConsumerName,
            IndustrialTelemetryIntegrationEventTypes.AlarmRaised,
            IndustrialTelemetryIntegrationEventVersions.V1));

    public async Task HandleAsync(AlarmRaisedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        await consumerGuard.HandleAsync(integrationEvent, HandleValidEventAsync, cancellationToken);
    }

    [CapSubscribe("Nerv.IIP.Contracts.IndustrialTelemetry.AlarmRaisedIntegrationEvent", Group = ConsumerName)]
    public Task HandleCapAsync(AlarmRaisedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        return HandleAsync(integrationEvent, cancellationToken);
    }

    private async Task HandleValidEventAsync(AlarmRaisedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        if (!await MaintenanceProcessedIntegrationEventInbox.TryRecordAsync(dbContext, ConsumerName, integrationEvent, cancellationToken))
        {
            return;
        }

        await sender.Send(
            new CreateMaintenanceWorkOrderCommand(
                integrationEvent.OrganizationId,
                integrationEvent.EnvironmentId,
                integrationEvent.Payload.DeviceAssetId,
                integrationEvent.Payload.Severity,
                integrationEvent.Payload.ExternalAlarmId,
                IndustrialTelemetryIntegrationEventSources.IndustrialTelemetry,
                integrationEvent.Payload.AlarmCode),
            cancellationToken);
    }
}

internal static class MaintenanceProcessedIntegrationEventInbox
{
    public static async Task<bool> TryRecordAsync(
        ApplicationDbContext dbContext,
        string consumerName,
        IIntegrationEventEnvelope integrationEvent,
        CancellationToken cancellationToken)
    {
        var eventId = Required(integrationEvent.EventId, "Integration event id is required.");
        var eventType = Required(integrationEvent.EventType, "Integration event type is required.");
        var sourceService = Required(integrationEvent.SourceService, "Integration event source service is required.");
        var dedupeKey = Required(integrationEvent.IdempotencyKey, "Integration event idempotency key is required.");

        if (dbContext.ProcessedIntegrationEvents.Local.Any(x => x.ConsumerName == consumerName && x.EventId == eventId))
        {
            return false;
        }

        if (await dbContext.ProcessedIntegrationEvents.AnyAsync(
            x => x.ConsumerName == consumerName && x.EventId == eventId,
            cancellationToken))
        {
            return false;
        }

        dbContext.ProcessedIntegrationEvents.Add(new ProcessedIntegrationEvent(
            consumerName,
            eventId,
            eventType,
            integrationEvent.EventVersion,
            sourceService,
            dedupeKey,
            DateTimeOffset.UtcNow));
        return true;
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
