using DotNetCore.CAP;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.PurchaseOrderAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.PurchaseReceiptAggregate;
using Nerv.IIP.Business.Erp.Infrastructure;
using Nerv.IIP.Business.Erp.Infrastructure.IntegrationEvents;
using Nerv.IIP.Business.Erp.Web.Application.Commands.Finance;
using Nerv.IIP.Business.Erp.Web.Application.IntegrationEvents;
using Nerv.IIP.Contracts.IntegrationEvents;
using Nerv.IIP.Messaging.CAP;
using NetCorePal.Extensions.DistributedTransactions;

namespace Nerv.IIP.Business.Erp.Web.Application.IntegrationEventHandlers;

[IntegrationEventConsumer("Nerv.IIP.Business.Erp.Web.Application.IntegrationEvents.PurchaseReceiptRecordedIntegrationEvent", ConsumerName)]
public sealed class PurchaseReceiptRecordedIntegrationEventHandlerForCreateAccountPayable(
    ApplicationDbContext dbContext,
    IIntegrationEventDeadLetterStore deadLetterStore)
    : IIntegrationEventHandler<PurchaseReceiptRecordedIntegrationEvent>, ICapSubscribe
{
    public const string ConsumerName = "business-erp.purchase-receipt-ap-accrual";

    private static readonly IntegrationEventConsumerOptions ConsumerOptions = new(
        ConsumerName,
        ErpIntegrationEventTypes.PurchaseReceiptRecorded,
        1);

    private readonly IntegrationEventConsumerGuard<PurchaseReceiptRecordedIntegrationEvent> consumerGuard = new(
        new IntegrationEventEnvelopeValidator(),
        deadLetterStore,
        ConsumerOptions);

    public Task HandleAsync(
        PurchaseReceiptRecordedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        return consumerGuard.HandleAsync(integrationEvent, HandleValidEventAsync, cancellationToken);
    }

    [CapSubscribe("Nerv.IIP.Business.Erp.Web.Application.IntegrationEvents.PurchaseReceiptRecordedIntegrationEvent", Group = ConsumerName)]
    public Task HandleCapAsync(
        PurchaseReceiptRecordedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        return HandleAsync(integrationEvent, cancellationToken);
    }

    private async Task HandleValidEventAsync(
        PurchaseReceiptRecordedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(integrationEvent.SourceService, ErpIntegrationEventSources.BusinessErp, StringComparison.OrdinalIgnoreCase))
        {
            await DeadLetterAsync(
                integrationEvent,
                "unexpected-source-service",
                $"Integration event source service '{integrationEvent.SourceService}' is not supported by consumer '{ConsumerName}'.",
                cancellationToken);
            return;
        }

        var receipt = await dbContext.PurchaseReceipts
            .Include(x => x.Lines)
            .SingleOrDefaultAsync(x =>
                x.OrganizationId == integrationEvent.OrganizationId
                && x.EnvironmentId == integrationEvent.EnvironmentId
                && x.PurchaseReceiptNo == integrationEvent.Payload.PurchaseReceiptNo,
                cancellationToken);
        if (receipt is null)
        {
            await DeadLetterAsync(
                integrationEvent,
                "missing-source-facts",
                $"Purchase receipt '{integrationEvent.Payload.PurchaseReceiptNo}' was not found for AP accrual.",
                cancellationToken);
            return;
        }

        var order = await dbContext.PurchaseOrders
            .Include(x => x.Lines)
            .SingleOrDefaultAsync(x =>
                x.OrganizationId == receipt.OrganizationId
                && x.EnvironmentId == receipt.EnvironmentId
                && x.PurchaseOrderNo == receipt.PurchaseOrderNo,
                cancellationToken);
        if (order is null)
        {
            await DeadLetterAsync(
                integrationEvent,
                "missing-source-facts",
                $"Purchase order '{receipt.PurchaseOrderNo}' was not found for receipt '{receipt.PurchaseReceiptNo}'.",
                cancellationToken);
            return;
        }

        if (!TryCalculateReceiptAmount(receipt, order, out var amount, out var failureMessage))
        {
            await DeadLetterAsync(
                integrationEvent,
                "unsupported-quality-status",
                failureMessage,
                cancellationToken);
            return;
        }

        if (!await ErpProcessedIntegrationEventInbox.TryRecordAsync(dbContext, ConsumerName, integrationEvent, cancellationToken))
        {
            return;
        }

        if (await dbContext.AccountPayables.AnyAsync(x =>
            x.OrganizationId == receipt.OrganizationId
            && x.EnvironmentId == receipt.EnvironmentId
            && x.SourceDocumentNo == receipt.PurchaseReceiptNo,
            cancellationToken))
        {
            return;
        }

        await new CreateAccountPayableCommandHandler(dbContext).Handle(
            new CreateAccountPayableCommand(
                receipt.OrganizationId,
                receipt.EnvironmentId,
                null,
                receipt.PurchaseReceiptNo,
                receipt.SupplierCode,
                amount,
                "CNY",
                DateOnly.FromDateTime(receipt.RecordedAtUtc),
                null,
                "RECEIPT-ACCRUAL",
                $"{integrationEvent.IdempotencyKey}:account-payable"),
            cancellationToken);
    }

    private static bool TryCalculateReceiptAmount(
        PurchaseReceipt receipt,
        PurchaseOrder order,
        out decimal amount,
        out string failureMessage)
    {
        amount = 0m;
        failureMessage = string.Empty;
        var orderLines = order.Lines.ToDictionary(x => x.LineNo, StringComparer.Ordinal);
        foreach (var receiptLine in receipt.Lines)
        {
            if (!IsPayableQuality(receiptLine.QualityStatus))
            {
                failureMessage = $"Purchase receipt '{receipt.PurchaseReceiptNo}' line '{receiptLine.PurchaseOrderLineNo}' has unsupported quality status '{receiptLine.QualityStatus}'.";
                return false;
            }

            if (!orderLines.TryGetValue(receiptLine.PurchaseOrderLineNo, out var orderLine))
            {
                failureMessage = $"Purchase order line '{receiptLine.PurchaseOrderLineNo}' was not found for receipt '{receipt.PurchaseReceiptNo}'.";
                return false;
            }

            amount += receiptLine.ReceivedQuantity * orderLine.UnitPrice;
        }

        if (amount <= 0m)
        {
            failureMessage = $"Purchase receipt '{receipt.PurchaseReceiptNo}' does not have a positive AP accrual amount.";
            return false;
        }

        return true;
    }

    private static bool IsPayableQuality(string qualityStatus)
    {
        var normalized = qualityStatus.Trim().ToLowerInvariant();
        return normalized is "accepted" or "unrestricted";
    }

    private Task DeadLetterAsync(
        PurchaseReceiptRecordedIntegrationEvent integrationEvent,
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
}
