using FastEndpoints;
using FluentValidation;
using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.BusinessGateway.Web.Application.OpenApi;
using Nerv.IIP.BusinessGateway.Web.Endpoints;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.BusinessGateway.Web.Endpoints.Mes;

[Tags("Business Console MES")]
[HttpGet("/api/business-console/v1/mes/downtime-events")]
[BusinessGatewayOperationId("listBusinessConsoleMesDowntimeEvents")]
public sealed class ListBusinessConsoleMesDowntimeEventsEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMesClient mes,
    PrincipalWorkScopeResolver workScopeResolver,
    IBusinessMaintenanceClient maintenance,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleMesDowntimeEventListRequest, BusinessConsoleMesDowntimeEventListResponse>(
        auth,
        BusinessGatewayPermissions.MesDowntimeRead)
{
    protected override bool IncludePrincipalContext => true;

    protected override BusinessGatewayAuthorizationContinuityMode AuthorizationContinuityMode =>
        BusinessGatewayAuthorizationContinuityMode.RealtimeRequired;

    protected override string OrganizationId(BusinessConsoleMesDowntimeEventListRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleMesDowntimeEventListRequest request) => request.EnvironmentId;

    protected override async Task<BusinessConsoleMesDowntimeEventListResponse> ForwardAsync(
        BusinessConsoleMesDowntimeEventListRequest request,
        string bearerToken,
        CancellationToken cancellationToken)
    {
        var authorizedWorkCenterIds = await workScopeResolver.ResolveDowntimeWorkCentersAsync(
            AuthorizationResult, request.OrganizationId, request.EnvironmentId,
            BusinessGatewayPermissions.MesDowntimeRead, request.WorkCenterId, cancellationToken);
        var response = await mes.ListDowntimeEventsAsync(
            tokenProvider.BearerToken, request, cancellationToken, authorizedWorkCenterIds);
        return await MesDowntimeReasonNameEnricher.EnrichAsync(
            response,
            maintenance,
            tokenProvider.BearerToken,
            request.OrganizationId,
            request.EnvironmentId,
            cancellationToken);
    }
}

/// <summary>
/// 停机原因码归 Maintenance 的 <c>downtime-reason</c> 目录所有，MES 只存码。读面按
/// api-contract-and-codegen §17「列表行必须随业务标识返回可显示的 *Name」在门面层补中文名，
/// 与 <see cref="MaintenanceDeviceAssetWarrantyEnricher"/> 同一姿势（读面跨域取名走门面，不让页面自己拼）。
/// 目录里没有的码（历史自由文本停机原因）不编名字：<c>ReasonName</c> 留空，页面照实显示原值。
/// </summary>
internal static class MesDowntimeReasonNameEnricher
{
    /// <summary>
    /// 停机原因目录是受控词表（当前世界史种子 5 条），一页取回即可覆盖。目录真超过这一页时本方法
    /// **静默截断**：多出来的码解不出名字，与「目录里没有这个码」落在同一条降级路径上。
    /// </summary>
    private const int CatalogPageSize = 200;

    public static async Task<BusinessConsoleMesDowntimeEventListResponse> EnrichAsync(
        BusinessConsoleMesDowntimeEventListResponse response,
        IBusinessMaintenanceClient maintenance,
        string internalBearerToken,
        string organizationId,
        string environmentId,
        CancellationToken cancellationToken)
    {
        var names = await LoadReasonNamesAsync(
            maintenance, internalBearerToken, organizationId, environmentId, cancellationToken);

        string? Resolve(string? reasonCode) =>
            reasonCode is not null && names.TryGetValue(reasonCode, out var name) ? name : null;

        return response with
        {
            Items = [.. response.Items.Select(item => item with { ReasonName = Resolve(item.ReasonCode) })],
            ReasonSummary = [.. response.ReasonSummary.Select(row => row with { ReasonName = Resolve(row.ReasonCode) })],
        };
    }

