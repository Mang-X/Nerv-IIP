using DotNetCore.CAP;
using Nerv.IIP.Business.Wms.Web.Application.Commands;
using Nerv.IIP.Contracts.Wms;
using Nerv.IIP.Messaging.CAP;
using NetCorePal.Extensions.DistributedTransactions;

namespace Nerv.IIP.Business.Wms.Web.Application.IntegrationEventHandlers;

[IntegrationEventConsumer("Nerv.IIP.Contracts.Wms.WmsOutboundOrderRequestedIntegrationEvent", ConsumerName)]
public sealed class WmsOutboundOrderRequestedIntegrationEventHandler(
    ISender sender,
    IIntegrationEventDeadLetterStore deadLetterStore)
    : IIntegrationEventHandler<WmsOutboundOrderRequestedIntegrationEvent>, ICapSubscribe
{
    public const string ConsumerName = "business-wms.outbound-order-requested";

    private readonly IntegrationEventConsumerGuard<WmsOutboundOrderRequestedIntegrationEvent> consumerGuard = new(
        new IntegrationEventEnvelopeValidator(),
        deadLetterStore,
        new IntegrationEventConsumerOptions(
            ConsumerName,
            WmsIntegrationEventTypes.OutboundOrderRequested,
            WmsIntegrationEventVersions.V1));

    public async Task HandleAsync(WmsOutboundOrderRequestedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        await consumerGuard.HandleAsync(integrationEvent, HandleValidEventAsync, cancellationToken);
    }

    [CapSubscribe("Nerv.IIP.Contracts.Wms.WmsOutboundOrderRequestedIntegrationEvent", Group = ConsumerName)]
    public Task HandleCapAsync(WmsOutboundOrderRequestedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        return HandleAsync(integrationEvent, cancellationToken);
    }

    private async Task HandleValidEventAsync(WmsOutboundOrderRequestedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        if (!string.Equals(integrationEvent.SourceService, WmsIntegrationEventSources.BusinessErp, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var payload = integrationEvent.Payload;
        var siteCode = string.IsNullOrWhiteSpace(payload.SiteCode) ? "default" : payload.SiteCode.Trim();
        await sender.Send(
            new CreateOutboundOrderCommand(
                integrationEvent.OrganizationId,
                integrationEvent.EnvironmentId,
                payload.DeliveryOrderNo,
                "erp-delivery-order",
                payload.SalesOrderNo,
                siteCode,
                payload.Lines.Select(x => new WmsOutboundLineInput(
                    x.SourceLineNo,
                    x.SkuCode,
                    x.UomCode,
                    x.Quantity,
                    x.LocationCode,
                    x.LotNo,
                    null,
                    "qualified",
                    "company",
                    payload.CustomerCode)).ToArray()),
            cancellationToken);
    }
}
