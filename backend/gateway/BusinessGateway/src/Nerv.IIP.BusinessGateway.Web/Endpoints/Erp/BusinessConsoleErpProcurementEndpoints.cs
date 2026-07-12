using FastEndpoints;
using FluentValidation;
using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.BusinessGateway.Web.Application.OpenApi;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.BusinessGateway.Web.Endpoints.Erp;

[Tags("Business Console ERP")]
[HttpGet("/api/business-console/v1/erp/procurement/rfqs")]
[BusinessGatewayOperationId("listBusinessConsoleErpRequestsForQuotation")]
public sealed class ListBusinessConsoleErpRequestsForQuotationEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessErpClient erp,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleErpListRequest, BusinessConsoleErpRequestForQuotationListResponse>(
        auth,
        BusinessGatewayPermissions.ErpProcurementRead)
{
    protected override string OrganizationId(BusinessConsoleErpListRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleErpListRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleErpRequestForQuotationListResponse> ForwardAsync(
        BusinessConsoleErpListRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        erp.ListRequestsForQuotationAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console ERP")]
[HttpGet("/api/business-console/v1/erp/procurement/purchase-requisitions")]
[BusinessGatewayOperationId("listBusinessConsoleErpPurchaseRequisitions")]
public sealed class ListBusinessConsoleErpPurchaseRequisitionsEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessErpClient erp,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleErpListRequest, BusinessConsoleErpPurchaseRequisitionListResponse>(
        auth,
        BusinessGatewayPermissions.ErpProcurementRead)
{
    protected override string OrganizationId(BusinessConsoleErpListRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleErpListRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleErpPurchaseRequisitionListResponse> ForwardAsync(
        BusinessConsoleErpListRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        erp.ListPurchaseRequisitionsAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console ERP")]
[HttpPost("/api/business-console/v1/erp/procurement/purchase-requisitions/from-suggestion")]
[BusinessGatewayOperationId("createBusinessConsoleErpPurchaseRequisitionFromSuggestion")]
public sealed class CreateBusinessConsoleErpPurchaseRequisitionFromSuggestionEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessErpClient erp,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleCreateErpPurchaseRequisitionRequest, BusinessConsoleCreateErpPurchaseRequisitionResponse>(
        auth,
        BusinessGatewayPermissions.ErpProcurementManage)
{
    protected override string OrganizationId(BusinessConsoleCreateErpPurchaseRequisitionRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleCreateErpPurchaseRequisitionRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleCreateErpPurchaseRequisitionResponse> ForwardAsync(
        BusinessConsoleCreateErpPurchaseRequisitionRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        erp.CreatePurchaseRequisitionFromSuggestionAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console ERP")]
[HttpPost("/api/business-console/v1/erp/procurement/rfqs")]
[BusinessGatewayOperationId("createBusinessConsoleErpRequestForQuotation")]
public sealed class CreateBusinessConsoleErpRequestForQuotationEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessErpClient erp,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleCreateErpRequestForQuotationRequest, BusinessConsoleCreateErpRequestForQuotationResponse>(
        auth,
        BusinessGatewayPermissions.ErpProcurementManage)
{
    protected override string OrganizationId(BusinessConsoleCreateErpRequestForQuotationRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleCreateErpRequestForQuotationRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleCreateErpRequestForQuotationResponse> ForwardAsync(
        BusinessConsoleCreateErpRequestForQuotationRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        erp.CreateRequestForQuotationAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console ERP")]
[HttpPost("/api/business-console/v1/erp/procurement/purchase-requisitions/convert-to-purchase-order")]
[BusinessGatewayOperationId("convertBusinessConsoleErpPurchaseRequisitionsToPurchaseOrder")]
public sealed class ConvertBusinessConsoleErpPurchaseRequisitionsToPurchaseOrderEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessErpClient erp,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleConvertErpPurchaseRequisitionsRequest, BusinessConsoleConvertErpPurchaseRequisitionsResponse>(
        auth,
        BusinessGatewayPermissions.ErpProcurementManage)
{
    protected override string OrganizationId(BusinessConsoleConvertErpPurchaseRequisitionsRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleConvertErpPurchaseRequisitionsRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleConvertErpPurchaseRequisitionsResponse> ForwardAsync(
        BusinessConsoleConvertErpPurchaseRequisitionsRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        erp.ConvertPurchaseRequisitionsToPurchaseOrderAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console ERP")]
[HttpPost("/api/business-console/v1/erp/procurement/supplier-quotations")]
[BusinessGatewayOperationId("receiveBusinessConsoleErpSupplierQuotation")]
public sealed class ReceiveBusinessConsoleErpSupplierQuotationEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessErpClient erp,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleReceiveErpSupplierQuotationRequest, BusinessConsoleReceiveErpSupplierQuotationResponse>(
        auth,
        BusinessGatewayPermissions.ErpProcurementManage)
{
    protected override string OrganizationId(BusinessConsoleReceiveErpSupplierQuotationRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleReceiveErpSupplierQuotationRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleReceiveErpSupplierQuotationResponse> ForwardAsync(
        BusinessConsoleReceiveErpSupplierQuotationRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        erp.ReceiveSupplierQuotationAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console ERP")]
[HttpPost("/api/business-console/v1/erp/procurement/purchase-orders")]
[BusinessGatewayOperationId("createBusinessConsoleErpPurchaseOrder")]
public sealed class CreateBusinessConsoleErpPurchaseOrderEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessErpClient erp,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleCreateErpPurchaseOrderRequest, BusinessConsoleCreateErpPurchaseOrderResponse>(
        auth,
        BusinessGatewayPermissions.ErpProcurementManage)
{
    protected override string OrganizationId(BusinessConsoleCreateErpPurchaseOrderRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleCreateErpPurchaseOrderRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleCreateErpPurchaseOrderResponse> ForwardAsync(
        BusinessConsoleCreateErpPurchaseOrderRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        erp.CreatePurchaseOrderAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console ERP")]
[HttpPost("/api/business-console/v1/erp/procurement/purchase-receipts")]
[BusinessGatewayOperationId("recordBusinessConsoleErpPurchaseReceipt")]
public sealed class RecordBusinessConsoleErpPurchaseReceiptEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessErpClient erp,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleRecordErpPurchaseReceiptRequest, BusinessConsoleRecordErpPurchaseReceiptResponse>(
        auth,
        BusinessGatewayPermissions.ErpProcurementManage)
{
    protected override string OrganizationId(BusinessConsoleRecordErpPurchaseReceiptRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleRecordErpPurchaseReceiptRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleRecordErpPurchaseReceiptResponse> ForwardAsync(
        BusinessConsoleRecordErpPurchaseReceiptRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        erp.RecordPurchaseReceiptAsync(tokenProvider.BearerToken, request, cancellationToken);
}

public sealed class BusinessConsoleCreateErpPurchaseRequisitionRequestValidator : Validator<BusinessConsoleCreateErpPurchaseRequisitionRequest>
{
    public BusinessConsoleCreateErpPurchaseRequisitionRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.SuggestionId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.SkuCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.UomCode).NotEmpty().MaximumLength(30);
        RuleFor(x => x.SiteCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Quantity).GreaterThan(0);
    }
}

public sealed class BusinessConsoleConvertErpPurchaseRequisitionsRequestValidator : Validator<BusinessConsoleConvertErpPurchaseRequisitionsRequest>
{
    public BusinessConsoleConvertErpPurchaseRequisitionsRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.PurchaseRequisitionNos).NotEmpty();
        RuleForEach(x => x.PurchaseRequisitionNos).NotEmpty().MaximumLength(100);
        RuleFor(x => x.PurchaseOrderNo).MaximumLength(100);
        RuleFor(x => x.SupplierCode).MaximumLength(100);
        RuleForEach(x => x.RfqSupplierCodes).NotEmpty().MaximumLength(100);
        RuleFor(x => x.RfqNo).MaximumLength(100);
        RuleFor(x => x.CurrencyCode).NotEmpty().MaximumLength(10);
    }
}
