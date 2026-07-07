using FastEndpoints;
using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.BusinessGateway.Web.Application.OpenApi;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.BusinessGateway.Web.Endpoints.Erp;

[Tags("Business Console ERP")]
[HttpGet("/api/business-console/v1/erp/sales/sales-orders")]
[BusinessGatewayOperationId("listBusinessConsoleErpSalesOrders")]
public sealed class ListBusinessConsoleErpSalesOrdersEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessErpClient erp,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleErpListRequest, BusinessConsoleErpSalesOrderListResponse>(
        auth,
        BusinessGatewayPermissions.ErpSalesRead)
{
    protected override string OrganizationId(BusinessConsoleErpListRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleErpListRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleErpSalesOrderListResponse> ForwardAsync(
        BusinessConsoleErpListRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        erp.ListSalesOrdersAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console ERP")]
[HttpGet("/api/business-console/v1/erp/sales/opportunities")]
[BusinessGatewayOperationId("listBusinessConsoleErpOpportunities")]
public sealed class ListBusinessConsoleErpOpportunitiesEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessErpClient erp,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleErpListRequest, BusinessConsoleErpOpportunityListResponse>(
        auth,
        BusinessGatewayPermissions.ErpSalesRead)
{
    protected override string OrganizationId(BusinessConsoleErpListRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleErpListRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleErpOpportunityListResponse> ForwardAsync(
        BusinessConsoleErpListRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        erp.ListOpportunitiesAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console ERP")]
[HttpPost("/api/business-console/v1/erp/sales/opportunities")]
[BusinessGatewayOperationId("openBusinessConsoleErpOpportunity")]
public sealed class OpenBusinessConsoleErpOpportunityEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessErpClient erp,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleOpenErpOpportunityRequest, BusinessConsoleOpenErpOpportunityResponse>(
        auth,
        BusinessGatewayPermissions.ErpSalesManage)
{
    protected override string OrganizationId(BusinessConsoleOpenErpOpportunityRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleOpenErpOpportunityRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleOpenErpOpportunityResponse> ForwardAsync(
        BusinessConsoleOpenErpOpportunityRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        erp.OpenOpportunityAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console ERP")]
[HttpGet("/api/business-console/v1/erp/sales/quotations")]
[BusinessGatewayOperationId("listBusinessConsoleErpQuotations")]
public sealed class ListBusinessConsoleErpQuotationsEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessErpClient erp,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleErpListRequest, BusinessConsoleErpQuotationListResponse>(
        auth,
        BusinessGatewayPermissions.ErpSalesRead)
{
    protected override string OrganizationId(BusinessConsoleErpListRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleErpListRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleErpQuotationListResponse> ForwardAsync(
        BusinessConsoleErpListRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        erp.ListQuotationsAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console ERP")]
[HttpPost("/api/business-console/v1/erp/sales/quotations")]
[BusinessGatewayOperationId("createBusinessConsoleErpQuotation")]
public sealed class CreateBusinessConsoleErpQuotationEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessErpClient erp,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleCreateErpQuotationRequest, BusinessConsoleCreateErpQuotationResponse>(
        auth,
        BusinessGatewayPermissions.ErpSalesManage)
{
    protected override string OrganizationId(BusinessConsoleCreateErpQuotationRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleCreateErpQuotationRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleCreateErpQuotationResponse> ForwardAsync(
        BusinessConsoleCreateErpQuotationRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        erp.CreateQuotationAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console ERP")]
[HttpPost("/api/business-console/v1/erp/sales/quotations/{quotationNo}/approve")]
[BusinessGatewayOperationId("approveBusinessConsoleErpQuotation")]
public sealed class ApproveBusinessConsoleErpQuotationEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessErpClient erp,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleApproveErpQuotationRequest, string>(
        auth,
        BusinessGatewayPermissions.ErpSalesManage)
{
    protected override string OrganizationId(BusinessConsoleApproveErpQuotationRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleApproveErpQuotationRequest request) => request.EnvironmentId;

    protected override string ResourceType(BusinessConsoleApproveErpQuotationRequest request) => "erp-quotation";

    protected override string? ResourceId(BusinessConsoleApproveErpQuotationRequest request) =>
        Route<string>("quotationNo") ?? request.QuotationNo;

    protected override Task<string> ForwardAsync(
        BusinessConsoleApproveErpQuotationRequest request,
        string bearerToken,
        CancellationToken cancellationToken)
    {
        var downstreamRequest = request with { QuotationNo = Route<string>("quotationNo") ?? request.QuotationNo };
        return erp.ApproveQuotationAsync(tokenProvider.BearerToken, downstreamRequest, cancellationToken);
    }
}

[Tags("Business Console ERP")]
[HttpPost("/api/business-console/v1/erp/sales/sales-orders")]
[BusinessGatewayOperationId("createBusinessConsoleErpSalesOrder")]
public sealed class CreateBusinessConsoleErpSalesOrderEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessErpClient erp,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleCreateErpSalesOrderRequest, BusinessConsoleCreateErpSalesOrderResponse>(
        auth,
        BusinessGatewayPermissions.ErpSalesManage)
{
    protected override string OrganizationId(BusinessConsoleCreateErpSalesOrderRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleCreateErpSalesOrderRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleCreateErpSalesOrderResponse> ForwardAsync(
        BusinessConsoleCreateErpSalesOrderRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        erp.CreateSalesOrderAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console ERP")]
[HttpGet("/api/business-console/v1/erp/sales/delivery-orders")]
[BusinessGatewayOperationId("listBusinessConsoleErpDeliveryOrders")]
public sealed class ListBusinessConsoleErpDeliveryOrdersEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessErpClient erp,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleErpListRequest, BusinessConsoleErpDeliveryOrderListResponse>(
        auth,
        BusinessGatewayPermissions.ErpSalesRead)
{
    protected override string OrganizationId(BusinessConsoleErpListRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleErpListRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleErpDeliveryOrderListResponse> ForwardAsync(
        BusinessConsoleErpListRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        erp.ListDeliveryOrdersAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console ERP")]
[HttpPost("/api/business-console/v1/erp/sales/delivery-orders")]
[BusinessGatewayOperationId("releaseBusinessConsoleErpDeliveryOrder")]
public sealed class ReleaseBusinessConsoleErpDeliveryOrderEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessErpClient erp,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleReleaseErpDeliveryOrderRequest, BusinessConsoleReleaseErpDeliveryOrderResponse>(
        auth,
        BusinessGatewayPermissions.ErpSalesManage)
{
    protected override string OrganizationId(BusinessConsoleReleaseErpDeliveryOrderRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleReleaseErpDeliveryOrderRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleReleaseErpDeliveryOrderResponse> ForwardAsync(
        BusinessConsoleReleaseErpDeliveryOrderRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        erp.ReleaseDeliveryOrderAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console ERP")]
[HttpPost("/api/business-console/v1/erp/finance/payables")]
[BusinessGatewayOperationId("createBusinessConsoleErpAccountPayable")]
public sealed class CreateBusinessConsoleErpAccountPayableEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessErpClient erp,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleCreateErpAccountPayableRequest, BusinessConsoleCreateErpAccountPayableResponse>(
        auth,
        BusinessGatewayPermissions.ErpFinanceManage)
{
    protected override string OrganizationId(BusinessConsoleCreateErpAccountPayableRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleCreateErpAccountPayableRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleCreateErpAccountPayableResponse> ForwardAsync(
        BusinessConsoleCreateErpAccountPayableRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        erp.CreateAccountPayableAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console ERP")]
[HttpPost("/api/business-console/v1/erp/finance/receivables")]
[BusinessGatewayOperationId("createBusinessConsoleErpAccountReceivable")]
public sealed class CreateBusinessConsoleErpAccountReceivableEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessErpClient erp,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleCreateErpAccountReceivableRequest, BusinessConsoleCreateErpAccountReceivableResponse>(
        auth,
        BusinessGatewayPermissions.ErpFinanceManage)
{
    protected override string OrganizationId(BusinessConsoleCreateErpAccountReceivableRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleCreateErpAccountReceivableRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleCreateErpAccountReceivableResponse> ForwardAsync(
        BusinessConsoleCreateErpAccountReceivableRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        erp.CreateAccountReceivableAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console ERP")]
[HttpPost("/api/business-console/v1/erp/finance/cost-candidates")]
[BusinessGatewayOperationId("createBusinessConsoleErpCostCandidate")]
public sealed class CreateBusinessConsoleErpCostCandidateEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessErpClient erp,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleCreateErpCostCandidateRequest, BusinessConsoleCreateErpCostCandidateResponse>(
        auth,
        BusinessGatewayPermissions.ErpFinanceManage)
{
    protected override string OrganizationId(BusinessConsoleCreateErpCostCandidateRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleCreateErpCostCandidateRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleCreateErpCostCandidateResponse> ForwardAsync(
        BusinessConsoleCreateErpCostCandidateRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        erp.CreateCostCandidateAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console ERP")]
[HttpPost("/api/business-console/v1/erp/finance/vouchers")]
[BusinessGatewayOperationId("postBusinessConsoleErpJournalVoucher")]
public sealed class PostBusinessConsoleErpJournalVoucherEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessErpClient erp,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsolePostErpJournalVoucherRequest, BusinessConsolePostErpJournalVoucherResponse>(
        auth,
        BusinessGatewayPermissions.ErpFinanceManage)
{
    protected override string OrganizationId(BusinessConsolePostErpJournalVoucherRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsolePostErpJournalVoucherRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsolePostErpJournalVoucherResponse> ForwardAsync(
        BusinessConsolePostErpJournalVoucherRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        erp.PostJournalVoucherAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console ERP")]
[HttpPost("/api/business-console/v1/erp/finance/payment-executions")]
[BusinessGatewayOperationId("approveBusinessConsoleErpPaymentExecution")]
public sealed class ApproveBusinessConsoleErpPaymentExecutionEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessErpClient erp,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleApproveErpPaymentExecutionRequest, string>(
        auth,
        BusinessGatewayPermissions.ErpFinanceManage)
{
    protected override string OrganizationId(BusinessConsoleApproveErpPaymentExecutionRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleApproveErpPaymentExecutionRequest request) => request.EnvironmentId;

    protected override Task<string> ForwardAsync(
        BusinessConsoleApproveErpPaymentExecutionRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        erp.ApprovePaymentExecutionAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console ERP")]
[HttpPost("/api/business-console/v1/erp/finance/payment-executions/{paymentExecutionNo}/execute")]
[BusinessGatewayOperationId("executeBusinessConsoleErpPaymentExecution")]
public sealed class ExecuteBusinessConsoleErpPaymentExecutionEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessErpClient erp,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleExecuteErpPaymentExecutionRequest, string>(
        auth,
        BusinessGatewayPermissions.ErpFinanceManage)
{
    protected override string OrganizationId(BusinessConsoleExecuteErpPaymentExecutionRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleExecuteErpPaymentExecutionRequest request) => request.EnvironmentId;

    protected override Task<string> ForwardAsync(
        BusinessConsoleExecuteErpPaymentExecutionRequest request,
        string bearerToken,
        CancellationToken cancellationToken)
    {
        var downstreamRequest = request with { PaymentExecutionNo = Route<string>("paymentExecutionNo") ?? request.PaymentExecutionNo };
        return erp.ExecutePaymentExecutionAsync(tokenProvider.BearerToken, downstreamRequest, cancellationToken);
    }
}

[Tags("Business Console ERP")]
[HttpPost("/api/business-console/v1/erp/finance/cash-receipts")]
[BusinessGatewayOperationId("registerBusinessConsoleErpCashReceipt")]
public sealed class RegisterBusinessConsoleErpCashReceiptEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessErpClient erp,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleRegisterErpCashReceiptRequest, string>(
        auth,
        BusinessGatewayPermissions.ErpFinanceManage)
{
    protected override string OrganizationId(BusinessConsoleRegisterErpCashReceiptRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleRegisterErpCashReceiptRequest request) => request.EnvironmentId;

    protected override Task<string> ForwardAsync(
        BusinessConsoleRegisterErpCashReceiptRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        erp.RegisterCashReceiptAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console ERP")]
[HttpPost("/api/business-console/v1/erp/finance/cash-receipts/{cashReceiptNo}/match")]
[BusinessGatewayOperationId("matchBusinessConsoleErpCashReceipt")]
public sealed class MatchBusinessConsoleErpCashReceiptEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessErpClient erp,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleMatchErpCashReceiptRequest, string>(
        auth,
        BusinessGatewayPermissions.ErpFinanceManage)
{
    protected override string OrganizationId(BusinessConsoleMatchErpCashReceiptRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleMatchErpCashReceiptRequest request) => request.EnvironmentId;

    protected override Task<string> ForwardAsync(
        BusinessConsoleMatchErpCashReceiptRequest request,
        string bearerToken,
        CancellationToken cancellationToken)
    {
        var downstreamRequest = request with { CashReceiptNo = Route<string>("cashReceiptNo") ?? request.CashReceiptNo };
        return erp.MatchCashReceiptAsync(tokenProvider.BearerToken, downstreamRequest, cancellationToken);
    }
}

[Tags("Business Console ERP")]
[HttpPost("/api/business-console/v1/erp/finance/accounting-periods")]
[BusinessGatewayOperationId("openBusinessConsoleErpAccountingPeriod")]
public sealed class OpenBusinessConsoleErpAccountingPeriodEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessErpClient erp,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleOpenErpAccountingPeriodRequest, BusinessConsoleOpenErpAccountingPeriodResponse>(
        auth,
        BusinessGatewayPermissions.ErpFinanceManage)
{
    protected override string OrganizationId(BusinessConsoleOpenErpAccountingPeriodRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleOpenErpAccountingPeriodRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleOpenErpAccountingPeriodResponse> ForwardAsync(
        BusinessConsoleOpenErpAccountingPeriodRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        erp.OpenAccountingPeriodAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console ERP")]
[HttpPost("/api/business-console/v1/erp/finance/accounting-periods/close")]
[BusinessGatewayOperationId("closeBusinessConsoleErpAccountingPeriod")]
public sealed class CloseBusinessConsoleErpAccountingPeriodEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessErpClient erp,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleCloseErpAccountingPeriodRequest, string>(
        auth,
        BusinessGatewayPermissions.ErpFinanceManage)
{
    protected override string OrganizationId(BusinessConsoleCloseErpAccountingPeriodRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleCloseErpAccountingPeriodRequest request) => request.EnvironmentId;

    protected override Task<string> ForwardAsync(
        BusinessConsoleCloseErpAccountingPeriodRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        erp.CloseAccountingPeriodAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console ERP")]
[HttpPost("/api/business-console/v1/erp/finance/accounting-periods/reopen")]
[BusinessGatewayOperationId("reopenBusinessConsoleErpAccountingPeriod")]
public sealed class ReopenBusinessConsoleErpAccountingPeriodEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessErpClient erp,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleReopenErpAccountingPeriodRequest, string>(
        auth,
        BusinessGatewayPermissions.ErpFinanceManage)
{
    protected override string OrganizationId(BusinessConsoleReopenErpAccountingPeriodRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleReopenErpAccountingPeriodRequest request) => request.EnvironmentId;

    protected override Task<string> ForwardAsync(
        BusinessConsoleReopenErpAccountingPeriodRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        erp.ReopenAccountingPeriodAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console ERP")]
[HttpGet("/api/business-console/v1/erp/finance/vouchers")]
[BusinessGatewayOperationId("listBusinessConsoleErpJournalVouchers")]
public sealed class ListBusinessConsoleErpJournalVouchersEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessErpClient erp,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleErpListRequest, BusinessConsoleErpJournalVoucherListResponse>(
        auth,
        BusinessGatewayPermissions.ErpFinanceRead)
{
    protected override string OrganizationId(BusinessConsoleErpListRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleErpListRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleErpJournalVoucherListResponse> ForwardAsync(
        BusinessConsoleErpListRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        erp.ListJournalVouchersAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console ERP")]
[HttpGet("/api/business-console/v1/erp/finance/trial-balance")]
[BusinessGatewayOperationId("getBusinessConsoleErpTrialBalance")]
public sealed class GetBusinessConsoleErpTrialBalanceEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessErpClient erp,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleErpPeriodRequest, BusinessConsoleErpTrialBalanceResponse>(
        auth,
        BusinessGatewayPermissions.ErpFinanceRead)
{
    protected override string OrganizationId(BusinessConsoleErpPeriodRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleErpPeriodRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleErpTrialBalanceResponse> ForwardAsync(
        BusinessConsoleErpPeriodRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        erp.GetTrialBalanceAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console ERP")]
[HttpGet("/api/business-console/v1/erp/finance/month-end-checklist")]
[BusinessGatewayOperationId("getBusinessConsoleErpMonthEndChecklist")]
public sealed class GetBusinessConsoleErpMonthEndChecklistEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessErpClient erp,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleErpPeriodRequest, BusinessConsoleErpMonthEndChecklistResponse>(
        auth,
        BusinessGatewayPermissions.ErpFinanceRead)
{
    protected override string OrganizationId(BusinessConsoleErpPeriodRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleErpPeriodRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleErpMonthEndChecklistResponse> ForwardAsync(
        BusinessConsoleErpPeriodRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        erp.GetMonthEndChecklistAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console ERP")]
[HttpGet("/api/business-console/v1/erp/finance/summary")]
[BusinessGatewayOperationId("getBusinessConsoleErpFinanceSummary")]
public sealed class GetBusinessConsoleErpFinanceSummaryEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessErpClient erp,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleErpContextRequest, BusinessConsoleErpFinanceSummaryResponse>(
        auth,
        BusinessGatewayPermissions.ErpFinanceRead)
{
    protected override string OrganizationId(BusinessConsoleErpContextRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleErpContextRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleErpFinanceSummaryResponse> ForwardAsync(
        BusinessConsoleErpContextRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        erp.GetFinanceSummaryAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console ERP")]
[HttpGet("/api/business-console/v1/erp/finance/payables")]
[BusinessGatewayOperationId("listBusinessConsoleErpPayables")]
public sealed class ListBusinessConsoleErpPayablesEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessErpClient erp,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleErpListRequest, BusinessConsoleErpPayableListResponse>(
        auth,
        BusinessGatewayPermissions.ErpFinanceRead)
{
    protected override string OrganizationId(BusinessConsoleErpListRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleErpListRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleErpPayableListResponse> ForwardAsync(
        BusinessConsoleErpListRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        erp.ListPayablesAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console ERP")]
[HttpGet("/api/business-console/v1/erp/finance/receivables")]
[BusinessGatewayOperationId("listBusinessConsoleErpReceivables")]
public sealed class ListBusinessConsoleErpReceivablesEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessErpClient erp,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleErpListRequest, BusinessConsoleErpReceivableListResponse>(
        auth,
        BusinessGatewayPermissions.ErpFinanceRead)
{
    protected override string OrganizationId(BusinessConsoleErpListRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleErpListRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleErpReceivableListResponse> ForwardAsync(
        BusinessConsoleErpListRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        erp.ListReceivablesAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console ERP")]
[HttpGet("/api/business-console/v1/erp/finance/cost-candidates")]
[BusinessGatewayOperationId("listBusinessConsoleErpCostCandidates")]
public sealed class ListBusinessConsoleErpCostCandidatesEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessErpClient erp,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleErpListRequest, BusinessConsoleErpCostCandidateListResponse>(
        auth,
        BusinessGatewayPermissions.ErpFinanceRead)
{
    protected override string OrganizationId(BusinessConsoleErpListRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleErpListRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleErpCostCandidateListResponse> ForwardAsync(
        BusinessConsoleErpListRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        erp.ListCostCandidatesAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console ERP")]
[HttpGet("/api/business-console/v1/erp/finance/payables/by-source")]
[BusinessGatewayOperationId("getBusinessConsoleErpPayableBySourceDocument")]
public sealed class GetBusinessConsoleErpPayableBySourceDocumentEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessErpClient erp,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleErpSourceDocumentRequest, BusinessConsoleErpPayableSourceDocumentResponse>(
        auth,
        BusinessGatewayPermissions.ErpFinanceRead)
{
    protected override string OrganizationId(BusinessConsoleErpSourceDocumentRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleErpSourceDocumentRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleErpPayableSourceDocumentResponse> ForwardAsync(
        BusinessConsoleErpSourceDocumentRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        erp.GetPayableBySourceDocumentAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console ERP")]
[HttpGet("/api/business-console/v1/erp/finance/receivables/by-source")]
[BusinessGatewayOperationId("getBusinessConsoleErpReceivableBySourceDocument")]
public sealed class GetBusinessConsoleErpReceivableBySourceDocumentEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessErpClient erp,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleErpSourceDocumentRequest, BusinessConsoleErpReceivableSourceDocumentResponse>(
        auth,
        BusinessGatewayPermissions.ErpFinanceRead)
{
    protected override string OrganizationId(BusinessConsoleErpSourceDocumentRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleErpSourceDocumentRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleErpReceivableSourceDocumentResponse> ForwardAsync(
        BusinessConsoleErpSourceDocumentRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        erp.GetReceivableBySourceDocumentAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console ERP")]
[HttpGet("/api/business-console/v1/erp/finance/cost-candidates/by-source")]
[BusinessGatewayOperationId("getBusinessConsoleErpCostCandidateBySourceDocument")]
public sealed class GetBusinessConsoleErpCostCandidateBySourceDocumentEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessErpClient erp,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleErpSourceDocumentRequest, BusinessConsoleErpCostCandidateSourceDocumentResponse>(
        auth,
        BusinessGatewayPermissions.ErpFinanceRead)
{
    protected override string OrganizationId(BusinessConsoleErpSourceDocumentRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleErpSourceDocumentRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleErpCostCandidateSourceDocumentResponse> ForwardAsync(
        BusinessConsoleErpSourceDocumentRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        erp.GetCostCandidateBySourceDocumentAsync(tokenProvider.BearerToken, request, cancellationToken);
}
