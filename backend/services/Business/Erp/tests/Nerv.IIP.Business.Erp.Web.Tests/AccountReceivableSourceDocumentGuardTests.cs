using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.Erp.Web.Application.Commands.Finance;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Business.Erp.Web.Tests;

/// <summary>
/// GitHub #1349：应收手工登记可对不存在的销售订单号与客户直接入账（实测 200 产生垃圾单）。
/// 登记必须校验来源单据存在且客户一致，否则财务账可凭空生成。
/// </summary>
public sealed class AccountReceivableSourceDocumentGuardTests
{
    [Fact]
    public async Task Receivable_on_a_fabricated_source_document_is_rejected()
    {
        await using var provider = ErpTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Infrastructure.ApplicationDbContext>();

        var exception = await Assert.ThrowsAsync<KnownException>(() => new CreateAccountReceivableCommandHandler(dbContext).Handle(
            new CreateAccountReceivableCommand("org-001", "env-dev", null, "SO-DOES-NOT-EXIST", "CUST-001", 1m, "CNY"),
            CancellationToken.None));

        Assert.Contains("在 ERP 中不存在", exception.Message, StringComparison.Ordinal);
        Assert.Contains("财务 › 会计凭证 › 过账凭证", exception.Message, StringComparison.Ordinal);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        Assert.Empty(dbContext.AccountReceivables);
        Assert.Empty(dbContext.JournalVouchers);
    }

    [Fact]
    public async Task Receivable_on_a_fabricated_customer_is_rejected_even_when_the_source_document_exists()
    {
        await using var provider = ErpTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Infrastructure.ApplicationDbContext>();
        await ErpFinanceSourceDocumentFixtures.SeedDeliveryOrderAsync(dbContext, "DO-GUARD-001", "CUST-REAL");

        var exception = await Assert.ThrowsAsync<KnownException>(() => new CreateAccountReceivableCommandHandler(dbContext).Handle(
            new CreateAccountReceivableCommand("org-001", "env-dev", null, "DO-GUARD-001", "CUST-FAKE", 1m, "CNY"),
            CancellationToken.None));

        Assert.Contains("与登记的客户", exception.Message, StringComparison.Ordinal);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        Assert.Empty(dbContext.AccountReceivables);
    }

    [Fact]
    public async Task Receivable_on_a_real_delivery_order_with_matching_customer_is_accepted()
    {
        await using var provider = ErpTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Infrastructure.ApplicationDbContext>();
        await ErpFinanceSourceDocumentFixtures.SeedDeliveryOrderAsync(dbContext, "DO-GUARD-002", "CUST-REAL");

        await new CreateAccountReceivableCommandHandler(dbContext).Handle(
            new CreateAccountReceivableCommand("org-001", "env-dev", "AR-GUARD-002", " DO-GUARD-002 ", "CUST-REAL", 120m, "CNY"),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var receivable = Assert.Single(dbContext.AccountReceivables);
        Assert.Equal("DO-GUARD-002", receivable.SourceDocumentNo);
        Assert.Equal("CUST-REAL", receivable.CustomerCode);
        Assert.Equal(120m, receivable.Amount);
    }

    [Fact]
    public async Task Customer_code_case_difference_is_not_treated_as_a_different_customer()
    {
        await using var provider = ErpTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Infrastructure.ApplicationDbContext>();
        await ErpFinanceSourceDocumentFixtures.SeedDeliveryOrderAsync(dbContext, "DO-GUARD-CASE", "CUST-REAL");

        await new CreateAccountReceivableCommandHandler(dbContext).Handle(
            new CreateAccountReceivableCommand("org-001", "env-dev", "AR-GUARD-CASE", "DO-GUARD-CASE", "cust-real", 60m, "CNY"),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var receivable = Assert.Single(dbContext.AccountReceivables);
        Assert.Equal(60m, receivable.Amount);
        Assert.Equal("CUST-REAL", receivable.CustomerCode);
    }

    [Fact]
    public async Task Receivable_can_also_reference_the_sales_order_behind_the_delivery()
    {
        await using var provider = ErpTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Infrastructure.ApplicationDbContext>();
        await ErpFinanceSourceDocumentFixtures.SeedDeliveryOrderAsync(dbContext, "DO-GUARD-003", "CUST-REAL");
        var salesOrderNo = await dbContext.SalesOrders.Select(x => x.SalesOrderNo).SingleAsync(CancellationToken.None);

        await new CreateAccountReceivableCommandHandler(dbContext).Handle(
            new CreateAccountReceivableCommand("org-001", "env-dev", "AR-GUARD-003", salesOrderNo, "CUST-REAL", 80m, "CNY"),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        Assert.Equal(salesOrderNo, Assert.Single(dbContext.AccountReceivables).SourceDocumentNo);
    }
}
