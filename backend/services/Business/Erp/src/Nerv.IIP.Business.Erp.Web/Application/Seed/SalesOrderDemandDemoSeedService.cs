using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.QuotationAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.SalesOrderAggregate;
using Nerv.IIP.Business.Erp.Infrastructure;
using Nerv.IIP.Business.Erp.Web.Application.IntegrationEventConverters;

namespace Nerv.IIP.Business.Erp.Web.Application.Seed;

/// <summary>
/// Explicit, idempotent demo seed for the MAN-517 ERP-to-planning bridge. It only
/// fills the reserved SO-DEMO-001 facts and never overwrites tenant-maintained data.
/// </summary>
public sealed class SalesOrderDemandDemoSeedService(
    ApplicationDbContext dbContext,
    IErpIntegrationEventContextAccessor integrationEventContext)
{
    public const string QuotationNo = "QUO-DEMO-001";
    public const string SalesOrderNo = "SO-DEMO-001";
    public const string CustomerCode = "CUST-DEMO-001";
    public const string SiteCode = "SITE-001";
    public const string SkuCode = "SKU-DEMO-001";

    public async Task SeedAsync(string organizationId, string environmentId, CancellationToken cancellationToken = default)
    {
        if (await dbContext.SalesOrders.AnyAsync(x =>
                x.OrganizationId == organizationId &&
                x.EnvironmentId == environmentId &&
                x.SalesOrderNo == SalesOrderNo,
                cancellationToken))
        {
            return;
        }

        var quotation = await dbContext.Quotations
            .Include(x => x.Lines)
            .SingleOrDefaultAsync(x =>
                x.OrganizationId == organizationId &&
                x.EnvironmentId == environmentId &&
                x.QuotationNo == QuotationNo,
                cancellationToken);
        if (quotation is null)
        {
            quotation = Quotation.Create(
                organizationId,
                environmentId,
                QuotationNo,
                CustomerCode,
                new DateOnly(2027, 12, 31),
                [new QuotationLineDraft("10", SkuCode, "EA", 2m, 100m, new DateOnly(2026, 8, 15))]);
            quotation.Approve();
            dbContext.Quotations.Add(quotation);
        }

        if (!string.Equals(quotation.CustomerCode, CustomerCode, StringComparison.Ordinal) ||
            quotation.Status != QuotationStatus.Approved ||
            quotation.Lines.Count != 1 ||
            !string.Equals(quotation.Lines.Single().SkuCode, SkuCode, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Reserved demo quotation '{QuotationNo}' exists with incompatible tenant facts; the seed will not overwrite it.");
        }

        dbContext.SalesOrders.Add(SalesOrder.CreateFromQuotation(
            SalesOrderNo,
            SiteCode,
            quotation,
            new CustomerCreditSnapshot(CustomerCode, 100_000m, 0m, 0m)));
        using var causationScope = integrationEventContext.BeginScope(
            "seed:man-517-demo",
            "seed:man-517-demo",
            "system:erp-demo-seed");
        await dbContext.SaveEntitiesAsync(cancellationToken);
    }
}
