using FastEndpoints;
using FluentValidation;
using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.BusinessGateway.Web.Application.OpenApi;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.BusinessGateway.Web.Endpoints.Wms;

[Tags("Business Console WMS")]
[HttpPost("/api/business-console/v1/wms/inbound-orders")]
[BusinessGatewayOperationId("createBusinessConsoleWmsInboundOrder")]
public sealed class CreateBusinessConsoleWmsInboundOrderEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessWmsClient wms,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleCreateWmsInboundOrderRequest, BusinessConsoleCreateWmsInboundOrderResponse>(
        auth,
        BusinessGatewayPermissions.WmsReceiptsManage)
{
    protected override string OrganizationId(BusinessConsoleCreateWmsInboundOrderRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleCreateWmsInboundOrderRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleCreateWmsInboundOrderResponse> ForwardAsync(
        BusinessConsoleCreateWmsInboundOrderRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        wms.CreateInboundOrderAsync(tokenProvider.BearerToken, request, cancellationToken);
}

public abstract class BusinessConsoleWmsTrustedProxyEndpoint<TRequest, TResponse>
    : AuthorizedBusinessProxyEndpoint<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly WmsTrustedRequestContextResolver _trustedContextResolver;
    private readonly string _permissionCode;

    protected BusinessConsoleWmsTrustedProxyEndpoint(
        IBusinessGatewayAuthorizationClient auth,
        WmsTrustedRequestContextResolver trustedContextResolver,
        string permissionCode)
        : base(auth, permissionCode)
    {
        _trustedContextResolver = trustedContextResolver;
        _permissionCode = permissionCode;
    }

    protected override bool IncludePrincipalContext => true;

    protected override BusinessGatewayAuthorizationContinuityMode AuthorizationContinuityMode =>
        BusinessGatewayAuthorizationContinuityMode.RealtimeRequired;

    protected Task<WmsTrustedRequestContext> ResolveTrustedContextAsync(
        TRequest request,
        CancellationToken cancellationToken) =>
        _trustedContextResolver.ResolveAsync(
            AuthorizationResult,
            OrganizationId(request),
            EnvironmentId(request),
            _permissionCode,
            cancellationToken);
}

public abstract class BusinessConsoleWmsWorkScopeCatalogEndpoint
    : BusinessConsoleWmsTrustedProxyEndpoint<
        BusinessConsoleWmsWorkScopeCatalogRequest,
        BusinessConsoleWmsWorkScopeCatalog>
{
    private readonly IBusinessWmsClient _wms;
    private readonly IInternalServiceTokenProvider _tokenProvider;

    protected BusinessConsoleWmsWorkScopeCatalogEndpoint(
        IBusinessGatewayAuthorizationClient auth,
        IBusinessWmsClient wms,
        IInternalServiceTokenProvider tokenProvider,
        WmsTrustedRequestContextResolver trustedContextResolver,
        string permissionCode)
        : base(auth, trustedContextResolver, permissionCode)
    {
        _wms = wms;
        _tokenProvider = tokenProvider;
    }

    protected override string OrganizationId(BusinessConsoleWmsWorkScopeCatalogRequest request) =>
        request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleWmsWorkScopeCatalogRequest request) =>
        request.EnvironmentId;

    protected override async Task<BusinessConsoleWmsWorkScopeCatalog> ForwardAsync(
        BusinessConsoleWmsWorkScopeCatalogRequest request,
        string bearerToken,
        CancellationToken cancellationToken)
    {
        var trusted = await ResolveTrustedContextAsync(request, cancellationToken);
        return await GetCatalogAsync(
            _wms,
            _tokenProvider.BearerToken,
            new BusinessWmsWorkScopeCatalogRequest(
                request.OrganizationId,
                request.EnvironmentId,
                trusted.ActorPrincipalId,
                trusted.AuthorizedSiteCodes),
            cancellationToken);
    }

    protected abstract Task<BusinessConsoleWmsWorkScopeCatalog> GetCatalogAsync(
        IBusinessWmsClient wmsClient,
        string internalBearerToken,
        BusinessWmsWorkScopeCatalogRequest request,
        CancellationToken cancellationToken);
}

[Tags("Business Console WMS")]
[HttpGet("/api/business-console/v1/wms/work-scopes/receipts")]
[BusinessGatewayOperationId("getBusinessConsoleWmsReceiptWorkScopes")]
[Microsoft.AspNetCore.Mvc.ProducesResponseType(typeof(NetCorePal.Extensions.Dto.ResponseData), StatusCodes.Status403Forbidden)]
public sealed class GetBusinessConsoleWmsReceiptWorkScopesEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessWmsClient wms,
    IInternalServiceTokenProvider tokenProvider,
    WmsTrustedRequestContextResolver trustedContextResolver)
    : BusinessConsoleWmsWorkScopeCatalogEndpoint(
        auth,
        wms,
        tokenProvider,
        trustedContextResolver,
        BusinessGatewayPermissions.WmsReceiptsRead)
{
    protected override Task<BusinessConsoleWmsWorkScopeCatalog> GetCatalogAsync(
        IBusinessWmsClient wmsClient,
        string internalBearerToken,
        BusinessWmsWorkScopeCatalogRequest request,
        CancellationToken cancellationToken) =>
        wmsClient.GetReceiptWorkScopesAsync(
            internalBearerToken,
            request,
            cancellationToken);
}

[Tags("Business Console WMS")]
[HttpGet("/api/business-console/v1/wms/work-scopes/shipments")]
[BusinessGatewayOperationId("getBusinessConsoleWmsShipmentWorkScopes")]
[Microsoft.AspNetCore.Mvc.ProducesResponseType(typeof(NetCorePal.Extensions.Dto.ResponseData), StatusCodes.Status403Forbidden)]
public sealed class GetBusinessConsoleWmsShipmentWorkScopesEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessWmsClient wms,
    IInternalServiceTokenProvider tokenProvider,
    WmsTrustedRequestContextResolver trustedContextResolver)
    : BusinessConsoleWmsWorkScopeCatalogEndpoint(
        auth,
        wms,
        tokenProvider,
        trustedContextResolver,
        BusinessGatewayPermissions.WmsShipmentsRead)
{
    protected override Task<BusinessConsoleWmsWorkScopeCatalog> GetCatalogAsync(
        IBusinessWmsClient wmsClient,
        string internalBearerToken,
        BusinessWmsWorkScopeCatalogRequest request,
        CancellationToken cancellationToken) =>
        wmsClient.GetShipmentWorkScopesAsync(
            internalBearerToken,
            request,
            cancellationToken);
}

[Tags("Business Console WMS")]
[HttpGet("/api/business-console/v1/wms/work-scopes/counts")]
[BusinessGatewayOperationId("getBusinessConsoleWmsCountWorkScopes")]
[Microsoft.AspNetCore.Mvc.ProducesResponseType(typeof(NetCorePal.Extensions.Dto.ResponseData), StatusCodes.Status403Forbidden)]
public sealed class GetBusinessConsoleWmsCountWorkScopesEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessWmsClient wms,
    IInternalServiceTokenProvider tokenProvider,
    WmsTrustedRequestContextResolver trustedContextResolver)
    : BusinessConsoleWmsWorkScopeCatalogEndpoint(
        auth,
        wms,
        tokenProvider,
        trustedContextResolver,
        BusinessGatewayPermissions.WmsCountsRead)
{
    protected override Task<BusinessConsoleWmsWorkScopeCatalog> GetCatalogAsync(
        IBusinessWmsClient wmsClient,
        string internalBearerToken,
        BusinessWmsWorkScopeCatalogRequest request,
        CancellationToken cancellationToken) =>
        wmsClient.GetCountWorkScopesAsync(
            internalBearerToken,
            request,
            cancellationToken);
}

public abstract class BusinessConsoleWmsOperationalCandidatesEndpoint
    : BusinessConsoleWmsTrustedProxyEndpoint<
        BusinessConsoleWmsOperationalCandidatesRequest,
        BusinessConsoleWmsOperationalCandidatesResponse>
{
    private readonly IBusinessWmsClient _wms;
    private readonly IInternalServiceTokenProvider _tokenProvider;
    private readonly string _candidateDomain;

    protected BusinessConsoleWmsOperationalCandidatesEndpoint(
        IBusinessGatewayAuthorizationClient auth,
        IBusinessWmsClient wms,
        IInternalServiceTokenProvider tokenProvider,
        WmsTrustedRequestContextResolver trustedContextResolver,
        string permissionCode,
        string candidateDomain)
        : base(auth, trustedContextResolver, permissionCode)
    {
        _wms = wms;
        _tokenProvider = tokenProvider;
        _candidateDomain = candidateDomain;
    }

    protected override string OrganizationId(
        BusinessConsoleWmsOperationalCandidatesRequest request) =>
        request.OrganizationId;

    protected override string EnvironmentId(
        BusinessConsoleWmsOperationalCandidatesRequest request) =>
        request.EnvironmentId;

    protected override async Task<BusinessConsoleWmsOperationalCandidatesResponse> ForwardAsync(
        BusinessConsoleWmsOperationalCandidatesRequest request,
        string bearerToken,
        CancellationToken cancellationToken)
    {
        var trusted = await ResolveTrustedContextAsync(request, cancellationToken);
        var scope = trusted.ResolveScope(request.ScopeKind, request.ScopeId);
        return await _wms.ListOperationalCandidatesAsync(
            _tokenProvider.BearerToken,
            new BusinessWmsOperationalCandidatesRequest(
                request.OrganizationId,
                request.EnvironmentId,
                trusted.ActorPrincipalId,
                trusted.AuthorizedSiteCodes,
                scope.ScopeKind,
                scope.ScopeId,
                _candidateDomain,
                request.Keyword,
                request.SkuCode,
                request.LocationCode,
                request.Take,
                request.SiteCode),
            cancellationToken);
    }
}

[Tags("Business Console WMS")]
[HttpGet("/api/business-console/v1/wms/operational-candidates/receipts")]
[BusinessGatewayOperationId("listBusinessConsoleWmsReceiptOperationalCandidates")]
[Microsoft.AspNetCore.Mvc.ProducesResponseType(
    typeof(NetCorePal.Extensions.Dto.ResponseData),
    StatusCodes.Status403Forbidden)]
public sealed class ListBusinessConsoleWmsReceiptOperationalCandidatesEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessWmsClient wms,
    IInternalServiceTokenProvider tokenProvider,
    WmsTrustedRequestContextResolver trustedContextResolver)
    : BusinessConsoleWmsOperationalCandidatesEndpoint(
        auth,
        wms,
        tokenProvider,
        trustedContextResolver,
        BusinessGatewayPermissions.WmsReceiptsRead,
        "receipts");

[Tags("Business Console WMS")]
[HttpGet("/api/business-console/v1/wms/operational-candidates/shipments")]
[BusinessGatewayOperationId("listBusinessConsoleWmsShipmentOperationalCandidates")]
[Microsoft.AspNetCore.Mvc.ProducesResponseType(
    typeof(NetCorePal.Extensions.Dto.ResponseData),
    StatusCodes.Status403Forbidden)]
public sealed class ListBusinessConsoleWmsShipmentOperationalCandidatesEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessWmsClient wms,
    IInternalServiceTokenProvider tokenProvider,
    WmsTrustedRequestContextResolver trustedContextResolver)
    : BusinessConsoleWmsOperationalCandidatesEndpoint(
        auth,
        wms,
        tokenProvider,
        trustedContextResolver,
        BusinessGatewayPermissions.WmsShipmentsRead,
        "shipments");

[Tags("Business Console WMS")]
[HttpGet("/api/business-console/v1/wms/operational-candidates/counts")]
[BusinessGatewayOperationId("listBusinessConsoleWmsCountOperationalCandidates")]
[Microsoft.AspNetCore.Mvc.ProducesResponseType(
    typeof(NetCorePal.Extensions.Dto.ResponseData),
    StatusCodes.Status403Forbidden)]
public sealed class ListBusinessConsoleWmsCountOperationalCandidatesEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessWmsClient wms,
    IInternalServiceTokenProvider tokenProvider,
    WmsTrustedRequestContextResolver trustedContextResolver)
    : BusinessConsoleWmsOperationalCandidatesEndpoint(
        auth,
        wms,
        tokenProvider,
        trustedContextResolver,
        BusinessGatewayPermissions.WmsCountsRead,
        "counts");

