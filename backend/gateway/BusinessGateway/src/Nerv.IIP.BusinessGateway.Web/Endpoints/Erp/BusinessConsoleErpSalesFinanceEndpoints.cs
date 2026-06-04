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
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleErpContextRequest, BusinessConsoleErpSalesOrderListResponse>(
        auth,
        BusinessGatewayPermissions.ErpSalesRead)
{
    protected override string OrganizationId(BusinessConsoleErpContextRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleErpContextRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleErpSalesOrderListResponse> ForwardAsync(
        BusinessConsoleErpContextRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        erp.ListSalesOrdersAsync(tokenProvider.BearerToken, request, cancellationToken);
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

