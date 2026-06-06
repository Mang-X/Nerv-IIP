using Nerv.IIP.Business.Erp.Web.Application.Auth;
using Nerv.IIP.Business.Erp.Web.Application.Commands.Sales;
using Nerv.IIP.Business.Erp.Web.Application.Commands.Finance;
using Nerv.IIP.Business.Erp.Web.Application.Queries.SalesFinance;
using Nerv.IIP.Business.Erp.Web.Endpoints.Erp;
using Nerv.IIP.ServiceAuth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Nerv.IIP.Business.Erp.Web.Tests;

public sealed class ErpSalesFinanceEndpointContractTests
{
    [Fact]
    public void Erp_sales_endpoints_expose_issue_138_routes_permissions_policies_and_operation_ids()
    {
        var contracts = ErpSalesEndpointContracts.All.ToArray();

        Assert.Equal(6, contracts.Length);
        Assert.Contains(contracts, x => x.Route == "/api/business/v1/erp/opportunities" && x.PermissionCode == ErpPermissionCodes.SalesManage && x.AuthorizationPolicy == InternalServiceAuthorizationPolicy.Name && x.OperationId == "openErpOpportunity");
        Assert.Contains(contracts, x => x.Route == "/api/business/v1/erp/quotations" && x.OperationId == "createErpQuotation");
        Assert.Contains(contracts, x => x.Route == "/api/business/v1/erp/quotations/{quotationId}/approve" && x.OperationId == "approveErpQuotation");
        Assert.Contains(contracts, x => x.Route == "/api/business/v1/erp/sales-orders" && x.OperationId == "createErpSalesOrder");
        Assert.Contains(contracts, x => x.Route == "/api/business/v1/erp/delivery-orders" && x.OperationId == "releaseErpDeliveryOrder");
        Assert.Contains(contracts, x => x.Route == "/api/business/v1/erp/sales-orders" && x.HttpMethod == "GET" && x.PermissionCode == ErpPermissionCodes.SalesRead && x.OperationId == "listErpSalesOrders");
    }

    [Fact]
    public void Erp_finance_endpoints_expose_issue_139_routes_permissions_policies_and_operation_ids()
    {
        var contracts = ErpFinanceEndpointContracts.All.ToArray();

        Assert.Equal(11, contracts.Length);
        Assert.Contains(contracts, x => x.Route == "/api/business/v1/erp/finance/payables" && x.PermissionCode == ErpPermissionCodes.FinanceManage && x.AuthorizationPolicy == InternalServiceAuthorizationPolicy.Name && x.OperationId == "createErpAccountPayable");
        Assert.Contains(contracts, x => x.Route == "/api/business/v1/erp/finance/receivables" && x.OperationId == "createErpAccountReceivable");
        Assert.Contains(contracts, x => x.Route == "/api/business/v1/erp/finance/cost-candidates" && x.OperationId == "createErpCostCandidate");
        Assert.Contains(contracts, x => x.Route == "/api/business/v1/erp/finance/vouchers" && x.OperationId == "postErpJournalVoucher");
        Assert.Contains(contracts, x => x.Route == "/api/business/v1/erp/finance/summary" && x.HttpMethod == "GET" && x.PermissionCode == ErpPermissionCodes.FinanceRead && x.OperationId == "getErpFinanceSummary");
        Assert.Contains(contracts, x => x.Route == "/api/business/v1/erp/finance/payables" && x.HttpMethod == "GET" && x.PermissionCode == ErpPermissionCodes.FinanceRead && x.OperationId == "listErpAccountPayables");
        Assert.Contains(contracts, x => x.Route == "/api/business/v1/erp/finance/receivables" && x.HttpMethod == "GET" && x.PermissionCode == ErpPermissionCodes.FinanceRead && x.OperationId == "listErpAccountReceivables");
        Assert.Contains(contracts, x => x.Route == "/api/business/v1/erp/finance/cost-candidates" && x.HttpMethod == "GET" && x.PermissionCode == ErpPermissionCodes.FinanceRead && x.OperationId == "listErpCostCandidates");
        Assert.Contains(contracts, x => x.Route == "/api/business/v1/erp/finance/payables/by-source" && x.HttpMethod == "GET" && x.PermissionCode == ErpPermissionCodes.FinanceRead && x.OperationId == "getErpPayableBySourceDocument");
        Assert.Contains(contracts, x => x.Route == "/api/business/v1/erp/finance/receivables/by-source" && x.HttpMethod == "GET" && x.PermissionCode == ErpPermissionCodes.FinanceRead && x.OperationId == "getErpReceivableBySourceDocument");
        Assert.Contains(contracts, x => x.Route == "/api/business/v1/erp/finance/cost-candidates/by-source" && x.HttpMethod == "GET" && x.PermissionCode == ErpPermissionCodes.FinanceRead && x.OperationId == "getErpCostCandidateBySourceDocument");
    }