public abstract class BusinessConsoleWmsAssignmentEndpoint
    : BusinessConsoleWmsTrustedProxyEndpoint<
        BusinessConsoleAssignWmsResourceRequest,
        BusinessConsoleWmsAssignmentResult>
{
    private readonly IBusinessWmsClient _wms;
    private readonly IInternalServiceTokenProvider _tokenProvider;
    private readonly string _routeParameterName;

    protected BusinessConsoleWmsAssignmentEndpoint(
        IBusinessGatewayAuthorizationClient auth,
        IBusinessWmsClient wms,
        IInternalServiceTokenProvider tokenProvider,
        WmsTrustedRequestContextResolver trustedContextResolver,
        string permissionCode,
        string routeParameterName)
        : base(auth, trustedContextResolver, permissionCode)
    {
        _wms = wms;
        _tokenProvider = tokenProvider;
        _routeParameterName = routeParameterName;
    }

    protected override string OrganizationId(BusinessConsoleAssignWmsResourceRequest request) =>
        request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleAssignWmsResourceRequest request) =>
        request.EnvironmentId;

    protected override async Task<BusinessConsoleWmsAssignmentResult> ForwardAsync(
        BusinessConsoleAssignWmsResourceRequest request,
        string bearerToken,
        CancellationToken cancellationToken)
    {
        var resourceId = Route<string>(_routeParameterName);
        if (string.IsNullOrWhiteSpace(resourceId))
        {
            throw new BusinessServiceProxyException(
                System.Net.HttpStatusCode.UnprocessableEntity,
                "resource-id-required");
        }

        var trusted = await ResolveTrustedContextAsync(request, cancellationToken);
        return await AssignAsync(
            _wms,
            _tokenProvider.BearerToken,
            resourceId,
            request,
            trusted,
            cancellationToken);
    }

    protected abstract Task<BusinessConsoleWmsAssignmentResult> AssignAsync(
        IBusinessWmsClient wmsClient,
        string internalBearerToken,
        string resourceId,
        BusinessConsoleAssignWmsResourceRequest request,
        WmsTrustedRequestContext trusted,
        CancellationToken cancellationToken);
}

[Tags("Business Console WMS")]
[HttpPost("/api/business-console/v1/wms/inbound-orders/{inboundOrderId}/assignment")]
[BusinessGatewayOperationId("assignBusinessConsoleWmsInboundOrder")]
[Microsoft.AspNetCore.Mvc.ProducesResponseType(typeof(NetCorePal.Extensions.Dto.ResponseData), StatusCodes.Status403Forbidden)]
[Microsoft.AspNetCore.Mvc.ProducesResponseType(typeof(NetCorePal.Extensions.Dto.ResponseData), StatusCodes.Status409Conflict)]
[Microsoft.AspNetCore.Mvc.ProducesResponseType(typeof(NetCorePal.Extensions.Dto.ResponseData), StatusCodes.Status422UnprocessableEntity)]
public sealed class AssignBusinessConsoleWmsInboundOrderEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessWmsClient wms,
    IInternalServiceTokenProvider tokenProvider,
    WmsTrustedRequestContextResolver trustedContextResolver)
    : BusinessConsoleWmsAssignmentEndpoint(
        auth,
        wms,
        tokenProvider,
        trustedContextResolver,
        BusinessGatewayPermissions.WmsReceiptsManage,
        "inboundOrderId")
{
    protected override Task<BusinessConsoleWmsAssignmentResult> AssignAsync(
        IBusinessWmsClient wmsClient,
        string internalBearerToken,
        string resourceId,
        BusinessConsoleAssignWmsResourceRequest request,
        WmsTrustedRequestContext trusted,
        CancellationToken cancellationToken) =>
        wmsClient.AssignInboundOrderAsync(
            internalBearerToken,
            resourceId,
            new BusinessWmsAssignInboundOrderRequest(
                resourceId,
                request.OrganizationId,
                request.EnvironmentId,
                trusted.ActorPrincipalId,
                trusted.AuthorizedSiteCodes,
                request.PoolCode,
                request.OperatorPrincipalId,
                request.IdempotencyKey,
                request.ExpectedVersion),
            cancellationToken);
}

[Tags("Business Console WMS")]
[HttpPost("/api/business-console/v1/wms/putaway-tasks/{warehouseTaskId}/assignment")]
[BusinessGatewayOperationId("assignBusinessConsoleWmsPutawayTask")]
[Microsoft.AspNetCore.Mvc.ProducesResponseType(typeof(NetCorePal.Extensions.Dto.ResponseData), StatusCodes.Status403Forbidden)]
[Microsoft.AspNetCore.Mvc.ProducesResponseType(typeof(NetCorePal.Extensions.Dto.ResponseData), StatusCodes.Status409Conflict)]
[Microsoft.AspNetCore.Mvc.ProducesResponseType(typeof(NetCorePal.Extensions.Dto.ResponseData), StatusCodes.Status422UnprocessableEntity)]
public sealed class AssignBusinessConsoleWmsPutawayTaskEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessWmsClient wms,
    IInternalServiceTokenProvider tokenProvider,
    WmsTrustedRequestContextResolver trustedContextResolver)
    : BusinessConsoleWmsAssignmentEndpoint(
        auth,
        wms,
        tokenProvider,
        trustedContextResolver,
        BusinessGatewayPermissions.WmsReceiptsManage,
        "warehouseTaskId")
{
    protected override Task<BusinessConsoleWmsAssignmentResult> AssignAsync(
        IBusinessWmsClient wmsClient,
        string internalBearerToken,
        string resourceId,
        BusinessConsoleAssignWmsResourceRequest request,
        WmsTrustedRequestContext trusted,
        CancellationToken cancellationToken) =>
        wmsClient.AssignPutawayTaskAsync(
            internalBearerToken,
            resourceId,
            new BusinessWmsAssignPutawayTaskRequest(
                resourceId,
                request.OrganizationId,
                request.EnvironmentId,
                trusted.ActorPrincipalId,
                trusted.AuthorizedSiteCodes,
                request.PoolCode,
                request.OperatorPrincipalId,
                request.IdempotencyKey,
                request.ExpectedVersion),
            cancellationToken);
}

[Tags("Business Console WMS")]
[HttpPost("/api/business-console/v1/wms/outbound-orders/{outboundOrderId}/assignment")]
[BusinessGatewayOperationId("assignBusinessConsoleWmsOutboundOrder")]
[Microsoft.AspNetCore.Mvc.ProducesResponseType(typeof(NetCorePal.Extensions.Dto.ResponseData), StatusCodes.Status403Forbidden)]
[Microsoft.AspNetCore.Mvc.ProducesResponseType(typeof(NetCorePal.Extensions.Dto.ResponseData), StatusCodes.Status409Conflict)]
[Microsoft.AspNetCore.Mvc.ProducesResponseType(typeof(NetCorePal.Extensions.Dto.ResponseData), StatusCodes.Status422UnprocessableEntity)]
public sealed class AssignBusinessConsoleWmsOutboundOrderEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessWmsClient wms,
    IInternalServiceTokenProvider tokenProvider,
    WmsTrustedRequestContextResolver trustedContextResolver)
    : BusinessConsoleWmsAssignmentEndpoint(
        auth,
        wms,
        tokenProvider,
        trustedContextResolver,
        BusinessGatewayPermissions.WmsShipmentsManage,
        "outboundOrderId")
{
    protected override Task<BusinessConsoleWmsAssignmentResult> AssignAsync(
        IBusinessWmsClient wmsClient,
        string internalBearerToken,
        string resourceId,
        BusinessConsoleAssignWmsResourceRequest request,
        WmsTrustedRequestContext trusted,
        CancellationToken cancellationToken) =>
        wmsClient.AssignOutboundOrderAsync(
            internalBearerToken,
            resourceId,
            new BusinessWmsAssignOutboundOrderRequest(
                resourceId,
                request.OrganizationId,
                request.EnvironmentId,
                trusted.ActorPrincipalId,
                trusted.AuthorizedSiteCodes,
                request.PoolCode,
                request.OperatorPrincipalId,
                request.IdempotencyKey,
                request.ExpectedVersion),
            cancellationToken);
}

[Tags("Business Console WMS")]
[HttpPost("/api/business-console/v1/wms/picking-tasks/{warehouseTaskId}/assignment")]
[BusinessGatewayOperationId("assignBusinessConsoleWmsPickingTask")]
[Microsoft.AspNetCore.Mvc.ProducesResponseType(typeof(NetCorePal.Extensions.Dto.ResponseData), StatusCodes.Status403Forbidden)]
[Microsoft.AspNetCore.Mvc.ProducesResponseType(typeof(NetCorePal.Extensions.Dto.ResponseData), StatusCodes.Status409Conflict)]
[Microsoft.AspNetCore.Mvc.ProducesResponseType(typeof(NetCorePal.Extensions.Dto.ResponseData), StatusCodes.Status422UnprocessableEntity)]
public sealed class AssignBusinessConsoleWmsPickingTaskEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessWmsClient wms,
    IInternalServiceTokenProvider tokenProvider,
    WmsTrustedRequestContextResolver trustedContextResolver)
    : BusinessConsoleWmsAssignmentEndpoint(
        auth,
        wms,
        tokenProvider,
        trustedContextResolver,
        BusinessGatewayPermissions.WmsShipmentsManage,
        "warehouseTaskId")
{
    protected override Task<BusinessConsoleWmsAssignmentResult> AssignAsync(
        IBusinessWmsClient wmsClient,
        string internalBearerToken,
        string resourceId,
        BusinessConsoleAssignWmsResourceRequest request,
        WmsTrustedRequestContext trusted,
        CancellationToken cancellationToken) =>
        wmsClient.AssignPickingTaskAsync(
            internalBearerToken,
            resourceId,
            new BusinessWmsAssignPickingTaskRequest(
                resourceId,
                request.OrganizationId,
                request.EnvironmentId,
                trusted.ActorPrincipalId,
                trusted.AuthorizedSiteCodes,
                request.PoolCode,
                request.OperatorPrincipalId,
                request.IdempotencyKey,
                request.ExpectedVersion),
            cancellationToken);
}

[Tags("Business Console WMS")]
[HttpPost("/api/business-console/v1/wms/count-executions/{countExecutionId}/assignment")]
[BusinessGatewayOperationId("assignBusinessConsoleWmsCountExecution")]
[Microsoft.AspNetCore.Mvc.ProducesResponseType(typeof(NetCorePal.Extensions.Dto.ResponseData), StatusCodes.Status403Forbidden)]
[Microsoft.AspNetCore.Mvc.ProducesResponseType(typeof(NetCorePal.Extensions.Dto.ResponseData), StatusCodes.Status409Conflict)]
[Microsoft.AspNetCore.Mvc.ProducesResponseType(typeof(NetCorePal.Extensions.Dto.ResponseData), StatusCodes.Status422UnprocessableEntity)]
public sealed class AssignBusinessConsoleWmsCountExecutionEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessWmsClient wms,
    IInternalServiceTokenProvider tokenProvider,
    WmsTrustedRequestContextResolver trustedContextResolver)
    : BusinessConsoleWmsAssignmentEndpoint(
        auth,
        wms,
        tokenProvider,
        trustedContextResolver,
        BusinessGatewayPermissions.WmsReceiptsManage,
        "countExecutionId")
{
    protected override Task<BusinessConsoleWmsAssignmentResult> AssignAsync(
        IBusinessWmsClient wmsClient,
        string internalBearerToken,
        string resourceId,
        BusinessConsoleAssignWmsResourceRequest request,
        WmsTrustedRequestContext trusted,
        CancellationToken cancellationToken) =>
        wmsClient.AssignCountExecutionAsync(
            internalBearerToken,
            resourceId,
            new BusinessWmsAssignCountExecutionRequest(
                resourceId,
                request.OrganizationId,
                request.EnvironmentId,
                trusted.ActorPrincipalId,
                trusted.AuthorizedSiteCodes,
                request.PoolCode,
                request.OperatorPrincipalId,
                request.IdempotencyKey,
                request.ExpectedVersion),
            cancellationToken);
}

