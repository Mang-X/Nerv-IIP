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
    private const string ErpFinishedGoodsQualityStatus = WmsReceivingQualityStatuses.Unrestricted;
    // 库存归属只按法律所有权区分（#3930）：可售库存无论来自采购收货还是完工入库都记在本公司名下，
    // 发货必须按同一口径预留；客供料/寄售由 customer/supplier 单独表达，不从 ERP 发货链路进来。
    private const string ErpDeliveryOwnerType = "company";

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

    [CapSubscribe(nameof(WmsOutboundOrderRequestedIntegrationEvent), Group = ConsumerName)]
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
        if (string.IsNullOrWhiteSpace(payload.SiteCode))
        {
            await deadLetterStore.AddAsync(
                IntegrationEventDeadLetterMessage.Create(
                    ConsumerName,
                    integrationEvent,
                    "missing-payload-field",
                    "ERP outbound order request must contain the authoritative fulfillment site code."),
                cancellationToken);
            return;
        }

        var siteCode = payload.SiteCode.Trim();
        await sender.Send(
            new CreateOutboundOrderCommand(
                integrationEvent.OrganizationId,
                integrationEvent.EnvironmentId,
                payload.DeliveryOrderNo,
                WmsSourceDocumentTypes.DeliveryOrder,
                payload.DeliveryOrderNo,
                siteCode,
                payload.Lines.Select(x => new WmsOutboundLineInput(
                    x.SourceLineNo,
                    x.SkuCode,
                    x.UomCode,
                    x.Quantity,
                    x.LocationCode,
                    x.LotNo,
                    null,
                    // ERP delivery currently ships the released (unrestricted) bucket.
                    // A future quality allocation policy must carry that dimension explicitly instead of falling back.
                    ErpFinishedGoodsQualityStatus,
                    ErpDeliveryOwnerType,
                    null)).ToArray()),
            cancellationToken);
    }
}
