using FastEndpoints;
using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.ProductionVersionAggregate;
using Nerv.IIP.Business.ProductEngineering.Web.Application.Auth;
using Nerv.IIP.Business.ProductEngineering.Web.Application.Commands.ProductionVersions;
using Nerv.IIP.Business.ProductEngineering.Web.Application.Queries.ProductionVersions;
using Nerv.IIP.Contracts.ProductEngineering;
using Nerv.IIP.ServiceAuth;
using NetCorePal.Extensions.Dto;
using System.Diagnostics.CodeAnalysis;

namespace Nerv.IIP.Business.ProductEngineering.Web.Endpoints.ProductionVersions;

public abstract class ProductionVersionEndpoint<TRequest, TResponse> : Endpoint<TRequest, TResponse>
    where TRequest : notnull
{
    protected void ConfigureProductionVersionContract(ProductionVersionEndpointContract contract)
    {
        switch (contract.HttpMethod)
        {
            case "GET":
                Get(contract.Route);
                break;
            case "POST":
                Post(contract.Route);
                break;
            case "PUT":
                Put(contract.Route);
                break;
            default:
                throw new NotSupportedException($"HTTP method '{contract.HttpMethod}' is not supported by ProductionVersion endpoints.");
        }

        Tags("Business ProductEngineering");
        Policies(InternalServiceAuthorizationPolicy.Name);
    }
}

public sealed record CreateProductionVersionRequest(
    string OrganizationId,
    string EnvironmentId,
    string SkuCode,
    string MbomVersionId,
    string RoutingVersionId,
    DateOnly ValidFrom,
    DateOnly? ValidTo,
    decimal? LotSizeMin,
    decimal? LotSizeMax,
    int Priority,
    bool IsDefault);

public sealed record CreateProductionVersionResponse(string ProductionVersionId);

public sealed class CreateProductionVersionEndpoint(ISender sender)
    : ProductionVersionEndpoint<CreateProductionVersionRequest, ResponseData<CreateProductionVersionResponse>>
{
    public override void Configure()
    {
        var contract = ProductionVersionEndpointContracts.Get<CreateProductionVersionEndpoint>();
        ConfigureProductionVersionContract(contract);
    }

    public override async Task HandleAsync(CreateProductionVersionRequest req, CancellationToken ct)
    {
        var result = await sender.Send(new CreateProductionVersionCommand(
            req.OrganizationId,
            req.EnvironmentId,
            req.SkuCode,
            req.MbomVersionId,
            req.RoutingVersionId,
            req.ValidFrom,
            req.ValidTo,
            req.LotSizeMin,
            req.LotSizeMax,
            req.Priority,
            req.IsDefault), ct);
        await Send.OkAsync(new CreateProductionVersionResponse(result.ProductionVersionId).AsResponseData(), ct);
    }
}

public sealed record UpdateProductionVersionRequest(
    string OrganizationId,
    string EnvironmentId,
    string ProductionVersionId,
    string MbomVersionId,
    string RoutingVersionId,
    DateOnly ValidFrom,
    DateOnly? ValidTo,
    decimal? LotSizeMin,
    decimal? LotSizeMax,
    int Priority,
    bool IsDefault);

public sealed class UpdateProductionVersionEndpoint(ISender sender)
    : ProductionVersionEndpoint<UpdateProductionVersionRequest, ResponseData<CreateProductionVersionResponse>>
{
    public override void Configure()
    {
        var contract = ProductionVersionEndpointContracts.Get<UpdateProductionVersionEndpoint>();
        ConfigureProductionVersionContract(contract);
    }

    public override async Task HandleAsync(UpdateProductionVersionRequest req, CancellationToken ct)
    {
        var result = await sender.Send(new UpdateProductionVersionCommand(
            req.OrganizationId,
            req.EnvironmentId,
            req.ProductionVersionId,
            req.MbomVersionId,
            req.RoutingVersionId,
            req.ValidFrom,
            req.ValidTo,
            req.LotSizeMin,
            req.LotSizeMax,
            req.Priority,
            req.IsDefault), ct);
        await Send.OkAsync(new CreateProductionVersionResponse(result.ProductionVersionId).AsResponseData(), ct);
    }
}

public sealed record ArchiveProductionVersionRequest(
    string OrganizationId,
    string EnvironmentId,
    string ProductionVersionId,
    string Reason);