[Tags("Business Console WMS")]
[HttpGet("/api/business-console/v1/wms/inbound-orders")]
[BusinessGatewayOperationId("listBusinessConsoleWmsInboundOrders")]
public sealed class ListBusinessConsoleWmsInboundOrdersEndpoint
    : BusinessConsoleWmsTrustedProxyEndpoint<BusinessConsoleWmsInboundOrderListRequest, BusinessConsoleWmsInboundOrderListResponse>
{
    private readonly IBusinessGatewayAuthorizationClient _auth;
    private readonly IBusinessWmsClient _wms;
    private readonly IBusinessInventoryClient _inventory;
    private readonly IInternalServiceTokenProvider _tokenProvider;
    private readonly WmsTrustedRequestContextResolver _trustedContextResolver;

    public ListBusinessConsoleWmsInboundOrdersEndpoint(
        IBusinessGatewayAuthorizationClient auth,
        IBusinessWmsClient wms,
        IBusinessInventoryClient inventory,
        IInternalServiceTokenProvider tokenProvider,
        WmsTrustedRequestContextResolver trustedContextResolver)
        : base(
            auth,
            trustedContextResolver,
            BusinessGatewayPermissions.WmsReceiptsRead)
    {
        _auth = auth;
        _wms = wms;
        _inventory = inventory;
        _tokenProvider = tokenProvider;
        _trustedContextResolver = trustedContextResolver;
    }

    protected override string OrganizationId(BusinessConsoleWmsInboundOrderListRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleWmsInboundOrderListRequest request) => request.EnvironmentId;

    protected override async Task<BusinessConsoleWmsInboundOrderListResponse> ForwardAsync(
        BusinessConsoleWmsInboundOrderListRequest request,
        string bearerToken,
        CancellationToken cancellationToken)
    {
        var trusted = await ResolveTrustedContextAsync(request, cancellationToken);
        var scope = trusted.ResolveScope(request.ScopeKind, request.ScopeId);
        var response = await _wms.ListInboundOrdersAsync(
            _tokenProvider.BearerToken,
            new BusinessWmsScopedListRequest(
                request.OrganizationId,
                request.EnvironmentId,
                trusted.ActorPrincipalId,
                trusted.AuthorizedSiteCodes,
                scope.ScopeKind,
                scope.ScopeId,
                request.LocationCode,
                request.LotNo,
                request.SiteCode,
                request.Skip,
                request.Take,
                request.Status,
                request.Keyword),
            request.InboundOrderId,
            cancellationToken);
        var inventoryContext = await TryGetInventoryContextAsync(request, bearerToken, cancellationToken);
        return response with
        {
            InventoryContext = inventoryContext,
            SourceStatus = inventoryContext?.Status ?? "scope-required",
        };
    }

    private async Task<BusinessConsoleWmsInventoryContext?> TryGetInventoryContextAsync(
        BusinessConsoleWmsInboundOrderListRequest request,
        string bearerToken,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.SkuCode)
            || string.IsNullOrWhiteSpace(request.UomCode)
            || string.IsNullOrWhiteSpace(request.SiteCode))
        {
            return new BusinessConsoleWmsInventoryContext(
                "BusinessInventory",
                "scope-required",
                BusinessGatewayPermissions.InventoryLedgerRead,
                "sku-uom-site-required",
                request.SkuCode,
                request.UomCode,
                request.SiteCode,
                request.LocationCode,
                request.LotNo,
                request.SerialNo,
                request.QualityStatus,
                request.OwnerType,
                request.OwnerId,
                null,
                null,
                null,
                []);
        }

        var authorization = await _auth.CheckAsync(
            bearerToken,
            new BusinessGatewayPermissionRequirement(
                BusinessGatewayPermissions.InventoryLedgerRead,
                request.OrganizationId,
                request.EnvironmentId,
                null,
                null,
                IncludePrincipalContext: true),
            BusinessGatewayAuthorizationContinuityMode.RealtimeRequired,
            cancellationToken);
        if (!authorization.IsAllowed)
        {
            return ForbiddenInventoryContext(request, authorization.DenialReason ?? "forbidden");
        }

        if (!await IsInventorySiteAuthorizedAsync(authorization, request, cancellationToken))
        {
            return ForbiddenInventoryContext(request, "work-scope-not-authorized");
        }

        try
        {
            var availability = await _inventory.GetAvailabilityAsync(
                _tokenProvider.BearerToken,
                new BusinessConsoleInventoryAvailabilityRequest(
                    request.OrganizationId,
                    request.EnvironmentId,
                    request.SkuCode,
                    request.UomCode,
                    request.SiteCode,
                    request.LocationCode,
                    request.LotNo,
                    request.SerialNo,
                    request.QualityStatus,
                    request.OwnerType,
                    request.OwnerId),
                cancellationToken);
            return new BusinessConsoleWmsInventoryContext(
                "BusinessInventory",
                "available",
                BusinessGatewayPermissions.InventoryLedgerRead,
                null,
                availability.SkuCode,
                availability.UomCode,
                availability.SiteCode,
                availability.LocationCode,
                availability.LotNo,
                availability.SerialNo,
                availability.QualityStatus,
                availability.OwnerType,
                availability.OwnerId,
                availability.OnHandQuantity,
                availability.ReservedQuantity,
                availability.AvailableQuantity,
                availability.Items);
        }
        catch (BusinessServiceProxyException)
        {
            return new BusinessConsoleWmsInventoryContext(
                "BusinessInventory",
                "unavailable",
                BusinessGatewayPermissions.InventoryLedgerRead,
                "downstream-request-failed",
                request.SkuCode,
                request.UomCode,
                request.SiteCode,
                request.LocationCode,
                request.LotNo,
                request.SerialNo,
                request.QualityStatus,
                request.OwnerType,
                request.OwnerId,
                null,
                null,
                null,
                []);
        }
        catch (HttpRequestException)
        {
            return new BusinessConsoleWmsInventoryContext(
                "BusinessInventory",
                "unavailable",
                BusinessGatewayPermissions.InventoryLedgerRead,
                "downstream-unavailable",
                request.SkuCode,
                request.UomCode,
                request.SiteCode,
                request.LocationCode,
                request.LotNo,
                request.SerialNo,
                request.QualityStatus,
                request.OwnerType,
                request.OwnerId,
                null,
                null,
                null,
                []);
        }
    }

    private async Task<bool> IsInventorySiteAuthorizedAsync(
        BusinessGatewayAuthorizationResult authorization,
        BusinessConsoleWmsInboundOrderListRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var trusted = await _trustedContextResolver.ResolveAsync(
                authorization,
                request.OrganizationId,
                request.EnvironmentId,
                BusinessGatewayPermissions.InventoryLedgerRead,
                cancellationToken);
            return trusted.AuthorizedSiteCodes.Contains(request.SiteCode!, StringComparer.Ordinal);
        }
        catch (BusinessServiceProxyException)
        {
            return false;
        }
    }

    private static BusinessConsoleWmsInventoryContext ForbiddenInventoryContext(
        BusinessConsoleWmsInboundOrderListRequest request,
        string reason) =>
        new(
            "BusinessInventory",
            "forbidden",
            BusinessGatewayPermissions.InventoryLedgerRead,
            reason,
            request.SkuCode,
            request.UomCode,
            request.SiteCode,
            request.LocationCode,
            request.LotNo,
            request.SerialNo,
            request.QualityStatus,
            request.OwnerType,
            request.OwnerId,
            null,
            null,
            null,
            []);
}

[Tags("Business Console WMS")]
[HttpPost("/api/business-console/v1/wms/inbound-orders/{inboundOrderId}/putaway-tasks")]
[BusinessGatewayOperationId("createBusinessConsoleWmsPutawayTask")]
public sealed class CreateBusinessConsoleWmsPutawayTaskEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessWmsClient wms,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleCreateWmsPutawayTaskRequest, BusinessConsoleCreateWmsWarehouseTaskResponse>(
        auth,
        BusinessGatewayPermissions.WmsReceiptsManage)
{
    protected override string OrganizationId(BusinessConsoleCreateWmsPutawayTaskRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleCreateWmsPutawayTaskRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleCreateWmsWarehouseTaskResponse> ForwardAsync(
        BusinessConsoleCreateWmsPutawayTaskRequest request,
        string bearerToken,
        CancellationToken cancellationToken)
    {
        var inboundOrderId = Route<string>("inboundOrderId") ?? request.InboundOrderId;
        return wms.CreatePutawayTaskAsync(
            tokenProvider.BearerToken,
            inboundOrderId,
            request with { InboundOrderId = inboundOrderId },
            cancellationToken);
    }
}

[Tags("Business Console WMS")]
[HttpGet("/api/business-console/v1/wms/putaway-tasks")]
[BusinessGatewayOperationId("listBusinessConsoleWmsPutawayTasks")]
public sealed class ListBusinessConsoleWmsPutawayTasksEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessWmsClient wms,
    IInternalServiceTokenProvider tokenProvider,
    WmsTrustedRequestContextResolver trustedContextResolver)
    : BusinessConsoleWmsTrustedProxyEndpoint<BusinessConsoleWmsWarehouseTaskListRequest, BusinessConsoleWmsWarehouseTaskListResponse>(
        auth,
        trustedContextResolver,
        BusinessGatewayPermissions.WmsReceiptsRead)
{
    protected override bool IncludePrincipalContext => true;

    protected override BusinessGatewayAuthorizationContinuityMode AuthorizationContinuityMode =>
        BusinessGatewayAuthorizationContinuityMode.RealtimeRequired;

    protected override string OrganizationId(BusinessConsoleWmsWarehouseTaskListRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleWmsWarehouseTaskListRequest request) => request.EnvironmentId;

    protected override async Task<BusinessConsoleWmsWarehouseTaskListResponse> ForwardAsync(
        BusinessConsoleWmsWarehouseTaskListRequest request,
        string bearerToken,
        CancellationToken cancellationToken)
    {
        var trusted = await ResolveTrustedContextAsync(request, cancellationToken);
        var scope = trusted.ResolveScope(request.ScopeKind, request.ScopeId);
        return await wms.ListPutawayTasksAsync(
            tokenProvider.BearerToken,
            new BusinessWmsWarehouseTaskListRequest(
                request.OrganizationId,
                request.EnvironmentId,
                trusted.ActorPrincipalId,
                trusted.AuthorizedSiteCodes,
                scope.ScopeKind,
                scope.ScopeId,
                request.LocationCode,
                request.LotNo,
                SiteCode: null,
                request.Skip,
                request.Take,
                request.Status,
                request.Keyword),
            cancellationToken);
    }
}

[Tags("Business Console WMS")]
[HttpPost("/api/business-console/v1/wms/putaway-tasks/{warehouseTaskId}/start")]
[BusinessGatewayOperationId("startBusinessConsoleWmsPutawayTask")]
[Microsoft.AspNetCore.Mvc.ProducesResponseType(typeof(NetCorePal.Extensions.Dto.ResponseData), StatusCodes.Status403Forbidden)]
[Microsoft.AspNetCore.Mvc.ProducesResponseType(typeof(NetCorePal.Extensions.Dto.ResponseData), StatusCodes.Status409Conflict)]
[Microsoft.AspNetCore.Mvc.ProducesResponseType(typeof(NetCorePal.Extensions.Dto.ResponseData), StatusCodes.Status422UnprocessableEntity)]
public sealed class StartBusinessConsoleWmsPutawayTaskEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessWmsClient wms,
    IInternalServiceTokenProvider tokenProvider,
    WmsTrustedRequestContextResolver trustedContextResolver)
    : BusinessConsoleWmsTrustedProxyEndpoint<BusinessConsoleStartWmsWarehouseTaskRequest, BusinessConsoleWmsWarehouseTaskActionResult>(
        auth,
        trustedContextResolver,
        BusinessGatewayPermissions.WmsReceiptsManage)
{
    protected override bool IncludePrincipalContext => true;
    protected override BusinessGatewayAuthorizationContinuityMode AuthorizationContinuityMode => BusinessGatewayAuthorizationContinuityMode.RealtimeRequired;
    protected override string OrganizationId(BusinessConsoleStartWmsWarehouseTaskRequest request) => request.OrganizationId;
    protected override string EnvironmentId(BusinessConsoleStartWmsWarehouseTaskRequest request) => request.EnvironmentId;

    protected override async Task<BusinessConsoleWmsWarehouseTaskActionResult> ForwardAsync(
        BusinessConsoleStartWmsWarehouseTaskRequest request,
        string bearerToken,
        CancellationToken cancellationToken)
    {
        var taskId = Route<string>("warehouseTaskId") ?? request.WarehouseTaskId;
        var trusted = await ResolveTrustedContextAsync(request, cancellationToken);
        var scope = trusted.ResolveScope(request.ScopeKind, request.ScopeId);
        return await wms.StartPutawayTaskAsync(
            tokenProvider.BearerToken,
            taskId,
            new BusinessWmsStartWarehouseTaskActionRequest(
                taskId,
                request.OrganizationId,
                request.EnvironmentId,
                trusted.ActorPrincipalId,
                request.IdempotencyKey,
                request.ExpectedVersion,
                trusted.AuthorizedSiteCodes,
                scope.ScopeKind,
                scope.ScopeId),
            cancellationToken);
    }
}

