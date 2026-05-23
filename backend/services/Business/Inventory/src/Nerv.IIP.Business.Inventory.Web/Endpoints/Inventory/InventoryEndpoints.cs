using System.Diagnostics.CodeAnalysis;
using FastEndpoints;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockCountTaskAggregate;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockLocationAggregate;
using Nerv.IIP.Business.Inventory.Web.Application.Auth;
using Nerv.IIP.Business.Inventory.Web.Application.Commands.StockCounts;
using Nerv.IIP.Business.Inventory.Web.Application.Commands.StockLocations;
using Nerv.IIP.Business.Inventory.Web.Application.Commands.StockMovements;
using Nerv.IIP.Business.Inventory.Web.Application.Queries;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.Business.Inventory.Web.Endpoints.Inventory;

public abstract class InventoryEndpoint<TRequest, TResponse> : Endpoint<TRequest, TResponse>
    where TRequest : notnull
{
    protected void ConfigureInventoryContract(InventoryEndpointContract contract)
    {
        switch (contract.HttpMethod)
        {
            case "GET":
                Get(contract.Route);
                break;
            case "POST":
                Post(contract.Route);
                break;
            default:
                throw new NotSupportedException($"HTTP method '{contract.HttpMethod}' is not supported by Inventory endpoints.");
        }

        Tags("Business Inventory");
        Policies(contract.AuthorizationPolicy);
        Permissions(contract.PermissionCode);
    }
}

public sealed record CreateOrUpdateStockLocationRequest(
    string OrganizationId,
    string EnvironmentId,
    string LocationCode,
    string LocationType,
    string SiteCode,
    string? ParentLocationCode,
    string Status);

public sealed record CreateOrUpdateStockLocationResponse(StockLocationId LocationId);

public sealed record PostStockMovementRequest(
    string OrganizationId,
    string EnvironmentId,
    string MovementType,
    string SourceService,
    string SourceDocumentId,
    string? SourceDocumentLineId,
    string IdempotencyKey,
    string SkuCode,
    string UomCode,
    string SiteCode,
    string LocationCode,
    string? LotNo,
    string? SerialNo,
    string QualityStatus,
    string OwnerType,
    string? OwnerId,
    decimal Quantity);

public sealed record PostStockMovementResponse(string MovementId, decimal OnHandQuantity, decimal AvailableQuantity);

public sealed record GetStockAvailabilityRequest(
    string OrganizationId,
    string EnvironmentId,
    string SkuCode,
    string UomCode,
    string SiteCode,
    string? LocationCode,
    string? LotNo,
    string? SerialNo,
    string? QualityStatus,
    string? OwnerType,
    string? OwnerId);

public sealed record CreateStockCountTaskRequest(
    string OrganizationId,
    string EnvironmentId,
    string CountTaskCode,
    string SkuCode,
    string UomCode,
    string SiteCode,
    string LocationCode,
    string? LotNo,
    string? SerialNo,
    string QualityStatus,
    string OwnerType,
    string? OwnerId);

public sealed record CreateStockCountTaskResponse(StockCountTaskId CountTaskId, long ExpectedLedgerVersion);

public sealed record ConfirmStockCountAdjustmentRequest(
    StockCountTaskId CountTaskId,
    decimal CountedQuantity,
    string IdempotencyKey);

public sealed record ConfirmStockCountAdjustmentResponse(string MovementId, decimal VarianceQuantity, decimal OnHandQuantity);

public sealed class CreateOrUpdateStockLocationEndpoint(ISender sender)
    : InventoryEndpoint<CreateOrUpdateStockLocationRequest, ResponseData<CreateOrUpdateStockLocationResponse>>
{
    public override void Configure()
    {
        ConfigureInventoryContract(InventoryEndpointContracts.Get<CreateOrUpdateStockLocationEndpoint>());
    }

    public override async Task HandleAsync(CreateOrUpdateStockLocationRequest req, CancellationToken ct)
    {
        var result = await sender.Send(new CreateStockLocationCommand(
            req.OrganizationId,
            req.EnvironmentId,
            req.LocationCode,
            req.LocationType,
            req.SiteCode,
            req.ParentLocationCode,
            req.Status), ct);
        await Send.OkAsync(new CreateOrUpdateStockLocationResponse(result.LocationId).AsResponseData(), cancellation: ct);
    }
}

public sealed class PostStockMovementEndpoint(ISender sender)
    : InventoryEndpoint<PostStockMovementRequest, ResponseData<PostStockMovementResponse>>
{
    public override void Configure()
    {
        ConfigureInventoryContract(InventoryEndpointContracts.Get<PostStockMovementEndpoint>());
    }

    public override async Task HandleAsync(PostStockMovementRequest req, CancellationToken ct)
    {
        var result = await sender.Send(new PostStockMovementCommand(
            req.OrganizationId,
            req.EnvironmentId,
            req.MovementType,
            req.SourceService,
            req.SourceDocumentId,
            req.SourceDocumentLineId,
            req.IdempotencyKey,
            req.SkuCode,
            req.UomCode,
            req.SiteCode,
            req.LocationCode,
            req.LotNo,
            req.SerialNo,
            req.QualityStatus,
            req.OwnerType,
            req.OwnerId,
            req.Quantity), ct);
        await Send.OkAsync(new PostStockMovementResponse(result.MovementId.ToString(), result.OnHandQuantity, result.AvailableQuantity).AsResponseData(), cancellation: ct);
    }
}

