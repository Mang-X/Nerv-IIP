using DotNetCore.CAP;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.OutboundOrderAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.InboundOrderAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.SupplierReturnAggregate;
using Nerv.IIP.Contracts.Quality;
using Nerv.IIP.Contracts.Wms;
using Nerv.IIP.Messaging.CAP;
using NetCorePal.Extensions.DistributedTransactions;

namespace Nerv.IIP.Business.Wms.Web.Application.IntegrationEventHandlers;

[IntegrationEventConsumer("Nerv.IIP.Contracts.Quality.InspectionResultIntegrationEvent", ConsumerName)]
public sealed class QualityInspectionResultIntegrationEventHandlerForReleaseWmsInboundGate(
    ApplicationDbContext dbContext,
    IIntegrationEventDeadLetterStore deadLetterStore)
    : IIntegrationEventHandler<InspectionResultIntegrationEvent>, ICapSubscribe
{
    public const string ConsumerName = "business-wms.quality-inspection-result";

    private readonly IntegrationEventConsumerGuard<InspectionResultIntegrationEvent> consumerGuard = new(
        new IntegrationEventEnvelopeValidator(),
        deadLetterStore,
        new IntegrationEventConsumerOptions(
            ConsumerName,
            [
                QualityIntegrationEventTypes.InspectionPassed,
                QualityIntegrationEventTypes.InspectionConditionalReleased,
                QualityIntegrationEventTypes.InspectionRejected
            ],
            QualityIntegrationEventVersions.V1));

    public async Task HandleAsync(InspectionResultIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        await consumerGuard.HandleAsync(integrationEvent, (ev, token) => HandleValidEventAsync(ev, false, token), cancellationToken);
    }

    [CapSubscribe(nameof(InspectionResultIntegrationEvent), Group = ConsumerName)]
    public Task HandleCapAsync(InspectionResultIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        return HandleAsync(integrationEvent, cancellationToken);
    }

    internal Task ReplayAsync(InspectionResultIntegrationEvent integrationEvent, CancellationToken cancellationToken) =>
        consumerGuard.HandleAsync(integrationEvent, (ev, token) => HandleValidEventAsync(ev, true, token), cancellationToken);

    private async Task HandleValidEventAsync(InspectionResultIntegrationEvent integrationEvent, bool replay, CancellationToken cancellationToken)
    {
        var payload = integrationEvent.Payload;
        if (!string.Equals(payload.SourceService, QualityInspectionSourceTypes.Wms, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(payload.SourceType, QualityInspectionSourceTypes.Receiving, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var inbox = await dbContext.ProcessedIntegrationEvents.SingleOrDefaultAsync(
            x => x.ConsumerName == ConsumerName && x.IdempotencyKey == integrationEvent.IdempotencyKey, cancellationToken);
        if (inbox is not null && (!replay || inbox.EventId != integrationEvent.EventId
            || inbox.EventType != integrationEvent.EventType || inbox.EventVersion != integrationEvent.EventVersion
            || inbox.SourceService != integrationEvent.SourceService))
        {
            return;
        }

        var inbound = await dbContext.InboundOrders
            .Include(x => x.Lines)
            .SingleOrDefaultAsync(
                x => x.OrganizationId == integrationEvent.OrganizationId
                    && x.EnvironmentId == integrationEvent.EnvironmentId
                    && x.InboundOrderNo == payload.SourceDocumentId,
                cancellationToken);
        if (inbound is null)
        {
            await DeadLetterAsync(
                integrationEvent,
                "missing-inbound-order",
                $"WMS inbound order '{payload.SourceDocumentId}' was not found for quality inspection result '{payload.InspectionRecordId}'.",
                cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        // Historical rejection could have committed inbox before the business fact. Keep that
        // identity: only the selected replay may resume its still-uninspected original receipt line.
        if (inbox is not null)
        {
            var matches = inbound.Lines.Where(x => x.SkuCode == WmsText.Required(payload.SkuCode, nameof(payload.SkuCode))
                && x.LotNo == WmsText.Optional(payload.StockRelease?.LotNo ?? payload.LotNo)
                && x.SerialNo == WmsText.Optional(payload.StockRelease?.SerialNo ?? payload.SerialNo)).ToArray();
            if (inbound.Status != InboundOrderStatus.PendingQualityCheck
                || matches.Length != 1 || matches[0].InspectionRecordId is not null
                || matches[0].QualityGateStatus != InboundQualityGateStatuses.Pending)
                return;
        }

        try
        {
            var lotNo = payload.StockRelease?.LotNo ?? payload.LotNo;
            var serialNo = payload.StockRelease?.SerialNo ?? payload.SerialNo;
            var supplierReturn = inbound.ApplyInspectionResult(
                integrationEvent.EventType,
                payload.InspectionRecordId,
                payload.SkuCode,
                lotNo,
                serialNo,
                payload.InspectedQuantity,
                payload.DispositionReason);
            if (supplierReturn is not null
                && !await SupplierReturnExistsAsync(supplierReturn.SupplierReturnNo, integrationEvent, cancellationToken))
            {
                dbContext.SupplierReturnRequests.Add(supplierReturn);
                var sourceDocumentId = string.Equals(inbound.SourceDocumentType, WmsSourceDocumentTypes.PurchaseReceipt, StringComparison.OrdinalIgnoreCase)
                    ? inbound.SourceDocumentId
                    : inbound.InboundOrderNo;
                dbContext.OutboundOrders.Add(CreateSupplierReturnOutboundOrder(supplierReturn, sourceDocumentId));
            }
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            await DeadLetterDivergenceAsync(integrationEvent, exception, cancellationToken);
            return;
        }

        if (inbox is null)
            await WmsProcessedIntegrationEventInbox.TryRecordAsync(dbContext, ConsumerName, integrationEvent, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private Task<bool> SupplierReturnExistsAsync(
        string supplierReturnNo,
        InspectionResultIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        return dbContext.SupplierReturnRequests.AnyAsync(
            x => x.OrganizationId == integrationEvent.OrganizationId
                && x.EnvironmentId == integrationEvent.EnvironmentId
                && x.SupplierReturnNo == supplierReturnNo,
            cancellationToken);
    }

    private static OutboundOrder CreateSupplierReturnOutboundOrder(SupplierReturnRequest supplierReturn, string sourceDocumentId)
    {
        return OutboundOrder.Create(
            supplierReturn.OrganizationId,
            supplierReturn.EnvironmentId,
            supplierReturn.SupplierReturnNo,
            WmsSourceDocumentTypes.PurchaseReceiptReturn,
            sourceDocumentId,
            supplierReturn.SiteCode,
            [new OutboundOrderLineDraft(
                supplierReturn.InboundOrderLineNo,
                supplierReturn.SkuCode,
                supplierReturn.UomCode,
                supplierReturn.Quantity,
                supplierReturn.LocationCode,
                supplierReturn.LotNo,
                supplierReturn.SerialNo,
                WmsReceivingQualityStatuses.Quality,
                supplierReturn.OwnerType,
                supplierReturn.OwnerId)]);
    }

    private Task DeadLetterAsync(
        InspectionResultIntegrationEvent integrationEvent,
        string failureCode,
        string failureMessage,
        CancellationToken cancellationToken)
    {
        return deadLetterStore.AddAsync(
            IntegrationEventDeadLetterMessage.Create(
                ConsumerName,
                integrationEvent,
                failureCode,
                failureMessage),
            cancellationToken);
    }

    private Task DeadLetterDivergenceAsync(
        InspectionResultIntegrationEvent integrationEvent,
        Exception exception,
        CancellationToken cancellationToken)
    {
        return DeadLetterAsync(
            integrationEvent,
            "quality-inspection-result-divergence",
            exception.Message,
            cancellationToken);
    }
}
