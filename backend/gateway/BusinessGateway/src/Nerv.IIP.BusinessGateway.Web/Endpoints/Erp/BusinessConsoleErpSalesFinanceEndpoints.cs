using FastEndpoints;
using FluentValidation;
using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.BusinessGateway.Web.Application.OpenApi;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.BusinessGateway.Web.Endpoints.Erp;

// 本文件末尾的 *RequestValidator 是 #3325 补的端点级幂等键上界。
// 共同口径（方向、为什么写在校验器上、头部与请求体两条来源的覆盖边界、失败响应形状）
// 写在 BusinessConsoleErpProcurementEndpoints.cs 顶部的「#3325 端点级幂等键上界」注释块，
// 不在这里复制第二份。

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
[HttpPost("/api/business-console/v1/erp/sales/sales-orders/{salesOrderNo}/release-credit-hold")]
[BusinessGatewayOperationId("releaseBusinessConsoleErpSalesOrderCreditHold")]
public sealed class ReleaseBusinessConsoleErpSalesOrderCreditHoldEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessErpClient erp,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleReleaseErpSalesOrderCreditHoldRequest, string>(
        auth,
        BusinessGatewayPermissions.ErpSalesManage)
{
    protected override string OrganizationId(BusinessConsoleReleaseErpSalesOrderCreditHoldRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleReleaseErpSalesOrderCreditHoldRequest request) => request.EnvironmentId;

    protected override string ResourceType(BusinessConsoleReleaseErpSalesOrderCreditHoldRequest request) => "erp-sales-order";

    protected override string? ResourceId(BusinessConsoleReleaseErpSalesOrderCreditHoldRequest request) =>
        Route<string>("salesOrderNo") ?? request.SalesOrderNo;

    protected override Task<string> ForwardAsync(
        BusinessConsoleReleaseErpSalesOrderCreditHoldRequest request,
        string bearerToken,
        CancellationToken cancellationToken)
    {
        // 审计身份不信任请求体：StartedBy 一律取已认证 principal（谁提交解冻复核记谁）。
        var (_, actorRef) = RequireAuthorizedPrincipalActor();
        var downstreamRequest = request with
        {
            SalesOrderNo = Route<string>("salesOrderNo") ?? request.SalesOrderNo,
            StartedBy = actorRef,
        };
        return erp.ReleaseSalesOrderCreditHoldAsync(tokenProvider.BearerToken, downstreamRequest, cancellationToken);
    }
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

/// <summary>
/// 端点级幂等键长度上界（#3325）。本处下游权威：Erp 侧 OpenOpportunityCommandHandler 把原始键交给 CodeAllocator，
/// 落 erp.code_idempotency_keys.idempotency_key(150)；该命令的校验器没有幂等键长度规则。
/// 共同口径见 BusinessConsoleErpProcurementEndpoints.cs 顶部的「#3325 端点级幂等键上界」注释块。
/// </summary>
public sealed class BusinessConsoleOpenErpOpportunityRequestValidator
    : Validator<BusinessConsoleOpenErpOpportunityRequest>
{
    public BusinessConsoleOpenErpOpportunityRequestValidator() =>
        RuleFor(x => x.IdempotencyKey).MaximumLength(150);
}

/// <summary>
/// 端点级幂等键长度上界（#3325）。本处下游权威：Erp 侧 CreateQuotationCommandHandler 把原始键交给 CodeAllocator，
/// 落 erp.code_idempotency_keys.idempotency_key(150)；该命令的校验器没有幂等键长度规则。
/// 共同口径见 BusinessConsoleErpProcurementEndpoints.cs 顶部的「#3325 端点级幂等键上界」注释块。
/// </summary>
public sealed class BusinessConsoleCreateErpQuotationRequestValidator
    : Validator<BusinessConsoleCreateErpQuotationRequest>
{
    public BusinessConsoleCreateErpQuotationRequestValidator() =>
        RuleFor(x => x.IdempotencyKey).MaximumLength(150);
}

/// <summary>
/// 端点级幂等键长度上界（#3325）。本处下游权威：Erp 侧 CreateSalesOrderCommandHandler 把原始键交给 CodeAllocator，
/// 落 erp.code_idempotency_keys.idempotency_key(150)；该命令的校验器没有幂等键长度规则。
/// 同一个键还进 ErpCommandCausationIds.ForHttpCommand，但那是无条件 SHA256 截 24 位的定长摘要，
/// 对原始键零约束（#3290 的幽灵权威形状），故**不**作为权威登记。
/// 共同口径见 BusinessConsoleErpProcurementEndpoints.cs 顶部的「#3325 端点级幂等键上界」注释块。
/// </summary>
public sealed class BusinessConsoleCreateErpSalesOrderRequestValidator
    : Validator<BusinessConsoleCreateErpSalesOrderRequest>
{
    public BusinessConsoleCreateErpSalesOrderRequestValidator() =>
        RuleFor(x => x.IdempotencyKey).MaximumLength(150);
}

/// <summary>
/// 端点级幂等键长度上界（#3325）。本处下游权威：Erp 侧 ReleaseDeliveryOrderCommandHandler 把原始键交给 CodeAllocator，
/// 落 erp.code_idempotency_keys.idempotency_key(150)；该命令的校验器没有幂等键长度规则。
/// 共同口径见 BusinessConsoleErpProcurementEndpoints.cs 顶部的「#3325 端点级幂等键上界」注释块。
/// </summary>
public sealed class BusinessConsoleReleaseErpDeliveryOrderRequestValidator
    : Validator<BusinessConsoleReleaseErpDeliveryOrderRequest>
{
    public BusinessConsoleReleaseErpDeliveryOrderRequestValidator() =>
        RuleFor(x => x.IdempotencyKey).MaximumLength(150);
}

/// <summary>
/// 端点级幂等键长度上界（#3325）。本处下游权威：Erp 侧 CreateAccountPayableCommandHandler 把原始键交给 CodeAllocator，
/// 落 erp.code_idempotency_keys.idempotency_key(150)；该命令的校验器没有幂等键长度规则。
/// 共同口径见 BusinessConsoleErpProcurementEndpoints.cs 顶部的「#3325 端点级幂等键上界」注释块。
/// </summary>
public sealed class BusinessConsoleCreateErpAccountPayableRequestValidator
    : Validator<BusinessConsoleCreateErpAccountPayableRequest>
{
    public BusinessConsoleCreateErpAccountPayableRequestValidator() =>
        RuleFor(x => x.IdempotencyKey).MaximumLength(150);
}

/// <summary>
/// 端点级幂等键长度上界（#3325）。本处下游权威：Erp 侧 CreateAccountReceivableCommandHandler 把原始键交给 CodeAllocator，
/// 落 erp.code_idempotency_keys.idempotency_key(150)；该命令的校验器没有幂等键长度规则。
/// 共同口径见 BusinessConsoleErpProcurementEndpoints.cs 顶部的「#3325 端点级幂等键上界」注释块。
/// </summary>
public sealed class BusinessConsoleCreateErpAccountReceivableRequestValidator
    : Validator<BusinessConsoleCreateErpAccountReceivableRequest>
{
    public BusinessConsoleCreateErpAccountReceivableRequestValidator() =>
        RuleFor(x => x.IdempotencyKey).MaximumLength(150);
}

/// <summary>
/// 端点级幂等键长度上界（#3325）。本处下游权威：Erp 侧 CreateCostCandidateCommandHandler 把原始键交给 CodeAllocator，
/// 落 erp.code_idempotency_keys.idempotency_key(150)；该命令的校验器没有幂等键长度规则。
/// 共同口径见 BusinessConsoleErpProcurementEndpoints.cs 顶部的「#3325 端点级幂等键上界」注释块。
/// </summary>
public sealed class BusinessConsoleCreateErpCostCandidateRequestValidator
    : Validator<BusinessConsoleCreateErpCostCandidateRequest>
{
    public BusinessConsoleCreateErpCostCandidateRequestValidator() =>
        RuleFor(x => x.IdempotencyKey).MaximumLength(150);
}

/// <summary>
/// 端点级幂等键长度上界（#3325）。本处下游权威：Erp 侧 PostJournalVoucherCommandHandler 把原始键交给 CodeAllocator，
/// 落 erp.code_idempotency_keys.idempotency_key(150)；该命令的校验器没有幂等键长度规则。
/// 分配器产出的是凭证号（allocation.Code，另一列），幂等键本身不参与凭证号构成，
/// 因此与 #3278 正在改的凭证号形状无交集。
/// 共同口径见 BusinessConsoleErpProcurementEndpoints.cs 顶部的「#3325 端点级幂等键上界」注释块。
/// </summary>
public sealed class BusinessConsolePostErpJournalVoucherRequestValidator
    : Validator<BusinessConsolePostErpJournalVoucherRequest>
{
    public BusinessConsolePostErpJournalVoucherRequestValidator() =>
        RuleFor(x => x.IdempotencyKey).MaximumLength(150);
}

/// <summary>
/// 端点级幂等键长度上界（#3325）。本处下游权威：Erp 侧 ApprovePaymentExecutionCommandHandler 把原始键交给 CodeAllocator，
/// 落 erp.code_idempotency_keys.idempotency_key(150)；该命令**没有任何校验器**，因而同样没有幂等键长度规则。
/// 共同口径见 BusinessConsoleErpProcurementEndpoints.cs 顶部的「#3325 端点级幂等键上界」注释块。
/// </summary>
public sealed class BusinessConsoleApproveErpPaymentExecutionRequestValidator
    : Validator<BusinessConsoleApproveErpPaymentExecutionRequest>
{
    public BusinessConsoleApproveErpPaymentExecutionRequestValidator() =>
        RuleFor(x => x.IdempotencyKey).MaximumLength(150);
}

/// <summary>
/// 端点级幂等键长度上界（#3325）。本处下游权威：Erp 侧 RegisterCashReceiptCommandHandler 把原始键交给 CodeAllocator，
/// 落 erp.code_idempotency_keys.idempotency_key(150)；该命令**没有任何校验器**，因而同样没有幂等键长度规则。
/// 共同口径见 BusinessConsoleErpProcurementEndpoints.cs 顶部的「#3325 端点级幂等键上界」注释块。
/// </summary>
public sealed class BusinessConsoleRegisterErpCashReceiptRequestValidator
    : Validator<BusinessConsoleRegisterErpCashReceiptRequest>
{
    public BusinessConsoleRegisterErpCashReceiptRequestValidator() =>
        RuleFor(x => x.IdempotencyKey).MaximumLength(150);
}