public sealed class GetStockAvailabilityEndpoint(ISender sender)
    : InventoryEndpoint<GetStockAvailabilityRequest, ResponseData<StockAvailabilityResponse>>
{
    public override void Configure()
    {
        ConfigureInventoryContract(InventoryEndpointContracts.Get<GetStockAvailabilityEndpoint>());
    }

    public override async Task HandleAsync(GetStockAvailabilityRequest req, CancellationToken ct)
    {
        var response = await sender.Send(new GetStockAvailabilityQuery(
            req.OrganizationId,
            req.EnvironmentId,
            req.SkuCode,
            req.UomCode,
            req.SiteCode,
            req.LocationCode,
            req.LotNo,
            req.SerialNo,
            req.QualityStatus,
            req.OwnerType,
            req.OwnerId), ct);
        await Send.OkAsync(response.AsResponseData(), cancellation: ct);
    }
}

public sealed class CreateStockCountTaskEndpoint(ISender sender)
    : InventoryEndpoint<CreateStockCountTaskRequest, ResponseData<CreateStockCountTaskResponse>>
{
    public override void Configure()
    {
        ConfigureInventoryContract(InventoryEndpointContracts.Get<CreateStockCountTaskEndpoint>());
    }

    public override async Task HandleAsync(CreateStockCountTaskRequest req, CancellationToken ct)
    {
        var result = await sender.Send(new CreateStockCountTaskCommand(
            req.OrganizationId,
            req.EnvironmentId,
            req.CountTaskCode,
            req.SkuCode,
            req.UomCode,
            req.SiteCode,
            req.LocationCode,
            req.LotNo,
            req.SerialNo,
            req.QualityStatus,
            req.OwnerType,
            req.OwnerId), ct);
        await Send.OkAsync(new CreateStockCountTaskResponse(result.CountTaskId, result.ExpectedLedgerVersion).AsResponseData(), cancellation: ct);
    }
}

public sealed class ConfirmStockCountAdjustmentEndpoint(ISender sender)
    : InventoryEndpoint<ConfirmStockCountAdjustmentRequest, ResponseData<ConfirmStockCountAdjustmentResponse>>
{
    public override void Configure()
    {
        ConfigureInventoryContract(InventoryEndpointContracts.Get<ConfirmStockCountAdjustmentEndpoint>());
    }

    public override async Task HandleAsync(ConfirmStockCountAdjustmentRequest req, CancellationToken ct)
    {
        var result = await sender.Send(new ConfirmStockCountAdjustmentCommand(
            req.CountTaskId,
            req.CountedQuantity,
            req.IdempotencyKey), ct);
        await Send.OkAsync(new ConfirmStockCountAdjustmentResponse(result.MovementId.ToString(), result.VarianceQuantity, result.OnHandQuantity).AsResponseData(), cancellation: ct);
    }
}

public sealed record InventoryEndpointContract(
    Type EndpointType,
    string HttpMethod,
    string Route,
    string PermissionCode,
    string AuthorizationPolicy,
    string OperationId);

public static class InventoryEndpointContracts
{
    public static readonly IReadOnlyCollection<InventoryEndpointContract> All =
    [
        new(typeof(CreateOrUpdateStockLocationEndpoint), "POST", "/api/inventory/v1/locations", InventoryPermissionCodes.LocationsManage, InternalServiceAuthorizationPolicy.Name, "createOrUpdateInventoryLocation"),
        new(typeof(PostStockMovementEndpoint), "POST", "/api/inventory/v1/movements", InventoryPermissionCodes.MovementsCreate, InternalServiceAuthorizationPolicy.Name, "postInventoryMovement"),
        new(typeof(GetStockAvailabilityEndpoint), "GET", "/api/inventory/v1/availability", InventoryPermissionCodes.LedgerRead, InternalServiceAuthorizationPolicy.Name, "getInventoryAvailability"),
        new(typeof(CreateStockCountTaskEndpoint), "POST", "/api/inventory/v1/count-tasks", InventoryPermissionCodes.CountsManage, InternalServiceAuthorizationPolicy.Name, "createInventoryCountTask"),
        new(typeof(ConfirmStockCountAdjustmentEndpoint), "POST", "/api/inventory/v1/count-tasks/{countTaskId}/adjustments", InventoryPermissionCodes.CountsManage, InternalServiceAuthorizationPolicy.Name, "confirmInventoryCountAdjustment"),
    ];

    public static InventoryEndpointContract Get<TEndpoint>()
    {
        return All.Single(x => x.EndpointType == typeof(TEndpoint));
    }

    public static bool TryGet(Type endpointType, [NotNullWhen(true)] out InventoryEndpointContract? contract)
    {
        contract = All.SingleOrDefault(x => x.EndpointType == endpointType);
        return contract is not null;
    }
}
