using FastEndpoints;
using FluentValidation;
using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.BusinessGateway.Web.Application.OpenApi;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.BusinessGateway.Web.Endpoints.Mes;

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

[Tags("Business Console MES")]
[HttpGet("/api/business-console/v1/mes/production-plans")]
[BusinessGatewayOperationId("listBusinessConsoleMesProductionPlans")]
public sealed class ListBusinessConsoleMesProductionPlansEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMesClient mes,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleMesProductionPlanListRequest, BusinessConsoleMesProductionPlanListResponse>(
        auth,
        BusinessGatewayPermissions.MesPlansRead)
{
    protected override string OrganizationId(BusinessConsoleMesProductionPlanListRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleMesProductionPlanListRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleMesProductionPlanListResponse> ForwardAsync(
        BusinessConsoleMesProductionPlanListRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        mes.ListProductionPlansAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console MES")]
[HttpGet("/api/business-console/v1/mes/production-plans/{productionPlanId}/readiness")]
[BusinessGatewayOperationId("getBusinessConsoleMesProductionPlanReadiness")]
public sealed class GetBusinessConsoleMesProductionPlanReadinessEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMesClient mes,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleMesProductionPlanReadinessRequest, BusinessConsoleMesFoundationReadinessResponse>(
        auth,
        BusinessGatewayPermissions.MesPlansRead)
{
    protected override string OrganizationId(BusinessConsoleMesProductionPlanReadinessRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleMesProductionPlanReadinessRequest request) => request.EnvironmentId;

    protected override async Task<BusinessConsoleMesFoundationReadinessResponse> ForwardAsync(
        BusinessConsoleMesProductionPlanReadinessRequest request,
        string bearerToken,
        CancellationToken cancellationToken)
    {
        try
        {
            return await mes.GetProductionPlanReadinessAsync(
                tokenProvider.BearerToken,
                request.ProductionPlanId,
                new BusinessConsoleMesContextRequest(request.OrganizationId, request.EnvironmentId),
                cancellationToken);
        }
        catch (BusinessServiceProxyException)
        {
            return SourceUnavailableReadiness(request.ProductionPlanId);
        }
        catch (HttpRequestException)
        {
            return SourceUnavailableReadiness(request.ProductionPlanId);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return SourceUnavailableReadiness(request.ProductionPlanId);
        }
        catch (InvalidOperationException)
        {
            return SourceUnavailableReadiness(request.ProductionPlanId);
        }
    }

    private static BusinessConsoleMesFoundationReadinessResponse SourceUnavailableReadiness(string productionPlanId)
    {
        var issue = new BusinessConsoleMesReadinessIssue(
            "SOURCE_SERVICE_UNAVAILABLE",
            "Blocked",
            "Business MES readiness service is unavailable or returned invalid readiness data.",
            "BusinessMes",
            "ProductionPlan",
            productionPlanId,
            productionPlanId,
            null,
            null,
            null,
            "请稍后重试或联系管理员检查 MES 就绪服务");
        var area = new BusinessConsoleMesReadinessArea("mes-readiness", "Blocked", [issue]);
        return new BusinessConsoleMesFoundationReadinessResponse("Blocked", [area], [issue], []);
    }
}

[Tags("Business Console MES")]
[HttpPost("/api/business-console/v1/mes/production-plans/{productionPlanId}/work-orders")]
[BusinessGatewayOperationId("convertBusinessConsoleMesPlanToWorkOrder")]
public sealed class ConvertBusinessConsoleMesPlanToWorkOrderEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMesClient mes,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleMesConvertPlanToWorkOrderRequest, BusinessConsoleAcceptedResponse>(
        auth,
        BusinessGatewayPermissions.MesWorkOrdersManage)
{
    protected override string OrganizationId(BusinessConsoleMesConvertPlanToWorkOrderRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleMesConvertPlanToWorkOrderRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleAcceptedResponse> ForwardAsync(
        BusinessConsoleMesConvertPlanToWorkOrderRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        mes.ConvertPlanToWorkOrderAsync(tokenProvider.BearerToken, request.ProductionPlanId, request, cancellationToken);
}

[Tags("Business Console MES")]
[HttpGet("/api/business-console/v1/mes/work-orders")]
[BusinessGatewayOperationId("listBusinessConsoleMesWorkOrders")]
public sealed class ListBusinessConsoleMesWorkOrdersEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMesClient mes,
    PrincipalWorkScopeResolver workScopeResolver,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleMesWorkOrderListRequest, BusinessConsoleMesWorkOrderListResponse>(
        auth,
        BusinessGatewayPermissions.MesWorkOrdersRead)
{
    protected override bool IncludePrincipalContext => true;

    protected override BusinessGatewayAuthorizationContinuityMode AuthorizationContinuityMode =>
        BusinessGatewayAuthorizationContinuityMode.RealtimeRequired;

    protected override string OrganizationId(BusinessConsoleMesWorkOrderListRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleMesWorkOrderListRequest request) => request.EnvironmentId;

    protected override async Task<BusinessConsoleMesWorkOrderListResponse> ForwardAsync(
        BusinessConsoleMesWorkOrderListRequest request,
        string bearerToken,
        CancellationToken cancellationToken)
    {
        var scope = await workScopeResolver.ResolveAsync(
            AuthorizationResult,
            request.OrganizationId,
            request.EnvironmentId,
            BusinessGatewayPermissions.MesWorkOrdersRead,
            request.ScopeKind,
            request.ScopeId,
            cancellationToken);
        return await mes.ListWorkOrdersAsync(
            tokenProvider.BearerToken,
            new BusinessMesWorkOrderListRequest(
                request.OrganizationId,
                request.EnvironmentId,
                request.Status,
                request.Keyword,
                request.WorkCenterId,
                request.ShiftId,
                request.DeviceAssetId,
                request.Skip,
                request.Take,
                Join(scope.AssignedUserIds),
                Join(scope.TeamIds),
                NarrowRequestedIds(request.WorkCenterIds, scope.WorkCenterIds),
                request.DeviceAssetIds,
                request.Statuses),
            cancellationToken);
    }

    private static string? NarrowRequestedIds(
        string? requestedIds,
        IReadOnlyCollection<string> authorizedIds)
    {
        var requested = Split(requestedIds);
        if (authorizedIds.Count == 0)
        {
            return requested.Count == 0 ? null : string.Join(',', requested);
        }

        if (requested.Count == 0)
        {
            return Join(authorizedIds);
        }

        var authorized = authorizedIds.ToHashSet(StringComparer.Ordinal);
        var narrowed = requested.Where(authorized.Contains).ToArray();
        return narrowed.Length == 0 ? "__principal_scope_no_match__" : string.Join(',', narrowed);
    }

    private static IReadOnlyCollection<string> Split(string? values) =>
        string.IsNullOrWhiteSpace(values)
            ? []
            : values
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray();

    private static string? Join(IReadOnlyCollection<string> values) =>
        values.Count == 0 ? null : string.Join(',', values.Order(StringComparer.Ordinal));
}

[Tags("Business Console MES")]
[HttpGet("/api/business-console/v1/mes/work-orders/{workOrderId}")]
[BusinessGatewayOperationId("getBusinessConsoleMesWorkOrderDetail")]
public sealed class GetBusinessConsoleMesWorkOrderDetailEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMesClient mes,
    MesPrincipalWorkScopeAuthorizer workScopeAuthorizer,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleMesWorkOrderDetailRequest, BusinessConsoleMesWorkOrderDetailResponse>(
        auth,
        BusinessGatewayPermissions.MesWorkOrdersRead)
{
    protected override bool IncludePrincipalContext => true;

    protected override BusinessGatewayAuthorizationContinuityMode AuthorizationContinuityMode =>
        BusinessGatewayAuthorizationContinuityMode.RealtimeRequired;

    protected override string OrganizationId(BusinessConsoleMesWorkOrderDetailRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleMesWorkOrderDetailRequest request) => request.EnvironmentId;

    protected override async Task<BusinessConsoleMesWorkOrderDetailResponse> ForwardAsync(
        BusinessConsoleMesWorkOrderDetailRequest request,
        string bearerToken,
        CancellationToken cancellationToken)
    {
        await workScopeAuthorizer.EnsureWorkOrderAccessAsync(
            AuthorizationResult,
            request.OrganizationId,
            request.EnvironmentId,
            BusinessGatewayPermissions.MesWorkOrdersRead,
            request.ScopeKind,
            request.ScopeId,
            request.WorkOrderId,
            cancellationToken);
        return await mes.GetWorkOrderDetailAsync(
            tokenProvider.BearerToken,
            request.WorkOrderId,
            new BusinessConsoleMesContextRequest(request.OrganizationId, request.EnvironmentId),
            cancellationToken);
    }
}

[Tags("Business Console MES")]
[HttpPost("/api/business-console/v1/mes/work-orders/{workOrderId}/release")]
[BusinessGatewayOperationId("releaseBusinessConsoleMesWorkOrder")]
[Microsoft.AspNetCore.Mvc.ProducesResponseType(typeof(NetCorePal.Extensions.Dto.ResponseData), StatusCodes.Status409Conflict)]
public sealed class ReleaseBusinessConsoleMesWorkOrderEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMesClient mes,
    MesPrincipalWorkScopeAuthorizer workScopeAuthorizer,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleMesReleaseWorkOrderRequest, BusinessConsoleAcceptedResponse>(
        auth,
        BusinessGatewayPermissions.MesWorkOrdersManage)
{
    protected override bool IncludePrincipalContext => true;

    protected override BusinessGatewayAuthorizationContinuityMode AuthorizationContinuityMode =>
        BusinessGatewayAuthorizationContinuityMode.RealtimeRequired;

    protected override string OrganizationId(BusinessConsoleMesReleaseWorkOrderRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleMesReleaseWorkOrderRequest request) => request.EnvironmentId;

    protected override async Task<BusinessConsoleAcceptedResponse> ForwardAsync(
        BusinessConsoleMesReleaseWorkOrderRequest request,
        string bearerToken,
        CancellationToken cancellationToken)
    {
        await workScopeAuthorizer.EnsureWorkOrderAccessAsync(
            AuthorizationResult,
            request.OrganizationId,
            request.EnvironmentId,
            BusinessGatewayPermissions.MesWorkOrdersManage,
            request.ScopeKind,
            request.ScopeId,
            request.WorkOrderId,
            cancellationToken);
        return await mes.ReleaseWorkOrderAsync(
            tokenProvider.BearerToken,
            request.WorkOrderId,
            request,
            cancellationToken);
    }
}

public abstract class BusinessConsoleMesWorkOrderReasonActionEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    MesPrincipalWorkScopeAuthorizer workScopeAuthorizer,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleMesWorkOrderReasonRequest, BusinessConsoleAcceptedResponse>(
        auth,
        BusinessGatewayPermissions.MesWorkOrdersManage)
{
    protected override bool IncludePrincipalContext => true;

    protected override BusinessGatewayAuthorizationContinuityMode AuthorizationContinuityMode =>
        BusinessGatewayAuthorizationContinuityMode.RealtimeRequired;

    protected override string OrganizationId(BusinessConsoleMesWorkOrderReasonRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleMesWorkOrderReasonRequest request) => request.EnvironmentId;

    protected abstract Task<BusinessConsoleAcceptedResponse> ForwardOperationAsync(
        string internalBearerToken,
        BusinessConsoleMesWorkOrderReasonRequest request,
        CancellationToken cancellationToken);

    protected override async Task<BusinessConsoleAcceptedResponse> ForwardAsync(
        BusinessConsoleMesWorkOrderReasonRequest request,
        string bearerToken,
        CancellationToken cancellationToken)
    {
        await workScopeAuthorizer.EnsureWorkOrderAccessAsync(
            AuthorizationResult,
            request.OrganizationId,
            request.EnvironmentId,
            BusinessGatewayPermissions.MesWorkOrdersManage,
            request.ScopeKind,
            request.ScopeId,
            request.WorkOrderId,
            cancellationToken);
        return await ForwardOperationAsync(tokenProvider.BearerToken, request, cancellationToken);
    }
}

[Tags("Business Console MES")]
[HttpPost("/api/business-console/v1/mes/work-orders/{workOrderId}/hold")]
[BusinessGatewayOperationId("holdBusinessConsoleMesWorkOrder")]
[Microsoft.AspNetCore.Mvc.ProducesResponseType(typeof(NetCorePal.Extensions.Dto.ResponseData), StatusCodes.Status409Conflict)]
public sealed class HoldBusinessConsoleMesWorkOrderEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMesClient mes,
    MesPrincipalWorkScopeAuthorizer workScopeAuthorizer,
    IInternalServiceTokenProvider tokenProvider)
    : BusinessConsoleMesWorkOrderReasonActionEndpoint(auth, workScopeAuthorizer, tokenProvider)
{
    protected override Task<BusinessConsoleAcceptedResponse> ForwardOperationAsync(
        string internalBearerToken,
        BusinessConsoleMesWorkOrderReasonRequest request,
        CancellationToken cancellationToken) =>
        mes.HoldWorkOrderAsync(internalBearerToken, request.WorkOrderId, request, cancellationToken);
}

[Tags("Business Console MES")]
[HttpPost("/api/business-console/v1/mes/work-orders/{workOrderId}/cancel")]
[BusinessGatewayOperationId("cancelBusinessConsoleMesWorkOrder")]
[Microsoft.AspNetCore.Mvc.ProducesResponseType(typeof(NetCorePal.Extensions.Dto.ResponseData), StatusCodes.Status409Conflict)]
public sealed class CancelBusinessConsoleMesWorkOrderEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMesClient mes,
    MesPrincipalWorkScopeAuthorizer workScopeAuthorizer,
    IInternalServiceTokenProvider tokenProvider)
    : BusinessConsoleMesWorkOrderReasonActionEndpoint(auth, workScopeAuthorizer, tokenProvider)
{
    protected override Task<BusinessConsoleAcceptedResponse> ForwardOperationAsync(
        string internalBearerToken,
        BusinessConsoleMesWorkOrderReasonRequest request,
        CancellationToken cancellationToken) =>
        mes.CancelWorkOrderAsync(internalBearerToken, request.WorkOrderId, request, cancellationToken);
}

[Tags("Business Console MES")]
[HttpPost("/api/business-console/v1/mes/work-orders/{workOrderId}/close")]
[BusinessGatewayOperationId("closeBusinessConsoleMesWorkOrder")]
public sealed class CloseBusinessConsoleMesWorkOrderEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMesClient mes,
    MesPrincipalWorkScopeAuthorizer workScopeAuthorizer,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleMesCloseWorkOrderRequest, BusinessConsoleAcceptedResponse>(
        auth,
        BusinessGatewayPermissions.MesWorkOrdersManage)
{
    protected override bool IncludePrincipalContext => true;

    protected override BusinessGatewayAuthorizationContinuityMode AuthorizationContinuityMode =>
        BusinessGatewayAuthorizationContinuityMode.RealtimeRequired;

    protected override string OrganizationId(BusinessConsoleMesCloseWorkOrderRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleMesCloseWorkOrderRequest request) => request.EnvironmentId;

    protected override async Task<BusinessConsoleAcceptedResponse> ForwardAsync(
        BusinessConsoleMesCloseWorkOrderRequest request,
        string bearerToken,
        CancellationToken cancellationToken)
    {
        await workScopeAuthorizer.EnsureWorkOrderAccessAsync(
            AuthorizationResult,
            request.OrganizationId,
            request.EnvironmentId,
            BusinessGatewayPermissions.MesWorkOrdersManage,
            request.ScopeKind,
            request.ScopeId,
            request.WorkOrderId,
            cancellationToken);
        return await mes.CloseWorkOrderAsync(tokenProvider.BearerToken, request.WorkOrderId, request, cancellationToken);
    }
}

[Tags("Business Console MES")]
[HttpPost("/api/business-console/v1/mes/work-orders/{workOrderId}/engineering-change-decisions")]
[BusinessGatewayOperationId("recordBusinessConsoleMesEngineeringChangeDecision")]
public sealed class RecordBusinessConsoleMesEngineeringChangeDecisionEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMesClient mes,
    MesPrincipalWorkScopeAuthorizer workScopeAuthorizer,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleMesEngineeringChangeDecisionRequest, BusinessConsoleAcceptedResponse>(
        auth,
        BusinessGatewayPermissions.MesWorkOrdersManage)
{
    protected override bool IncludePrincipalContext => true;

    protected override BusinessGatewayAuthorizationContinuityMode AuthorizationContinuityMode =>
        BusinessGatewayAuthorizationContinuityMode.RealtimeRequired;

    protected override string OrganizationId(BusinessConsoleMesEngineeringChangeDecisionRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleMesEngineeringChangeDecisionRequest request) => request.EnvironmentId;

    protected override async Task<BusinessConsoleAcceptedResponse> ForwardAsync(
        BusinessConsoleMesEngineeringChangeDecisionRequest request,
        string bearerToken,
        CancellationToken cancellationToken)
    {
        await workScopeAuthorizer.EnsureWorkOrderAccessAsync(
            AuthorizationResult,
            request.OrganizationId,
            request.EnvironmentId,
            BusinessGatewayPermissions.MesWorkOrdersManage,
            request.ScopeKind,
            request.ScopeId,
            request.WorkOrderId,
            cancellationToken);
        return await mes.RecordEngineeringChangeDecisionAsync(
            tokenProvider.BearerToken,
            request.WorkOrderId,
            request,
            RequireAuthorizedPrincipalActorReference(),
            cancellationToken);
    }
}

[Tags("Business Console MES")]
[HttpPost("/api/business-console/v1/mes/quality-holds/{sourceDocumentId}/force-release")]
[BusinessGatewayOperationId("forceReleaseBusinessConsoleMesQualityHold")]
public sealed class ForceReleaseBusinessConsoleMesQualityHoldEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMesClient mes,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleMesForceReleaseQualityHoldRequest, BusinessConsoleAcceptedResponse>(
        auth,
        BusinessGatewayPermissions.MesQualityWrite)
{
    protected override string OrganizationId(BusinessConsoleMesForceReleaseQualityHoldRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleMesForceReleaseQualityHoldRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleAcceptedResponse> ForwardAsync(
        BusinessConsoleMesForceReleaseQualityHoldRequest request,
        string bearerToken,
        CancellationToken cancellationToken)
    {
        // Bind the force-release audit actor to the authenticated principal so a caller holding
        // MesQualityWrite cannot forge the releaser identity via a request-body field.
        var actorRef = RequireAuthorizedPrincipalActorReference();
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            throw new BusinessServiceProxyException(System.Net.HttpStatusCode.BadRequest, "idempotency-key-required");
        }
        var correlationId = HttpContext.Request.Headers["X-Correlation-Id"].FirstOrDefault();
        correlationId = string.IsNullOrWhiteSpace(correlationId)
            ? Guid.CreateVersion7().ToString("N")
            : correlationId.Trim();
        return mes.ForceReleaseQualityHoldAsync(
            tokenProvider.BearerToken,
            request.SourceDocumentId,
            request,
            actorRef,
            correlationId,
            cancellationToken);
    }
}

[Tags("Business Console MES")]
[HttpGet("/api/business-console/v1/mes/quality-holds/{sourceDocumentId}/timeline")]
[BusinessGatewayOperationId("getBusinessConsoleMesQualityHoldTimeline")]
public sealed class GetBusinessConsoleMesQualityHoldTimelineEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMesClient mes,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleMesQualityHoldTimelineRequest, BusinessConsoleMesQualityHoldTimelineResponse>(
        auth,
        BusinessGatewayPermissions.MesQualityRead)
{
    protected override string OrganizationId(BusinessConsoleMesQualityHoldTimelineRequest request) => request.OrganizationId;
    protected override string EnvironmentId(BusinessConsoleMesQualityHoldTimelineRequest request) => request.EnvironmentId;
    protected override Task<BusinessConsoleMesQualityHoldTimelineResponse> ForwardAsync(
        BusinessConsoleMesQualityHoldTimelineRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        mes.GetQualityHoldTimelineAsync(tokenProvider.BearerToken, request.SourceDocumentId, request, cancellationToken);
}

[Tags("Business Console MES")]
[HttpPost("/api/business-console/v1/mes/production-reports/{reportNo}/reverse")]
[BusinessGatewayOperationId("reverseBusinessConsoleMesProductionReport")]
public sealed class ReverseBusinessConsoleMesProductionReportEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMesClient mes,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleMesReverseProductionReportRequest, BusinessConsoleMesReverseProductionReportResponse>(
        auth,
        BusinessGatewayPermissions.MesReportingWrite)
{
    protected override string OrganizationId(BusinessConsoleMesReverseProductionReportRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleMesReverseProductionReportRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleMesReverseProductionReportResponse> ForwardAsync(
        BusinessConsoleMesReverseProductionReportRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        mes.ReverseProductionReportAsync(
            tokenProvider.BearerToken,
            request.ReportNo,
            request,
            RequireAuthorizedPrincipalActor().ActorRef,
            cancellationToken);
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

[Tags("Business Console MES")]
[HttpPost("/api/business-console/v1/mes/work-orders/rush")]
[BusinessGatewayOperationId("createBusinessConsoleMesRushWorkOrder")]
public sealed class CreateBusinessConsoleMesRushWorkOrderEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMesClient mes,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleCreateRushWorkOrderRequest, BusinessConsoleCreateRushWorkOrderResponse>(
        auth,
        BusinessGatewayPermissions.MesWorkOrdersManage)
{
    protected override string OrganizationId(BusinessConsoleCreateRushWorkOrderRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleCreateRushWorkOrderRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleCreateRushWorkOrderResponse> ForwardAsync(
        BusinessConsoleCreateRushWorkOrderRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        mes.CreateRushWorkOrderAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console MES")]
[HttpGet("/api/business-console/v1/mes/work-orders/{workOrderId}/material-readiness")]
[BusinessGatewayOperationId("getBusinessConsoleMesMaterialReadiness")]
public sealed class GetBusinessConsoleMesMaterialReadinessEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMesClient mes,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleMesMaterialReadinessRequest, BusinessConsoleMesMaterialReadinessResponse>(
        auth,
        BusinessGatewayPermissions.MesMaterialsRead)
{
    protected override string OrganizationId(BusinessConsoleMesMaterialReadinessRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleMesMaterialReadinessRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleMesMaterialReadinessResponse> ForwardAsync(
        BusinessConsoleMesMaterialReadinessRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        mes.GetMaterialReadinessAsync(
            tokenProvider.BearerToken,
            request.WorkOrderId,
            new BusinessConsoleMesContextRequest(request.OrganizationId, request.EnvironmentId),
            cancellationToken);
}

[Tags("Business Console MES")]
[HttpGet("/api/business-console/v1/mes/work-orders/{workOrderId}/produced-lots")]
[BusinessGatewayOperationId("listBusinessConsoleMesReceivableProducedLots")]
public sealed class ListBusinessConsoleMesReceivableProducedLotsEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMesClient mes,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleMesReceivableProducedLotsRequest, BusinessConsoleMesReceivableProducedLotListResponse>(
        auth,
        BusinessGatewayPermissions.MesReceiptsRead)
{
    protected override string OrganizationId(BusinessConsoleMesReceivableProducedLotsRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleMesReceivableProducedLotsRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleMesReceivableProducedLotListResponse> ForwardAsync(
        BusinessConsoleMesReceivableProducedLotsRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        mes.ListReceivableProducedLotsAsync(
            tokenProvider.BearerToken,
            request.WorkOrderId,
            new BusinessConsoleMesContextRequest(request.OrganizationId, request.EnvironmentId),
            cancellationToken);
}

[Tags("Business Console MES")]
[HttpPost("/api/business-console/v1/mes/work-orders/{workOrderId}/material-issue-requests")]
[BusinessGatewayOperationId("createBusinessConsoleMesMaterialIssueRequest")]
public sealed class CreateBusinessConsoleMesMaterialIssueRequestEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMesClient mes,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleMesCreateMaterialIssueRequest, BusinessConsoleAcceptedResponse>(
        auth,
        BusinessGatewayPermissions.MesMaterialsManage)
{
    protected override string OrganizationId(BusinessConsoleMesCreateMaterialIssueRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleMesCreateMaterialIssueRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleAcceptedResponse> ForwardAsync(
        BusinessConsoleMesCreateMaterialIssueRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        mes.CreateMaterialIssueRequestAsync(tokenProvider.BearerToken, request.WorkOrderId, request, cancellationToken);
}

[Tags("Business Console MES")]
[HttpGet("/api/business-console/v1/mes/material-issue-requests")]
[BusinessGatewayOperationId("listBusinessConsoleMesMaterialIssueRequests")]
public sealed class ListBusinessConsoleMesMaterialIssueRequestsEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMesClient mes,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleMesMaterialIssueRequestListRequest, BusinessConsoleMesMaterialIssueRequestListResponse>(
        auth,
        BusinessGatewayPermissions.MesMaterialsRead)
{
    protected override string OrganizationId(BusinessConsoleMesMaterialIssueRequestListRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleMesMaterialIssueRequestListRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleMesMaterialIssueRequestListResponse> ForwardAsync(
        BusinessConsoleMesMaterialIssueRequestListRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        mes.ListMaterialIssueRequestsAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console MES")]
[HttpPost("/api/business-console/v1/mes/material-issue-requests/{requestId}/line-side-receipts")]
[BusinessGatewayOperationId("confirmBusinessConsoleMesLineSideMaterialReceipt")]
[Microsoft.AspNetCore.Mvc.ProducesResponseType(typeof(NetCorePal.Extensions.Dto.ResponseData), StatusCodes.Status409Conflict)]
public sealed class ConfirmBusinessConsoleMesLineSideMaterialReceiptEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMesClient mes,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleMesConfirmLineSideReceiptRequest, BusinessConsoleAcceptedResponse>(
        auth,
        BusinessGatewayPermissions.MesMaterialsManage)
{
    protected override string OrganizationId(BusinessConsoleMesConfirmLineSideReceiptRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleMesConfirmLineSideReceiptRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleAcceptedResponse> ForwardAsync(
        BusinessConsoleMesConfirmLineSideReceiptRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        mes.ConfirmLineSideMaterialReceiptAsync(tokenProvider.BearerToken, request.RequestId, request, cancellationToken);
}

[Tags("Business Console MES")]
[HttpPost("/api/business-console/v1/mes/material-issue-requests/{requestId}/line-side-returns")]
[BusinessGatewayOperationId("returnBusinessConsoleMesLineSideMaterial")]
[Microsoft.AspNetCore.Mvc.ProducesResponseType(typeof(NetCorePal.Extensions.Dto.ResponseData), StatusCodes.Status409Conflict)]
public sealed class ReturnBusinessConsoleMesLineSideMaterialEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMesClient mes,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleMesReturnLineSideMaterialRequest, BusinessConsoleAcceptedResponse>(
        auth,
        BusinessGatewayPermissions.MesMaterialsManage)
{
    protected override string OrganizationId(BusinessConsoleMesReturnLineSideMaterialRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleMesReturnLineSideMaterialRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleAcceptedResponse> ForwardAsync(
        BusinessConsoleMesReturnLineSideMaterialRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        mes.ReturnLineSideMaterialAsync(tokenProvider.BearerToken, request.RequestId, request, cancellationToken);
}

[Tags("Business Console MES")]
[HttpGet("/api/business-console/v1/mes/dispatch-tasks")]
[BusinessGatewayOperationId("listBusinessConsoleMesDispatchTasks")]
public sealed class ListBusinessConsoleMesDispatchTasksEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMesClient mes,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleMesDispatchTaskListRequest, BusinessConsoleMesDispatchTaskListResponse>(
        auth,
        BusinessGatewayPermissions.MesDispatchRead)
{
    protected override string OrganizationId(BusinessConsoleMesDispatchTaskListRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleMesDispatchTaskListRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleMesDispatchTaskListResponse> ForwardAsync(
        BusinessConsoleMesDispatchTaskListRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        mes.ListDispatchTasksAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console MES")]
[HttpPost("/api/business-console/v1/mes/dispatch-tasks/{operationTaskId}/assign")]
[BusinessGatewayOperationId("assignBusinessConsoleMesDispatchTask")]
public sealed class AssignBusinessConsoleMesDispatchTaskEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMesClient mes,
    IBusinessMasterDataClient masterData,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleMesAssignDispatchTaskRequest, BusinessConsoleAcceptedResponse>(
        auth,
        BusinessGatewayPermissions.MesDispatchManage)
{
    protected override string OrganizationId(BusinessConsoleMesAssignDispatchTaskRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleMesAssignDispatchTaskRequest request) => request.EnvironmentId;

    protected override async Task<BusinessConsoleAcceptedResponse> ForwardAsync(
        BusinessConsoleMesAssignDispatchTaskRequest request,
        string bearerToken,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Every assignee/participant must be a registered, on-duty worker. Resolving all names here keeps
        // collaboration snapshots trustworthy and stops arbitrary caller-provided identities from reaching MES.
        var workerIds = new[] { request.AssignedUserId }
            .Concat(request.Participants?.Select(x => x.WorkerId) ?? [])
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(x => x!)
            .ToArray();
        var resolvedWorkers = await Task.WhenAll(workerIds.Select(ResolveWorkerAsync));
        var workers = resolvedWorkers.ToDictionary(x => x.WorkerId, x => x.Worker, StringComparer.OrdinalIgnoreCase);

        async Task<(string WorkerId, BusinessConsoleWorkerDirectoryItem Worker)> ResolveWorkerAsync(string workerId)
        {
            var directory = await masterData.ListWorkersAsync(
                tokenProvider.BearerToken,
                new BusinessConsoleWorkerDirectoryRequest(
                    request.OrganizationId,
                    request.EnvironmentId,
                    UserId: workerId,
                    PageIndex: 1,
                    PageSize: 1),
                cancellationToken);
            var worker = directory.Items.FirstOrDefault();
            if (worker is null)
            {
                throw new BusinessServiceProxyException(System.Net.HttpStatusCode.BadRequest, $"未找到员工，工人标识 = {workerId}");
            }

            if (!worker.Active || !string.Equals(worker.EmploymentStatus, "active", StringComparison.Ordinal))
            {
                throw new BusinessServiceProxyException(System.Net.HttpStatusCode.BadRequest, $"员工 {worker.DisplayName} 当前不在岗，无法派工。");
            }

            return (workerId, worker);
        }

        string? assignedUserName = null;
        string? teamId = null;
        string? teamName = null;
        if (!string.IsNullOrWhiteSpace(request.AssignedUserId))
        {
            var worker = workers[request.AssignedUserId];
            assignedUserName = worker.DisplayName;

            // 班组随派工落快照，口径与 assignedUserName 一致：由网关从主数据解析，不信调用方传入。
            // 一名工人可能挂多个班组，取其带班的那个，没有带班关系则取首个。
            var team = worker.Teams.FirstOrDefault(x => x.IsLeader) ?? worker.Teams.FirstOrDefault();
            teamId = team?.TeamCode;
            teamName = team?.TeamName;
        }
        var participants = request.Participants?.Select(participant =>
            new BusinessConsoleMesDispatchParticipantForwardInput(
                participant.WorkerId,
                workers[participant.WorkerId].DisplayName,
                participant.SharePercent)).ToArray();

        return await mes.AssignDispatchTaskAsync(
            tokenProvider.BearerToken,
            request.OperationTaskId,
            new BusinessConsoleMesAssignDispatchTaskForwardRequest(
                request.OrganizationId,
                request.EnvironmentId,
                request.AssignedUserId,
                assignedUserName,
                request.DeviceAssetId,
                request.ShiftId,
                request.IdempotencyKey,
                teamId,
                teamName,
                participants),
            RequireAuthorizedPrincipalActorReference(),
            cancellationToken);
    }
}

public sealed class BusinessConsoleMesAssignDispatchTaskRequestValidator
    : Validator<BusinessConsoleMesAssignDispatchTaskRequest>
{
    public BusinessConsoleMesAssignDispatchTaskRequestValidator()
    {
        RuleFor(x => x.Participants).Must(participants => participants is null || participants.Count is > 0 and <= 20)
            .WithMessage("提供参与者列表时必须包含 1 至 20 人。");
        RuleForEach(x => x.Participants).ChildRules(participant =>
        {
            participant.RuleFor(x => x.WorkerId)
                .NotEmpty().WithMessage("参与者人员 ID 不能为空。")
                .MaximumLength(100).WithMessage("参与者人员 ID 长度不能超过 100 个字符。");
            participant.RuleFor(x => x.SharePercent)
                .GreaterThan(0m).WithMessage("工时占比必须大于 0。")
                .LessThanOrEqualTo(100m).WithMessage("工时占比不能超过 100。")
                .Must(HasPersistableSharePrecision)
                .WithMessage("工时占比最多保留四位小数。");
        });
        RuleFor(x => x.Participants).Must(HaveUniqueWorkersAndBalancedShares)
            .WithMessage("工序参与者必须唯一，且工时占比合计必须为 100%。");
    }

    private static bool HaveUniqueWorkersAndBalancedShares(
        IReadOnlyCollection<BusinessConsoleMesDispatchParticipantRequest>? participants)
    {
        if (participants is null || participants.Count == 0)
        {
            return true;
        }

        return participants.All(x => !string.IsNullOrWhiteSpace(x.WorkerId)) &&
            participants.Select(x => x.WorkerId.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() == participants.Count &&
            participants.Sum(x => x.SharePercent) == 100m;
    }

    private static bool HasPersistableSharePrecision(decimal sharePercent) =>
        decimal.Round(sharePercent, 4) == sharePercent;
}

[Tags("Business Console MES")]
[HttpGet("/api/business-console/v1/mes/operation-tasks")]
[BusinessGatewayOperationId("listBusinessConsoleMesOperationTasks")]
public sealed class ListBusinessConsoleMesOperationTasksEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMesClient mes,
    PrincipalWorkScopeResolver workScopeResolver,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleMesOperationTaskListRequest, BusinessConsoleMesOperationTaskListResponse>(
        auth,
        BusinessGatewayPermissions.MesOperationsRead)
{
    protected override bool IncludePrincipalContext => true;

    protected override BusinessGatewayAuthorizationContinuityMode AuthorizationContinuityMode =>
        BusinessGatewayAuthorizationContinuityMode.RealtimeRequired;

    protected override string OrganizationId(BusinessConsoleMesOperationTaskListRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleMesOperationTaskListRequest request) => request.EnvironmentId;

    protected override async Task<BusinessConsoleMesOperationTaskListResponse> ForwardAsync(
        BusinessConsoleMesOperationTaskListRequest request,
        string bearerToken,
        CancellationToken cancellationToken)
    {
        var scopedRequest = await ResolveRequestAsync(
            workScopeResolver,
            AuthorizationResult,
            request,
            BusinessGatewayPermissions.MesOperationsRead,
            cancellationToken);
        return await mes.ListOperationTasksAsync(tokenProvider.BearerToken, scopedRequest, cancellationToken);
    }

    internal static async Task<BusinessMesOperationTaskListRequest> ResolveRequestAsync(
        PrincipalWorkScopeResolver workScopeResolver,
        BusinessGatewayAuthorizationResult? authorization,
        BusinessConsoleMesOperationTaskListRequest request,
        string permissionCode,
        CancellationToken cancellationToken) =>
        await ResolveRequestCoreAsync(
            workScopeResolver,
            authorization,
            new OperationTaskListInput(
                request.OrganizationId,
                request.EnvironmentId,
                request.Status,
                request.Keyword,
                request.WorkCenterId,
                request.ShiftId,
                request.DeviceAssetId,
                request.WorkOrderId,
                request.Skip,
                request.Take,
                request.ScopeKind,
                request.ScopeId,
                request.OperationTaskId),
            permissionCode,
            cancellationToken);

    internal static async Task<BusinessMesOperationTaskListRequest> ResolveRequestAsync(
        PrincipalWorkScopeResolver workScopeResolver,
        BusinessGatewayAuthorizationResult? authorization,
        BusinessConsoleMesReportableOperationTaskListRequest request,
        string permissionCode,
        CancellationToken cancellationToken) =>
        await ResolveRequestCoreAsync(
            workScopeResolver,
            authorization,
            new OperationTaskListInput(
                request.OrganizationId,
                request.EnvironmentId,
                request.Status,
                request.Keyword,
                request.WorkCenterId,
                request.ShiftId,
                request.DeviceAssetId,
                request.WorkOrderId,
                request.Skip,
                request.Take,
                request.ScopeKind,
                request.ScopeId,
                OperationTaskId: null),
            permissionCode,
            cancellationToken);

    private static async Task<BusinessMesOperationTaskListRequest> ResolveRequestCoreAsync(
        PrincipalWorkScopeResolver workScopeResolver,
        BusinessGatewayAuthorizationResult? authorization,
        OperationTaskListInput request,
        string permissionCode,
        CancellationToken cancellationToken)
    {
        var scope = await workScopeResolver.ResolveAsync(
            authorization,
            request.OrganizationId,
            request.EnvironmentId,
            permissionCode,
            request.ScopeKind,
            request.ScopeId,
            cancellationToken);
        return new BusinessMesOperationTaskListRequest(
            request.OrganizationId,
            request.EnvironmentId,
            request.Status,
            request.Keyword,
            request.WorkCenterId,
            request.ShiftId,
            request.DeviceAssetId,
            request.WorkOrderId,
            request.Skip,
            request.Take,
            Join(scope.AssignedUserIds),
            Join(scope.TeamIds),
            Join(scope.WorkCenterIds),
            request.OperationTaskId);
    }

    private sealed record OperationTaskListInput(
        string OrganizationId,
        string EnvironmentId,
        string? Status,
        string? Keyword,
        string? WorkCenterId,
        string? ShiftId,
        string? DeviceAssetId,
        string? WorkOrderId,
        int Skip,
        int Take,
        string? ScopeKind,
        string? ScopeId,
        string? OperationTaskId);

    private static string? Join(IReadOnlyCollection<string> values) =>
        values.Count == 0 ? null : string.Join(',', values.Order(StringComparer.Ordinal));
}

[Tags("Business Console MES")]
[HttpGet("/api/business-console/v1/mes/reportable-operation-tasks")]
[BusinessGatewayOperationId("listBusinessConsoleMesReportableOperationTasks")]
public sealed class ListBusinessConsoleMesReportableOperationTasksEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMesClient mes,
    PrincipalWorkScopeResolver workScopeResolver,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleMesReportableOperationTaskListRequest, BusinessConsoleMesOperationTaskListResponse>(
        auth,
        BusinessGatewayPermissions.MesReportingRead)
{
    protected override bool IncludePrincipalContext => true;

    protected override BusinessGatewayAuthorizationContinuityMode AuthorizationContinuityMode =>
        BusinessGatewayAuthorizationContinuityMode.RealtimeRequired;

    protected override string OrganizationId(BusinessConsoleMesReportableOperationTaskListRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleMesReportableOperationTaskListRequest request) => request.EnvironmentId;

    protected override async Task<BusinessConsoleMesOperationTaskListResponse> ForwardAsync(
        BusinessConsoleMesReportableOperationTaskListRequest request,
        string bearerToken,
        CancellationToken cancellationToken)
    {
        var scopedRequest = await ListBusinessConsoleMesOperationTasksEndpoint.ResolveRequestAsync(
            workScopeResolver,
            AuthorizationResult,
            request,
            BusinessGatewayPermissions.MesReportingRead,
            cancellationToken);
        return await mes.ListReportableOperationTasksAsync(
            tokenProvider.BearerToken,
            scopedRequest,
            cancellationToken);
    }
}

[Tags("Business Console MES")]
[HttpGet("/api/business-console/v1/mes/operation-sops/current")]
[BusinessGatewayOperationId("getBusinessConsoleMesCurrentOperationSops")]
public sealed class GetBusinessConsoleMesCurrentOperationSopsEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessProductEngineeringClient engineering,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleCurrentSopDocumentsRequest, BusinessConsoleCurrentSopDocumentsResponse>(
        auth,
        BusinessGatewayPermissions.MesOperationsRead)
{
    protected override string OrganizationId(BusinessConsoleCurrentSopDocumentsRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleCurrentSopDocumentsRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleCurrentSopDocumentsResponse> ForwardAsync(
        BusinessConsoleCurrentSopDocumentsRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        engineering.GetCurrentSopDocumentsAsync(tokenProvider.BearerToken, request, cancellationToken);
}

public abstract class BusinessConsoleMesOperationTaskActionEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    MesPrincipalWorkScopeAuthorizer workScopeAuthorizer,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleMesOperationTaskActionRequest, BusinessConsoleMesOperationTaskActionResponse>(
        auth,
        BusinessGatewayPermissions.MesOperationsManage)
{
    protected override bool IncludePrincipalContext => true;

    protected override BusinessGatewayAuthorizationContinuityMode AuthorizationContinuityMode =>
        BusinessGatewayAuthorizationContinuityMode.RealtimeRequired;

    protected override string OrganizationId(BusinessConsoleMesOperationTaskActionRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleMesOperationTaskActionRequest request) => request.EnvironmentId;

    protected abstract Task<BusinessConsoleMesOperationTaskActionResponse> ForwardOperationAsync(
        string internalBearerToken,
        BusinessConsoleMesOperationTaskActionRequest request,
        CancellationToken cancellationToken);

    protected override async Task<BusinessConsoleMesOperationTaskActionResponse> ForwardAsync(
        BusinessConsoleMesOperationTaskActionRequest request,
        string bearerToken,
        CancellationToken cancellationToken)
    {
        await workScopeAuthorizer.EnsureOperationTaskAccessAsync(
            AuthorizationResult,
            request.OrganizationId,
            request.EnvironmentId,
            BusinessGatewayPermissions.MesOperationsManage,
            request.ScopeKind,
            request.ScopeId,
            request.OperationTaskId,
            cancellationToken);
        return await ForwardOperationAsync(tokenProvider.BearerToken, request, cancellationToken);
    }
}

[Tags("Business Console MES")]
[HttpPost("/api/business-console/v1/mes/operation-tasks/{operationTaskId}/start")]
[BusinessGatewayOperationId("startBusinessConsoleMesOperationTask")]
[Microsoft.AspNetCore.Mvc.ProducesResponseType(typeof(NetCorePal.Extensions.Dto.ResponseData), StatusCodes.Status409Conflict)]
public sealed class StartBusinessConsoleMesOperationTaskEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMesClient mes,
    MesPrincipalWorkScopeAuthorizer workScopeAuthorizer,
    IInternalServiceTokenProvider tokenProvider)
    : BusinessConsoleMesOperationTaskActionEndpoint(auth, workScopeAuthorizer, tokenProvider)
{
    protected override Task<BusinessConsoleMesOperationTaskActionResponse> ForwardOperationAsync(
        string internalBearerToken,
        BusinessConsoleMesOperationTaskActionRequest request,
        CancellationToken cancellationToken) =>
        mes.StartOperationTaskAsync(internalBearerToken, request.OperationTaskId, request, cancellationToken);
}

[Tags("Business Console MES")]
[HttpPost("/api/business-console/v1/mes/operation-tasks/{operationTaskId}/pause")]
[BusinessGatewayOperationId("pauseBusinessConsoleMesOperationTask")]
[Microsoft.AspNetCore.Mvc.ProducesResponseType(typeof(NetCorePal.Extensions.Dto.ResponseData), StatusCodes.Status409Conflict)]
public sealed class PauseBusinessConsoleMesOperationTaskEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMesClient mes,
    MesPrincipalWorkScopeAuthorizer workScopeAuthorizer,
    IInternalServiceTokenProvider tokenProvider)
    : BusinessConsoleMesOperationTaskActionEndpoint(auth, workScopeAuthorizer, tokenProvider)
{
    protected override Task<BusinessConsoleMesOperationTaskActionResponse> ForwardOperationAsync(
        string internalBearerToken,
        BusinessConsoleMesOperationTaskActionRequest request,
        CancellationToken cancellationToken) =>
        mes.PauseOperationTaskAsync(internalBearerToken, request.OperationTaskId, request, cancellationToken);
}

[Tags("Business Console MES")]
[HttpPost("/api/business-console/v1/mes/operation-tasks/{operationTaskId}/resume")]
[BusinessGatewayOperationId("resumeBusinessConsoleMesOperationTask")]
[Microsoft.AspNetCore.Mvc.ProducesResponseType(typeof(NetCorePal.Extensions.Dto.ResponseData), StatusCodes.Status409Conflict)]
public sealed class ResumeBusinessConsoleMesOperationTaskEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMesClient mes,
    MesPrincipalWorkScopeAuthorizer workScopeAuthorizer,
    IInternalServiceTokenProvider tokenProvider)
    : BusinessConsoleMesOperationTaskActionEndpoint(auth, workScopeAuthorizer, tokenProvider)
{
    protected override Task<BusinessConsoleMesOperationTaskActionResponse> ForwardOperationAsync(
        string internalBearerToken,
        BusinessConsoleMesOperationTaskActionRequest request,
        CancellationToken cancellationToken) =>
        mes.ResumeOperationTaskAsync(internalBearerToken, request.OperationTaskId, request, cancellationToken);
}

[Tags("Business Console MES")]
[HttpPost("/api/business-console/v1/mes/operation-tasks/{operationTaskId}/complete")]
[BusinessGatewayOperationId("completeBusinessConsoleMesOperationTask")]
[Microsoft.AspNetCore.Mvc.ProducesResponseType(typeof(NetCorePal.Extensions.Dto.ResponseData), StatusCodes.Status409Conflict)]
public sealed class CompleteBusinessConsoleMesOperationTaskEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMesClient mes,
    MesPrincipalWorkScopeAuthorizer workScopeAuthorizer,
    IInternalServiceTokenProvider tokenProvider)
    : BusinessConsoleMesOperationTaskActionEndpoint(auth, workScopeAuthorizer, tokenProvider)
{
    protected override Task<BusinessConsoleMesOperationTaskActionResponse> ForwardOperationAsync(
        string internalBearerToken,
        BusinessConsoleMesOperationTaskActionRequest request,
        CancellationToken cancellationToken) =>
        mes.CompleteOperationTaskAsync(internalBearerToken, request.OperationTaskId, request, cancellationToken);
}

[Tags("Business Console MES")]
[HttpGet("/api/business-console/v1/mes/wip")]
[BusinessGatewayOperationId("getBusinessConsoleMesWipSummary")]
public sealed class GetBusinessConsoleMesWipSummaryEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMesClient mes,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleMesListRequest, BusinessConsoleMesWipSummaryResponse>(
        auth,
        BusinessGatewayPermissions.MesOperationsRead)
{
    protected override string OrganizationId(BusinessConsoleMesListRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleMesListRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleMesWipSummaryResponse> ForwardAsync(
        BusinessConsoleMesListRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        mes.GetWipSummaryAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console MES")]
[HttpGet("/api/business-console/v1/mes/production-reports")]
[BusinessGatewayOperationId("listBusinessConsoleMesProductionReports")]
public sealed class ListBusinessConsoleMesProductionReportsEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMesClient mes,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleMesListWithoutStatusRequest, BusinessConsoleMesProductionReportListResponse>(
        auth,
        BusinessGatewayPermissions.MesReportingRead)
{
    protected override string OrganizationId(BusinessConsoleMesListWithoutStatusRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleMesListWithoutStatusRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleMesProductionReportListResponse> ForwardAsync(
        BusinessConsoleMesListWithoutStatusRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        mes.ListProductionReportsAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console MES")]
[HttpGet("/api/business-console/v1/mes/production-reports/{reportNo}")]
[BusinessGatewayOperationId("getBusinessConsoleMesProductionReport")]
public sealed class GetBusinessConsoleMesProductionReportEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMesClient mes,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleMesProductionReportDetailRequest, BusinessConsoleMesProductionReportDetailResponse>(
        auth,
        BusinessGatewayPermissions.MesReportingRead)
{
    protected override string OrganizationId(BusinessConsoleMesProductionReportDetailRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleMesProductionReportDetailRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleMesProductionReportDetailResponse> ForwardAsync(
        BusinessConsoleMesProductionReportDetailRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        mes.GetProductionReportAsync(
            tokenProvider.BearerToken,
            request.ReportNo,
            new BusinessConsoleMesContextRequest(request.OrganizationId, request.EnvironmentId),
            cancellationToken);
}

[Tags("Business Console MES")]
[HttpPost("/api/business-console/v1/mes/production-reports")]
[BusinessGatewayOperationId("recordBusinessConsoleMesProductionReport")]
[Microsoft.AspNetCore.Mvc.ProducesResponseType(typeof(NetCorePal.Extensions.Dto.ResponseData), StatusCodes.Status409Conflict)]
public sealed class RecordBusinessConsoleMesProductionReportEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMesClient mes,
    MesPrincipalWorkScopeAuthorizer workScopeAuthorizer,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleRecordProductionReportRequest, BusinessConsoleRecordProductionReportResponse>(
        auth,
        BusinessGatewayPermissions.MesReportingWrite)
{
    protected override bool IncludePrincipalContext => true;

    protected override BusinessGatewayAuthorizationContinuityMode AuthorizationContinuityMode =>
        BusinessGatewayAuthorizationContinuityMode.RealtimeRequired;

    protected override string OrganizationId(BusinessConsoleRecordProductionReportRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleRecordProductionReportRequest request) => request.EnvironmentId;

    protected override async Task<BusinessConsoleRecordProductionReportResponse> ForwardAsync(
        BusinessConsoleRecordProductionReportRequest request,
        string bearerToken,
        CancellationToken cancellationToken)
    {
        await workScopeAuthorizer.EnsureOperationTaskAccessAsync(
            AuthorizationResult,
            request.OrganizationId,
            request.EnvironmentId,
            BusinessGatewayPermissions.MesReportingWrite,
            request.ScopeKind,
            request.ScopeId,
            request.OperationTaskId,
            cancellationToken);
        return await mes.RecordProductionReportAsync(
            tokenProvider.BearerToken,
            request,
            cancellationToken);
    }
}

public sealed class BusinessConsoleRecordProductionReportRequestValidator
    : Validator<BusinessConsoleRecordProductionReportRequest>
{
    public BusinessConsoleRecordProductionReportRequestValidator()
    {
        RuleFor(x => x.IdempotencyKey).NotEmpty().MaximumLength(150);
        RuleFor(x => x.ScopeKind)
            .NotEmpty()
            .MaximumLength(50)
            .Must(Endpoints.Principal.BusinessGatewayWorkScopeKinds.Contains);
        RuleFor(x => x.ScopeId).NotEmpty().MaximumLength(200);
    }
}

public sealed class BusinessConsoleMesOperationTaskActionRequestValidator
    : Validator<BusinessConsoleMesOperationTaskActionRequest>
{
    public BusinessConsoleMesOperationTaskActionRequestValidator()
    {
        RuleFor(x => x.IdempotencyKey).NotEmpty().MaximumLength(150);
        RuleFor(x => x.ScopeKind)
            .NotEmpty()
            .MaximumLength(50)
            .Must(Endpoints.Principal.BusinessGatewayWorkScopeKinds.Contains);
        RuleFor(x => x.ScopeId).NotEmpty().MaximumLength(200);
    }
}

[Tags("Business Console MES")]
[HttpGet("/api/business-console/v1/mes/telemetry-production-report-candidates")]
[BusinessGatewayOperationId("listBusinessConsoleMesTelemetryProductionReportCandidates")]
public sealed class ListBusinessConsoleMesTelemetryCandidatesEndpoint(IBusinessGatewayAuthorizationClient auth, IBusinessMesClient mes, IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleMesTelemetryCandidateListRequest, BusinessConsoleMesTelemetryCandidateListResponse>(auth, BusinessGatewayPermissions.MesReportingRead)
{
    protected override string OrganizationId(BusinessConsoleMesTelemetryCandidateListRequest request) => request.OrganizationId;
    protected override string EnvironmentId(BusinessConsoleMesTelemetryCandidateListRequest request) => request.EnvironmentId;
    protected override Task<BusinessConsoleMesTelemetryCandidateListResponse> ForwardAsync(BusinessConsoleMesTelemetryCandidateListRequest request, string bearerToken, CancellationToken cancellationToken) =>
        mes.ListTelemetryCandidatesAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console MES")]
[HttpGet("/api/business-console/v1/mes/telemetry-production-report-candidates/{candidateId}")]
[BusinessGatewayOperationId("getBusinessConsoleMesTelemetryProductionReportCandidate")]
public sealed class GetBusinessConsoleMesTelemetryCandidateEndpoint(IBusinessGatewayAuthorizationClient auth, IBusinessMesClient mes, IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleMesTelemetryCandidateDetailRequest, BusinessConsoleMesTelemetryCandidateRow>(auth, BusinessGatewayPermissions.MesReportingRead)
{
    protected override string OrganizationId(BusinessConsoleMesTelemetryCandidateDetailRequest request) => request.OrganizationId;
    protected override string EnvironmentId(BusinessConsoleMesTelemetryCandidateDetailRequest request) => request.EnvironmentId;
    protected override Task<BusinessConsoleMesTelemetryCandidateRow> ForwardAsync(BusinessConsoleMesTelemetryCandidateDetailRequest request, string bearerToken, CancellationToken cancellationToken) =>
        mes.GetTelemetryCandidateAsync(tokenProvider.BearerToken, request.CandidateId, request.OrganizationId, request.EnvironmentId, cancellationToken);
}

[Tags("Business Console MES")]
[HttpPost("/api/business-console/v1/mes/telemetry-production-report-candidates/{candidateId}/promote")]
[BusinessGatewayOperationId("promoteBusinessConsoleMesTelemetryProductionReportCandidate")]
public sealed class PromoteBusinessConsoleMesTelemetryCandidateEndpoint(IBusinessGatewayAuthorizationClient auth, IBusinessMesClient mes, IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleMesTelemetryCandidatePromoteRequest, BusinessConsoleRecordProductionReportResponse>(auth, BusinessGatewayPermissions.MesReportingWrite)
{
    protected override string OrganizationId(BusinessConsoleMesTelemetryCandidatePromoteRequest request) => request.OrganizationId;
    protected override string EnvironmentId(BusinessConsoleMesTelemetryCandidatePromoteRequest request) => request.EnvironmentId;
    protected override Task<BusinessConsoleRecordProductionReportResponse> ForwardAsync(BusinessConsoleMesTelemetryCandidatePromoteRequest request, string bearerToken, CancellationToken cancellationToken) =>
        mes.PromoteTelemetryCandidateAsync(tokenProvider.BearerToken, request.CandidateId, request, RequireAuthorizedPrincipalActor().ActorRef, cancellationToken);
}

[Tags("Business Console MES")]
[HttpPost("/api/business-console/v1/mes/telemetry-production-report-candidates/{candidateId}/dismiss")]
[BusinessGatewayOperationId("dismissBusinessConsoleMesTelemetryProductionReportCandidate")]
public sealed class DismissBusinessConsoleMesTelemetryCandidateEndpoint(IBusinessGatewayAuthorizationClient auth, IBusinessMesClient mes, IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleMesTelemetryCandidateDismissRequest, BusinessConsoleAcceptedResponse>(auth, BusinessGatewayPermissions.MesReportingWrite)
{
    protected override string OrganizationId(BusinessConsoleMesTelemetryCandidateDismissRequest request) => request.OrganizationId;
    protected override string EnvironmentId(BusinessConsoleMesTelemetryCandidateDismissRequest request) => request.EnvironmentId;
    protected override Task<BusinessConsoleAcceptedResponse> ForwardAsync(BusinessConsoleMesTelemetryCandidateDismissRequest request, string bearerToken, CancellationToken cancellationToken) =>
        mes.DismissTelemetryCandidateAsync(tokenProvider.BearerToken, request.CandidateId, request, RequireAuthorizedPrincipalActor().ActorRef, cancellationToken);
}

[Tags("Business Console MES")]
[HttpPost("/api/business-console/v1/mes/defects")]
[BusinessGatewayOperationId("recordBusinessConsoleMesDefect")]
public sealed class RecordBusinessConsoleMesDefectEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMesClient mes,
    IInternalServiceTokenProvider tokenProvider,
    TimeProvider timeProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleMesRecordDefectRequest, BusinessConsoleAcceptedResponse>(
        auth,
        BusinessGatewayPermissions.MesQualityWrite)
{
    protected override string OrganizationId(BusinessConsoleMesRecordDefectRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleMesRecordDefectRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleAcceptedResponse> ForwardAsync(
        BusinessConsoleMesRecordDefectRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        mes.RecordDefectAsync(
            tokenProvider.BearerToken,
            new BusinessMesRecordDefectRequest(
                request.OrganizationId,
                request.EnvironmentId,
                request.WorkOrderId,
                request.OperationTaskId,
                request.DefectCode,
                request.DefectQuantity,
                timeProvider.GetUtcNow(),
                request.IdempotencyKey),
            cancellationToken);
}

[Tags("Business Console MES")]
[HttpPost("/api/business-console/v2/mes/defects")]
[BusinessGatewayOperationId("recordBusinessConsoleMesDefectV2")]
public sealed class RecordBusinessConsoleMesDefectV2Endpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMesClient mes,
    MesPrincipalWorkScopeAuthorizer workScopeAuthorizer,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleMesRecordDefectV2Request, BusinessConsoleAcceptedResponse>(
        auth,
        BusinessGatewayPermissions.MesQualityWrite)
{
    protected override bool IncludePrincipalContext => true;

    protected override BusinessGatewayAuthorizationContinuityMode AuthorizationContinuityMode =>
        BusinessGatewayAuthorizationContinuityMode.RealtimeRequired;

    protected override string OrganizationId(BusinessConsoleMesRecordDefectV2Request request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleMesRecordDefectV2Request request) => request.EnvironmentId;

    protected override async Task<BusinessConsoleAcceptedResponse> ForwardAsync(
        BusinessConsoleMesRecordDefectV2Request request,
        string bearerToken,
        CancellationToken cancellationToken)
    {
        await workScopeAuthorizer.EnsureWorkOrderAccessAsync(
            AuthorizationResult,
            request.OrganizationId,
            request.EnvironmentId,
            BusinessGatewayPermissions.MesQualityWrite,
            request.ScopeKind,
            request.ScopeId,
            request.WorkOrderId,
            cancellationToken);
        return await mes.RecordDefectAsync(
            tokenProvider.BearerToken,
            new BusinessMesRecordDefectRequest(
                request.OrganizationId,
                request.EnvironmentId,
                request.WorkOrderId,
                request.OperationTaskId,
                request.DefectCode,
                request.Quantity,
                request.RecordedAtUtc,
                request.IdempotencyKey),
            cancellationToken);
    }
}

public sealed class BusinessConsoleMesRecordDefectV2RequestValidator
    : Validator<BusinessConsoleMesRecordDefectV2Request>
{
    public BusinessConsoleMesRecordDefectV2RequestValidator()
    {
        RuleFor(x => x.WorkOrderId).NotEmpty().MaximumLength(200);
        RuleFor(x => x.OperationTaskId).MaximumLength(200);
        RuleFor(x => x.DefectCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Quantity).NotEmpty().GreaterThan(0);
        RuleFor(x => x.RecordedAtUtc).NotEmpty();
        RuleFor(x => x.IdempotencyKey).NotEmpty().MaximumLength(150);
        RuleFor(x => x.ScopeKind)
            .NotEmpty()
            .MaximumLength(50)
            .Must(Endpoints.Principal.BusinessGatewayWorkScopeKinds.Contains);
        RuleFor(x => x.ScopeId).NotEmpty().MaximumLength(200);
    }
}

[Tags("Business Console MES")]
[HttpGet("/api/business-console/v1/mes/related-quality-items")]
[BusinessGatewayOperationId("listBusinessConsoleMesRelatedQualityItems")]
public sealed class ListBusinessConsoleMesRelatedQualityItemsEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMesClient mes,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleMesListRequest, BusinessConsoleMesRelatedQualityItemListResponse>(
        auth,
        BusinessGatewayPermissions.MesQualityRead)
{
    protected override string OrganizationId(BusinessConsoleMesListRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleMesListRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleMesRelatedQualityItemListResponse> ForwardAsync(
        BusinessConsoleMesListRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        mes.ListRelatedQualityItemsAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console MES")]
[HttpGet("/api/business-console/v1/mes/finished-goods-receipt-requests")]
[BusinessGatewayOperationId("listBusinessConsoleMesFinishedGoodsReceiptRequests")]
public sealed class ListBusinessConsoleMesFinishedGoodsReceiptRequestsEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMesClient mes,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleMesListRequest, BusinessConsoleMesReceiptRequestListResponse>(
        auth,
        BusinessGatewayPermissions.MesReceiptsRead)
{
    protected override string OrganizationId(BusinessConsoleMesListRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleMesListRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleMesReceiptRequestListResponse> ForwardAsync(
        BusinessConsoleMesListRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        mes.ListFinishedGoodsReceiptRequestsAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console MES")]
[HttpGet("/api/business-console/v1/mes/finished-goods-receipt-requests/{requestNo}/inventory-link")]
[BusinessGatewayOperationId("getBusinessConsoleMesFinishedGoodsReceiptInventoryLink")]
public sealed class GetBusinessConsoleMesFinishedGoodsReceiptInventoryLinkEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMesClient mes,
    IBusinessInventoryClient inventory,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleMesFinishedGoodsInventoryLinkRequest, BusinessConsoleMesFinishedGoodsInventoryLinkResponse>(
        auth,
        BusinessGatewayPermissions.MesReceiptsRead)
{
    private const string MesSourceService = "business-mes";

    protected override string OrganizationId(BusinessConsoleMesFinishedGoodsInventoryLinkRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleMesFinishedGoodsInventoryLinkRequest request) => request.EnvironmentId;

    protected override async Task<BusinessConsoleMesFinishedGoodsInventoryLinkResponse> ForwardAsync(
        BusinessConsoleMesFinishedGoodsInventoryLinkRequest request,
        string bearerToken,
        CancellationToken cancellationToken)
    {
        var inventoryAuthorization = await AuthorizationClient.CheckAsync(
            bearerToken,
            new BusinessGatewayPermissionRequirement(
                BusinessGatewayPermissions.InventoryLedgerRead,
                request.OrganizationId,
                request.EnvironmentId,
                null,
                null),
            cancellationToken);
        if (!inventoryAuthorization.IsAllowed)
        {
            throw new BusinessServiceProxyException(
                System.Net.HttpStatusCode.Forbidden,
                inventoryAuthorization.DenialReason ?? "forbidden");
        }

        var receipts = await mes.ListFinishedGoodsReceiptRequestsAsync(
            tokenProvider.BearerToken,
            new BusinessConsoleMesListRequest(
                request.OrganizationId,
                request.EnvironmentId,
                WorkOrderId: request.WorkOrderId,
                Take: 2),
            cancellationToken,
            request.RequestNo);
        var receipt = receipts.Items.SingleOrDefault(item =>
            string.Equals(item.RequestNo, request.RequestNo, StringComparison.Ordinal)
            && (string.IsNullOrWhiteSpace(request.WorkOrderId)
                || string.Equals(item.WorkOrderId, request.WorkOrderId, StringComparison.Ordinal)));
        if (receipt is null)
        {
            throw new BusinessServiceProxyException(System.Net.HttpStatusCode.NotFound, "finished-goods-receipt-not-found");
        }

        var stock = await inventory.GetStockBySourceAsync(
            tokenProvider.BearerToken,
            new BusinessConsoleInventoryStockBySourceRequest(
                request.OrganizationId,
                request.EnvironmentId,
                MesSourceService,
                receipt.RequestNo,
                receipt.WorkOrderId),
            cancellationToken);

        return new BusinessConsoleMesFinishedGoodsInventoryLinkResponse(
            LinkStatus(receipt, stock),
            receipt.RequestNo,
            receipt.WorkOrderId,
            receipt.WorkOrderNo,
            receipt.SkuId,
            receipt.SkuCode,
            receipt.ProducedLotNo,
            receipt.SerialNo,
            receipt.Quantity,
            receipt.PostedQuantity,
            receipt.RemainingQuantity,
            receipt.ReceiptStatus,
            receipt.PostedInventoryMovementId,
            receipt.PostedAtUtc,
            receipt.InventoryPostingFailureCode,
            receipt.InventoryPostingFailureMessage,
            receipt.InventoryPostingFailedAtUtc,
            stock.SourceService,
            stock.SourceDocumentId ?? receipt.RequestNo,
            stock.SourceDocumentLineId ?? receipt.WorkOrderId,
            stock.IsEstablished,
            stock.Movements,
            stock.Balances);
    }

    private static string LinkStatus(
        BusinessConsoleMesReceiptRequestRow receipt,
        BusinessConsoleInventoryStockBySourceResponse stock)
    {
        if (string.Equals(receipt.ReceiptStatus, "InventoryPostingFailed", StringComparison.OrdinalIgnoreCase))
        {
            return stock.IsEstablished ? "partiallyPosted" : "postingFailed";
        }

        if (!stock.IsEstablished)
        {
            return "notPosted";
        }

        if (stock.Balances.Any(balance =>
                balance.OnHandQuantity > 0
                && !string.Equals(balance.QualityStatus, "unrestricted", StringComparison.OrdinalIgnoreCase)))
        {
            return "qualityRestricted";
        }

        if (string.Equals(receipt.ReceiptStatus, "PartiallyPosted", StringComparison.OrdinalIgnoreCase)
            || receipt.RemainingQuantity > 0)
        {
            return "partiallyPosted";
        }

        return "posted";
    }
}

[Tags("Business Console MES")]
[HttpPost("/api/business-console/v1/mes/finished-goods-receipt-requests")]
[BusinessGatewayOperationId("createBusinessConsoleMesFinishedGoodsReceiptRequest")]
public sealed class CreateBusinessConsoleMesFinishedGoodsReceiptRequestEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMesClient mes,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleMesCreateReceiptRequest, BusinessConsoleMesCreateReceiptResponse>(
        auth,
        BusinessGatewayPermissions.MesReceiptsManage)
{
    protected override string OrganizationId(BusinessConsoleMesCreateReceiptRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleMesCreateReceiptRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleMesCreateReceiptResponse> ForwardAsync(
        BusinessConsoleMesCreateReceiptRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        mes.CreateFinishedGoodsReceiptRequestAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console MES")]
[HttpGet("/api/business-console/v1/mes/downtime-events")]
[BusinessGatewayOperationId("listBusinessConsoleMesDowntimeEvents")]
public sealed class ListBusinessConsoleMesDowntimeEventsEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMesClient mes,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleMesListRequest, BusinessConsoleMesDowntimeEventListResponse>(
        auth,
        BusinessGatewayPermissions.MesDowntimeRead)
{
    protected override string OrganizationId(BusinessConsoleMesListRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleMesListRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleMesDowntimeEventListResponse> ForwardAsync(
        BusinessConsoleMesListRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        mes.ListDowntimeEventsAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console MES")]
[HttpPost("/api/business-console/v1/mes/downtime-events")]
[BusinessGatewayOperationId("recordBusinessConsoleMesDowntimeEvent")]
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
        throw BusinessServiceProxyException.FromSafeDownstreamMessage(
            System.Net.HttpStatusCode.BadRequest,
            "work-center-required-use-v2");
}

public sealed class BusinessConsoleMesRecordDowntimeEventRequestValidator
    : Validator<BusinessConsoleMesRecordDowntimeEventRequest>
{
    public BusinessConsoleMesRecordDowntimeEventRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.WorkOrderId).NotEmpty().MaximumLength(200);
        RuleFor(x => x.OperationTaskId).MaximumLength(200);
        RuleFor(x => x.DeviceAssetId).MaximumLength(200);
        RuleFor(x => x.ReasonCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.StartedAtUtc).NotEmpty();
        RuleFor(x => x.IdempotencyKey).NotEmpty().MaximumLength(150);
    }
}

[Tags("Business Console MES")]
[HttpPost("/api/business-console/v2/mes/downtime-events")]
[BusinessGatewayOperationId("recordBusinessConsoleMesDowntimeEventV2")]
public sealed class RecordBusinessConsoleMesDowntimeEventV2Endpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMesClient mes,
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
        return await mes.RecordDowntimeEventAsync(
            tokenProvider.BearerToken,
            new BusinessMesRecordDowntimeEventRequest(
                request.OrganizationId,
                request.EnvironmentId,
                request.WorkOrderId,
                request.OperationTaskId,
                request.WorkCenterId,
                request.DeviceAssetId,
                request.ReasonCode,
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
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleMesRecoverDowntimeEventRequest, BusinessConsoleAcceptedResponse>(
        auth,
        BusinessGatewayPermissions.MesDowntimeManage)
{
    protected override string OrganizationId(BusinessConsoleMesRecoverDowntimeEventRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleMesRecoverDowntimeEventRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleAcceptedResponse> ForwardAsync(
        BusinessConsoleMesRecoverDowntimeEventRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        mes.ConfirmDowntimeRecoveryAsync(tokenProvider.BearerToken, request.DowntimeEventId, request, cancellationToken);
}

[Tags("Business Console MES")]
[HttpGet("/api/business-console/v1/mes/schedules")]
[BusinessGatewayOperationId("listBusinessConsoleMesScheduleResults")]
public sealed class ListBusinessConsoleMesScheduleResultsEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMesClient mes,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleMesScheduleResultListRequest, BusinessConsoleMesScheduleResultListResponse>(
        auth,
        BusinessGatewayPermissions.MesSchedulesRead)
{
    protected override string OrganizationId(BusinessConsoleMesScheduleResultListRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleMesScheduleResultListRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleMesScheduleResultListResponse> ForwardAsync(
        BusinessConsoleMesScheduleResultListRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        mes.ListScheduleResultsAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console MES")]
[HttpGet("/api/business-console/v1/mes/shift-handovers")]
[BusinessGatewayOperationId("listBusinessConsoleMesShiftHandovers")]
public sealed class ListBusinessConsoleMesShiftHandoversEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMesClient mes,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleMesListRequest, BusinessConsoleMesShiftHandoverListResponse>(
        auth,
        BusinessGatewayPermissions.MesHandoversRead)
{
    protected override string OrganizationId(BusinessConsoleMesListRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleMesListRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleMesShiftHandoverListResponse> ForwardAsync(
        BusinessConsoleMesListRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        mes.ListShiftHandoversAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console MES")]
[HttpPost("/api/business-console/v1/mes/shift-handovers")]
[BusinessGatewayOperationId("createBusinessConsoleMesShiftHandover")]
public sealed class CreateBusinessConsoleMesShiftHandoverEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMesClient mes,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleMesCreateShiftHandoverRequest, BusinessConsoleAcceptedResponse>(
        auth,
        BusinessGatewayPermissions.MesHandoversManage)
{
    protected override string OrganizationId(BusinessConsoleMesCreateShiftHandoverRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleMesCreateShiftHandoverRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleAcceptedResponse> ForwardAsync(
        BusinessConsoleMesCreateShiftHandoverRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        mes.CreateShiftHandoverAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console MES")]
[HttpPost("/api/business-console/v1/mes/shift-handovers/{handoverId}/accept")]
[BusinessGatewayOperationId("acceptBusinessConsoleMesShiftHandover")]
public sealed class AcceptBusinessConsoleMesShiftHandoverEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMesClient mes,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleMesAcceptShiftHandoverRequest, BusinessConsoleAcceptedResponse>(
        auth,
        BusinessGatewayPermissions.MesHandoversManage)
{
    protected override string OrganizationId(BusinessConsoleMesAcceptShiftHandoverRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleMesAcceptShiftHandoverRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleAcceptedResponse> ForwardAsync(
        BusinessConsoleMesAcceptShiftHandoverRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        mes.AcceptShiftHandoverAsync(tokenProvider.BearerToken, request.HandoverId, request, cancellationToken);
}

[Tags("Business Console MES")]
[HttpGet("/api/business-console/v1/mes/traceability/work-orders/{workOrderId}")]
[BusinessGatewayOperationId("getBusinessConsoleMesWorkOrderTraceability")]
public sealed class GetBusinessConsoleMesWorkOrderTraceabilityEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMesClient mes,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleMesTraceabilityByWorkOrderRequest, BusinessConsoleMesTraceabilityResponse>(
        auth,
        BusinessGatewayPermissions.MesTraceabilityRead)
{
    protected override string OrganizationId(BusinessConsoleMesTraceabilityByWorkOrderRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleMesTraceabilityByWorkOrderRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleMesTraceabilityResponse> ForwardAsync(
        BusinessConsoleMesTraceabilityByWorkOrderRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        mes.GetWorkOrderTraceabilityAsync(
            tokenProvider.BearerToken,
            request.WorkOrderId,
            new BusinessConsoleMesContextRequest(request.OrganizationId, request.EnvironmentId),
            cancellationToken);
}

[Tags("Business Console MES")]
[HttpGet("/api/business-console/v1/mes/traceability/batches/{batchOrSerial}")]
[BusinessGatewayOperationId("getBusinessConsoleMesBatchTraceability")]
public sealed class GetBusinessConsoleMesBatchTraceabilityEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMesClient mes,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleMesTraceabilityByBatchRequest, BusinessConsoleMesTraceabilityResponse>(
        auth,
        BusinessGatewayPermissions.MesTraceabilityRead)
{
    protected override string OrganizationId(BusinessConsoleMesTraceabilityByBatchRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleMesTraceabilityByBatchRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleMesTraceabilityResponse> ForwardAsync(
        BusinessConsoleMesTraceabilityByBatchRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        mes.GetBatchTraceabilityAsync(
            tokenProvider.BearerToken,
            request.BatchOrSerial,
            new BusinessConsoleMesContextRequest(request.OrganizationId, request.EnvironmentId),
            cancellationToken);
}

[Tags("Business Console MES")]
[HttpGet("/api/business-console/v1/mes/traceability/material-lots/{materialLotId}")]
[BusinessGatewayOperationId("getBusinessConsoleMesMaterialLotTraceability")]
public sealed class GetBusinessConsoleMesMaterialLotTraceabilityEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMesClient mes,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleMesTraceabilityByMaterialLotRequest, BusinessConsoleMesTraceabilityResponse>(
        auth,
        BusinessGatewayPermissions.MesTraceabilityRead)
{
    protected override string OrganizationId(BusinessConsoleMesTraceabilityByMaterialLotRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleMesTraceabilityByMaterialLotRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleMesTraceabilityResponse> ForwardAsync(
        BusinessConsoleMesTraceabilityByMaterialLotRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        mes.GetMaterialLotTraceabilityAsync(
            tokenProvider.BearerToken,
            request.MaterialLotId,
            new BusinessConsoleMesContextRequest(request.OrganizationId, request.EnvironmentId),
            cancellationToken);
}

[Tags("Business Console MES")]
[HttpGet("/api/business-console/v1/mes/capacity-impacts")]
[BusinessGatewayOperationId("listBusinessConsoleMesCapacityImpacts")]
public sealed class ListBusinessConsoleMesCapacityImpactsEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMesClient mes,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleMesListRequest, BusinessConsoleMesCapacityImpactListResponse>(
        auth,
        BusinessGatewayPermissions.MesCapacityRead)
{
    protected override string OrganizationId(BusinessConsoleMesListRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleMesListRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleMesCapacityImpactListResponse> ForwardAsync(
        BusinessConsoleMesListRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        mes.ListCapacityImpactsAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console MES")]
[HttpPost("/api/business-console/v1/mes/schedules/run")]
[BusinessGatewayOperationId("runBusinessConsoleMesSchedule")]
public sealed class RunBusinessConsoleMesScheduleEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMesClient mes,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleRunScheduleRequest, BusinessConsoleMesScheduleResult>(
        auth,
        BusinessGatewayPermissions.MesSchedulesManage)
{
    protected override string OrganizationId(BusinessConsoleRunScheduleRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleRunScheduleRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleMesScheduleResult> ForwardAsync(
        BusinessConsoleRunScheduleRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        mes.RunScheduleAsync(tokenProvider.BearerToken, request, cancellationToken);
}
