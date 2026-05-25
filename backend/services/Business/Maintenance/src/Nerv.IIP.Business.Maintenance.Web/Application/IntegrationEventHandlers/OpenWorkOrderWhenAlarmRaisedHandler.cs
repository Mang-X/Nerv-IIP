using DotNetCore.CAP;
using Nerv.IIP.Business.Maintenance.Web.Application.Commands;
using Nerv.IIP.Contracts.IndustrialTelemetry;
using Nerv.IIP.Messaging.CAP;
using NetCorePal.Extensions.DistributedTransactions;

namespace Nerv.IIP.Business.Maintenance.Web.Application.IntegrationEventHandlers;

[IntegrationEventConsumer("Nerv.IIP.Contracts.IndustrialTelemetry.AlarmRaisedIntegrationEvent", ConsumerName)]
public sealed class OpenWorkOrderWhenAlarmRaisedHandler(
    ISender sender,
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
