using DotNetCore.CAP;
using Nerv.IIP.Business.Wms.Web.Application.Commands;
using Nerv.IIP.Contracts.Inventory;
using Nerv.IIP.Messaging.CAP;
using NetCorePal.Extensions.DistributedTransactions;

namespace Nerv.IIP.Business.Wms.Web.Application.IntegrationEventHandlers;

[IntegrationEventConsumer("Nerv.IIP.Contracts.Inventory.StockCountApprovalSettledIntegrationEvent", ConsumerName)]
public sealed class StockCountApprovalSettledIntegrationEventHandler(
    ISender sender, IIntegrationEventDeadLetterStore deadLetterStore)
    : IIntegrationEventHandler<StockCountApprovalSettledIntegrationEvent>, ICapSubscribe
{
    public const string ConsumerName = "business-wms.stock-count-approval-settled";

    private readonly IntegrationEventConsumerGuard<StockCountApprovalSettledIntegrationEvent> guard = new(
        new IntegrationEventEnvelopeValidator(),
        deadLetterStore,
        new IntegrationEventConsumerOptions(ConsumerName, InventoryIntegrationEventTypes.StockCountApprovalSettled,
            InventoryIntegrationEventVersions.V1));

    [CapSubscribe(nameof(StockCountApprovalSettledIntegrationEvent), Group = ConsumerName)]
    public Task HandleAsync(StockCountApprovalSettledIntegrationEvent integrationEvent, CancellationToken cancellationToken) =>
        guard.HandleAsync(integrationEvent, ApplyAsync, cancellationToken);

    private async Task ApplyAsync(StockCountApprovalSettledIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        if (integrationEvent.SourceService != InventoryIntegrationEventSources.BusinessInventory
            || !integrationEvent.Payload.CountTaskKey.StartsWith(StockCountApprovalSettledPayload.WarehouseCountTaskKeyPrefix, StringComparison.Ordinal))
        {
            return;
        }

        await sender.Send(new SettleCountExecutionApprovalCommand(
            integrationEvent.OrganizationId, integrationEvent.EnvironmentId, integrationEvent.Payload), cancellationToken);
    }
}
