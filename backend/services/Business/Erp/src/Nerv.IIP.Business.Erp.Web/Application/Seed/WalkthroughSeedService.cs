using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.QuotationAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.RequestForQuotationAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.SupplierQuotationAggregate;
using Nerv.IIP.Business.Erp.Infrastructure;

namespace Nerv.IIP.Business.Erp.Web.Application.Seed;

/// <summary>
/// 人工走查的价格与寻源起点；只创建报价事实，不创建订单、收发货或财务结果。
/// </summary>
public sealed class WalkthroughSeedService(ApplicationDbContext dbContext)
{
    public async Task SeedAsync(
        string organizationId,
        string environmentId,
        CancellationToken cancellationToken = default)
    {
        await SeedSalesQuotationAsync(organizationId, environmentId, cancellationToken);
        await SeedSourcingAsync(organizationId, environmentId, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task SeedSalesQuotationAsync(
        string organizationId,
        string environmentId,
        CancellationToken cancellationToken)
    {
        var existing = await dbContext.Quotations.Include(x => x.Lines).SingleOrDefaultAsync(x =>
            x.OrganizationId == organizationId && x.EnvironmentId == environmentId &&
            x.QuotationNo == WalkthroughSeedSpec.SalesQuotationNo,
            cancellationToken);
        if (existing is null)
        {
            var quotation = Quotation.Create(
                organizationId,
                environmentId,
                WalkthroughSeedSpec.SalesQuotationNo,
                WalkthroughSeedSpec.CustomerCode,
                WalkthroughSeedSpec.ValidUntil,
                [new QuotationLineDraft("10", WalkthroughSeedSpec.FinishedSkuCode, "pcs", 1m, WalkthroughSeedSpec.SalesUnitPrice, WalkthroughSeedSpec.ValidUntil)]);
            quotation.Approve();
            dbContext.Quotations.Add(quotation);
            return;
        }

        var line = existing.Lines.SingleOrDefault();
        if (existing.CustomerCode != WalkthroughSeedSpec.CustomerCode ||
            existing.Status != QuotationStatus.Approved ||
            line is null || line.SkuCode != WalkthroughSeedSpec.FinishedSkuCode ||
            line.UnitPrice != WalkthroughSeedSpec.SalesUnitPrice)
        {
            throw Collision(WalkthroughSeedSpec.SalesQuotationNo);
        }
    }

    private async Task SeedSourcingAsync(
        string organizationId,
        string environmentId,
        CancellationToken cancellationToken)
    {
        var rfq = await dbContext.RequestForQuotations
            .Include(x => x.Lines)
            .Include(x => x.Suppliers)
            .SingleOrDefaultAsync(x =>
                x.OrganizationId == organizationId && x.EnvironmentId == environmentId &&
                x.RfqNo == WalkthroughSeedSpec.RfqNo,
                cancellationToken);
        if (rfq is null)
        {
            dbContext.RequestForQuotations.Add(RequestForQuotation.Create(
                organizationId,
                environmentId,
                WalkthroughSeedSpec.RfqNo,
                WalkthroughSeedSpec.PurchasePrices.Select(x => x.SupplierCode),
                WalkthroughSeedSpec.PurchasePrices.Select((price, index) => new RfqLineDraft(
                    $"{(index + 1) * 10}", price.SkuCode, price.UomCode, price.Quantity,
                    WalkthroughSeedSpec.SiteCode, WalkthroughSeedSpec.ValidUntil))));
        }
        else if (rfq.Lines.Count != WalkthroughSeedSpec.PurchasePrices.Count ||
                 !rfq.Suppliers.Select(x => x.SupplierCode).Order(StringComparer.Ordinal).SequenceEqual(
                     WalkthroughSeedSpec.PurchasePrices.Select(x => x.SupplierCode).Order(StringComparer.Ordinal),
                     StringComparer.Ordinal))
        {
            throw Collision(WalkthroughSeedSpec.RfqNo);
        }

        foreach (var price in WalkthroughSeedSpec.PurchasePrices)
        {
            var existing = await dbContext.SupplierQuotations.Include(x => x.Lines).SingleOrDefaultAsync(x =>
                x.OrganizationId == organizationId && x.EnvironmentId == environmentId &&
                x.QuotationNo == price.QuotationNo,
                cancellationToken);
            if (existing is null)
            {
                dbContext.SupplierQuotations.Add(SupplierQuotation.Receive(
                    organizationId,
                    environmentId,
                    price.QuotationNo,
                    WalkthroughSeedSpec.RfqNo,
                    price.SupplierCode,
                    [new SupplierQuotationLineDraft("10", price.SkuCode, price.UomCode, price.Quantity, price.UnitPrice, WalkthroughSeedSpec.ValidUntil)]));
                continue;
            }

            var line = existing.Lines.SingleOrDefault();
            if (existing.RfqNo != WalkthroughSeedSpec.RfqNo || existing.SupplierCode != price.SupplierCode ||
                line is null || line.SkuCode != price.SkuCode || line.UnitPrice != price.UnitPrice)
            {
                throw Collision(price.QuotationNo);
            }
        }
    }

    private static InvalidOperationException Collision(string key) =>
        new($"Reserved walkthrough ERP fact '{key}' exists with incompatible tenant facts; the seed will not overwrite it.");
}