    /// <summary>
    /// 取名是**增补**，不是停机读面的事实来源：Maintenance 不可用时停机数据本身完好、原因码原值也在，
    /// 整页 502 是拿一个可降级的依赖去否决一个能正常回答的读面。因此按
    /// <see cref="MaintenanceDeviceAssetWarrantyEnricher"/> 已判定可达的同一组失败降级为「解不出名字」，
    /// 与「目录里没有这个码」走同一条路径（页面照实显示原值），不新增前端分支。
    /// 调用方自己取消不在此列：那不是下游故障，必须原样抛回。
    /// </summary>
    private static async Task<Dictionary<string, string>> LoadReasonNamesAsync(
        IBusinessMaintenanceClient maintenance,
        string internalBearerToken,
        string organizationId,
        string environmentId,
        CancellationToken cancellationToken)
    {
        try
        {
            var catalog = await maintenance.ListDowntimeReasonsAsync(
                internalBearerToken,
                new BusinessConsoleMaintenanceReasonDirectoryRequest(organizationId, environmentId, Take: CatalogPageSize),
                cancellationToken);
            return catalog.Items
                .Where(x => !string.IsNullOrWhiteSpace(x.ReasonCode))
                .ToDictionary(x => x.ReasonCode, x => x.Description, StringComparer.Ordinal);
        }
        catch (BusinessServiceProxyException exception) when (
            BusinessConsoleReadEnrichmentFailurePolicy.CanDegrade(exception.StatusCode))
        {
            return [];
        }
        catch (HttpRequestException)
        {
            return [];
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return [];
        }
    }

}

[Tags("Business Console MES")]
[HttpPost("/api/business-console/v1/mes/downtime-events")]
[BusinessGatewayOperationId("recordBusinessConsoleMesDowntimeEvent")]
[Microsoft.AspNetCore.Mvc.ProducesResponseType(typeof(NetCorePal.Extensions.Dto.ResponseData), StatusCodes.Status400BadRequest)]
public sealed class RecordBusinessConsoleMesDowntimeEventEndpoint(
    IBusinessGatewayAuthorizationClient auth)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleMesRecordDowntimeEventRequest, BusinessConsoleAcceptedResponse>(
        auth,
        BusinessGatewayPermissions.MesDowntimeManage)
{
    protected override string OrganizationId(BusinessConsoleMesRecordDowntimeEventRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleMesRecordDowntimeEventRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleAcceptedResponse> ForwardAsync(
        BusinessConsoleMesRecordDowntimeEventRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        throw BusinessServiceProxyException.FromLocalRejection(
            System.Net.HttpStatusCode.BadRequest,
            BusinessGatewayLocalRejectionStableWireCodes.WorkCenterRequiredUseV2);
}

/// <summary>
/// 端点级幂等键长度上界（#3324）。本处下游权威：
/// MES 侧 RecordDowntimeEventRequestValidator(150) 与 CodeAllocator 落库列 mes.code_idempotency_keys.idempotency_key(150)，取最小。
/// 共同口径见 BusinessConsoleMesEndpoints.cs 顶部的「#3324 端点级幂等键上界」注释块。
/// </summary>
public sealed class BusinessConsoleMesRecordDowntimeEventRequestValidator
    : Validator<BusinessConsoleMesRecordDowntimeEventRequest>
{
    public BusinessConsoleMesRecordDowntimeEventRequestValidator() =>
        RuleFor(x => x.IdempotencyKey).MaximumLength(150);
}

[Tags("Business Console MES")]
[HttpPost("/api/business-console/v2/mes/downtime-events")]
[BusinessGatewayOperationId("recordBusinessConsoleMesDowntimeEventV2")]
public sealed class RecordBusinessConsoleMesDowntimeEventV2Endpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMesClient mes,
    IBusinessMaintenanceClient maintenance,
    MesPrincipalWorkScopeAuthorizer workScopeAuthorizer,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleMesRecordDowntimeEventV2Request, BusinessConsoleAcceptedResponse>(
        auth,
        BusinessGatewayPermissions.MesDowntimeManage)
{
    protected override bool IncludePrincipalContext => true;

    protected override BusinessGatewayAuthorizationContinuityMode AuthorizationContinuityMode =>
        BusinessGatewayAuthorizationContinuityMode.RealtimeRequired;

    protected override string OrganizationId(BusinessConsoleMesRecordDowntimeEventV2Request request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleMesRecordDowntimeEventV2Request request) => request.EnvironmentId;

    protected override async Task<BusinessConsoleAcceptedResponse> ForwardAsync(
        BusinessConsoleMesRecordDowntimeEventV2Request request,
        string bearerToken,
        CancellationToken cancellationToken)
    {
        await workScopeAuthorizer.EnsureWorkCenterAccessAsync(
            AuthorizationResult,
            request.OrganizationId,
            request.EnvironmentId,
            BusinessGatewayPermissions.MesDowntimeManage,
            request.ScopeKind,
            request.ScopeId,
            request.WorkCenterId,
            cancellationToken);
        const int pageSize = 100;
        var reasonCode = request.ReasonCode.Trim();
        var skip = 0;
        while (true)
        {
            var page = await maintenance.ListDowntimeReasonsAsync(
                tokenProvider.BearerToken,
                new BusinessConsoleMaintenanceReasonDirectoryRequest(
                    request.OrganizationId, request.EnvironmentId, reasonCode, skip, pageSize),
                cancellationToken);
            var reason = page.Items.FirstOrDefault(item =>
                string.Equals(item.ReasonCode, reasonCode, StringComparison.Ordinal));
            if (reason is not null)
            {
                reasonCode = reason.ReasonCode;
                break;
            }

            skip += page.Items.Count;
            if (page.Items.Count == 0 || skip >= page.Total)
            {
                throw BusinessServiceProxyException.FromLocalRejection(
                    System.Net.HttpStatusCode.BadRequest, "downtime-reason-not-found");
            }
        }

        return await mes.RecordDowntimeEventAsync(
            tokenProvider.BearerToken,
            new BusinessMesRecordDowntimeEventRequest(
                request.OrganizationId,
                request.EnvironmentId,
                request.WorkOrderId,
                request.OperationTaskId,
                request.WorkCenterId,
                request.DeviceAssetId,
                reasonCode,
                request.StartedAtUtc,
                request.IdempotencyKey,
                request.ToUtc),
            cancellationToken);
    }
}

public sealed class BusinessConsoleMesRecordDowntimeEventV2RequestValidator
    : Validator<BusinessConsoleMesRecordDowntimeEventV2Request>
{
    public BusinessConsoleMesRecordDowntimeEventV2RequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.WorkOrderId).NotEmpty().MaximumLength(200);
        RuleFor(x => x.OperationTaskId).MaximumLength(200);
        RuleFor(x => x.WorkCenterId).NotEmpty().MaximumLength(200);
        RuleFor(x => x.DeviceAssetId).MaximumLength(200);
        RuleFor(x => x.ReasonCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.StartedAtUtc).NotEmpty();
        RuleFor(x => x.IdempotencyKey).NotEmpty().MaximumLength(150);
        RuleFor(x => x.ScopeKind)
            .NotEmpty()
            .MaximumLength(50)
            .Must(Endpoints.Principal.BusinessGatewayWorkScopeKinds.Contains);
        RuleFor(x => x.ScopeId).NotEmpty().MaximumLength(200);
        RuleFor(x => x.ToUtc)
            .GreaterThanOrEqualTo(x => x.StartedAtUtc)
            .When(x => x.ToUtc.HasValue);
    }
}

