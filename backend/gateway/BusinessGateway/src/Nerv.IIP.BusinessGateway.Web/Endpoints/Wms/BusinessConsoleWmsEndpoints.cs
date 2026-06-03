using FastEndpoints;
using FluentValidation;
using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.BusinessGateway.Web.Application.OpenApi;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.BusinessGateway.Web.Endpoints.Wms;

[Tags("Business Console WMS")]
[HttpGet("/api/business-console/v1/wms/inbound-orders")]
[BusinessGatewayOperationId("listBusinessConsoleWmsInboundOrders")]
public sealed class ListBusinessConsoleWmsInboundOrdersEndpoint
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleWmsInboundOrderListRequest, BusinessConsoleWmsInboundOrderListResponse>
{
    private readonly IBusinessGatewayAuthorizationClient _auth;
    private readonly IBusinessWmsClient _wms;
    private readonly IBusinessInventoryClient _inventory;
    private readonly IInternalServiceTokenProvider _tokenProvider;

    public ListBusinessConsoleWmsInboundOrdersEndpoint(
        IBusinessGatewayAuthorizationClient auth,
        IBusinessWmsClient wms,
        IBusinessInventoryClient inventory,
        IInternalServiceTokenProvider tokenProvider)
        : base(auth, BusinessGatewayPermissions.WmsReceiptsRead)
    {
        _auth = auth;
        _wms = wms;
        _inventory = inventory;
        _tokenProvider = tokenProvider;
    }

    protected override string OrganizationId(BusinessConsoleWmsInboundOrderListRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleWmsInboundOrderListRequest request) => request.EnvironmentId;

    protected override async Task<BusinessConsoleWmsInboundOrderListResponse> ForwardAsync(
        BusinessConsoleWmsInboundOrderListRequest request,
        string bearerToken,
        CancellationToken cancellationToken)
    {
        var response = await _wms.ListInboundOrdersAsync(
            _tokenProvider.BearerToken,
            new BusinessConsoleWmsListRequest(request.OrganizationId, request.EnvironmentId),
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
                null),
            cancellationToken);
        if (!authorization.IsAllowed)
        {
            return new BusinessConsoleWmsInventoryContext(
                "BusinessInventory",
                "forbidden",
                BusinessGatewayPermissions.InventoryLedgerRead,
                authorization.DenialReason ?? "forbidden",
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
}

[Tags("Business Console WMS")]
[HttpGet("/api/business-console/v1/wms/outbound-orders")]
[BusinessGatewayOperationId("listBusinessConsoleWmsOutboundOrders")]
public sealed class ListBusinessConsoleWmsOutboundOrdersEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessWmsClient wms,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleWmsListRequest, BusinessConsoleWmsOutboundOrderListResponse>(
        auth,
        BusinessGatewayPermissions.WmsShipmentsRead)
{
    protected override string OrganizationId(BusinessConsoleWmsListRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleWmsListRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleWmsOutboundOrderListResponse> ForwardAsync(
        BusinessConsoleWmsListRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        wms.ListOutboundOrdersAsync(tokenProvider.BearerToken, request, cancellationToken);
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
    }
}

public sealed class BusinessConsoleWmsListRequestValidator : Validator<BusinessConsoleWmsListRequest>
{
    public BusinessConsoleWmsListRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
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
    }
}