    [Fact]
    public async Task Finance_source_document_queries_return_precise_ap_ar_and_cost_facts()
    {
        await using var provider = ErpTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Infrastructure.ApplicationDbContext>();
        await new CreateAccountPayableCommandHandler(dbContext).Handle(
            new CreateAccountPayableCommand("org-001", "env-dev", "AP-001", "RCV-001", "SUP-001", 125.50m, "CNY"),
            CancellationToken.None);
        await new CreateAccountReceivableCommandHandler(dbContext).Handle(
            new CreateAccountReceivableCommand("org-001", "env-dev", "AR-001", "DO-001", "CUS-001", 250.75m, "CNY"),
            CancellationToken.None);
        await new CreateCostCandidateCommandHandler(dbContext).Handle(
            new CreateCostCandidateCommand("org-001", "env-dev", "COST-001", "production-report", "RPT-001", 90.25m, "CNY"),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var payable = await new GetAccountPayableBySourceDocumentQueryHandler(dbContext).Handle(
            new GetAccountPayableBySourceDocumentQuery("org-001", "env-dev", "RCV-001"),
            CancellationToken.None);
        var receivable = await new GetAccountReceivableBySourceDocumentQueryHandler(dbContext).Handle(
            new GetAccountReceivableBySourceDocumentQuery("org-001", "env-dev", "DO-001"),
            CancellationToken.None);
        var cost = await new GetCostCandidateBySourceDocumentQueryHandler(dbContext).Handle(
            new GetCostCandidateBySourceDocumentQuery("org-001", "env-dev", "production-report", "RPT-001"),
            CancellationToken.None);

        Assert.Equal("AP-001", payable.PayableNo);
        Assert.Equal("RCV-001", payable.SourceDocumentNo);
        Assert.Equal(125.50m, payable.Amount);
        Assert.Equal("CNY", payable.CurrencyCode);
        Assert.Equal("AR-001", receivable.ReceivableNo);
        Assert.Equal("DO-001", receivable.SourceDocumentNo);
        Assert.Equal(250.75m, receivable.Amount);
        Assert.Equal("COST-001", cost.CandidateNo);
        Assert.Equal("production-report", cost.SourceType);
        Assert.Equal("RPT-001", cost.SourceDocumentNo);
        Assert.Equal(90.25m, cost.Amount);
    }

    [Fact]
    public async Task List_sales_orders_query_applies_status_keyword_and_server_paging()
    {
        await using var provider = ErpTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Infrastructure.ApplicationDbContext>();
        await CreateReleasedSalesOrderAsync(dbContext, "SO-001", "QUO-001", "CUS-001", "SKU-FG-001");
        await CreateReleasedSalesOrderAsync(dbContext, "SO-002", "QUO-002", "CUS-002", "SKU-FG-002");
        await CreateReleasedSalesOrderAsync(dbContext, "SO-003", "QUO-003", "CUS-002", "SKU-FG-003", "org-other");

        var response = await new ListSalesOrdersQueryHandler(dbContext).Handle(
            new ListSalesOrdersQuery("org-001", "env-dev", "Released", "CUS-002", 0, 1),
            CancellationToken.None);

        Assert.Equal(1, response.Total);
        var item = Assert.Single(response.Items);
        Assert.Equal("SO-002", item.SalesOrderNo);
        Assert.Equal("released", item.Status);
    }

    [Fact]
    public async Task Finance_list_queries_apply_status_keyword_and_server_paging()
    {
        await using var provider = ErpTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Infrastructure.ApplicationDbContext>();
        await new CreateAccountPayableCommandHandler(dbContext).Handle(
            new CreateAccountPayableCommand("org-001", "env-dev", "AP-001", "RCV-001", "SUP-001", 125.50m, "CNY"),
            CancellationToken.None);
        await new CreateAccountPayableCommandHandler(dbContext).Handle(
            new CreateAccountPayableCommand("org-001", "env-dev", "AP-002", "RCV-002", "SUP-002", 225.50m, "CNY"),
            CancellationToken.None);
        await new CreateAccountReceivableCommandHandler(dbContext).Handle(
            new CreateAccountReceivableCommand("org-001", "env-dev", "AR-001", "DO-001", "CUS-001", 250.75m, "CNY"),
            CancellationToken.None);
        await new CreateCostCandidateCommandHandler(dbContext).Handle(
            new CreateCostCandidateCommand("org-001", "env-dev", "COST-001", "production-report", "RPT-001", 90.25m, "CNY"),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var payables = await new ListAccountPayablesQueryHandler(dbContext).Handle(
            new ListAccountPayablesQuery("org-001", "env-dev", "open", "SUP-002", 0, 1),
            CancellationToken.None);
        var receivables = await new ListAccountReceivablesQueryHandler(dbContext).Handle(
            new ListAccountReceivablesQuery("org-001", "env-dev", "open", "CUS-001", 0, 10),
            CancellationToken.None);
        var costs = await new ListCostCandidatesQueryHandler(dbContext).Handle(
            new ListCostCandidatesQuery("org-001", "env-dev", "pending", "production", 0, 10),
            CancellationToken.None);

        Assert.Equal(1, payables.Total);
        Assert.Equal("AP-002", Assert.Single(payables.Items).PayableNo);
        Assert.Equal(1, receivables.Total);
        Assert.Equal("AR-001", Assert.Single(receivables.Items).ReceivableNo);
        Assert.Equal(1, costs.Total);
        Assert.Equal("COST-001", Assert.Single(costs.Items).CandidateNo);
    }

    [Fact]
    public async Task Finance_list_queries_reject_unknown_status_and_cap_take()
    {
        await using var provider = ErpTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Infrastructure.ApplicationDbContext>();
        await new CreateAccountPayableCommandHandler(dbContext).Handle(
            new CreateAccountPayableCommand("org-001", "env-dev", "AP-001", "RCV-001", "SUP-001", 125.50m, "CNY"),
            CancellationToken.None);
        await new CreateAccountReceivableCommandHandler(dbContext).Handle(
            new CreateAccountReceivableCommand("org-001", "env-dev", "AR-001", "DO-001", "CUS-001", 250.75m, "CNY"),
            CancellationToken.None);
        for (var index = 1; index <= 501; index++)
        {
            await new CreateCostCandidateCommandHandler(dbContext).Handle(
                new CreateCostCandidateCommand("org-001", "env-dev", $"COST-{index:D3}", "production-report", $"RPT-{index:D3}", 90.25m, "CNY"),
                CancellationToken.None);
        }

        await dbContext.SaveChangesAsync(CancellationToken.None);

        var payables = await new ListAccountPayablesQueryHandler(dbContext).Handle(
            new ListAccountPayablesQuery("org-001", "env-dev", "not-a-status", null, 0, 100),
            CancellationToken.None);
        var receivables = await new ListAccountReceivablesQueryHandler(dbContext).Handle(
            new ListAccountReceivablesQuery("org-001", "env-dev", "not-a-status", null, 0, 100),
            CancellationToken.None);
        var cappedCosts = await new ListCostCandidatesQueryHandler(dbContext).Handle(
            new ListCostCandidatesQuery("org-001", "env-dev", null, null, 0, 1000),
            CancellationToken.None);

        Assert.Equal(0, payables.Total);
        Assert.Empty(payables.Items);
        Assert.Equal(0, receivables.Total);
        Assert.Empty(receivables.Items);
        Assert.Equal(501, cappedCosts.Total);
        Assert.Equal(500, cappedCosts.Items.Count);
    }

    private static async Task CreateReleasedSalesOrderAsync(
        Infrastructure.ApplicationDbContext dbContext,
        string salesOrderNo,
        string quotationNo,
        string customerCode,
        string skuCode,
        string organizationId = "org-001")
    {
        await new CreateQuotationCommandHandler(dbContext).Handle(
            new CreateQuotationCommand(
                organizationId,
                "env-dev",
                quotationNo,
                customerCode,
                new DateOnly(2026, 12, 31),
                [new QuotationCommandLine("LINE-001", skuCode, "EA", 2m, 100m, new DateOnly(2026, 7, 1))]),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        await new ApproveQuotationCommandHandler(dbContext).Handle(
            new ApproveQuotationCommand(organizationId, "env-dev", quotationNo),
            CancellationToken.None);
        await new CreateSalesOrderCommandHandler(dbContext).Handle(
            new CreateSalesOrderCommand(organizationId, "env-dev", salesOrderNo, quotationNo),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
    }
}

internal static class ErpTestProvider
{
    public static ServiceProvider CreateInMemoryProvider()
    {
        var services = new ServiceCollection();
        services.AddMediatR(configuration => configuration.RegisterServicesFromAssembly(typeof(Program).Assembly));
        services.AddDbContext<Infrastructure.ApplicationDbContext>(options =>
            options.UseInMemoryDatabase($"erp-sales-finance-contract-{Guid.NewGuid():N}"));
        return services.BuildServiceProvider();
    }
}
