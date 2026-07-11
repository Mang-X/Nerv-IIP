using DotNetCore.CAP;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.PurchaseOrderAggregate;
using Nerv.IIP.Business.Erp.Infrastructure;
using Nerv.IIP.Business.Erp.Web.Application.Commands;
using Nerv.IIP.Business.Erp.Web.Application.Commands.Procurement;
using Nerv.IIP.Business.Erp.Web.Application.IntegrationEventConverters;
using Nerv.IIP.Contracts.IntegrationEvents;
using Nerv.IIP.Contracts.Wms;
using Nerv.IIP.Messaging.CAP;
using NetCorePal.Extensions.DistributedTransactions;

namespace Nerv.IIP.Business.Erp.Web.Application.IntegrationEventHandlers;

[IntegrationEventConsumer("Nerv.IIP.Contracts.Wms.WmsIntegrationEvent", ConsumerName)]
public sealed class WmsInboundOrderCompletedIntegrationEventHandlerForRecordPurchaseReceipt(
    ApplicationDbContext dbContext,
    IIntegrationEventDeadLetterStore deadLetterStore,
    ErpCodingService codingService,
    ILogger<WmsInboundOrderCompletedIntegrationEventHandlerForRecordPurchaseReceipt> logger)
    : IIntegrationEventHandler<WmsIntegrationEvent>, ICapSubscribe
{
    public const string ConsumerName = "business-erp.wms-inbound-completed-purchase-receipt";

    private static readonly IntegrationEventConsumerOptions ConsumerOptions = new(
        ConsumerName,
        WmsIntegrationEventTypes.InboundOrderCompleted,
        WmsIntegrationEventVersions.V1);

    private readonly IntegrationEventConsumerGuard<WmsIntegrationEvent> consumerGuard = new(
        new IntegrationEventEnvelopeValidator(),
        deadLetterStore,
        ConsumerOptions);

    public Task HandleAsync(
        WmsIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        return consumerGuard.HandleAsync(integrationEvent, HandleValidEventAsync, cancellationToken);
    }

    [CapSubscribe(nameof(WmsIntegrationEvent), Group = ConsumerName)]
    public Task HandleCapAsync(
        WmsIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        return HandleAsync(integrationEvent, cancellationToken);
    }

    private async Task HandleValidEventAsync(
        WmsIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(integrationEvent.SourceService, WmsIntegrationEventSources.BusinessWms, StringComparison.OrdinalIgnoreCase))
        {
            await DeadLetterAsync(
                integrationEvent,
                "unexpected-source-service",
                $"Integration event source service '{integrationEvent.SourceService}' is not supported by consumer '{ConsumerName}'.",
                cancellationToken);
            return;
        }

        if (string.IsNullOrWhiteSpace(integrationEvent.Payload.PublicReference))
        {
            await DeadLetterAsync(
                integrationEvent,
                "missing-payload-field",
                "WMS inbound completion payload must include PublicReference.",
                cancellationToken);
            return;
        }

        if (string.IsNullOrWhiteSpace(integrationEvent.Payload.SourceDocumentType))
        {
            await DeadLetterAsync(
                integrationEvent,
                "missing-payload-field",
                "WMS inbound completion payload must include SourceDocumentType.",
                cancellationToken);
            return;
        }

        if (IsErpPurchaseReceiptSource(integrationEvent.Payload.SourceDocumentType))
        {
            logger.LogDebug(
                "Ignoring WMS inbound completion {PublicReference} because source document type {SourceDocumentType} is already an ERP purchase receipt.",
                integrationEvent.Payload.PublicReference,
                integrationEvent.Payload.SourceDocumentType);
            return;
        }

        if (!IsPurchaseOrderSource(integrationEvent.Payload.SourceDocumentType))
        {
            logger.LogDebug(
                "Ignoring WMS inbound completion {PublicReference} with unsupported source document type {SourceDocumentType}.",
                integrationEvent.Payload.PublicReference,
                integrationEvent.Payload.SourceDocumentType);
            return;
        }

        if (string.IsNullOrWhiteSpace(integrationEvent.Payload.SourceDocumentId))
        {
            await DeadLetterAsync(
                integrationEvent,
                "missing-payload-field",
                "WMS purchase-order inbound completion payload must include SourceDocumentId.",
                cancellationToken);
            return;
        }

        var purchaseOrderNo = integrationEvent.Payload.SourceDocumentId.Trim();
        var order = await dbContext.PurchaseOrders
            .Include(x => x.Lines)
            .SingleOrDefaultAsync(x =>
                x.OrganizationId == integrationEvent.OrganizationId
                && x.EnvironmentId == integrationEvent.EnvironmentId
                && x.PurchaseOrderNo == purchaseOrderNo,
                cancellationToken);
        if (order is null)
        {
            await DeadLetterAsync(
                integrationEvent,
                "missing-source-facts",
                $"Purchase order '{purchaseOrderNo}' was not found for WMS inbound completion '{integrationEvent.Payload.PublicReference}'.",
                cancellationToken);
            return;
        }

        var purchaseReceiptNo = integrationEvent.Payload.PublicReference.Trim();
        if (await dbContext.PurchaseReceipts.AnyAsync(x =>
            x.OrganizationId == integrationEvent.OrganizationId
            && x.EnvironmentId == integrationEvent.EnvironmentId
            && x.PurchaseReceiptNo == purchaseReceiptNo,
            cancellationToken))
        {
            return;
        }

        var decision = TryBuildReceiptLines(
            integrationEvent,
            order,
            out var receiptLines,
            out var failureCode,
            out var failureMessage);
        if (decision == ReceiptProjectionDecision.Failed)
        {
            await DeadLetterAsync(
                integrationEvent,
                failureCode,
                failureMessage,
                cancellationToken);
            return;
        }

        if (!await ErpProcessedIntegrationEventInbox.TryRecordAsync(dbContext, ConsumerName, integrationEvent, cancellationToken))
        {
            return;
        }

        if (await dbContext.PurchaseReceipts.AnyAsync(x =>
            x.OrganizationId == integrationEvent.OrganizationId
            && x.EnvironmentId == integrationEvent.EnvironmentId
            && x.PurchaseReceiptNo == purchaseReceiptNo,
            cancellationToken))
        {
            return;
        }

        try
        {
            await new RecordPurchaseReceiptCommandHandler(dbContext, codingService).Handle(
                new RecordPurchaseReceiptCommand(
                    integrationEvent.OrganizationId,
                    integrationEvent.EnvironmentId,
                    purchaseReceiptNo,
                    purchaseOrderNo,
                    receiptLines,
                    $"{ConsumerName}:{integrationEvent.IdempotencyKey}"),
                cancellationToken);
        }
        catch (Exception exception) when (exception is KnownException or ArgumentException or InvalidOperationException)
        {
            await DeadLetterAsync(
                integrationEvent,
                "receipt-recording-rejected",
                exception.Message,
                cancellationToken);
        }
    }

    private static ReceiptProjectionDecision TryBuildReceiptLines(
        WmsIntegrationEvent integrationEvent,
        PurchaseOrder order,
        out IReadOnlyCollection<PurchaseReceiptCommandLine> receiptLines,
        out string failureCode,
        out string failureMessage)
    {
        receiptLines = [];
        failureCode = string.Empty;
        failureMessage = string.Empty;
        var payloadLines = integrationEvent.Payload.Lines;
        if (payloadLines is null || payloadLines.Count == 0)
        {
            failureCode = "missing-payload-field";
            failureMessage = "WMS purchase-order inbound completion payload must include completed lines.";
            return ReceiptProjectionDecision.Failed;
        }

        var orderLines = order.Lines.ToDictionary(x => x.LineNo, StringComparer.Ordinal);
        var commandLines = new List<PurchaseReceiptCommandLine>(payloadLines.Count);
        var seenLineReferences = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in payloadLines.OrderBy(x => x.LineReference, StringComparer.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(line.LineReference))
            {
                failureCode = "missing-payload-field";
                failureMessage = "WMS purchase-order inbound completion line must include LineReference.";
                return ReceiptProjectionDecision.Failed;
            }

            if (line.Quantity <= 0m)
            {
                failureCode = "non-positive-receipt-quantity";
                failureMessage = $"WMS inbound completion line '{line.LineReference}' must have a positive quantity.";
                return ReceiptProjectionDecision.Failed;
            }

            if (string.IsNullOrWhiteSpace(line.Status))
            {
                failureCode = "missing-payload-field";
                failureMessage = $"WMS inbound completion line '{line.LineReference}' must include receipt quality status.";
                return ReceiptProjectionDecision.Failed;
            }

            var lineReference = line.LineReference.Trim();
            if (!seenLineReferences.Add(lineReference))
            {
                failureCode = "duplicate-source-line";
                failureMessage = $"WMS inbound completion contains duplicate line '{lineReference}'.";
                return ReceiptProjectionDecision.Failed;
            }

            if (!orderLines.TryGetValue(lineReference, out var orderLine))
            {
                failureCode = "missing-source-facts";
                failureMessage = $"Purchase order '{order.PurchaseOrderNo}' does not contain WMS inbound line '{lineReference}'.";
                return ReceiptProjectionDecision.Failed;
            }

            if (!string.Equals(orderLine.SkuCode, line.SkuCode, StringComparison.Ordinal)
                || !string.Equals(orderLine.UomCode, line.UomCode, StringComparison.Ordinal))
            {
                failureCode = "source-line-mismatch";
                failureMessage = $"WMS inbound line '{lineReference}' does not match purchase order SKU/UOM facts.";
                return ReceiptProjectionDecision.Failed;
            }

            if (line.Quantity > orderLine.OpenQuantity)
            {
                failureCode = "receipt-quantity-exceeds-open-order";
                failureMessage = $"WMS inbound line '{lineReference}' quantity exceeds purchase order open quantity.";
                return ReceiptProjectionDecision.Failed;
            }

            var receiptQualityStatus = ErpQualityStatusNormalizer.NormalizeReceiptQualityStatus(line.Status);
            if (!ErpQualityStatusNormalizer.IsPayableReceiptQuality(receiptQualityStatus))
            {
                continue;
            }

            commandLines.Add(new PurchaseReceiptCommandLine(
                lineReference,
                line.Quantity,
                receiptQualityStatus,
                line.LocationCode,
                null));
        }

        if (commandLines.Count == 0)
        {
            failureCode = "no-payable-receipt-lines";
            failureMessage = $"WMS inbound completion '{integrationEvent.Payload.PublicReference}' does not contain payable quality lines.";
            return ReceiptProjectionDecision.Failed;
        }

        receiptLines = commandLines;
        return ReceiptProjectionDecision.Record;
    }

    private static bool IsPurchaseOrderSource(string sourceDocumentType)
    {
        return string.Equals(sourceDocumentType, "purchase-order", StringComparison.OrdinalIgnoreCase)
            || string.Equals(sourceDocumentType, "erp-purchase-order", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsErpPurchaseReceiptSource(string sourceDocumentType)
    {
        return string.Equals(sourceDocumentType, "purchase-receipt", StringComparison.OrdinalIgnoreCase)
            || string.Equals(sourceDocumentType, "erp-purchase-receipt", StringComparison.OrdinalIgnoreCase);
    }

    private Task DeadLetterAsync(
        WmsIntegrationEvent integrationEvent,
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

    private enum ReceiptProjectionDecision
    {
        Record,
        Failed,
    }
}
