using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.PurchaseReceiptAggregate;
using Nerv.IIP.Business.Erp.Infrastructure;
using Nerv.IIP.Business.Erp.Web.Application.Commands.Procurement;
using Nerv.IIP.Contracts.Erp;

namespace Nerv.IIP.Business.Erp.Web.Application.Queries.Procurement;

public sealed record GetWmsPurchaseReceiptAcceptanceQuery(
    string OrganizationId,
    string EnvironmentId,
    Guid WmsReceiptBatchId) : IQuery<WmsPurchaseReceiptAcceptanceResponse?>;

public sealed record WmsPurchaseReceiptAcceptanceResponse(
    Guid WmsReceiptBatchId,
    PurchaseReceiptId PurchaseReceiptId,
    string PurchaseReceiptNo,
    string PurchaseOrderNo,
    string SiteCode,
    string CurrencyCode,
    decimal ExchangeRate,
    IReadOnlyCollection<WmsPurchaseReceiptAcceptedLine> Lines);

public sealed record WmsPurchaseReceiptAcceptedLine(
    string PurchaseOrderLineNo,
    string SkuCode,
    string UomCode,
    decimal ReceivedQuantity,
    string QualityStatus,
    string LocationCode,
    string? LotNo,
    decimal UnitPrice,
    decimal UnitCost);

public sealed class GetWmsPurchaseReceiptAcceptanceQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<GetWmsPurchaseReceiptAcceptanceQuery, WmsPurchaseReceiptAcceptanceResponse?>
{
    public async Task<WmsPurchaseReceiptAcceptanceResponse?> Handle(
        GetWmsPurchaseReceiptAcceptanceQuery request, CancellationToken cancellationToken)
    {
        var key = WmsPurchaseReceiptAcceptanceIdentity.Key(request.WmsReceiptBatchId);
        var receiptNumbers = dbContext.CodeIdempotencyKeys
            .Where(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId
                && x.RuleKey == WmsPurchaseReceiptAcceptanceIdentity.ReceiptRuleKey && x.IdempotencyKey == key
                && x.PayloadFingerprint.StartsWith(WmsPurchaseReceiptAcceptanceIdentity.FingerprintPrefix))
            .Select(x => x.Code);
        var receipt = await dbContext.PurchaseReceipts.AsNoTracking().Include(x => x.Lines)
            .SingleOrDefaultAsync(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId
                && x.InventoryPostingRoute == PurchaseReceiptInventoryPostingRoute.Wms
                && receiptNumbers.Contains(x.PurchaseReceiptNo), cancellationToken);

        return receipt is null ? null : new WmsPurchaseReceiptAcceptanceResponse(
            request.WmsReceiptBatchId, receipt.Id, receipt.PurchaseReceiptNo, receipt.PurchaseOrderNo,
            receipt.SiteCode, receipt.CurrencyCode, receipt.ExchangeRate,
            receipt.Lines.OrderBy(line => line.PurchaseOrderLineNo, StringComparer.Ordinal)
                .Select(line => new WmsPurchaseReceiptAcceptedLine(
                    line.PurchaseOrderLineNo, line.SkuCode, line.UomCode, line.ReceivedQuantity, line.QualityStatus,
                    line.LocationCode, line.LotNo, line.UnitPrice!.Value, line.UnitPrice.Value * receipt.ExchangeRate))
                .ToArray());
    }
}