public sealed class ArchiveProductionVersionEndpoint(ISender sender)
    : ProductionVersionEndpoint<ArchiveProductionVersionRequest, ResponseData<object>>
{
    public override void Configure()
    {
        var contract = ProductionVersionEndpointContracts.Get<ArchiveProductionVersionEndpoint>();
        ConfigureProductionVersionContract(contract);
    }

    public override async Task HandleAsync(ArchiveProductionVersionRequest req, CancellationToken ct)
    {
        await sender.Send(new ArchiveProductionVersionCommand(req.OrganizationId, req.EnvironmentId, req.ProductionVersionId, req.Reason), ct);
        await Send.OkAsync(new object().AsResponseData(), ct);
    }
}

public sealed record ListProductionVersionsRequest(
    string OrganizationId,
    string EnvironmentId,
    string? SkuCode,
    string? Status,
    int Skip = 0,
    int Take = 100);

public sealed class ListProductionVersionsEndpoint(ISender sender)
    : ProductionVersionEndpoint<ListProductionVersionsRequest, ResponseData<ListProductionVersionsResponse>>
{
    public override void Configure()
    {
        var contract = ProductionVersionEndpointContracts.Get<ListProductionVersionsEndpoint>();
        ConfigureProductionVersionContract(contract);
    }

    public override async Task HandleAsync(ListProductionVersionsRequest req, CancellationToken ct)
    {
        var response = await sender.Send(new ListProductionVersionsQuery(req.OrganizationId, req.EnvironmentId, req.SkuCode, req.Status, req.Skip, req.Take), ct);
        await Send.OkAsync(response.AsResponseData(), ct);
    }
}

public sealed class ResolveProductionVersionEndpoint(ISender sender)
    : ProductionVersionEndpoint<ResolveProductionVersionRequest, ResponseData<ResolveProductionVersionResponse>>
{
    public override void Configure()
    {
        var contract = ProductionVersionEndpointContracts.Get<ResolveProductionVersionEndpoint>();
        ConfigureProductionVersionContract(contract);
    }

    public override async Task HandleAsync(ResolveProductionVersionRequest req, CancellationToken ct)
    {
        var response = await sender.Send(
            new ResolveProductionVersionQuery(req.OrganizationId, req.EnvironmentId, req.SkuCode, req.EffectiveDate, req.LotSize),
            ct);
        await Send.OkAsync(response.AsResponseData(), ct);
    }
}

public sealed record ProductionVersionEndpointContract(
    Type EndpointType,
    string HttpMethod,
    string Route,
    string PermissionCode,
    string OperationId);

public static class ProductionVersionEndpointContracts
{
    public static readonly IReadOnlyCollection<ProductionVersionEndpointContract> All =
    [
        new(typeof(ListProductionVersionsEndpoint), "GET", "/api/business/v1/engineering/production-versions", EngineeringPermissionCodes.ProductionVersionsRead, "listBusinessProductionVersions"),
        new(typeof(ResolveProductionVersionEndpoint), "GET", "/api/business/v1/engineering/production-versions/resolve", EngineeringPermissionCodes.ProductionVersionsRead, "resolveBusinessProductionVersion"),
        new(typeof(CreateProductionVersionEndpoint), "POST", "/api/business/v1/engineering/production-versions", EngineeringPermissionCodes.ProductionVersionsManage, "createBusinessProductionVersion"),
        new(typeof(UpdateProductionVersionEndpoint), "PUT", "/api/business/v1/engineering/production-versions/{productionVersionId}", EngineeringPermissionCodes.ProductionVersionsManage, "updateBusinessProductionVersion"),
        new(typeof(ArchiveProductionVersionEndpoint), "POST", "/api/business/v1/engineering/production-versions/{productionVersionId}/archive", EngineeringPermissionCodes.ProductionVersionsManage, "archiveBusinessProductionVersion"),
    ];

    public static ProductionVersionEndpointContract Get<TEndpoint>()
    {
        return All.Single(x => x.EndpointType == typeof(TEndpoint));
    }

    public static bool TryGet(Type endpointType, [NotNullWhen(true)] out ProductionVersionEndpointContract? contract)
    {
        contract = All.SingleOrDefault(x => x.EndpointType == endpointType);
        return contract is not null;
    }
}