[Tags("Business Console WMS")]
[HttpPost("/api/business-console/v1/wms/putaway-tasks/{warehouseTaskId}/progress")]
[BusinessGatewayOperationId("recordBusinessConsoleWmsPutawayTaskProgress")]
[Microsoft.AspNetCore.Mvc.ProducesResponseType(typeof(NetCorePal.Extensions.Dto.ResponseData), StatusCodes.Status403Forbidden)]
[Microsoft.AspNetCore.Mvc.ProducesResponseType(typeof(NetCorePal.Extensions.Dto.ResponseData), StatusCodes.Status409Conflict)]
[Microsoft.AspNetCore.Mvc.ProducesResponseType(typeof(NetCorePal.Extensions.Dto.ResponseData), StatusCodes.Status422UnprocessableEntity)]
public sealed class RecordBusinessConsoleWmsPutawayTaskProgressEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessWmsClient wms,
    IInternalServiceTokenProvider tokenProvider,
    WmsTrustedRequestContextResolver trustedContextResolver)
    : BusinessConsoleWmsTrustedProxyEndpoint<BusinessConsoleRecordWmsWarehouseTaskProgressRequest, BusinessConsoleWmsWarehouseTaskActionResult>(
        auth,
        trustedContextResolver,
        BusinessGatewayPermissions.WmsReceiptsManage)
{
    protected override bool IncludePrincipalContext => true;
    protected override BusinessGatewayAuthorizationContinuityMode AuthorizationContinuityMode => BusinessGatewayAuthorizationContinuityMode.RealtimeRequired;
    protected override string OrganizationId(BusinessConsoleRecordWmsWarehouseTaskProgressRequest request) => request.OrganizationId;
    protected override string EnvironmentId(BusinessConsoleRecordWmsWarehouseTaskProgressRequest request) => request.EnvironmentId;

    protected override async Task<BusinessConsoleWmsWarehouseTaskActionResult> ForwardAsync(
        BusinessConsoleRecordWmsWarehouseTaskProgressRequest request,
        string bearerToken,
        CancellationToken cancellationToken)
    {
        var taskId = Route<string>("warehouseTaskId") ?? request.WarehouseTaskId;
        var trusted = await ResolveTrustedContextAsync(request, cancellationToken);
        var scope = trusted.ResolveScope(request.ScopeKind, request.ScopeId);
        return await wms.RecordPutawayTaskProgressAsync(
            tokenProvider.BearerToken,
            taskId,
            new BusinessWmsRecordWarehouseTaskProgressActionRequest(
                taskId,
                request.OrganizationId,
                request.EnvironmentId,
                trusted.ActorPrincipalId,
                request.IdempotencyKey,
                request.ExpectedVersion,
                request.ExecutedQuantity,
                trusted.AuthorizedSiteCodes,
                scope.ScopeKind,
                scope.ScopeId),
            cancellationToken);
    }
}

[Tags("Business Console WMS")]
[HttpPost("/api/business-console/v1/wms/putaway-tasks/{warehouseTaskId}/exception")]
[BusinessGatewayOperationId("reportBusinessConsoleWmsPutawayTaskException")]
[Microsoft.AspNetCore.Mvc.ProducesResponseType(typeof(NetCorePal.Extensions.Dto.ResponseData), StatusCodes.Status403Forbidden)]
[Microsoft.AspNetCore.Mvc.ProducesResponseType(typeof(NetCorePal.Extensions.Dto.ResponseData), StatusCodes.Status409Conflict)]
[Microsoft.AspNetCore.Mvc.ProducesResponseType(typeof(NetCorePal.Extensions.Dto.ResponseData), StatusCodes.Status422UnprocessableEntity)]
public sealed class ReportBusinessConsoleWmsPutawayTaskExceptionEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessWmsClient wms,
    IInternalServiceTokenProvider tokenProvider,
    WmsTrustedRequestContextResolver trustedContextResolver)
    : BusinessConsoleWmsTrustedProxyEndpoint<BusinessConsoleReportWmsWarehouseTaskExceptionRequest, BusinessConsoleWmsWarehouseTaskActionResult>(
        auth,
        trustedContextResolver,
        BusinessGatewayPermissions.WmsReceiptsManage)
{
    protected override bool IncludePrincipalContext => true;
    protected override BusinessGatewayAuthorizationContinuityMode AuthorizationContinuityMode => BusinessGatewayAuthorizationContinuityMode.RealtimeRequired;
    protected override string OrganizationId(BusinessConsoleReportWmsWarehouseTaskExceptionRequest request) => request.OrganizationId;
    protected override string EnvironmentId(BusinessConsoleReportWmsWarehouseTaskExceptionRequest request) => request.EnvironmentId;

    protected override async Task<BusinessConsoleWmsWarehouseTaskActionResult> ForwardAsync(
        BusinessConsoleReportWmsWarehouseTaskExceptionRequest request,
        string bearerToken,
        CancellationToken cancellationToken)
    {
        var taskId = Route<string>("warehouseTaskId") ?? request.WarehouseTaskId;
        var trusted = await ResolveTrustedContextAsync(request, cancellationToken);
        var scope = trusted.ResolveScope(request.ScopeKind, request.ScopeId);
        return await wms.ReportPutawayTaskExceptionAsync(
            tokenProvider.BearerToken,
            taskId,
            new BusinessWmsReportWarehouseTaskExceptionActionRequest(
                taskId,
                request.OrganizationId,
                request.EnvironmentId,
                trusted.ActorPrincipalId,
                request.IdempotencyKey,
                request.ExpectedVersion,
                request.ExceptionCode,
                request.Reason,
                trusted.AuthorizedSiteCodes,
                scope.ScopeKind,
                scope.ScopeId),
            cancellationToken);
    }
}

[Tags("Business Console WMS")]
[HttpPost("/api/business-console/v1/wms/putaway-tasks/{warehouseTaskId}/complete")]
[BusinessGatewayOperationId("completeBusinessConsoleWmsPutawayTask")]
[Microsoft.AspNetCore.Mvc.ProducesResponseType(typeof(NetCorePal.Extensions.Dto.ResponseData), StatusCodes.Status403Forbidden)]
[Microsoft.AspNetCore.Mvc.ProducesResponseType(typeof(NetCorePal.Extensions.Dto.ResponseData), StatusCodes.Status409Conflict)]
[Microsoft.AspNetCore.Mvc.ProducesResponseType(typeof(NetCorePal.Extensions.Dto.ResponseData), StatusCodes.Status422UnprocessableEntity)]
public sealed class CompleteBusinessConsoleWmsPutawayTaskEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessWmsClient wms,
    IInternalServiceTokenProvider tokenProvider,
    WmsTrustedRequestContextResolver trustedContextResolver)
    : BusinessConsoleWmsTrustedProxyEndpoint<BusinessConsoleCompleteWmsWarehouseTaskRequest, BusinessConsoleWmsWarehouseTaskActionResult>(
        auth,
        trustedContextResolver,
        BusinessGatewayPermissions.WmsReceiptsManage)
{
    protected override bool IncludePrincipalContext => true;
    protected override BusinessGatewayAuthorizationContinuityMode AuthorizationContinuityMode => BusinessGatewayAuthorizationContinuityMode.RealtimeRequired;
    protected override string OrganizationId(BusinessConsoleCompleteWmsWarehouseTaskRequest request) => request.OrganizationId;
    protected override string EnvironmentId(BusinessConsoleCompleteWmsWarehouseTaskRequest request) => request.EnvironmentId;

    protected override async Task<BusinessConsoleWmsWarehouseTaskActionResult> ForwardAsync(
        BusinessConsoleCompleteWmsWarehouseTaskRequest request,
        string bearerToken,
        CancellationToken cancellationToken)
    {
        var taskId = Route<string>("warehouseTaskId") ?? request.WarehouseTaskId;
        var trusted = await ResolveTrustedContextAsync(request, cancellationToken);
        var scope = trusted.ResolveScope(request.ScopeKind, request.ScopeId);
        return await wms.CompletePutawayTaskAsync(
            tokenProvider.BearerToken,
            taskId,
            new BusinessWmsCompleteWarehouseTaskActionRequest(
                taskId,
                request.OrganizationId,
                request.EnvironmentId,
                trusted.ActorPrincipalId,
                request.IdempotencyKey,
                request.ExpectedVersion,
                request.ExecutedQuantity,
                trusted.AuthorizedSiteCodes,
                scope.ScopeKind,
                scope.ScopeId,
                request.DifferenceReason),
            cancellationToken);
    }
}

[Tags("Business Console WMS")]
[HttpPost("/api/business-console/v1/wms/inbound-orders/{inboundOrderId}/complete")]
[BusinessGatewayOperationId("completeBusinessConsoleWmsInboundOrder")]
[Microsoft.AspNetCore.Mvc.ProducesResponseType(typeof(NetCorePal.Extensions.Dto.ResponseData), StatusCodes.Status403Forbidden)]
[Microsoft.AspNetCore.Mvc.ProducesResponseType(typeof(NetCorePal.Extensions.Dto.ResponseData), StatusCodes.Status409Conflict)]
[Microsoft.AspNetCore.Mvc.ProducesResponseType(typeof(NetCorePal.Extensions.Dto.ResponseData), StatusCodes.Status422UnprocessableEntity)]
public sealed class CompleteBusinessConsoleWmsInboundOrderEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessWmsClient wms,
    IInternalServiceTokenProvider tokenProvider,
    WmsTrustedRequestContextResolver trustedContextResolver)
    : BusinessConsoleWmsTrustedProxyEndpoint<BusinessConsoleCompleteWmsInboundOrderRequest, BusinessConsoleCompleteWmsMovementResponse>(
        auth,
        trustedContextResolver,
        BusinessGatewayPermissions.WmsReceiptsManage)
{
    protected override bool IncludePrincipalContext => true;

    protected override BusinessGatewayAuthorizationContinuityMode AuthorizationContinuityMode =>
        BusinessGatewayAuthorizationContinuityMode.RealtimeRequired;

    protected override string OrganizationId(BusinessConsoleCompleteWmsInboundOrderRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleCompleteWmsInboundOrderRequest request) => request.EnvironmentId;

    protected override async Task<BusinessConsoleCompleteWmsMovementResponse> ForwardAsync(
        BusinessConsoleCompleteWmsInboundOrderRequest request,
        string bearerToken,
        CancellationToken cancellationToken)
    {
        var inboundOrderId = Route<string>("inboundOrderId") ?? request.InboundOrderId;
        var trusted = await ResolveTrustedContextAsync(request, cancellationToken);
        var scope = trusted.ResolveScope(request.ScopeKind, request.ScopeId);
        return await wms.CompleteInboundOrderAsync(
            tokenProvider.BearerToken,
            inboundOrderId,
            new BusinessWmsCompleteInboundOrderRequest(
                inboundOrderId,
                request.OrganizationId,
                request.EnvironmentId,
                trusted.ActorPrincipalId,
                trusted.AuthorizedSiteCodes,
                scope.ScopeKind,
                scope.ScopeId,
                request.ExpectedVersion,
                request.IdempotencyKey,
                request.Lines),
            cancellationToken);
    }
}

