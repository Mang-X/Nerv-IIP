using DotNetCore.CAP;
using Nerv.IIP.Business.Maintenance.Infrastructure;
using Nerv.IIP.Business.Maintenance.Infrastructure.IntegrationEvents;
using Nerv.IIP.Business.Maintenance.Web.Application.Commands;
using Nerv.IIP.Contracts.IntegrationEvents;
using Nerv.IIP.Contracts.MasterData;
using Nerv.IIP.Messaging.CAP;
using NetCorePal.Extensions.DistributedTransactions;

namespace Nerv.IIP.Business.Maintenance.Web.Application.IntegrationEventHandlers;

[IntegrationEventConsumer("Nerv.IIP.Contracts.MasterData.DeviceAssetChangedIntegrationEvent", ConsumerName)]
public sealed class PauseMaintenancePlansWhenDeviceDisabledHandler(
    ISender sender,
    ApplicationDbContext dbContext,
    IIntegrationEventDeadLetterStore deadLetterStore)
    : IIntegrationEventHandler<DeviceAssetChangedIntegrationEvent>, ICapSubscribe
{
    public const string ConsumerName = "business-maintenance.device-asset-changed";

    private readonly IntegrationEventConsumerGuard<DeviceAssetChangedIntegrationEvent> consumerGuard = new(
        new IntegrationEventEnvelopeValidator(),
        deadLetterStore,
        new IntegrationEventConsumerOptions(
            ConsumerName,
            MasterDataIntegrationEventTypes.DeviceAssetChanged,
            MasterDataIntegrationEventVersions.V1));

    public Task HandleAsync(DeviceAssetChangedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        return consumerGuard.HandleAsync(integrationEvent, HandleValidEventAsync, cancellationToken);
    }

    [CapSubscribe(nameof(DeviceAssetChangedIntegrationEvent), Group = ConsumerName)]
    public Task HandleCapAsync(DeviceAssetChangedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        return HandleAsync(integrationEvent, cancellationToken);
    }

    private async Task HandleValidEventAsync(DeviceAssetChangedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        if (!string.Equals(integrationEvent.Payload.ResourceType, "device-asset", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var disabled = string.Equals(integrationEvent.Payload.Status, "disabled", StringComparison.OrdinalIgnoreCase);
        var active = string.Equals(integrationEvent.Payload.Status, "active", StringComparison.OrdinalIgnoreCase);
        if (!disabled && !active)
        {
            return;
        }

        if (!await MaintenanceProcessedIntegrationEventInbox.TryRecordAsync(dbContext, ConsumerName, integrationEvent, cancellationToken))
        {
            return;
        }

        await sender.Send(
            new ApplyMaintenanceDeviceStateCommand(
                integrationEvent.OrganizationId,
                integrationEvent.EnvironmentId,
                integrationEvent.Payload.Code,
                disabled,
                integrationEvent.Payload.ChangedAtUtc,
                integrationEvent.EventId),
            cancellationToken);
    }
}
