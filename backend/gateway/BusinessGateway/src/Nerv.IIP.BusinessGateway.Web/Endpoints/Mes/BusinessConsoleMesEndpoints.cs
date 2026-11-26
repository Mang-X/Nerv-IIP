using FastEndpoints;
using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.BusinessGateway.Web.Application.OpenApi;
using Nerv.IIP.BusinessGateway.Web.Endpoints;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.BusinessGateway.Web.Endpoints.Mes;

// MES 端点分文件的数字前缀保留原发现顺序，避免 OpenAPI 快照和生成客户端重排。

// ---------------------------------------------------------------------------
// #3324 端点级幂等键上界：MES 端点 *RequestValidator 的共同口径
// ---------------------------------------------------------------------------
// 每条规则的值**不得大于**它下游权威解析出的上界。注意机器只校验这一个方向：
// BusinessGatewayIdempotencyKeyDownstreamBoundContractTests 的
// Gateway_never_promises_a_longer_idempotency_key_than_its_downstream_accepts
// 断言的是 `网关值 <= 下游值`，把某条规则**收窄**不会红（反方向归 #3287）。
// 登记表同时钉住「链接必须存在」：下游改名或删除会让权威解析失败而报红。
// 不写条数：那个计数每落一张子票就变，复述它等于制造一处会过期的手抄事实。
//
// 为什么写在校验器上而不是集中一张表：端点级规则会进 OpenAPI 的 maxLength，
// 对客户端是真实契约；集中表不会。
//
// 只加上界、不加 NotEmpty —— 本票不改这些字段的必填语义。
//
// 值域边界（#3327 按 #3330 落地后的事实重写；#3324 当时写的那句已作废，别沿用）：
// #3330 已把鉴权与 BusinessGatewayIdempotencyKey.Resolve 从
// AuthorizedBusinessProxyEndpoint.HandleAsync 挪进 OnBeforeValidateAsync，
// 即 FastEndpoints 执行序里 DTO 校验**之前**的那一格。⇒ 经 Idempotency-Key /
// X-Idempotency-Key 头传来的键在校验发生时已归一化写回 DTO，MES 校验器的这些规则
// 对**头部与请求体两条来源同时生效**（#3324 当时只对请求体生效）。
// ⚠️ 我在 #3327 第一版这里写过一个「残余窗口」，并断言它「走不到 ForwardAsync」——
// **那句是错的，#3345 审核用真 HTTP 探针实测走到了**（作用域字段缺省 ∧ 令牌无
// organizationId/environmentId 声明 ∧ 键走头部 ⇒ 200 + forwarded=1 + 键长 300）。
// 该窗口已在 #3327 本票内关掉：AuthorizedBusinessProxyEndpoint 把归一化从
// 「鉴权推迟」的那个早返回里拆了出来，鉴权可以推迟、归一化不推迟。
// ⇒ MES 校验器的这些规则对头部来源在**两条支路上都生效**。
// 「归一化先于校验」这条性质由网关自己的 BusinessGatewayRequestPipelineOrderTests
// 证明（含那五格探针）。MES 校验器只声明规则值。
// ---------------------------------------------------------------------------

[Tags("Business Console MES")]
[HttpGet("/api/business-console/v1/mes/foundation-readiness")]
[BusinessGatewayOperationId("getBusinessConsoleMesFoundationReadiness")]
public sealed class GetBusinessConsoleMesFoundationReadinessEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMesClient mes,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleMesFoundationReadinessRequest, BusinessConsoleMesFoundationReadinessResponse>(
        auth,
        BusinessGatewayPermissions.MesFoundationRead)
{
    private static readonly string[] AreaCodes =
    [
        "master-data",
        "product-engineering",
        "supply",
        "quality",
        "equipment",
        "barcode-coding",
        "iam-context",
        "erp",
        "inventory",
    ];

    protected override string OrganizationId(BusinessConsoleMesFoundationReadinessRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleMesFoundationReadinessRequest request) => request.EnvironmentId;

    protected override async Task<BusinessConsoleMesFoundationReadinessResponse> ForwardAsync(
        BusinessConsoleMesFoundationReadinessRequest request,
        string bearerToken,
        CancellationToken cancellationToken)
    {
        var areas = new List<BusinessConsoleMesReadinessArea>(AreaCodes.Length);
        foreach (var areaCode in AreaCodes)
        {
            areas.Add(await ReadAreaAsync(areaCode, request, cancellationToken));
        }

        return BuildReadiness(areas);
    }

    private async Task<BusinessConsoleMesReadinessArea> ReadAreaAsync(
        string areaCode,
        BusinessConsoleMesFoundationReadinessRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return await mes.GetFoundationReadinessAreaAsync(
                tokenProvider.BearerToken,
                areaCode,
                request,
                cancellationToken);
        }
        catch (BusinessServiceProxyException)
        {
            return SourceUnavailableArea(areaCode);
        }
        catch (HttpRequestException)
        {
            return SourceUnavailableArea(areaCode);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return SourceUnavailableArea(areaCode);
        }
        catch (InvalidOperationException)
        {
            return SourceUnavailableArea(areaCode);
        }
    }

    private static BusinessConsoleMesFoundationReadinessResponse BuildReadiness(
        IReadOnlyCollection<BusinessConsoleMesReadinessArea> areas)
    {
        var blockingIssues = areas.SelectMany(area => area.Issues)
            .Where(issue => string.Equals(issue.Severity, "Blocked", StringComparison.Ordinal))
            .ToArray();
        var warningIssues = areas.SelectMany(area => area.Issues)
            .Where(issue => string.Equals(issue.Severity, "Warning", StringComparison.Ordinal))
            .ToArray();
        var status = blockingIssues.Length > 0
            ? "Blocked"
            : warningIssues.Length > 0
                ? "Warning"
                : "Ready";
        return new BusinessConsoleMesFoundationReadinessResponse(status, areas, blockingIssues, warningIssues);
    }

    private static BusinessConsoleMesReadinessArea SourceUnavailableArea(string areaCode) =>
        new(
            areaCode,
            "Blocked",
            [
                new BusinessConsoleMesReadinessIssue(
                    "SOURCE_SERVICE_UNAVAILABLE",
                    "Blocked",
                    "Source service is unavailable or returned invalid readiness data.",
                    areaCode,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    "请稍后重试或联系管理员检查来源服务"),
            ]);
}