[Tags("Business Console WMS")]
[HttpPost("/api/business-console/v1/wms/outbound-orders")]
[BusinessGatewayOperationId("createBusinessConsoleWmsOutboundOrder")]
public sealed class CreateBusinessConsoleWmsOutboundOrderEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessWmsClient wms,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleCreateWmsOutboundOrderRequest, BusinessConsoleCreateWmsOutboundOrderResponse>(
        auth,
        BusinessGatewayPermissions.WmsShipmentsManage)
{
    protected override string OrganizationId(BusinessConsoleCreateWmsOutboundOrderRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleCreateWmsOutboundOrderRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleCreateWmsOutboundOrderResponse> ForwardAsync(
        BusinessConsoleCreateWmsOutboundOrderRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        wms.CreateOutboundOrderAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console WMS")]
[HttpGet("/api/business-console/v1/wms/outbound-orders")]
[BusinessGatewayOperationId("listBusinessConsoleWmsOutboundOrders")]
public sealed class ListBusinessConsoleWmsOutboundOrdersEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessWmsClient wms,
    IInternalServiceTokenProvider tokenProvider,
    WmsTrustedRequestContextResolver trustedContextResolver)
    : BusinessConsoleWmsTrustedProxyEndpoint<BusinessConsoleWmsOutboundOrderListRequest, BusinessConsoleWmsOutboundOrderListResponse>(
        auth,
        trustedContextResolver,
        BusinessGatewayPermissions.WmsShipmentsRead)
{
    protected override bool IncludePrincipalContext => true;

    protected override BusinessGatewayAuthorizationContinuityMode AuthorizationContinuityMode =>
        BusinessGatewayAuthorizationContinuityMode.RealtimeRequired;

    protected override string OrganizationId(BusinessConsoleWmsOutboundOrderListRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleWmsOutboundOrderListRequest request) => request.EnvironmentId;

    protected override async Task<BusinessConsoleWmsOutboundOrderListResponse> ForwardAsync(
        BusinessConsoleWmsOutboundOrderListRequest request,
        string bearerToken,
        CancellationToken cancellationToken)
    {
        var trusted = await ResolveTrustedContextAsync(request, cancellationToken);
        var scope = trusted.ResolveScope(request.ScopeKind, request.ScopeId);
        return await wms.ListOutboundOrdersAsync(
            tokenProvider.BearerToken,
            new BusinessWmsScopedListRequest(
                request.OrganizationId,
                request.EnvironmentId,
                trusted.ActorPrincipalId,
                trusted.AuthorizedSiteCodes,
                scope.ScopeKind,
                scope.ScopeId,
                LocationCode: null,
                LotNo: null,
                SiteCode: null,
                request.Skip,
                request.Take,
                request.Status,
                request.Keyword),
            request.OutboundOrderId,
            cancellationToken);
    }
}

[Tags("Business Console WMS")]
[HttpPost("/api/business-console/v1/wms/outbound-orders/{outboundOrderId}/picking-tasks")]
[BusinessGatewayOperationId("createBusinessConsoleWmsPickingTask")]
public sealed class CreateBusinessConsoleWmsPickingTaskEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessWmsClient wms,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleCreateWmsPickingTaskRequest, BusinessConsoleCreateWmsWarehouseTaskResponse>(
        auth,
        BusinessGatewayPermissions.WmsShipmentsManage)
{
    protected override string OrganizationId(BusinessConsoleCreateWmsPickingTaskRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleCreateWmsPickingTaskRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleCreateWmsWarehouseTaskResponse> ForwardAsync(
        BusinessConsoleCreateWmsPickingTaskRequest request,
        string bearerToken,
        CancellationToken cancellationToken)
    {
        var outboundOrderId = Route<string>("outboundOrderId") ?? request.OutboundOrderId;
        return wms.CreatePickingTaskAsync(
            tokenProvider.BearerToken,
            outboundOrderId,
            request with { OutboundOrderId = outboundOrderId },
            cancellationToken);
    }
}

[Tags("Business Console WMS")]
[HttpGet("/api/business-console/v1/wms/picking-tasks")]
[BusinessGatewayOperationId("listBusinessConsoleWmsPickingTasks")]
public sealed class ListBusinessConsoleWmsPickingTasksEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessWmsClient wms,
    IInternalServiceTokenProvider tokenProvider,
    WmsTrustedRequestContextResolver trustedContextResolver)
    : BusinessConsoleWmsTrustedProxyEndpoint<BusinessConsoleWmsWarehouseTaskListRequest, BusinessConsoleWmsWarehouseTaskListResponse>(
        auth,
        trustedContextResolver,
        BusinessGatewayPermissions.WmsShipmentsRead)
{
    protected override bool IncludePrincipalContext => true;

    protected override BusinessGatewayAuthorizationContinuityMode AuthorizationContinuityMode =>
        BusinessGatewayAuthorizationContinuityMode.RealtimeRequired;

    protected override string OrganizationId(BusinessConsoleWmsWarehouseTaskListRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleWmsWarehouseTaskListRequest request) => request.EnvironmentId;

    protected override async Task<BusinessConsoleWmsWarehouseTaskListResponse> ForwardAsync(
        BusinessConsoleWmsWarehouseTaskListRequest request,
        string bearerToken,
        CancellationToken cancellationToken)
    {
        var trusted = await ResolveTrustedContextAsync(request, cancellationToken);
        var scope = trusted.ResolveScope(request.ScopeKind, request.ScopeId);
        return await wms.ListPickingTasksAsync(
            tokenProvider.BearerToken,
            new BusinessWmsWarehouseTaskListRequest(
                request.OrganizationId,
                request.EnvironmentId,
                trusted.ActorPrincipalId,
                trusted.AuthorizedSiteCodes,
                scope.ScopeKind,
                scope.ScopeId,
                request.LocationCode,
                request.LotNo,
                SiteCode: null,
                request.Skip,
                request.Take,
                request.Status,
                request.Keyword),
            cancellationToken);
    }
}

[Tags("Business Console WMS")]
[HttpPost("/api/business-console/v1/wms/picking-tasks/{warehouseTaskId}/start")]
[BusinessGatewayOperationId("startBusinessConsoleWmsPickingTask")]
[Microsoft.AspNetCore.Mvc.ProducesResponseType(typeof(NetCorePal.Extensions.Dto.ResponseData), StatusCodes.Status403Forbidden)]
[Microsoft.AspNetCore.Mvc.ProducesResponseType(typeof(NetCorePal.Extensions.Dto.ResponseData), StatusCodes.Status409Conflict)]
[Microsoft.AspNetCore.Mvc.ProducesResponseType(typeof(NetCorePal.Extensions.Dto.ResponseData), StatusCodes.Status422UnprocessableEntity)]
public sealed class StartBusinessConsoleWmsPickingTaskEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessWmsClient wms,
    IInternalServiceTokenProvider tokenProvider,
    WmsTrustedRequestContextResolver trustedContextResolver)
    : BusinessConsoleWmsTrustedProxyEndpoint<BusinessConsoleStartWmsWarehouseTaskRequest, BusinessConsoleWmsWarehouseTaskActionResult>(
        auth,
        trustedContextResolver,
        BusinessGatewayPermissions.WmsShipmentsManage)
{
    protected override bool IncludePrincipalContext => true;
    protected override BusinessGatewayAuthorizationContinuityMode AuthorizationContinuityMode => BusinessGatewayAuthorizationContinuityMode.RealtimeRequired;
    protected override string OrganizationId(BusinessConsoleStartWmsWarehouseTaskRequest request) => request.OrganizationId;
    protected override string EnvironmentId(BusinessConsoleStartWmsWarehouseTaskRequest request) => request.EnvironmentId;

    protected override async Task<BusinessConsoleWmsWarehouseTaskActionResult> ForwardAsync(
        BusinessConsoleStartWmsWarehouseTaskRequest request,
        string bearerToken,
        CancellationToken cancellationToken)
    {
        var taskId = Route<string>("warehouseTaskId") ?? request.WarehouseTaskId;
        var trusted = await ResolveTrustedContextAsync(request, cancellationToken);
        var scope = trusted.ResolveScope(request.ScopeKind, request.ScopeId);
        return await wms.StartPickingTaskAsync(
            tokenProvider.BearerToken,
            taskId,
            new BusinessWmsStartWarehouseTaskActionRequest(
                taskId,
                request.OrganizationId,
                request.EnvironmentId,
                trusted.ActorPrincipalId,
                request.IdempotencyKey,
                request.ExpectedVersion,
                trusted.AuthorizedSiteCodes,
                scope.ScopeKind,
                scope.ScopeId),
            cancellationToken);
    }
}

[Tags("Business Console WMS")]
[HttpPost("/api/business-console/v1/wms/picking-tasks/{warehouseTaskId}/progress")]
[BusinessGatewayOperationId("recordBusinessConsoleWmsPickingTaskProgress")]
[Microsoft.AspNetCore.Mvc.ProducesResponseType(typeof(NetCorePal.Extensions.Dto.ResponseData), StatusCodes.Status403Forbidden)]
[Microsoft.AspNetCore.Mvc.ProducesResponseType(typeof(NetCorePal.Extensions.Dto.ResponseData), StatusCodes.Status409Conflict)]
[Microsoft.AspNetCore.Mvc.ProducesResponseType(typeof(NetCorePal.Extensions.Dto.ResponseData), StatusCodes.Status422UnprocessableEntity)]
public sealed class RecordBusinessConsoleWmsPickingTaskProgressEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessWmsClient wms,
    IInternalServiceTokenProvider tokenProvider,
    WmsTrustedRequestContextResolver trustedContextResolver)
    : BusinessConsoleWmsTrustedProxyEndpoint<BusinessConsoleRecordWmsWarehouseTaskProgressRequest, BusinessConsoleWmsWarehouseTaskActionResult>(
        auth,
        trustedContextResolver,
        BusinessGatewayPermissions.WmsShipmentsManage)
{
    protected override bool IncludePrincipalContext => true;
    protected override BusinessGatewayAuthorizationContinuityMode AuthorizationContinuityMode => BusinessGatewayAuthorizationContinuityMode.RealtimeRequired;
    protected override string OrganizationId(BusinessConsoleRecordWmsWarehouseTaskProgressRequest request) => request.OrganizationId;
    protected override string EnvironmentId(BusinessConsoleRecordWmsWarehouseTaskProgressRequest request) => request.EnvironmentId;

    protected override async Task<BusinessConsoleWmsWarehouseTaskActionResult> ForwardAsync(
        BusinessConsoleRecordWmsWarehouseTaskProgressRequest request,
        string bearerToken,
        CancellationToken cancellationToken)
    {
        var taskId = Route<string>("warehouseTaskId") ?? request.WarehouseTaskId;
        var trusted = await ResolveTrustedContextAsync(request, cancellationToken);
        var scope = trusted.ResolveScope(request.ScopeKind, request.ScopeId);
        return await wms.RecordPickingTaskProgressAsync(
            tokenProvider.BearerToken,
            taskId,
            new BusinessWmsRecordWarehouseTaskProgressActionRequest(
                taskId,
                request.OrganizationId,
                request.EnvironmentId,
                trusted.ActorPrincipalId,
                request.IdempotencyKey,
                request.ExpectedVersion,
                request.ExecutedQuantity,
                trusted.AuthorizedSiteCodes,
                scope.ScopeKind,
                scope.ScopeId),
            cancellationToken);
    }
}

[Tags("Business Console WMS")]
[HttpPost("/api/business-console/v1/wms/picking-tasks/{warehouseTaskId}/exception")]
[BusinessGatewayOperationId("reportBusinessConsoleWmsPickingTaskException")]
[Microsoft.AspNetCore.Mvc.ProducesResponseType(typeof(NetCorePal.Extensions.Dto.ResponseData), StatusCodes.Status403Forbidden)]
[Microsoft.AspNetCore.Mvc.ProducesResponseType(typeof(NetCorePal.Extensions.Dto.ResponseData), StatusCodes.Status409Conflict)]
[Microsoft.AspNetCore.Mvc.ProducesResponseType(typeof(NetCorePal.Extensions.Dto.ResponseData), StatusCodes.Status422UnprocessableEntity)]
public sealed class ReportBusinessConsoleWmsPickingTaskExceptionEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessWmsClient wms,
    IInternalServiceTokenProvider tokenProvider,
    WmsTrustedRequestContextResolver trustedContextResolver)
    : BusinessConsoleWmsTrustedProxyEndpoint<BusinessConsoleReportWmsWarehouseTaskExceptionRequest, BusinessConsoleWmsWarehouseTaskActionResult>(
        auth,
        trustedContextResolver,
        BusinessGatewayPermissions.WmsShipmentsManage)
{
    protected override bool IncludePrincipalContext => true;
    protected override BusinessGatewayAuthorizationContinuityMode AuthorizationContinuityMode => BusinessGatewayAuthorizationContinuityMode.RealtimeRequired;
    protected override string OrganizationId(BusinessConsoleReportWmsWarehouseTaskExceptionRequest request) => request.OrganizationId;
    protected override string EnvironmentId(BusinessConsoleReportWmsWarehouseTaskExceptionRequest request) => request.EnvironmentId;

    protected override async Task<BusinessConsoleWmsWarehouseTaskActionResult> ForwardAsync(
        BusinessConsoleReportWmsWarehouseTaskExceptionRequest request,
        string bearerToken,
        CancellationToken cancellationToken)
    {
        var taskId = Route<string>("warehouseTaskId") ?? request.WarehouseTaskId;
        var trusted = await ResolveTrustedContextAsync(request, cancellationToken);
        var scope = trusted.ResolveScope(request.ScopeKind, request.ScopeId);
        return await wms.ReportPickingTaskExceptionAsync(
            tokenProvider.BearerToken,
            taskId,
            new BusinessWmsReportWarehouseTaskExceptionActionRequest(
                taskId,
                request.OrganizationId,
                request.EnvironmentId,
                trusted.ActorPrincipalId,
                request.IdempotencyKey,
                request.ExpectedVersion,
                request.ExceptionCode,
                request.Reason,
                trusted.AuthorizedSiteCodes,
                scope.ScopeKind,
                scope.ScopeId),
            cancellationToken);
    }
}

