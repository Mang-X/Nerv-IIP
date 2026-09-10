using FastEndpoints;
using FluentValidation;
using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.BusinessGateway.Web.Application.OpenApi;
using Nerv.IIP.Contracts.Erp;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.BusinessGateway.Web.Endpoints.Erp;

// ---------------------------------------------------------------------------
// #3325 端点级幂等键上界：Erp 写面 *RequestValidator 的共同口径
// （本文件与 BusinessConsoleErpSalesFinanceEndpoints.cs 共用这一段）
// ---------------------------------------------------------------------------
// 每条规则的值**不得大于**它下游权威解析出的上界。注意机器只校验这一个方向：
// BusinessGatewayIdempotencyKeyDownstreamBoundContractTests 的
// Gateway_never_promises_a_longer_idempotency_key_than_its_downstream_accepts
// 断言的是 `网关值 <= 下游值`，把某条规则**收窄**不会红（反方向归 #3287）。
// 登记表同时钉住「链接必须存在」：下游改名或删除会让权威解析失败而报红。
//
// 为什么写在校验器上而不是集中一张表：端点级规则会进 OpenAPI 的 maxLength，
// 对客户端是真实契约；集中表不会。
//
// 只加上界、不加 NotEmpty —— 本票不改这些字段的必填语义。
//
// Erp 侧下游权威的形状：这些写面的 handler 都把**原始键**（只经 CodeAllocator.Normalize
// 的 Trim）交给 CodeAllocator，落 erp.code_idempotency_keys.idempotency_key。
// 唯一的例外是采购申请转换那一处——它在 RFQ 分支给键追加 :rfq 后缀，长度单调递增，
// 因此列宽仍是真权威、只是有效上界要减去后缀；那个减法已经由下游命令校验器
// （ErpCodingIdempotencyKeyPolicy.BaseMaxLengthFor）算好，见该处自己的注释。
//
// ⚠️ 值域边界（不要读成「这些规则挡住了所有超长键」）：FastEndpoints 的 DTO 校验
// 跑在 AuthorizedBusinessProxyEndpoint.HandleAsync **之前**，而经
// Idempotency-Key / X-Idempotency-Key 头传来的键要到 HandleAsync 里
// BusinessGatewayIdempotencyKey.Resolve 才写进 DTO。⇒ 这些规则**只约束请求体路径**；
// 头部路径今天仍只由全局钳（150）兜住。该顺序缺陷由 #3330 承接，
// 并且是 #3327 抬钳的硬前置。
//
// ⚠️ 校验失败的响应形状是 FastEndpoints 默认的
// {"statusCode":400,"message":"One or more errors occurred!","errors":{...}}，
// 前端拿不到稳定错误码。既有位点今天就是这个形状，本票不引入新形状；
// 但对本票新补规则的这些位点，行为确实变了——它们原先没有端点级规则、
// 超长键落到全局钳上因而能拿到稳定码。该缺陷归 #3333。
// ---------------------------------------------------------------------------

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
[HttpGet("/api/business-console/v1/erp/procurement/supplier-quotations")]
[BusinessGatewayOperationId("listBusinessConsoleErpSupplierQuotations")]
public sealed class ListBusinessConsoleErpSupplierQuotationsEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessErpClient erp,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleErpSupplierQuotationListRequest, BusinessConsoleErpSupplierQuotationListResponse>(
        auth,
        BusinessGatewayPermissions.ErpProcurementRead)
{
    protected override string OrganizationId(BusinessConsoleErpSupplierQuotationListRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleErpSupplierQuotationListRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleErpSupplierQuotationListResponse> ForwardAsync(
        BusinessConsoleErpSupplierQuotationListRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        erp.ListSupplierQuotationsAsync(tokenProvider.BearerToken, request, cancellationToken);
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

// #1345：收货契约把 qualityStatus 声明为必填并做值域收敛，避免非法值穿透到 ERP 后静默丢失应付计提。
public sealed class BusinessConsoleRecordErpPurchaseReceiptRequestValidator
    : Validator<BusinessConsoleRecordErpPurchaseReceiptRequest>
{
    public BusinessConsoleRecordErpPurchaseReceiptRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.PurchaseReceiptNo).MaximumLength(100);
        RuleFor(x => x.PurchaseOrderNo).NotEmpty().MaximumLength(100);
        RuleFor(x => x.IdempotencyKey).MaximumLength(150);
        RuleFor(x => x.Lines).NotEmpty();
        RuleForEach(x => x.Lines).SetValidator(new BusinessConsoleErpPurchaseReceiptLineValidator());
    }
}

public sealed class BusinessConsoleErpPurchaseReceiptLineValidator
    : Validator<BusinessConsoleErpPurchaseReceiptLine>
{
    public BusinessConsoleErpPurchaseReceiptLineValidator()
    {
        RuleFor(x => x.PurchaseOrderLineNo).NotEmpty().MaximumLength(100);
        RuleFor(x => x.ReceivedQuantity).GreaterThan(0);
        RuleFor(x => x.QualityStatus)
            .NotEmpty()
            .MaximumLength(50)
            .Must(ErpReceiptQualityStatuses.IsSupported)
            .WithMessage("质检状态只能是 unrestricted（合格）、quality（待检）、blocked（冻结）之一或其已知别名。");
    }
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
        // 端点级幂等键长度上界（#3325）。本处下游权威：
        // Erp 侧 CreatePurchaseRequisitionFromSuggestionCommandHandler 把原始键交给 CodeAllocator，
        // 落 erp.code_idempotency_keys.idempotency_key(150)；该命令的校验器没有幂等键长度规则。
        // 共同口径见本文件顶部的「#3325 端点级幂等键上界」注释块。
        RuleFor(x => x.IdempotencyKey).MaximumLength(150);
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
        // 端点级幂等键长度上界（#3325）。本处下游权威：
        // Erp 侧 ConvertPurchaseRequisitionsToPurchaseOrderCommandValidator，其值由
        // ErpCodingIdempotencyKeyPolicy.BaseMaxLengthFor(":rfq") 从列宽 150 减去后缀算出 = 146。
        // 承载列本身（erp.code_idempotency_keys.idempotency_key）也是真权威——RFQ 分支的拼接
        // 长度单调，不是 #3290 那种定长哈希——但它在这条腿上的**有效**上界正是 150-4=146，
        // 登记表里因此指向已经做完减法的命令校验器，而不是登记会高估 4 个字符的原始列宽。
        // 共同口径见本文件顶部的「#3325 端点级幂等键上界」注释块。
        RuleFor(x => x.IdempotencyKey).MaximumLength(146);
    }
}

