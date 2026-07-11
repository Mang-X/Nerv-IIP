using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.AccountReceivableAggregate;
using Nerv.IIP.Business.Erp.Web.Application.Commands;
using Nerv.IIP.Business.Erp.Web.Application.Commands.Finance;
using Nerv.IIP.Business.Erp.Web.Application.Commands.Sales;
using Nerv.IIP.Business.Erp.Web.Application.MasterData;

namespace Nerv.IIP.Business.Erp.Web.Tests;

public sealed class ErpReturnCommandTests
{
    [Fact]
    public async Task Authorize_rma_uses_sales_order_and_open_ar_facts_without_creating_a_wms_identifier()
    {
        await using var provider = ErpTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Infrastructure.ApplicationDbContext>();
        await CreateReleasedSalesOrderAsync(dbContext);
        await new CreateAccountReceivableCommandHandler(dbContext).Handle(
            new CreateAccountReceivableCommand("org-001", "env-dev", "AR-RMA-001", "DO-RMA-001", "CUST-001", 200m, "CNY"),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var rmaId = await new CreateSalesReturnAuthorizationCommandHandler(dbContext, new ErpCodingService()).Handle(
            new CreateSalesReturnAuthorizationCommand(
                "org-001",
                "env-dev",
                "RMA-001",
                "SO-RMA-001",
                "AR-RMA-001",
                "SITE-01",
                [new SalesReturnAuthorizationCommandLine("LINE-001", 1m, "LOC-RETURN", null)],
                "rma-command-001"),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var rma = await dbContext.SalesReturnAuthorizations.SingleAsync(x => x.Id == rmaId);
        Assert.Equal("RMA-001", rma.RmaNo);
        Assert.Null(rma.WmsInboundOrderNo);
        Assert.Equal("CUST-001", rma.CustomerCode);
    }

    private static async Task CreateReleasedSalesOrderAsync(Infrastructure.ApplicationDbContext dbContext)
    {
        await new CreateQuotationCommandHandler(dbContext).Handle(
            new CreateQuotationCommand(
                "org-001",
                "env-dev",
                "QUO-RMA-001",
                "CUST-001",
                new DateOnly(2026, 12, 31),
                [new QuotationCommandLine("LINE-001", "SKU-RMA-001", "EA", 2m, 100m, new DateOnly(2026, 7, 1))]),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        await new ApproveQuotationCommandHandler(dbContext).Handle(
            new ApproveQuotationCommand("org-001", "env-dev", "QUO-RMA-001"),
            CancellationToken.None);
        await new CreateSalesOrderCommandHandler(
                dbContext,
                new StaticCustomerCreditProfileReader(new CustomerCreditProfile("CUST-001", 1_000_000m, "CNY"))).Handle(
                new CreateSalesOrderCommand("org-001", "env-dev", "SO-RMA-001", "QUO-RMA-001"),
                CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        await new ReleaseDeliveryOrderCommandHandler(dbContext).Handle(
            new ReleaseDeliveryOrderCommand("org-001", "env-dev", "DO-RMA-001", "SO-RMA-001", [new DeliveryOrderCommandLine("LINE-001", 2m)]),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
    }
}