[Tags("Business Console WMS")]
[HttpPost("/api/business-console/v1/wms/picking-tasks/{warehouseTaskId}/complete")]
[BusinessGatewayOperationId("completeBusinessConsoleWmsPickingTask")]
[Microsoft.AspNetCore.Mvc.ProducesResponseType(typeof(NetCorePal.Extensions.Dto.ResponseData), StatusCodes.Status403Forbidden)]
[Microsoft.AspNetCore.Mvc.ProducesResponseType(typeof(NetCorePal.Extensions.Dto.ResponseData), StatusCodes.Status409Conflict)]
[Microsoft.AspNetCore.Mvc.ProducesResponseType(typeof(NetCorePal.Extensions.Dto.ResponseData), StatusCodes.Status422UnprocessableEntity)]
public sealed class CompleteBusinessConsoleWmsPickingTaskEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessWmsClient wms,
    IInternalServiceTokenProvider tokenProvider,
    WmsTrustedRequestContextResolver trustedContextResolver)
    : BusinessConsoleWmsTrustedProxyEndpoint<BusinessConsoleCompleteWmsWarehouseTaskRequest, BusinessConsoleWmsWarehouseTaskActionResult>(
        auth,
        trustedContextResolver,
        BusinessGatewayPermissions.WmsShipmentsManage)
{
    protected override bool IncludePrincipalContext => true;
    protected override BusinessGatewayAuthorizationContinuityMode AuthorizationContinuityMode => BusinessGatewayAuthorizationContinuityMode.RealtimeRequired;
    protected override string OrganizationId(BusinessConsoleCompleteWmsWarehouseTaskRequest request) => request.OrganizationId;
    protected override string EnvironmentId(BusinessConsoleCompleteWmsWarehouseTaskRequest request) => request.EnvironmentId;

    protected override async Task<BusinessConsoleWmsWarehouseTaskActionResult> ForwardAsync(
        BusinessConsoleCompleteWmsWarehouseTaskRequest request,
        string bearerToken,
        CancellationToken cancellationToken)
    {
        var taskId = Route<string>("warehouseTaskId") ?? request.WarehouseTaskId;
        var trusted = await ResolveTrustedContextAsync(request, cancellationToken);
        var scope = trusted.ResolveScope(request.ScopeKind, request.ScopeId);
        return await wms.CompletePickingTaskAsync(
            tokenProvider.BearerToken,
            taskId,
            new BusinessWmsCompleteWarehouseTaskActionRequest(
                taskId,
                request.OrganizationId,
                request.EnvironmentId,
                trusted.ActorPrincipalId,
                request.IdempotencyKey,
                request.ExpectedVersion,
                request.ExecutedQuantity,
                trusted.AuthorizedSiteCodes,
                scope.ScopeKind,
                scope.ScopeId,
                request.DifferenceReason),
            cancellationToken);
    }
}

[Tags("Business Console WMS")]
[HttpPost("/api/business-console/v1/wms/outbound-orders/{outboundOrderId}/complete")]
[BusinessGatewayOperationId("completeBusinessConsoleWmsOutboundOrder")]
[Microsoft.AspNetCore.Mvc.ProducesResponseType(typeof(NetCorePal.Extensions.Dto.ResponseData), StatusCodes.Status403Forbidden)]
[Microsoft.AspNetCore.Mvc.ProducesResponseType(typeof(NetCorePal.Extensions.Dto.ResponseData), StatusCodes.Status409Conflict)]
[Microsoft.AspNetCore.Mvc.ProducesResponseType(typeof(NetCorePal.Extensions.Dto.ResponseData), StatusCodes.Status422UnprocessableEntity)]
public sealed class CompleteBusinessConsoleWmsOutboundOrderEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessWmsClient wms,
    IInternalServiceTokenProvider tokenProvider,
    WmsTrustedRequestContextResolver trustedContextResolver)
    : BusinessConsoleWmsTrustedProxyEndpoint<BusinessConsoleCompleteWmsOutboundOrderRequest, BusinessConsoleCompleteWmsMovementResponse>(
        auth,
        trustedContextResolver,
        BusinessGatewayPermissions.WmsShipmentsManage)
{
    protected override bool IncludePrincipalContext => true;

    protected override BusinessGatewayAuthorizationContinuityMode AuthorizationContinuityMode =>
        BusinessGatewayAuthorizationContinuityMode.RealtimeRequired;

    protected override string OrganizationId(BusinessConsoleCompleteWmsOutboundOrderRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleCompleteWmsOutboundOrderRequest request) => request.EnvironmentId;

    protected override async Task<BusinessConsoleCompleteWmsMovementResponse> ForwardAsync(
        BusinessConsoleCompleteWmsOutboundOrderRequest request,
        string bearerToken,
        CancellationToken cancellationToken)
    {
        var outboundOrderId = Route<string>("outboundOrderId") ?? request.OutboundOrderId;
        var trusted = await ResolveTrustedContextAsync(request, cancellationToken);
        var scope = trusted.ResolveScope(request.ScopeKind, request.ScopeId);
        return await wms.CompleteOutboundOrderAsync(
            tokenProvider.BearerToken,
            outboundOrderId,
            new BusinessWmsCompleteOutboundOrderRequest(
                outboundOrderId,
                request.OrganizationId,
                request.EnvironmentId,
                trusted.ActorPrincipalId,
                trusted.AuthorizedSiteCodes,
                scope.ScopeKind,
                scope.ScopeId,
                request.ExpectedVersion,
                request.PackReviewNo,
                request.Passed,
                request.IdempotencyKey),
            cancellationToken);
    }
}