/// <summary>
/// 端点级幂等键长度上界（#3325）。本处下游权威：
/// Erp 侧 CreateRequestForQuotationCommandHandler 把原始键交给 CodeAllocator，
/// 落 erp.code_idempotency_keys.idempotency_key(150)；该命令的校验器没有幂等键长度规则。
/// 共同口径见本文件顶部的「#3325 端点级幂等键上界」注释块。
/// </summary>
public sealed class BusinessConsoleCreateErpRequestForQuotationRequestValidator
    : Validator<BusinessConsoleCreateErpRequestForQuotationRequest>
{
    public BusinessConsoleCreateErpRequestForQuotationRequestValidator() =>
        RuleFor(x => x.IdempotencyKey).MaximumLength(150);
}

/// <summary>
/// 端点级幂等键长度上界（#3325）。本处下游权威：
/// Erp 侧 ReceiveSupplierQuotationCommandHandler 把原始键交给 CodeAllocator，
/// 落 erp.code_idempotency_keys.idempotency_key(150)；该命令的校验器没有幂等键长度规则。
/// 共同口径见本文件顶部的「#3325 端点级幂等键上界」注释块。
/// </summary>
public sealed class BusinessConsoleReceiveErpSupplierQuotationRequestValidator
    : Validator<BusinessConsoleReceiveErpSupplierQuotationRequest>
{
    public BusinessConsoleReceiveErpSupplierQuotationRequestValidator() =>
        RuleFor(x => x.IdempotencyKey).MaximumLength(150);
}

/// <summary>
/// 端点级幂等键长度上界（#3325）。本处下游权威：
/// Erp 侧 CreatePurchaseOrderCommandHandler 把原始键交给 CodeAllocator（TryPeekReplay 与
/// AllocateAsync 两处都是原样传入），落 erp.code_idempotency_keys.idempotency_key(150)；
/// 该命令的校验器没有幂等键长度规则。
/// 共同口径见本文件顶部的「#3325 端点级幂等键上界」注释块。
/// </summary>
public sealed class BusinessConsoleCreateErpPurchaseOrderRequestValidator
    : Validator<BusinessConsoleCreateErpPurchaseOrderRequest>
{
    public BusinessConsoleCreateErpPurchaseOrderRequestValidator() =>
        RuleFor(x => x.IdempotencyKey).MaximumLength(150);
}
