using Nerv.IIP.Business.Erp.Web.Application.Auth;
using Nerv.IIP.Business.Erp.Web.Endpoints.Erp;
using Nerv.IIP.ServiceAuth;

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

        Assert.Equal(5, contracts.Length);
        Assert.Contains(contracts, x => x.Route == "/api/business/v1/erp/finance/payables" && x.PermissionCode == ErpPermissionCodes.FinanceManage && x.AuthorizationPolicy == InternalServiceAuthorizationPolicy.Name && x.OperationId == "createErpAccountPayable");
        Assert.Contains(contracts, x => x.Route == "/api/business/v1/erp/finance/receivables" && x.OperationId == "createErpAccountReceivable");
        Assert.Contains(contracts, x => x.Route == "/api/business/v1/erp/finance/cost-candidates" && x.OperationId == "createErpCostCandidate");
        Assert.Contains(contracts, x => x.Route == "/api/business/v1/erp/finance/vouchers" && x.OperationId == "postErpJournalVoucher");
        Assert.Contains(contracts, x => x.Route == "/api/business/v1/erp/finance/summary" && x.HttpMethod == "GET" && x.PermissionCode == ErpPermissionCodes.FinanceRead && x.OperationId == "getErpFinanceSummary");
    }
}
