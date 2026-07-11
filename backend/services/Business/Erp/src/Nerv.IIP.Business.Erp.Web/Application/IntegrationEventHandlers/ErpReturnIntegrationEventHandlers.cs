using DotNetCore.CAP;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.AccountPayableAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.CreditNoteAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.DebitNoteAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.PurchaseReturnAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.SalesReturnAuthorizationAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.SupplierInvoiceAggregate;
using Nerv.IIP.Business.Erp.Infrastructure;
using Nerv.IIP.Business.Erp.Web.Application.Commands;
using Nerv.IIP.Business.Erp.Web.Application.Commands.Finance;
using Nerv.IIP.Contracts.IntegrationEvents;
using Nerv.IIP.Contracts.Quality;
using Nerv.IIP.Contracts.Wms;
using Nerv.IIP.Messaging.CAP;
using NetCorePal.Extensions.DistributedTransactions;

namespace Nerv.IIP.Business.Erp.Web.Application.IntegrationEventHandlers;

[IntegrationEventConsumer("Nerv.IIP.Contracts.Wms.WmsIntegrationEvent", ConsumerName)]
public sealed class WmsOutboundOrderCompletedIntegrationEventHandlerForRecordPurchaseReturn(
    ApplicationDbContext dbContext,
    IIntegrationEventDeadLetterStore deadLetterStore,
    ErpCodingService codingService)
    : IIntegrationEventHandler<WmsIntegrationEvent>, ICapSubscribe
{
    public const string ConsumerName = "business-erp.wms-supplier-return-purchase-return";

    private readonly IntegrationEventConsumerGuard<WmsIntegrationEvent> consumerGuard = new(
        new IntegrationEventEnvelopeValidator(),
        deadLetterStore,
        new IntegrationEventConsumerOptions(ConsumerName, WmsIntegrationEventTypes.OutboundOrderCompleted, WmsIntegrationEventVersions.V1)
        {
            IgnoreUnsupportedEventTypes = true,
        });

    public Task HandleAsync(WmsIntegrationEvent integrationEvent, CancellationToken cancellationToken)
        => consumerGuard.HandleAsync(integrationEvent, HandleValidEventAsync, cancellationToken);

    [CapSubscribe("Nerv.IIP.Contracts.Wms.WmsIntegrationEvent", Group = ConsumerName)]
    public Task HandleCapAsync(WmsIntegrationEvent integrationEvent, CancellationToken cancellationToken)
        => HandleAsync(integrationEvent, cancellationToken);

    private async Task HandleValidEventAsync(WmsIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        try
        {
            await HandleValidEventCoreAsync(integrationEvent, cancellationToken);
        }
        catch (Exception exception) when (exception is KnownException or ArgumentException or InvalidOperationException)
        {
            dbContext.ChangeTracker.Clear();
            await DeadLetterAsync(integrationEvent, "invalid-purchase-return-projection", exception.Message, cancellationToken);
        }
    }

    private async Task HandleValidEventCoreAsync(WmsIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        if (!string.Equals(integrationEvent.SourceService, WmsIntegrationEventSources.BusinessWms, StringComparison.OrdinalIgnoreCase))
        {
            await DeadLetterAsync(integrationEvent, "unexpected-source-service", "Only BusinessWMS may complete supplier-return outbounds.", cancellationToken);
            return;
        }

        var payload = integrationEvent.Payload;
        if (!string.Equals(payload.SourceDocumentType, WmsSourceDocumentTypes.PurchaseReceiptReturn, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(payload.PublicReference) || string.IsNullOrWhiteSpace(payload.SourceDocumentId) || payload.Lines is null || payload.Lines.Count == 0)
        {
            await DeadLetterAsync(integrationEvent, "missing-payload-field", "Completed supplier-return outbound requires WMS reference, purchase receipt reference, and lines.", cancellationToken);
            return;
        }

        var receipt = await dbContext.PurchaseReceipts
            .Include(x => x.Lines)
            .SingleOrDefaultAsync(x => x.OrganizationId == integrationEvent.OrganizationId
                && x.EnvironmentId == integrationEvent.EnvironmentId
                && x.PurchaseReceiptNo == payload.SourceDocumentId,
                cancellationToken);
        if (receipt is null)
        {
            await DeadLetterAsync(integrationEvent, "missing-source-facts", $"Purchase receipt '{payload.SourceDocumentId}' was not found for completed WMS supplier return.", cancellationToken);
            return;
        }

        if (await dbContext.PurchaseReturns.AnyAsync(x => x.OrganizationId == integrationEvent.OrganizationId
            && x.EnvironmentId == integrationEvent.EnvironmentId
            && x.WmsOutboundOrderNo == payload.PublicReference,
            cancellationToken))
        {
            return;
        }

        var order = await dbContext.PurchaseOrders.Include(x => x.Lines).SingleOrDefaultAsync(x =>
            x.OrganizationId == receipt.OrganizationId
            && x.EnvironmentId == receipt.EnvironmentId
            && x.PurchaseOrderNo == receipt.PurchaseOrderNo,
            cancellationToken);
        if (order is null)
        {
            await DeadLetterAsync(integrationEvent, "missing-source-facts", $"Purchase order '{receipt.PurchaseOrderNo}' was not found for purchase return.", cancellationToken);
            return;
        }

        var priorReturns = await dbContext.PurchaseReturns.Include(x => x.Lines)
            .Where(x => x.OrganizationId == receipt.OrganizationId
                && x.EnvironmentId == receipt.EnvironmentId
                && x.PurchaseReceiptNo == receipt.PurchaseReceiptNo)
            .ToListAsync(cancellationToken);
        var invoices = await dbContext.SupplierInvoices.Include(x => x.Lines)
            .Where(x => x.OrganizationId == receipt.OrganizationId
                && x.EnvironmentId == receipt.EnvironmentId
                && x.PurchaseReceiptNo == receipt.PurchaseReceiptNo)
            .OrderBy(x => x.InvoiceDate)
            .ToListAsync(cancellationToken);
        var returnLines = new List<PurchaseReturnLineDraft>();
        foreach (var wmsLine in payload.Lines)
        {
            var receiptLine = receipt.Lines.SingleOrDefault(x => x.PurchaseOrderLineNo == wmsLine.LineReference)
                ?? throw new KnownException($"Purchase receipt '{receipt.PurchaseReceiptNo}' has no line '{wmsLine.LineReference}' for supplier return.");
            var orderLine = order.Lines.SingleOrDefault(x => x.LineNo == wmsLine.LineReference)
                ?? throw new KnownException($"Purchase order '{order.PurchaseOrderNo}' has no line '{wmsLine.LineReference}' for supplier return.");
            if (!string.Equals(receiptLine.SkuCode, wmsLine.SkuCode, StringComparison.Ordinal)
                || !string.Equals(receiptLine.UomCode, wmsLine.UomCode, StringComparison.Ordinal)
                || wmsLine.Quantity <= 0m)
            {
                throw new KnownException($"WMS supplier return line '{wmsLine.LineReference}' does not match immutable receipt SKU/UOM/quantity facts.");
            }

            var alreadyReturned = priorReturns.SelectMany(x => x.Lines)
                .Where(x => x.PurchaseOrderLineNo == wmsLine.LineReference)
                .Sum(x => x.ReturnedQuantity);
            if (wmsLine.Quantity > receiptLine.ReceivedQuantity - alreadyReturned)
            {
                throw new KnownException($"WMS supplier return line '{wmsLine.LineReference}' exceeds the unreturned receipt quantity.");
            }

            var invoicedQuantity = invoices.SelectMany(x => x.Lines)
                .Where(x => x.PurchaseOrderLineNo == wmsLine.LineReference)
                .Sum(x => x.InvoiceQuantity);
            var alreadyDebitedQuantity = priorReturns.SelectMany(x => x.Lines)
                .Where(x => x.PurchaseOrderLineNo == wmsLine.LineReference)
                .Sum(x => x.DebitNoteQuantity);
            var debitNoteQuantity = Math.Min(wmsLine.Quantity, Math.Max(0m, invoicedQuantity - alreadyDebitedQuantity));
            var debitQuantityRemaining = debitNoteQuantity;
            var alreadyDebitedRemaining = alreadyDebitedQuantity;
            foreach (var invoiceLine in invoices
                .SelectMany(invoice => invoice.Lines)
                .Where(line => line.PurchaseOrderLineNo == wmsLine.LineReference))
            {
                if (alreadyDebitedRemaining >= invoiceLine.InvoiceQuantity)
                {
                    alreadyDebitedRemaining -= invoiceLine.InvoiceQuantity;
                    continue;
                }

                var invoiceQuantityAvailable = invoiceLine.InvoiceQuantity - alreadyDebitedRemaining;
                alreadyDebitedRemaining = 0m;
                var lineDebitQuantity = Math.Min(debitQuantityRemaining, invoiceQuantityAvailable);
                if (lineDebitQuantity <= 0m)
                {
                    break;
                }

                returnLines.Add(new PurchaseReturnLineDraft(
                    wmsLine.LineReference,
                    receiptLine.SkuCode,
                    receiptLine.UomCode,
                    lineDebitQuantity,
                    invoiceLine.UnitPrice,
                    0m,
                    lineDebitQuantity));
                debitQuantityRemaining -= lineDebitQuantity;
                if (debitQuantityRemaining <= 0m)
                {
                    break;
                }
            }

            var grIrReversalQuantity = wmsLine.Quantity - debitNoteQuantity;
            if (grIrReversalQuantity > 0m)
            {
                returnLines.Add(new PurchaseReturnLineDraft(
                    wmsLine.LineReference,
                    receiptLine.SkuCode,
                    receiptLine.UomCode,
                    grIrReversalQuantity,
                    orderLine.UnitPrice,
                    grIrReversalQuantity,
                    0m));
            }
        }

        await AccountingPeriodPostingGuard.EnsureOpenAsync(
            dbContext,
            receipt.OrganizationId,
            receipt.EnvironmentId,
            DateOnly.FromDateTime(integrationEvent.OccurredAtUtc.UtcDateTime),
            "supplier purchase return voucher",
            cancellationToken);
        var remainingDebitAmount = returnLines.Sum(x => x.DebitNoteQuantity * x.UnitPrice);
        var payablesByInvoiceNo = await dbContext.AccountPayables
            .Where(x => x.OrganizationId == receipt.OrganizationId && x.EnvironmentId == receipt.EnvironmentId && x.SupplierCode == receipt.SupplierCode)
            .ToDictionaryAsync(x => x.SourceDocumentNo, StringComparer.Ordinal, cancellationToken);
        var debitApplications = new List<(SupplierInvoice Invoice, AccountPayable Payable, decimal Amount)>();
        foreach (var invoice in invoices)
        {
            if (remainingDebitAmount <= 0m)
            {
                break;
            }

            if (!payablesByInvoiceNo.TryGetValue(invoice.InvoiceNo, out var payable))
            {
                continue;
            }

            var appliedAmount = Math.Min(remainingDebitAmount, payable.OpenAmount);
            if (appliedAmount <= 0m)
            {
                continue;
            }

            debitApplications.Add((invoice, payable, appliedAmount));
            remainingDebitAmount -= appliedAmount;
        }

        if (remainingDebitAmount > 0m)
        {
            throw new KnownException("Supplier return debit-note amount cannot be applied because matching AP is absent or already settled.");
        }

        if (!await ErpProcessedIntegrationEventInbox.TryRecordAsync(dbContext, ConsumerName, integrationEvent, cancellationToken))
        {
            return;
        }

        var returnAllocation = await codingService.AllocateAsync(
            receipt.OrganizationId,
            receipt.EnvironmentId,
            "purchase-return",
            null,
            $"{ConsumerName}:{integrationEvent.IdempotencyKey}",
            ErpCodingService.Fingerprint(receipt.PurchaseReceiptNo, payload.PublicReference, returnLines),
            cancellationToken);
        var purchaseReturn = PurchaseReturn.Record(
            receipt.OrganizationId,
            receipt.EnvironmentId,
            returnAllocation.Code,
            receipt.PurchaseReceiptNo,
            payload.PublicReference,
            receipt.SupplierCode,
            receipt.CurrencyCode,
            receipt.ExchangeRate,
            returnLines);

        var debitNotes = new List<DebitNote>();
        foreach (var application in debitApplications)
        {
            var debitAllocation = await codingService.AllocateAsync(
                receipt.OrganizationId,
                receipt.EnvironmentId,
                "debit-note",
                null,
                $"{ConsumerName}:{integrationEvent.IdempotencyKey}:{application.Invoice.InvoiceNo}",
                ErpCodingService.Fingerprint(returnAllocation.Code, application.Invoice.InvoiceNo, application.Payable.PayableNo, application.Amount),
                cancellationToken);
            application.Payable.ApplyDebitNote(application.Amount);
            debitNotes.Add(DebitNote.Issue(
                receipt.OrganizationId,
                receipt.EnvironmentId,
                debitAllocation.Code,
                purchaseReturn.PurchaseReturnNo,
                application.Payable.PayableNo,
                receipt.SupplierCode,
                application.Amount,
                receipt.CurrencyCode,
                receipt.ExchangeRate));
        }

        dbContext.PurchaseReturns.Add(purchaseReturn);
        dbContext.DebitNotes.AddRange(debitNotes);
        dbContext.JournalVouchers.Add(FinanceVoucherFactory.ForPurchaseReturn(
            purchaseReturn,
            $"JV-PRTN-{purchaseReturn.PurchaseReturnNo}",
            DateOnly.FromDateTime(integrationEvent.OccurredAtUtc.UtcDateTime)));
    }

    private Task DeadLetterAsync(WmsIntegrationEvent integrationEvent, string failureCode, string failureMessage, CancellationToken cancellationToken)
        => deadLetterStore.AddAsync(IntegrationEventDeadLetterMessage.Create(ConsumerName, integrationEvent, failureCode, failureMessage), cancellationToken);
}

[IntegrationEventConsumer("Nerv.IIP.Contracts.Wms.WmsIntegrationEvent", ConsumerName)]
public sealed class WmsInboundOrderCompletedIntegrationEventHandlerForRecordSalesReturnReceipt(
    ApplicationDbContext dbContext,
    IIntegrationEventDeadLetterStore deadLetterStore)
    : IIntegrationEventHandler<WmsIntegrationEvent>, ICapSubscribe
{
    public const string ConsumerName = "business-erp.wms-sales-return-inbound";

    private readonly IntegrationEventConsumerGuard<WmsIntegrationEvent> consumerGuard = new(
        new IntegrationEventEnvelopeValidator(),
        deadLetterStore,
        new IntegrationEventConsumerOptions(ConsumerName, WmsIntegrationEventTypes.InboundOrderCompleted, WmsIntegrationEventVersions.V1)
        {
            IgnoreUnsupportedEventTypes = true,
        });

    public Task HandleAsync(WmsIntegrationEvent integrationEvent, CancellationToken cancellationToken)
        => consumerGuard.HandleAsync(integrationEvent, HandleValidEventAsync, cancellationToken);

    [CapSubscribe("Nerv.IIP.Contracts.Wms.WmsIntegrationEvent", Group = ConsumerName)]
    public Task HandleCapAsync(WmsIntegrationEvent integrationEvent, CancellationToken cancellationToken)
        => HandleAsync(integrationEvent, cancellationToken);

    private async Task HandleValidEventAsync(WmsIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        if (!string.Equals(integrationEvent.SourceService, WmsIntegrationEventSources.BusinessWms, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(integrationEvent.Payload.SourceDocumentType, WmsSourceDocumentTypes.SalesReturnRma, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(integrationEvent.Payload.SourceDocumentId) || string.IsNullOrWhiteSpace(integrationEvent.Payload.PublicReference))
        {
            await DeadLetterAsync(integrationEvent, "missing-payload-field", "WMS sales return inbound completion requires RMA and inbound order references.", cancellationToken);
            return;
        }

        var rma = await dbContext.SalesReturnAuthorizations.SingleOrDefaultAsync(x =>
            x.OrganizationId == integrationEvent.OrganizationId
            && x.EnvironmentId == integrationEvent.EnvironmentId
            && x.RmaNo == integrationEvent.Payload.SourceDocumentId,
            cancellationToken);
        if (rma is null)
        {
            await DeadLetterAsync(integrationEvent, "missing-source-facts", $"RMA '{integrationEvent.Payload.SourceDocumentId}' was not found for WMS inbound completion.", cancellationToken);
            return;
        }

        if (!await ErpProcessedIntegrationEventInbox.TryRecordAsync(dbContext, ConsumerName, integrationEvent, cancellationToken))
        {
            return;
        }

        try
        {
            rma.MarkWarehouseReceived(integrationEvent.Payload.PublicReference);
        }
        catch (InvalidOperationException exception)
        {
            dbContext.ChangeTracker.Clear();
            await DeadLetterAsync(integrationEvent, "rma-receipt-rejected", exception.Message, cancellationToken);
        }
    }

    private Task DeadLetterAsync(WmsIntegrationEvent integrationEvent, string failureCode, string failureMessage, CancellationToken cancellationToken)
        => deadLetterStore.AddAsync(IntegrationEventDeadLetterMessage.Create(ConsumerName, integrationEvent, failureCode, failureMessage), cancellationToken);
}

[IntegrationEventConsumer("Nerv.IIP.Contracts.Quality.InspectionResultIntegrationEvent", ConsumerName)]
public sealed class QualityInspectionResultIntegrationEventHandlerForSettleSalesReturnCredit(
    ApplicationDbContext dbContext,
    IIntegrationEventDeadLetterStore deadLetterStore,
    ErpCodingService codingService)
    : IIntegrationEventHandler<InspectionResultIntegrationEvent>, ICapSubscribe
{
    public const string ConsumerName = "business-erp.quality-sales-return-credit";

    private readonly IntegrationEventConsumerGuard<InspectionResultIntegrationEvent> consumerGuard = new(
        new IntegrationEventEnvelopeValidator(),
        deadLetterStore,
        new IntegrationEventConsumerOptions(
            ConsumerName,
            [QualityIntegrationEventTypes.InspectionPassed, QualityIntegrationEventTypes.InspectionConditionalReleased, QualityIntegrationEventTypes.InspectionRejected],
            QualityIntegrationEventVersions.V1));

    public Task HandleAsync(InspectionResultIntegrationEvent integrationEvent, CancellationToken cancellationToken)
        => consumerGuard.HandleAsync(integrationEvent, HandleValidEventAsync, cancellationToken);

    [CapSubscribe("Nerv.IIP.Contracts.Quality.InspectionResultIntegrationEvent", Group = ConsumerName)]
    public Task HandleCapAsync(InspectionResultIntegrationEvent integrationEvent, CancellationToken cancellationToken)
        => HandleAsync(integrationEvent, cancellationToken);

    private async Task HandleValidEventAsync(InspectionResultIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        var payload = integrationEvent.Payload;
        if (!string.Equals(payload.SourceService, QualityInspectionSourceTypes.Wms, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(payload.SourceType, QualityInspectionSourceTypes.Receiving, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var rma = await dbContext.SalesReturnAuthorizations.SingleOrDefaultAsync(x =>
            x.OrganizationId == integrationEvent.OrganizationId
            && x.EnvironmentId == integrationEvent.EnvironmentId
            && x.WmsInboundOrderNo == payload.SourceDocumentId,
            cancellationToken);
        if (rma is null)
        {
            return;
        }

        if (!await ErpProcessedIntegrationEventInbox.TryRecordAsync(dbContext, ConsumerName, integrationEvent, cancellationToken))
        {
            return;
        }

        try
        {
            var disposition = integrationEvent.EventType switch
            {
                QualityIntegrationEventTypes.InspectionPassed => SalesReturnQualityDispositions.Passed,
                QualityIntegrationEventTypes.InspectionConditionalReleased => SalesReturnQualityDispositions.ConditionalRelease,
                QualityIntegrationEventTypes.InspectionRejected => SalesReturnQualityDispositions.Rejected,
                _ => throw new KnownException($"Unsupported RMA Quality event type '{integrationEvent.EventType}'."),
            };
            rma.ApplyQualityDisposition(disposition);
            if (rma.Status == SalesReturnAuthorizationStatus.CreditDenied)
            {
                return;
            }

            await AccountingPeriodPostingGuard.EnsureOpenAsync(
                dbContext,
                rma.OrganizationId,
                rma.EnvironmentId,
                DateOnly.FromDateTime(integrationEvent.OccurredAtUtc.UtcDateTime),
                "sales return credit note voucher",
                cancellationToken);
            var receivable = await dbContext.AccountReceivables.SingleOrDefaultAsync(x =>
                x.OrganizationId == rma.OrganizationId
                && x.EnvironmentId == rma.EnvironmentId
                && x.ReceivableNo == rma.AccountReceivableNo,
                cancellationToken)
                ?? throw new KnownException($"Account receivable '{rma.AccountReceivableNo}' was not found for RMA credit.");
            if (rma.TotalAmount > receivable.OpenAmount)
            {
                throw new KnownException("RMA credit amount exceeds the current open AR balance.");
            }

            var allocation = await codingService.AllocateAsync(
                rma.OrganizationId,
                rma.EnvironmentId,
                "credit-note",
                null,
                $"{ConsumerName}:{integrationEvent.IdempotencyKey}",
                ErpCodingService.Fingerprint(rma.RmaNo, rma.AccountReceivableNo, rma.TotalAmount),
                cancellationToken);
            rma.MarkCreditIssued(allocation.Code);
            var creditNote = CreditNote.Issue(rma, allocation.Code);
            receivable.ApplyCreditNote(creditNote.Amount);
            dbContext.CreditNotes.Add(creditNote);
            dbContext.JournalVouchers.Add(FinanceVoucherFactory.ForCreditNote(
                creditNote,
                DateOnly.FromDateTime(integrationEvent.OccurredAtUtc.UtcDateTime)));
        }
        catch (Exception exception) when (exception is KnownException or ArgumentException or ArgumentOutOfRangeException or InvalidOperationException)
        {
            dbContext.ChangeTracker.Clear();
            await DeadLetterAsync(integrationEvent, "rma-credit-settlement-rejected", exception.Message, cancellationToken);
        }
    }

    private Task DeadLetterAsync(InspectionResultIntegrationEvent integrationEvent, string failureCode, string failureMessage, CancellationToken cancellationToken)
        => deadLetterStore.AddAsync(IntegrationEventDeadLetterMessage.Create(ConsumerName, integrationEvent, failureCode, failureMessage), cancellationToken);
}
