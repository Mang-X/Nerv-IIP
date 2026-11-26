using FastEndpoints;
using FluentValidation;
using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.BusinessGateway.Web.Application.OpenApi;
using Nerv.IIP.BusinessGateway.Web.Endpoints;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.BusinessGateway.Web.Endpoints.Mes;

/// <summary>
/// 端点级幂等键长度上界（#3327 补，#3324 当时因「算不出正上界」把本位点移出）。
/// </summary>
/// <remarks>
/// <para><b>本处下游权威取命令校验器，不取承载列，理由是那一列对本位点是「条件性派生」</b>
/// （与 <c>WmsText.LineIdempotencyKey</c> 同形，**不是** #3290 那种无条件摘要的纯幽灵权威）。
/// 键经 <c>FinishedGoodsReceiptRequest.RetryInventoryPosting</c> →
/// <c>FinishedGoodsReceiptInventoryPostingKey.BuildRetry</c> 拼成
/// <c>{可读作用域}:{原始键}</c> 后跨服务进 Inventory，两支要分开读：</para>
/// <list type="bullet">
/// <item><b>整键 ≤ <c>FinishedGoodsReceiptInventoryPostingKey.ColumnMaxLength</c>（128）⇒ 逐字保持</b>
/// —— 落进 <c>stock_movements.idempotency_key</c> 的就是含原始键的那个字符串，
/// 那一列**是**这一支的真权威（长度单调）。</item>
/// <item><b>整键 &gt; 128 ⇒ 作用域段与尾段一起回落成定长摘要</b>（#3332）
/// —— 落库值与原始键长度无关，那一列对原始键**零约束**。</item>
/// </list>
/// <para>⇒ 那一列**不构成本位点的上界**（它只约束前一支），但也**不是**「完全不承重」——
/// 别把这条读成「该列可以随意收窄」。可登记的**有效上界**因此取 Mes 命令校验器
/// <c>RetryFinishedGoodsReceiptInventoryPostingCommandValidator</c>
/// （<c>IdempotencyKey</c> <c>MaximumLength(200)</c>），登记见
/// <c>BusinessGatewayIdempotencyKeyDownstreamBoundContractTests.DownstreamBounds</c>。</para>
/// <para><b>补这条规则改变了本位点超长键的响应形状</b>（#3333 已修，本段按修后事实重写）：
/// 此前超长键落到全局钳上，拿的是带稳定码 <c>idempotency-key-too-long</c> 的 400；
/// 补规则之后 201..512 的键先被 DTO 校验拒，当时拿的是 FastEndpoints 默认的
/// <c>{"statusCode":400,"message":"One or more errors occurred!","errors":{...}}</c>，前端拿不到稳定码。
/// #3333 把校验失败也成形为同一个 <c>ResponseData</c> 信封
/// （<c>message</c> 位是稳定码 <c>request-payload-invalid</c>，见
/// <c>BusinessGatewayValidationErrorResponse</c>），⇒ 两条上界现在只差稳定码本身，形状一致。
/// OpenAPI 快照里本端点那条 <c>400</c> 响应的 schema 也因此从 <c>FastEndpointsErrorResponse</c>
/// 换成了共享信封。</para>
/// <para>共同口径见 BusinessConsoleMesEndpoints.cs 顶部的「#3324 端点级幂等键上界」注释块。</para>
/// </remarks>
public sealed class BusinessConsoleMesRetryFinishedGoodsReceiptInventoryPostingRequestValidator
    : Validator<BusinessConsoleMesRetryFinishedGoodsReceiptInventoryPostingRequest>
{
    public BusinessConsoleMesRetryFinishedGoodsReceiptInventoryPostingRequestValidator() =>
        RuleFor(x => x.IdempotencyKey).MaximumLength(200);
}

[Tags("Business Console MES")]
[HttpPost("/api/business-console/v1/mes/finished-goods-receipt-requests/{requestNo}/inventory-posting/retry")]
[BusinessGatewayOperationId("retryBusinessConsoleMesFinishedGoodsReceiptInventoryPosting")]
public sealed class RetryBusinessConsoleMesFinishedGoodsReceiptInventoryPostingEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMesClient mes,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleMesRetryFinishedGoodsReceiptInventoryPostingRequest, BusinessConsoleMesCreateReceiptResponse>(
        auth,
        BusinessGatewayPermissions.MesReceiptsManage)
{
    protected override string OrganizationId(BusinessConsoleMesRetryFinishedGoodsReceiptInventoryPostingRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleMesRetryFinishedGoodsReceiptInventoryPostingRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleMesCreateReceiptResponse> ForwardAsync(
        BusinessConsoleMesRetryFinishedGoodsReceiptInventoryPostingRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        mes.RetryFinishedGoodsReceiptInventoryPostingAsync(tokenProvider.BearerToken, request.RequestNo, request, cancellationToken);
}