public abstract class GetBusinessConsoleMesReadinessAreaEndpoint(
    string areaCode,
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMesClient mes,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleMesFoundationReadinessRequest, BusinessConsoleMesReadinessArea>(
        auth,
        BusinessGatewayPermissions.MesFoundationRead)
{
    protected override string OrganizationId(BusinessConsoleMesFoundationReadinessRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleMesFoundationReadinessRequest request) => request.EnvironmentId;

    protected override async Task<BusinessConsoleMesReadinessArea> ForwardAsync(
        BusinessConsoleMesFoundationReadinessRequest request,
        string bearerToken,
        CancellationToken cancellationToken)
    {
        try
        {
            return await mes.GetFoundationReadinessAreaAsync(tokenProvider.BearerToken, areaCode, request, cancellationToken);
        }
        catch (BusinessServiceProxyException)
        {
            return SourceUnavailableArea(areaCode);
        }
        catch (HttpRequestException)
        {
            return SourceUnavailableArea(areaCode);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return SourceUnavailableArea(areaCode);
        }
        catch (InvalidOperationException)
        {
            return SourceUnavailableArea(areaCode);
        }
    }

    private static BusinessConsoleMesReadinessArea SourceUnavailableArea(string areaCode) =>
        new(
            areaCode,
            "Blocked",
            [
                new BusinessConsoleMesReadinessIssue(
                    "SOURCE_SERVICE_UNAVAILABLE",
                    "Blocked",
                    "Source service is unavailable or returned invalid readiness data.",
                    areaCode,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    "请稍后重试或联系管理员检查来源服务"),
            ]);
}

[Tags("Business Console MES")]
[HttpGet("/api/business-console/v1/mes/foundation-readiness/master-data")]
[BusinessGatewayOperationId("getBusinessConsoleMesMasterDataReadiness")]
public sealed class GetBusinessConsoleMesMasterDataReadinessEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMesClient mes,
    IInternalServiceTokenProvider tokenProvider)
    : GetBusinessConsoleMesReadinessAreaEndpoint("master-data", auth, mes, tokenProvider);

[Tags("Business Console MES")]
[HttpGet("/api/business-console/v1/mes/foundation-readiness/product-engineering")]
[BusinessGatewayOperationId("getBusinessConsoleMesProductEngineeringReadiness")]
public sealed class GetBusinessConsoleMesProductEngineeringReadinessEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMesClient mes,
    IInternalServiceTokenProvider tokenProvider)
    : GetBusinessConsoleMesReadinessAreaEndpoint("product-engineering", auth, mes, tokenProvider);

[Tags("Business Console MES")]
[HttpGet("/api/business-console/v1/mes/foundation-readiness/supply")]
[BusinessGatewayOperationId("getBusinessConsoleMesSupplyReadiness")]
public sealed class GetBusinessConsoleMesSupplyReadinessEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMesClient mes,
    IInternalServiceTokenProvider tokenProvider)
    : GetBusinessConsoleMesReadinessAreaEndpoint("supply", auth, mes, tokenProvider);

[Tags("Business Console MES")]
[HttpGet("/api/business-console/v1/mes/foundation-readiness/quality")]
[BusinessGatewayOperationId("getBusinessConsoleMesQualityReadiness")]
public sealed class GetBusinessConsoleMesQualityReadinessEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMesClient mes,
    IInternalServiceTokenProvider tokenProvider)
    : GetBusinessConsoleMesReadinessAreaEndpoint("quality", auth, mes, tokenProvider);

[Tags("Business Console MES")]
[HttpGet("/api/business-console/v1/mes/foundation-readiness/equipment")]
[BusinessGatewayOperationId("getBusinessConsoleMesEquipmentReadiness")]
public sealed class GetBusinessConsoleMesEquipmentReadinessEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMesClient mes,
    IInternalServiceTokenProvider tokenProvider)
    : GetBusinessConsoleMesReadinessAreaEndpoint("equipment", auth, mes, tokenProvider);

[Tags("Business Console MES")]
[HttpGet("/api/business-console/v1/mes/foundation-readiness/barcode-coding")]
[BusinessGatewayOperationId("getBusinessConsoleMesBarcodeCodingReadiness")]
public sealed class GetBusinessConsoleMesBarcodeCodingReadinessEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMesClient mes,
    IInternalServiceTokenProvider tokenProvider)
    : GetBusinessConsoleMesReadinessAreaEndpoint("barcode-coding", auth, mes, tokenProvider);

[Tags("Business Console MES")]
[HttpGet("/api/business-console/v1/mes/overview")]
[BusinessGatewayOperationId("getBusinessConsoleMesOverview")]
public sealed class GetBusinessConsoleMesOverviewEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMesClient mes,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleMesContextRequest, BusinessConsoleMesOverviewResponse>(
        auth,
        BusinessGatewayPermissions.MesOverviewRead)
{
    protected override string OrganizationId(BusinessConsoleMesContextRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleMesContextRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleMesOverviewResponse> ForwardAsync(
        BusinessConsoleMesContextRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        mes.GetOverviewAsync(tokenProvider.BearerToken, request, cancellationToken);
}
