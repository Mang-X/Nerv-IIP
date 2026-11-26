using FastEndpoints;
using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.BusinessGateway.Web.Application.OpenApi;
using Nerv.IIP.BusinessGateway.Web.Endpoints;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.BusinessGateway.Web.Endpoints.Mes;

[Tags("Business Console MES")]
[HttpGet("/api/business-console/v1/mes/work-orders")]
[BusinessGatewayOperationId("listBusinessConsoleMesWorkOrders")]
public sealed class ListBusinessConsoleMesWorkOrdersEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMesClient mes,
    BusinessMesCommercialSourceReader commercialSources,
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
        var scope = await workScopeResolver.ResolveWorkOrderListAsync(
            AuthorizationResult,
            request.OrganizationId,
            request.EnvironmentId,
            BusinessGatewayPermissions.MesWorkOrdersRead,
            request.ScopeKind,
            request.ScopeId,
            cancellationToken);
        var response = await mes.ListWorkOrdersAsync(
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
                null,
                null,
                request.WorkCenterIds,
                request.DeviceAssetIds,
                request.Statuses,
                AuthorizedAssignedUserIds: Join(scope.AssignedUserIds),
                AuthorizedTeamIds: Join(scope.TeamIds),
                AuthorizedWorkCenterIds: Join(scope.WorkCenterIds)),
            cancellationToken);
        var facts = await commercialSources.ReadAsync(request.OrganizationId, request.EnvironmentId, bearerToken,
            response.Items.Select(x => (x.WorkOrderId, x.SourcePlanReference)).ToArray(), cancellationToken);
        return response with { Items = response.Items.Select(x => x with { CommercialSourceFacts = facts.GetValueOrDefault(x.WorkOrderId) }).ToArray() };

    }

    private static string? Join(IReadOnlyCollection<string> values) =>
        values.Count == 0 ? null : string.Join(',', values.Order(StringComparer.Ordinal));
}

[Tags("Business Console MES")]
[HttpGet("/api/business-console/v1/mes/work-orders/{workOrderId}")]
[BusinessGatewayOperationId("getBusinessConsoleMesWorkOrderDetail")]
[Microsoft.AspNetCore.Mvc.ProducesResponseType(typeof(NetCorePal.Extensions.Dto.ResponseData), StatusCodes.Status404NotFound)]
public sealed class GetBusinessConsoleMesWorkOrderDetailEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMesClient mes,
    BusinessMesCommercialSourceReader commercialSources,
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
            cancellationToken,
            distinguishMissing: true);
        var response = await mes.GetWorkOrderDetailAsync(
            tokenProvider.BearerToken,
            request.WorkOrderId,
            new BusinessConsoleMesContextRequest(request.OrganizationId, request.EnvironmentId),
            cancellationToken);
        var facts = await commercialSources.ReadAsync(request.OrganizationId, request.EnvironmentId, bearerToken,
            [(response.WorkOrderId, response.SourcePlanReference)], cancellationToken);
        return response with { CommercialSourceFacts = facts.GetValueOrDefault(response.WorkOrderId) };

    }
}

[Tags("Business Console MES")]
[HttpPost("/api/business-console/v1/mes/work-orders/{workOrderId}/priority")]
[BusinessGatewayOperationId("adjustBusinessConsoleMesWorkOrderPriority")]
[Microsoft.AspNetCore.Mvc.ProducesResponseType(typeof(NetCorePal.Extensions.Dto.ResponseData), StatusCodes.Status409Conflict)]
public sealed class AdjustBusinessConsoleMesWorkOrderPriorityEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMesClient mes,
    MesPrincipalWorkScopeAuthorizer workScopeAuthorizer,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleMesAdjustWorkOrderPriorityRequest, BusinessConsoleAcceptedResponse>(
        auth,
        BusinessGatewayPermissions.MesWorkOrdersManage)
{
    protected override bool IncludePrincipalContext => true;

    protected override BusinessGatewayAuthorizationContinuityMode AuthorizationContinuityMode =>
        BusinessGatewayAuthorizationContinuityMode.RealtimeRequired;

    protected override string OrganizationId(BusinessConsoleMesAdjustWorkOrderPriorityRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleMesAdjustWorkOrderPriorityRequest request) => request.EnvironmentId;

    protected override async Task<BusinessConsoleAcceptedResponse> ForwardAsync(
        BusinessConsoleMesAdjustWorkOrderPriorityRequest request,
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
        return await mes.AdjustWorkOrderPriorityAsync(
            tokenProvider.BearerToken,
            request.WorkOrderId,
            request,
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
        mes.CancelWorkOrderAsync(
            internalBearerToken,
            request.WorkOrderId,
            request,
            RequireAuthorizedPrincipalActorReference(),
            cancellationToken);
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
