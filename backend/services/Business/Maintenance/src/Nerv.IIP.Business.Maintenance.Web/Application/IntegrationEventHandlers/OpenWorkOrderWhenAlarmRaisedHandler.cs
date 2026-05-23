using Nerv.IIP.Business.Maintenance.Web.Application.Commands;
using Nerv.IIP.Contracts.IndustrialTelemetry;
using NetCorePal.Extensions.DistributedTransactions;

namespace Nerv.IIP.Business.Maintenance.Web.Application.IntegrationEventHandlers;

[IntegrationEventConsumer("Nerv.IIP.Contracts.IndustrialTelemetry.AlarmRaisedIntegrationEvent", ConsumerName)]
public sealed class OpenWorkOrderWhenAlarmRaisedHandler(ISender sender)
    : IIntegrationEventHandler<AlarmRaisedIntegrationEvent>
{
    public const string ConsumerName = "business-maintenance.alarm-raised";

    public async Task HandleAsync(AlarmRaisedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        await sender.Send(
            new CreateMaintenanceWorkOrderCommand(
                integrationEvent.OrganizationId,
                integrationEvent.EnvironmentId,
                integrationEvent.DeviceAssetId,
                integrationEvent.Severity,
                integrationEvent.ExternalAlarmId,
                IndustrialTelemetryIntegrationEventSources.IndustrialTelemetry,
                integrationEvent.AlarmCode),
            cancellationToken);
    }
}
