using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.Erp.Web.Application.Commands.Finance;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Business.Erp.Web.Tests;

/// <summary>
/// GitHub #1360：应付手工登记可对不存在的采购单号与供应商直接入账。
/// 登记必须校验来源单据存在且供应商一致，否则财务账可凭空生成。
/// </summary>
public sealed class AccountPayableSourceDocumentGuardTests
{
    [Fact]
    public async Task Payable_on_a_fabricated_source_document_is_rejected()
    {
        await using var provider = ErpTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Infrastructure.ApplicationDbContext>();

        var exception = await Assert.ThrowsAsync<KnownException>(() => new CreateAccountPayableCommandHandler(dbContext).Handle(
            new CreateAccountPayableCommand("org-001", "env-dev", null, "PO-DOES-NOT-EXIST", "SUP-001", 1m, "CNY"),
            CancellationToken.None));

        Assert.Contains("在 ERP 中不存在", exception.Message, StringComparison.Ordinal);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        Assert.Empty(dbContext.AccountPayables);
        Assert.Empty(dbContext.JournalVouchers);
    }

    [Fact]
    public async Task Payable_on_a_fabricated_supplier_is_rejected_even_when_the_source_document_exists()
    {
        await using var provider = ErpTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Infrastructure.ApplicationDbContext>();
        await ErpFinanceSourceDocumentFixtures.SeedPurchaseReceiptAsync(dbContext, "RCV-GUARD-001", "SUP-REAL");

        var exception = await Assert.ThrowsAsync<KnownException>(() => new CreateAccountPayableCommandHandler(dbContext).Handle(
            new CreateAccountPayableCommand("org-001", "env-dev", null, "RCV-GUARD-001", "SUP-FAKE", 1m, "CNY"),
            CancellationToken.None));

        Assert.Contains("与登记的供应商", exception.Message, StringComparison.Ordinal);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        Assert.Empty(dbContext.AccountPayables);
    }

    [Fact]
    public async Task Payable_is_rejected_when_supplier_exists_but_does_not_match_the_source_document()
    {
        await using var provider = ErpTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Infrastructure.ApplicationDbContext>();
        await ErpFinanceSourceDocumentFixtures.SeedPurchaseReceiptAsync(dbContext, "RCV-GUARD-MISMATCH", "SUP-SOURCE");
        await ErpFinanceSourceDocumentFixtures.SeedPurchaseOrderAsync(dbContext, "PO-OTHER-SUPPLIER", "SUP-OTHER");

        var exception = await Assert.ThrowsAsync<KnownException>(() => new CreateAccountPayableCommandHandler(dbContext).Handle(
            new CreateAccountPayableCommand("org-001", "env-dev", null, "RCV-GUARD-MISMATCH", "SUP-OTHER", 1m, "CNY"),
            CancellationToken.None));

        Assert.Contains("来源单据『RCV-GUARD-MISMATCH』的供应商是『SUP-SOURCE』", exception.Message, StringComparison.Ordinal);
        Assert.Contains("与登记的供应商『SUP-OTHER』不一致", exception.Message, StringComparison.Ordinal);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        Assert.Empty(dbContext.AccountPayables);
    }

    [Fact]
    public async Task Payable_on_a_real_purchase_receipt_with_matching_supplier_is_accepted()
    {
        await using var provider = ErpTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Infrastructure.ApplicationDbContext>();
        await ErpFinanceSourceDocumentFixtures.SeedPurchaseReceiptAsync(dbContext, "RCV-GUARD-002", "SUP-REAL");

        await new CreateAccountPayableCommandHandler(dbContext).Handle(
            new CreateAccountPayableCommand("org-001", "env-dev", "AP-GUARD-002", "RCV-GUARD-002", "SUP-REAL", 120m, "CNY"),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var payable = Assert.Single(dbContext.AccountPayables);
        Assert.Equal("RCV-GUARD-002", payable.SourceDocumentNo);
        Assert.Equal("SUP-REAL", payable.SupplierCode);
        Assert.Equal(120m, payable.Amount);
    }

    [Fact]
    public async Task Supplier_code_case_difference_is_not_treated_as_a_different_supplier()
    {
        await using var provider = ErpTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Infrastructure.ApplicationDbContext>();
        await ErpFinanceSourceDocumentFixtures.SeedPurchaseReceiptAsync(dbContext, "RCV-GUARD-CASE", "SUP-REAL");

        await new CreateAccountPayableCommandHandler(dbContext).Handle(
            new CreateAccountPayableCommand("org-001", "env-dev", "AP-GUARD-CASE", "RCV-GUARD-CASE", "sup-real", 60m, "CNY"),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        Assert.Equal(60m, Assert.Single(dbContext.AccountPayables).Amount);
    }

    [Fact]
    public async Task Payable_can_also_reference_the_purchase_order_behind_the_receipt()
    {
        await using var provider = ErpTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Infrastructure.ApplicationDbContext>();
        await ErpFinanceSourceDocumentFixtures.SeedPurchaseReceiptAsync(dbContext, "RCV-GUARD-003", "SUP-REAL");
        var purchaseOrderNo = await dbContext.PurchaseOrders.Select(x => x.PurchaseOrderNo).SingleAsync(CancellationToken.None);

        await new CreateAccountPayableCommandHandler(dbContext).Handle(
            new CreateAccountPayableCommand("org-001", "env-dev", "AP-GUARD-003", purchaseOrderNo, "SUP-REAL", 80m, "CNY"),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        Assert.Equal(purchaseOrderNo, Assert.Single(dbContext.AccountPayables).SourceDocumentNo);
    }
}