[Tags("Business Console WMS")]
[HttpPost("/api/business-console/v1/wms/outbound-orders/{outboundOrderId}/inventory-posting/retry")]
[BusinessGatewayOperationId("retryBusinessConsoleWmsOutboundInventoryPosting")]
public sealed class RetryBusinessConsoleWmsOutboundInventoryPostingEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessWmsClient wms,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleRetryWmsOutboundInventoryPostingRequest, BusinessConsoleCompleteWmsMovementResponse>(
        auth,
        BusinessGatewayPermissions.WmsShipmentsManage)
{
    protected override string OrganizationId(BusinessConsoleRetryWmsOutboundInventoryPostingRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleRetryWmsOutboundInventoryPostingRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleCompleteWmsMovementResponse> ForwardAsync(
        BusinessConsoleRetryWmsOutboundInventoryPostingRequest request,
        string bearerToken,
        CancellationToken cancellationToken)
    {
        var outboundOrderId = Route<string>("outboundOrderId") ?? request.OutboundOrderId;
        return wms.RetryOutboundInventoryPostingAsync(
            tokenProvider.BearerToken,
            outboundOrderId,
            request with { OutboundOrderId = outboundOrderId },
            cancellationToken);
    }
}

[Tags("Business Console WMS")]
[HttpPost("/api/business-console/v1/wms/count-executions")]
[BusinessGatewayOperationId("createBusinessConsoleWmsCountExecution")]
public sealed class CreateBusinessConsoleWmsCountExecutionEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessWmsClient wms,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleCreateWmsCountExecutionRequest, BusinessConsoleCreateWmsCountExecutionResponse>(
        auth,
        BusinessGatewayPermissions.WmsReceiptsManage)
{
    protected override string OrganizationId(BusinessConsoleCreateWmsCountExecutionRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleCreateWmsCountExecutionRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleCreateWmsCountExecutionResponse> ForwardAsync(
        BusinessConsoleCreateWmsCountExecutionRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        wms.CreateCountExecutionAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console WMS")]
[HttpGet("/api/business-console/v1/wms/count-executions")]
[BusinessGatewayOperationId("listBusinessConsoleWmsCountExecutions")]
public sealed class ListBusinessConsoleWmsCountExecutionsEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessWmsClient wms,
    IInternalServiceTokenProvider tokenProvider,
    WmsTrustedRequestContextResolver trustedContextResolver)
    : BusinessConsoleWmsTrustedProxyEndpoint<BusinessConsoleWmsCountExecutionListRequest, BusinessConsoleWmsCountExecutionListResponse>(
        auth,
        trustedContextResolver,
        BusinessGatewayPermissions.WmsCountsRead)
{
    protected override bool IncludePrincipalContext => true;

    protected override BusinessGatewayAuthorizationContinuityMode AuthorizationContinuityMode =>
        BusinessGatewayAuthorizationContinuityMode.RealtimeRequired;

    protected override string OrganizationId(BusinessConsoleWmsCountExecutionListRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleWmsCountExecutionListRequest request) => request.EnvironmentId;

    protected override async Task<BusinessConsoleWmsCountExecutionListResponse> ForwardAsync(
        BusinessConsoleWmsCountExecutionListRequest request,
        string bearerToken,
        CancellationToken cancellationToken)
    {
        var trusted = await ResolveTrustedContextAsync(request, cancellationToken);
        var scope = trusted.ResolveScope(request.ScopeKind, request.ScopeId);
        return await wms.ListCountExecutionsAsync(
            tokenProvider.BearerToken,
            new BusinessWmsCountExecutionListRequest(
                request.OrganizationId,
                request.EnvironmentId,
                trusted.ActorPrincipalId,
                trusted.AuthorizedSiteCodes,
                scope.ScopeKind,
                scope.ScopeId,
                request.LocationCode,
                SiteCode: null,
                request.Skip,
                request.Take,
                request.Status,
                request.Keyword,
                request.CountExecutionId),
            cancellationToken);
    }
}

[Tags("Business Console WMS")]
[HttpPost("/api/business-console/v1/wms/count-executions/{countExecutionId}/complete")]
[BusinessGatewayOperationId("completeBusinessConsoleWmsCountExecution")]
[Microsoft.AspNetCore.Mvc.ProducesResponseType(typeof(NetCorePal.Extensions.Dto.ResponseData), StatusCodes.Status403Forbidden)]
[Microsoft.AspNetCore.Mvc.ProducesResponseType(typeof(NetCorePal.Extensions.Dto.ResponseData), StatusCodes.Status409Conflict)]
[Microsoft.AspNetCore.Mvc.ProducesResponseType(typeof(NetCorePal.Extensions.Dto.ResponseData), StatusCodes.Status422UnprocessableEntity)]
public sealed class CompleteBusinessConsoleWmsCountExecutionEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessWmsClient wms,
    IInternalServiceTokenProvider tokenProvider,
    WmsTrustedRequestContextResolver trustedContextResolver)
    : BusinessConsoleWmsTrustedProxyEndpoint<BusinessConsoleCompleteWmsCountExecutionRequest, BusinessConsoleCompleteWmsMovementResponse>(
        auth,
        trustedContextResolver,
        BusinessGatewayPermissions.WmsReceiptsManage)
{
    protected override bool IncludePrincipalContext => true;

    protected override BusinessGatewayAuthorizationContinuityMode AuthorizationContinuityMode =>
        BusinessGatewayAuthorizationContinuityMode.RealtimeRequired;

    protected override string OrganizationId(BusinessConsoleCompleteWmsCountExecutionRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleCompleteWmsCountExecutionRequest request) => request.EnvironmentId;

    protected override async Task<BusinessConsoleCompleteWmsMovementResponse> ForwardAsync(
        BusinessConsoleCompleteWmsCountExecutionRequest request,
        string bearerToken,
        CancellationToken cancellationToken)
    {
        var countExecutionId = Route<string>("countExecutionId") ?? request.CountExecutionId;
        var trusted = await ResolveTrustedContextAsync(request, cancellationToken);
        var scope = trusted.ResolveScope(request.ScopeKind, request.ScopeId);
        return await wms.CompleteCountExecutionAsync(
            tokenProvider.BearerToken,
            countExecutionId,
            new BusinessWmsCompleteCountExecutionRequest(
                countExecutionId,
                request.OrganizationId,
                request.EnvironmentId,
                trusted.ActorPrincipalId,
                trusted.AuthorizedSiteCodes,
                scope.ScopeKind,
                scope.ScopeId,
                request.ExpectedVersion,
                request.CountedQuantity,
                request.IdempotencyKey),
            cancellationToken);
    }
}

[Tags("Business Console WMS")]
[HttpPost("/api/business-console/v1/wms/wcs-tasks/{warehouseTaskId}/dispatch")]
[BusinessGatewayOperationId("dispatchBusinessConsoleWmsWcsTask")]
[Microsoft.AspNetCore.Mvc.ProducesResponseType(typeof(NetCorePal.Extensions.Dto.ResponseData), StatusCodes.Status403Forbidden)]
[Microsoft.AspNetCore.Mvc.ProducesResponseType(typeof(NetCorePal.Extensions.Dto.ResponseData), StatusCodes.Status409Conflict)]
[Microsoft.AspNetCore.Mvc.ProducesResponseType(typeof(NetCorePal.Extensions.Dto.ResponseData), StatusCodes.Status422UnprocessableEntity)]
public sealed class DispatchBusinessConsoleWmsWcsTaskEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessWmsClient wms,
    IInternalServiceTokenProvider tokenProvider,
    WmsTrustedRequestContextResolver trustedContextResolver)
    : BusinessConsoleWmsTrustedProxyEndpoint<BusinessConsoleDispatchWmsWcsTaskRequest, BusinessConsoleDispatchWmsWcsTaskResponse>(
        auth,
        trustedContextResolver,
        BusinessGatewayPermissions.WmsAutomationManage)
{
    protected override bool IncludePrincipalContext => true;

    protected override BusinessGatewayAuthorizationContinuityMode AuthorizationContinuityMode =>
        BusinessGatewayAuthorizationContinuityMode.RealtimeRequired;

    protected override string OrganizationId(BusinessConsoleDispatchWmsWcsTaskRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleDispatchWmsWcsTaskRequest request) => request.EnvironmentId;

    protected override async Task<BusinessConsoleDispatchWmsWcsTaskResponse> ForwardAsync(
        BusinessConsoleDispatchWmsWcsTaskRequest request,
        string bearerToken,
        CancellationToken cancellationToken)
    {
        var warehouseTaskId = Route<string>("warehouseTaskId") ?? request.WarehouseTaskId;
        var trusted = await ResolveTrustedContextAsync(request, cancellationToken);
        return await wms.DispatchWcsTaskAsync(
            tokenProvider.BearerToken,
            warehouseTaskId,
            new BusinessWmsDispatchWcsTaskRequest(
                warehouseTaskId,
                request.OrganizationId,
                request.EnvironmentId,
                trusted.ActorPrincipalId,
                trusted.AuthorizedSiteCodes,
                request.ExpectedVersion,
                request.AdapterType,
                request.ExternalTaskId,
                request.PayloadJson,
                request.DeviceId),
            cancellationToken);
    }
}

[Tags("Business Console WMS")]
[HttpPost("/api/business-console/v1/wms/wcs-tasks/{externalTaskId}/fail")]
[BusinessGatewayOperationId("failBusinessConsoleWmsWcsTask")]
public sealed class FailBusinessConsoleWmsWcsTaskEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessWmsClient wms,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleFailWmsWcsTaskRequest, BusinessConsoleAcceptedResponse>(
        auth,
        BusinessGatewayPermissions.WmsAutomationManage)
{
    protected override string OrganizationId(BusinessConsoleFailWmsWcsTaskRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleFailWmsWcsTaskRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleAcceptedResponse> ForwardAsync(
        BusinessConsoleFailWmsWcsTaskRequest request,
        string bearerToken,
        CancellationToken cancellationToken)
    {
        var externalTaskId = Route<string>("externalTaskId") ?? request.ExternalTaskId;
        return wms.FailWcsTaskAsync(
            tokenProvider.BearerToken,
            externalTaskId,
            request with { ExternalTaskId = externalTaskId },
            cancellationToken);
    }
}

[Tags("Business Console WMS")]
[HttpPost("/api/business-console/v1/wms/wcs-tasks/{externalTaskId}/complete")]
[BusinessGatewayOperationId("completeBusinessConsoleWmsWcsTask")]
public sealed class CompleteBusinessConsoleWmsWcsTaskEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessWmsClient wms,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleCompleteWmsWcsTaskRequest, BusinessConsoleAcceptedResponse>(
        auth,
        BusinessGatewayPermissions.WmsAutomationManage)
{
    protected override string OrganizationId(BusinessConsoleCompleteWmsWcsTaskRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleCompleteWmsWcsTaskRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleAcceptedResponse> ForwardAsync(
        BusinessConsoleCompleteWmsWcsTaskRequest request,
        string bearerToken,
        CancellationToken cancellationToken)
    {
        var externalTaskId = Route<string>("externalTaskId") ?? request.ExternalTaskId;
        return wms.CompleteWcsTaskAsync(
            tokenProvider.BearerToken,
            externalTaskId,
            request with { ExternalTaskId = externalTaskId },
            cancellationToken);
    }
}

[Tags("Business Console WMS")]
[HttpGet("/api/business-console/v1/wms/wcs-tasks")]
[BusinessGatewayOperationId("listBusinessConsoleWmsWcsTasks")]
public sealed class ListBusinessConsoleWmsWcsTasksEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessWmsClient wms,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleWmsWcsTaskListRequest, BusinessConsoleWmsWcsTaskListResponse>(
        auth,
        BusinessGatewayPermissions.WmsAutomationManage)
{
    protected override string OrganizationId(BusinessConsoleWmsWcsTaskListRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleWmsWcsTaskListRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleWmsWcsTaskListResponse> ForwardAsync(
        BusinessConsoleWmsWcsTaskListRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        wms.ListWcsTasksAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console WMS")]
[HttpGet("/api/business-console/v1/wms/receiving-quality-gates")]
[BusinessGatewayOperationId("listBusinessConsoleWmsReceivingQualityGates")]
public sealed class ListBusinessConsoleWmsReceivingQualityGatesEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessWmsClient wms,
    IInternalServiceTokenProvider tokenProvider,
    WmsTrustedRequestContextResolver trustedContextResolver)
    : BusinessConsoleWmsTrustedProxyEndpoint<BusinessConsoleWmsReceivingQualityGateListRequest, BusinessConsoleWmsReceivingQualityGateListResponse>(
        auth,
        trustedContextResolver,
        BusinessGatewayPermissions.WmsReceiptsRead)
{
    protected override bool IncludePrincipalContext => true;

    protected override BusinessGatewayAuthorizationContinuityMode AuthorizationContinuityMode =>
        BusinessGatewayAuthorizationContinuityMode.RealtimeRequired;

    protected override string OrganizationId(BusinessConsoleWmsReceivingQualityGateListRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleWmsReceivingQualityGateListRequest request) => request.EnvironmentId;

    protected override async Task<BusinessConsoleWmsReceivingQualityGateListResponse> ForwardAsync(
        BusinessConsoleWmsReceivingQualityGateListRequest request,
        string bearerToken,
        CancellationToken cancellationToken)
    {
        var trusted = await ResolveTrustedContextAsync(request, cancellationToken);
        var scope = trusted.ResolveScope(request.ScopeKind, request.ScopeId);
        return await wms.ListReceivingQualityGatesAsync(
            tokenProvider.BearerToken,
            new BusinessWmsReceivingQualityGateListRequest(
                request.OrganizationId,
                request.EnvironmentId,
                trusted.ActorPrincipalId,
                trusted.AuthorizedSiteCodes,
                scope.ScopeKind,
                scope.ScopeId,
                request.Skip,
                request.Take,
                request.GateStatus,
                request.Keyword,
                request.IncludeNotRequired,
                request.InboundOrderNo),
            cancellationToken);
    }
}

[Tags("Business Console WMS")]
[HttpGet("/api/business-console/v1/wms/supplier-return-requests")]
[BusinessGatewayOperationId("listBusinessConsoleWmsSupplierReturnRequests")]
public sealed class ListBusinessConsoleWmsSupplierReturnRequestsEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessWmsClient wms,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleWmsListRequest, BusinessConsoleWmsSupplierReturnListResponse>(
        auth,
        BusinessGatewayPermissions.WmsReceiptsRead)
{
    protected override string OrganizationId(BusinessConsoleWmsListRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleWmsListRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleWmsSupplierReturnListResponse> ForwardAsync(
        BusinessConsoleWmsListRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        wms.ListSupplierReturnRequestsAsync(tokenProvider.BearerToken, request, cancellationToken);
}

public sealed class BusinessConsoleWmsInboundOrderListRequestValidator
    : Validator<BusinessConsoleWmsInboundOrderListRequest>
{
    public BusinessConsoleWmsInboundOrderListRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.SkuCode).MaximumLength(100);
        RuleFor(x => x.UomCode).MaximumLength(50);
        RuleFor(x => x.SiteCode).MaximumLength(100);
        RuleFor(x => x.LocationCode).MaximumLength(100);
        RuleFor(x => x.LotNo).MaximumLength(100);
        RuleFor(x => x.SerialNo).MaximumLength(100);
        RuleFor(x => x.QualityStatus).MaximumLength(50);
        RuleFor(x => x.OwnerType).MaximumLength(50);
        RuleFor(x => x.OwnerId).MaximumLength(100);
        RuleFor(x => x.Skip).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Take).InclusiveBetween(1, 500);
        RuleFor(x => x.Status).MaximumLength(50);
        RuleFor(x => x.Keyword).MaximumLength(150);
        RuleFor(x => x.ScopeKind)
            .Must(BusinessConsoleWmsScopeKinds.Contains)
            .When(x => !string.IsNullOrWhiteSpace(x.ScopeKind));
        RuleFor(x => x.ScopeId).MaximumLength(200);
    }
}

public sealed class BusinessConsoleWmsWorkScopeCatalogRequestValidator
    : Validator<BusinessConsoleWmsWorkScopeCatalogRequest>
{
    public BusinessConsoleWmsWorkScopeCatalogRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
    }
}

public sealed class BusinessConsoleAssignWmsResourceRequestValidator
    : Validator<BusinessConsoleAssignWmsResourceRequest>
{
    public BusinessConsoleAssignWmsResourceRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.PoolCode).NotEmpty().MaximumLength(150);
        RuleFor(x => x.OperatorPrincipalId).MaximumLength(150);
        RuleFor(x => x.IdempotencyKey).NotEmpty().MaximumLength(128);
        RuleFor(x => x.ExpectedVersion).GreaterThanOrEqualTo(0);
    }
}

public sealed class BusinessConsoleCreateWmsInboundOrderRequestValidator
    : Validator<BusinessConsoleCreateWmsInboundOrderRequest>
{
    public BusinessConsoleCreateWmsInboundOrderRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.InboundOrderNo).NotEmpty().MaximumLength(100);
        RuleFor(x => x.SourceDocumentType).NotEmpty().MaximumLength(100);
        RuleFor(x => x.SourceDocumentId).NotEmpty().MaximumLength(150);
        RuleFor(x => x.SiteCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Lines).NotEmpty();
    }
}

public sealed class BusinessConsoleCreateWmsPutawayTaskRequestValidator
    : Validator<BusinessConsoleCreateWmsPutawayTaskRequest>
{
    public BusinessConsoleCreateWmsPutawayTaskRequestValidator()
    {
        RuleFor(x => x.InboundOrderId).NotEmpty().MaximumLength(150);
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.TaskNo).NotEmpty().MaximumLength(100);
        RuleFor(x => x.LineNo).NotEmpty().MaximumLength(50);
        RuleFor(x => x.FromLocationCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.ToLocationCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Quantity).GreaterThan(0);
    }
}

public sealed class BusinessConsoleCompleteWmsInboundOrderRequestValidator
    : Validator<BusinessConsoleCompleteWmsInboundOrderRequest>
{
    public BusinessConsoleCompleteWmsInboundOrderRequestValidator()
    {
        RuleFor(x => x.InboundOrderId).NotEmpty().MaximumLength(150);
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.IdempotencyKey).NotEmpty().MaximumLength(150);
        RuleFor(x => x.ExpectedVersion).GreaterThan(0);
        RuleFor(x => x.ScopeKind)
            .Must(BusinessConsoleWmsScopeKinds.Contains)
            .When(x => !string.IsNullOrWhiteSpace(x.ScopeKind));
        RuleFor(x => x.ScopeId).MaximumLength(200);
        RuleFor(x => x)
            .Must(x =>
                string.IsNullOrWhiteSpace(x.ScopeKind)
                == string.IsNullOrWhiteSpace(x.ScopeId))
            .WithMessage("scopeKind and scopeId must be supplied together.");
        RuleForEach(x => x.Lines).SetValidator(new BusinessConsoleWmsInboundLineCaptureInputValidator());
    }
}

public sealed class BusinessConsoleWmsInboundLineCaptureInputValidator
    : Validator<BusinessConsoleWmsInboundLineCaptureInput>
{
    public BusinessConsoleWmsInboundLineCaptureInputValidator()
    {
        RuleFor(x => x.LineNo).NotEmpty().MaximumLength(50);
    }
}

public sealed class BusinessConsoleWmsWarehouseTaskListRequestValidator : Validator<BusinessConsoleWmsWarehouseTaskListRequest>
{
    public BusinessConsoleWmsWarehouseTaskListRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.LocationCode).MaximumLength(100);
        RuleFor(x => x.LotNo).MaximumLength(100);
        RuleFor(x => x.Skip).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Take).InclusiveBetween(1, 500);
        RuleFor(x => x.Status).MaximumLength(50);
        RuleFor(x => x.Keyword).MaximumLength(150);
        RuleFor(x => x.ScopeKind)
            .Must(BusinessConsoleWmsScopeKinds.Contains)
            .When(x => !string.IsNullOrWhiteSpace(x.ScopeKind));
        RuleFor(x => x.ScopeId).MaximumLength(200);
    }
}

