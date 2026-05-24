using Nerv.IIP.Business.Erp.Web.Application.Auth;
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

        Assert.Equal(8, contracts.Length);
        Assert.Contains(contracts, x => x.Route == "/api/business/v1/erp/finance/payables" && x.PermissionCode == ErpPermissionCodes.FinanceManage && x.AuthorizationPolicy == InternalServiceAuthorizationPolicy.Name && x.OperationId == "createErpAccountPayable");
        Assert.Contains(contracts, x => x.Route == "/api/business/v1/erp/finance/receivables" && x.OperationId == "createErpAccountReceivable");
        Assert.Contains(contracts, x => x.Route == "/api/business/v1/erp/finance/cost-candidates" && x.OperationId == "createErpCostCandidate");
        Assert.Contains(contracts, x => x.Route == "/api/business/v1/erp/finance/vouchers" && x.OperationId == "postErpJournalVoucher");
        Assert.Contains(contracts, x => x.Route == "/api/business/v1/erp/finance/summary" && x.HttpMethod == "GET" && x.PermissionCode == ErpPermissionCodes.FinanceRead && x.OperationId == "getErpFinanceSummary");
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