[Tags("Business Console MES")]
[HttpPost("/api/business-console/v1/mes/downtime-events/{downtimeEventId}/recover")]
[BusinessGatewayOperationId("confirmBusinessConsoleMesDowntimeRecovery")]
public sealed class ConfirmBusinessConsoleMesDowntimeRecoveryEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMesClient mes,
    PrincipalWorkScopeResolver workScopeResolver,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleMesRecoverDowntimeEventRequest, BusinessConsoleAcceptedResponse>(
        auth,
        BusinessGatewayPermissions.MesDowntimeManage)
{
    protected override bool IncludePrincipalContext => true;

    protected override BusinessGatewayAuthorizationContinuityMode AuthorizationContinuityMode =>
        BusinessGatewayAuthorizationContinuityMode.RealtimeRequired;

    protected override string OrganizationId(BusinessConsoleMesRecoverDowntimeEventRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleMesRecoverDowntimeEventRequest request) => request.EnvironmentId;

    protected override async Task<BusinessConsoleAcceptedResponse> ForwardAsync(
        BusinessConsoleMesRecoverDowntimeEventRequest request,
        string bearerToken,
        CancellationToken cancellationToken)
    {
        var authorizedWorkCenterIds = await workScopeResolver.ResolveDowntimeWorkCentersAsync(
            AuthorizationResult, request.OrganizationId, request.EnvironmentId,
            BusinessGatewayPermissions.MesDowntimeManage, null, cancellationToken);
        return await mes.ConfirmDowntimeRecoveryAsync(
            tokenProvider.BearerToken, request.DowntimeEventId, request, cancellationToken, authorizedWorkCenterIds);
    }
}