public sealed class BusinessConsoleCreateWmsOutboundOrderRequestValidator
    : Validator<BusinessConsoleCreateWmsOutboundOrderRequest>
{
    public BusinessConsoleCreateWmsOutboundOrderRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.OutboundOrderNo).NotEmpty().MaximumLength(100);
        RuleFor(x => x.SourceDocumentType).NotEmpty().MaximumLength(100);
        RuleFor(x => x.SourceDocumentId).NotEmpty().MaximumLength(150);
        RuleFor(x => x.SiteCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Lines).NotEmpty();
    }
}

public sealed class BusinessConsoleCreateWmsPickingTaskRequestValidator
    : Validator<BusinessConsoleCreateWmsPickingTaskRequest>
{
    public BusinessConsoleCreateWmsPickingTaskRequestValidator()
    {
        RuleFor(x => x.OutboundOrderId).NotEmpty().MaximumLength(150);
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.TaskNo).NotEmpty().MaximumLength(100);
        RuleFor(x => x.LineNo).NotEmpty().MaximumLength(50);
        RuleFor(x => x.FromLocationCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.ToLocationCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Quantity).GreaterThan(0);
    }
}

public sealed class BusinessConsoleCompleteWmsOutboundOrderRequestValidator
    : Validator<BusinessConsoleCompleteWmsOutboundOrderRequest>
{
    public BusinessConsoleCompleteWmsOutboundOrderRequestValidator()
    {
        RuleFor(x => x.OutboundOrderId).NotEmpty().MaximumLength(150);
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.PackReviewNo).NotEmpty().MaximumLength(100);
        RuleFor(x => x.IdempotencyKey).NotEmpty().MaximumLength(150);
        RuleFor(x => x.ExpectedVersion).GreaterThan(0);
        RuleFor(x => x.ScopeKind)
            .Must(BusinessConsoleWmsScopeKinds.Contains)
            .When(x => !string.IsNullOrWhiteSpace(x.ScopeKind));
        RuleFor(x => x.ScopeId).MaximumLength(200);
        RuleFor(x => x)
            .Must(x =>
                string.IsNullOrWhiteSpace(x.ScopeKind)
                == string.IsNullOrWhiteSpace(x.ScopeId))
            .WithMessage("scopeKind and scopeId must be supplied together.");
    }
}

public sealed class BusinessConsoleCreateWmsCountExecutionRequestValidator
    : Validator<BusinessConsoleCreateWmsCountExecutionRequest>
{
    public BusinessConsoleCreateWmsCountExecutionRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.CountNo).NotEmpty().MaximumLength(100);
        RuleFor(x => x.SkuCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.UomCode).NotEmpty().MaximumLength(50);
        RuleFor(x => x.SiteCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.LocationCode).NotEmpty().MaximumLength(100);
    }
}

public sealed class BusinessConsoleWmsCountExecutionListRequestValidator : Validator<BusinessConsoleWmsCountExecutionListRequest>
{
    public BusinessConsoleWmsCountExecutionListRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.LocationCode).MaximumLength(100);
        RuleFor(x => x.Skip).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Take).InclusiveBetween(1, 500);
        RuleFor(x => x.Status).MaximumLength(50);
        RuleFor(x => x.Keyword).MaximumLength(150);
        RuleFor(x => x.ScopeKind)
            .Must(BusinessConsoleWmsScopeKinds.Contains)
            .When(x => !string.IsNullOrWhiteSpace(x.ScopeKind));
        RuleFor(x => x.ScopeId).MaximumLength(200);
    }
}

public sealed class BusinessConsoleCompleteWmsCountExecutionRequestValidator
    : Validator<BusinessConsoleCompleteWmsCountExecutionRequest>
{
    public BusinessConsoleCompleteWmsCountExecutionRequestValidator()
    {
        RuleFor(x => x.CountExecutionId).NotEmpty().MaximumLength(150);
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.CountedQuantity).GreaterThanOrEqualTo(0);
        RuleFor(x => x.IdempotencyKey).NotEmpty().MaximumLength(150);
        RuleFor(x => x.ExpectedVersion).GreaterThan(0);
        RuleFor(x => x.ScopeKind)
            .Must(BusinessConsoleWmsScopeKinds.Contains)
            .When(x => !string.IsNullOrWhiteSpace(x.ScopeKind));
        RuleFor(x => x.ScopeId).MaximumLength(200);
        RuleFor(x => x)
            .Must(x =>
                string.IsNullOrWhiteSpace(x.ScopeKind)
                == string.IsNullOrWhiteSpace(x.ScopeId))
            .WithMessage("scopeKind and scopeId must be supplied together.");
    }
}

public sealed class BusinessConsoleStartWmsWarehouseTaskRequestValidator
    : Validator<BusinessConsoleStartWmsWarehouseTaskRequest>
{
    public BusinessConsoleStartWmsWarehouseTaskRequestValidator()
    {
        RuleFor(x => x.WarehouseTaskId).NotEmpty().MaximumLength(150);
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.IdempotencyKey).NotEmpty().MaximumLength(150);
        RuleFor(x => x.ExpectedVersion).GreaterThanOrEqualTo(0);
        AddScopeRules();
    }

    private void AddScopeRules()
    {
        RuleFor(x => x.ScopeKind)
            .Must(BusinessConsoleWmsScopeKinds.Contains)
            .When(x => !string.IsNullOrWhiteSpace(x.ScopeKind));
        RuleFor(x => x.ScopeId).MaximumLength(200);
    }
}

public sealed class BusinessConsoleRecordWmsWarehouseTaskProgressRequestValidator
    : Validator<BusinessConsoleRecordWmsWarehouseTaskProgressRequest>
{
    public BusinessConsoleRecordWmsWarehouseTaskProgressRequestValidator()
    {
        RuleFor(x => x.WarehouseTaskId).NotEmpty().MaximumLength(150);
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.IdempotencyKey).NotEmpty().MaximumLength(150);
        RuleFor(x => x.ExpectedVersion).GreaterThanOrEqualTo(0);
        RuleFor(x => x.ExecutedQuantity).GreaterThanOrEqualTo(0);
        RuleFor(x => x.ScopeKind)
            .Must(BusinessConsoleWmsScopeKinds.Contains)
            .When(x => !string.IsNullOrWhiteSpace(x.ScopeKind));
        RuleFor(x => x.ScopeId).MaximumLength(200);
    }
}

public sealed class BusinessConsoleReportWmsWarehouseTaskExceptionRequestValidator
    : Validator<BusinessConsoleReportWmsWarehouseTaskExceptionRequest>
{
    public BusinessConsoleReportWmsWarehouseTaskExceptionRequestValidator()
    {
        RuleFor(x => x.WarehouseTaskId).NotEmpty().MaximumLength(150);
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.IdempotencyKey).NotEmpty().MaximumLength(150);
        RuleFor(x => x.ExpectedVersion).GreaterThanOrEqualTo(0);
        RuleFor(x => x.ExceptionCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
        RuleFor(x => x.ScopeKind)
            .Must(BusinessConsoleWmsScopeKinds.Contains)
            .When(x => !string.IsNullOrWhiteSpace(x.ScopeKind));
        RuleFor(x => x.ScopeId).MaximumLength(200);
    }
}

public sealed class BusinessConsoleCompleteWmsWarehouseTaskRequestValidator
    : Validator<BusinessConsoleCompleteWmsWarehouseTaskRequest>
{
    public BusinessConsoleCompleteWmsWarehouseTaskRequestValidator()
    {
        RuleFor(x => x.WarehouseTaskId).NotEmpty().MaximumLength(150);
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.IdempotencyKey).NotEmpty().MaximumLength(150);
        RuleFor(x => x.ExpectedVersion).GreaterThanOrEqualTo(0);
        RuleFor(x => x.ExecutedQuantity).GreaterThanOrEqualTo(0);
        RuleFor(x => x.DifferenceReason).MaximumLength(500);
        RuleFor(x => x.ScopeKind)
            .Must(BusinessConsoleWmsScopeKinds.Contains)
            .When(x => !string.IsNullOrWhiteSpace(x.ScopeKind));
        RuleFor(x => x.ScopeId).MaximumLength(200);
    }
}

public sealed class BusinessConsoleDispatchWmsWcsTaskRequestValidator
    : Validator<BusinessConsoleDispatchWmsWcsTaskRequest>
{
    public BusinessConsoleDispatchWmsWcsTaskRequestValidator()
    {
        RuleFor(x => x.WarehouseTaskId).NotEmpty().MaximumLength(150);
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.ExpectedVersion).GreaterThanOrEqualTo(0);
        RuleFor(x => x.AdapterType).NotEmpty().MaximumLength(100);
        RuleFor(x => x.ExternalTaskId).NotEmpty().MaximumLength(150);
        RuleFor(x => x.PayloadJson).NotEmpty();
        RuleFor(x => x.DeviceId).MaximumLength(150);
    }
}

public sealed class BusinessConsoleFailWmsWcsTaskRequestValidator
    : Validator<BusinessConsoleFailWmsWcsTaskRequest>
{
    public BusinessConsoleFailWmsWcsTaskRequestValidator()
    {
        RuleFor(x => x.ExternalTaskId).NotEmpty().MaximumLength(150);
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.FailureCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.FailureMessage).NotEmpty().MaximumLength(500);
    }
}

public sealed class BusinessConsoleCompleteWmsWcsTaskRequestValidator
    : Validator<BusinessConsoleCompleteWmsWcsTaskRequest>
{
    public BusinessConsoleCompleteWmsWcsTaskRequestValidator()
    {
        RuleFor(x => x.ExternalTaskId).NotEmpty().MaximumLength(150);
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.CompletionPayloadJson).NotEmpty();
    }
}

public sealed class BusinessConsoleWmsListRequestValidator : Validator<BusinessConsoleWmsListRequest>
{
    public BusinessConsoleWmsListRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Skip).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Take).InclusiveBetween(1, 500);
        RuleFor(x => x.Status).MaximumLength(50);
        RuleFor(x => x.Keyword).MaximumLength(150);
    }
}

public sealed class BusinessConsoleWmsOutboundOrderListRequestValidator
    : Validator<BusinessConsoleWmsOutboundOrderListRequest>
{
    public BusinessConsoleWmsOutboundOrderListRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Skip).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Take).InclusiveBetween(1, 500);
        RuleFor(x => x.Status).MaximumLength(50);
        RuleFor(x => x.Keyword).MaximumLength(150);
        RuleFor(x => x.OutboundOrderId)
            .Must(value => value is null || (Guid.TryParse(value, out var parsed) && parsed != Guid.Empty))
            .WithMessage("OutboundOrderId must be a non-empty GUID.");
        RuleFor(x => x.ScopeKind)
            .Must(BusinessConsoleWmsScopeKinds.Contains)
            .When(x => !string.IsNullOrWhiteSpace(x.ScopeKind));
        RuleFor(x => x.ScopeId).MaximumLength(200);
    }
}

public sealed class BusinessConsoleWmsWcsTaskListRequestValidator : Validator<BusinessConsoleWmsWcsTaskListRequest>
{
    public BusinessConsoleWmsWcsTaskListRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.ExternalTaskId).MaximumLength(150);
        RuleFor(x => x.WarehouseTaskId).MaximumLength(150);
        RuleFor(x => x.Skip).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Take).InclusiveBetween(1, 500);
        RuleFor(x => x.Status).MaximumLength(50);
        RuleFor(x => x.Keyword).MaximumLength(150);
    }
}

public sealed class BusinessConsoleWmsReceivingQualityGateListRequestValidator : Validator<BusinessConsoleWmsReceivingQualityGateListRequest>
{
    public BusinessConsoleWmsReceivingQualityGateListRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Skip).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Take).InclusiveBetween(1, 500);
        RuleFor(x => x.GateStatus).MaximumLength(50);
        RuleFor(x => x.Keyword).MaximumLength(150);
        RuleFor(x => x.InboundOrderNo).MaximumLength(150);
        RuleFor(x => x.ScopeKind)
            .Must(BusinessConsoleWmsScopeKinds.Contains)
            .When(x => !string.IsNullOrWhiteSpace(x.ScopeKind));
        RuleFor(x => x.ScopeId).MaximumLength(200);
    }
}

internal static class BusinessConsoleWmsScopeKinds
{
    public static bool Contains(string? value) =>
        value?.Trim().ToLowerInvariant() is "self" or "work-pool" or "site";
}
