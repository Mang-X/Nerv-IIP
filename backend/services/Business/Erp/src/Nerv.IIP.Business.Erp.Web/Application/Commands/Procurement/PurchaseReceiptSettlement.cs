using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.PurchaseReceiptAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.SupplierInvoiceAggregate;
using Nerv.IIP.Business.Erp.Infrastructure;
using Nerv.IIP.Contracts.Erp;

namespace Nerv.IIP.Business.Erp.Web.Application.Commands.Procurement;

internal static class PurchaseReceiptSettlement
{
    public static Task<bool> IsAcceptedBatchAsync(
        ApplicationDbContext dbContext, PurchaseReceipt receipt, CancellationToken cancellationToken) =>
        receipt.InventoryPostingRoute != PurchaseReceiptInventoryPostingRoute.Wms
            ? Task.FromResult(false)
            : dbContext.CodeIdempotencyKeys.AnyAsync(key =>
                key.OrganizationId == receipt.OrganizationId
                && key.EnvironmentId == receipt.EnvironmentId
                && key.Code == receipt.PurchaseReceiptNo
                && key.RuleKey == WmsPurchaseReceiptAcceptanceIdentity.ReceiptRuleKey
                && key.IdempotencyKey.StartsWith(WmsPurchaseReceiptAcceptanceIdentity.KeyPrefix)
                && key.PayloadFingerprint.StartsWith(WmsPurchaseReceiptAcceptanceIdentity.FingerprintPrefix),
                cancellationToken);

    public static Task LockAsync(
        ApplicationDbContext dbContext, PurchaseReceipt receipt, CancellationToken cancellationToken) =>
        dbContext.LockPurchaseReceiptSettlementAsync(
            receipt.OrganizationId, receipt.EnvironmentId, receipt.PurchaseReceiptNo, cancellationToken);

    public static async Task<Dictionary<string, decimal>> ReadReturnedGrIrAsync(
        ApplicationDbContext dbContext, PurchaseReceipt receipt, CancellationToken cancellationToken) =>
        (await dbContext.PurchaseReturns
            .Where(x => x.OrganizationId == receipt.OrganizationId && x.EnvironmentId == receipt.EnvironmentId
                && x.PurchaseReceiptNo == receipt.PurchaseReceiptNo)
            .SelectMany(x => x.Lines)
            // Debit-note quantities are already accounted for by the original matched invoice.
            .Where(x => x.GrIrReversalQuantity > 0m)
            .Select(x => new { x.PurchaseOrderLineNo, x.GrIrReversalQuantity })
            .ToListAsync(cancellationToken))
        .GroupBy(x => x.PurchaseOrderLineNo, StringComparer.Ordinal)
        .ToDictionary(group => group.Key, group => group.Sum(x => x.GrIrReversalQuantity), StringComparer.Ordinal);

    public static async Task EnsureHeldInvoiceCanClearAsync(
        ApplicationDbContext dbContext, SupplierInvoice invoice, CancellationToken cancellationToken)
    {
        if (invoice.MatchStatus != SupplierInvoiceMatchStatus.PaymentHeld)
        {
            return;
        }

        var receipt = await dbContext.PurchaseReceipts.Include(x => x.Lines).SingleOrDefaultAsync(x =>
            x.OrganizationId == invoice.OrganizationId && x.EnvironmentId == invoice.EnvironmentId
            && x.PurchaseReceiptNo == invoice.PurchaseReceiptNo, cancellationToken);
        if (receipt is null || !await IsAcceptedBatchAsync(dbContext, receipt, cancellationToken))
        {
            return;
        }

        await LockAsync(dbContext, receipt, cancellationToken);
        await dbContext.Entry(invoice).ReloadAsync(cancellationToken);
        if (invoice.MatchStatus != SupplierInvoiceMatchStatus.PaymentHeld)
        {
            return;
        }

        var returnedGrIr = await ReadReturnedGrIrAsync(dbContext, receipt, cancellationToken);
        if (returnedGrIr.Count == 0)
        {
            return;
        }

        var matched = (await dbContext.SupplierInvoices
            .Where(x => x.OrganizationId == receipt.OrganizationId && x.EnvironmentId == receipt.EnvironmentId
                && x.PurchaseReceiptNo == receipt.PurchaseReceiptNo
                && x.MatchStatus == SupplierInvoiceMatchStatus.Matched)
            .SelectMany(x => x.Lines)
            .Select(x => new { x.PurchaseReceiptLineNo, x.InvoiceQuantity })
            .ToListAsync(cancellationToken))
            .ToLookup(x => x.PurchaseReceiptLineNo, StringComparer.Ordinal);

        foreach (var line in invoice.Lines.GroupBy(x => x.PurchaseReceiptLineNo, StringComparer.Ordinal))
        {
            if (returnedGrIr.TryGetValue(line.Key, out var returnedQuantity)
                && matched[line.Key].Sum(x => x.InvoiceQuantity) + line.Sum(x => x.InvoiceQuantity)
                    > receipt.Lines.Single(x => x.PurchaseOrderLineNo == line.Key).ReceivedQuantity - returnedQuantity)
            {
                throw new KnownException($"收货行『{line.Key}』的发票数量超出退货后的可匹配数量，无法解除付款冻结。");
            }
        }
    }
}
