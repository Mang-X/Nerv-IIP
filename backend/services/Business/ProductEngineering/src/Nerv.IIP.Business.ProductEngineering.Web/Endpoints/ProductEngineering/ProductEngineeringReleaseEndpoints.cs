using System.Diagnostics.CodeAnalysis;
using FastEndpoints;
using Nerv.IIP.Business.ProductEngineering.Web.Application.Auth;
using Nerv.IIP.Business.ProductEngineering.Web.Application.Commands;
using Nerv.IIP.Business.ProductEngineering.Web.Application.Queries;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.Business.ProductEngineering.Web.Endpoints.ProductEngineering;

public abstract class ProductEngineeringEndpoint<TRequest, TResponse> : Endpoint<TRequest, TResponse>
    where TRequest : notnull
{
    protected void ConfigureProductEngineeringContract(ProductEngineeringEndpointContract contract)
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
                throw new NotSupportedException($"HTTP method '{contract.HttpMethod}' is not supported by ProductEngineering endpoints.");
        }

        Tags("Business ProductEngineering");
        Policies(InternalServiceAuthorizationPolicy.Name);
    }
}

public sealed record RegisterEngineeringDocumentRequest(
    string OrganizationId,
    string EnvironmentId,
    string? DocumentNumber,
    string Revision,
    string FileId,
    string FileName,
    string ContentType,
    string DocumentType,
    string? IdempotencyKey = null);

public sealed record EntityResponse(string Id);

public sealed class RegisterEngineeringDocumentEndpoint(ISender sender)
    : ProductEngineeringEndpoint<RegisterEngineeringDocumentRequest, ResponseData<EntityResponse>>
{
    public override void Configure()
    {
        ConfigureProductEngineeringContract(ProductEngineeringEndpointContracts.Get<RegisterEngineeringDocumentEndpoint>());
    }

    public override async Task HandleAsync(RegisterEngineeringDocumentRequest req, CancellationToken ct)
    {
        var result = await sender.Send(new RegisterEngineeringDocumentCommand(req.OrganizationId, req.EnvironmentId, req.DocumentNumber, req.Revision, req.FileId, req.FileName, req.ContentType, req.DocumentType, req.IdempotencyKey), ct);
        await Send.OkAsync(new EntityResponse(result.Id).AsResponseData(), ct);
    }
}

public sealed record CreateEngineeringItemRevisionRequest(
    string OrganizationId,
    string EnvironmentId,
    string? ItemCode,
    string Revision,
    string Name,
    bool Release,
    string? IdempotencyKey = null);

public sealed class CreateEngineeringItemRevisionEndpoint(ISender sender)
    : ProductEngineeringEndpoint<CreateEngineeringItemRevisionRequest, ResponseData<EntityResponse>>
{
    public override void Configure()
    {
        ConfigureProductEngineeringContract(ProductEngineeringEndpointContracts.Get<CreateEngineeringItemRevisionEndpoint>());
    }

    public override async Task HandleAsync(CreateEngineeringItemRevisionRequest req, CancellationToken ct)
    {
        var result = await sender.Send(new CreateEngineeringItemRevisionCommand(req.OrganizationId, req.EnvironmentId, req.ItemCode, req.Revision, req.Name, req.Release, req.IdempotencyKey), ct);
        await Send.OkAsync(new EntityResponse(result.Id).AsResponseData(), ct);
    }
}

public sealed record ReleaseEngineeringBomRequest(
    string OrganizationId,
    string EnvironmentId,
    string? BomCode,
    string Revision,
    string ParentItemCode,
    DateOnly EffectiveDate,
    IReadOnlyCollection<BomLineCommand> Lines,
    string? IdempotencyKey = null);

public sealed class ReleaseEngineeringBomEndpoint(ISender sender)
    : ProductEngineeringEndpoint<ReleaseEngineeringBomRequest, ResponseData<EntityResponse>>
{
    public override void Configure()
    {
        ConfigureProductEngineeringContract(ProductEngineeringEndpointContracts.Get<ReleaseEngineeringBomEndpoint>());
    }

    public override async Task HandleAsync(ReleaseEngineeringBomRequest req, CancellationToken ct)
    {
        var result = await sender.Send(new ReleaseEngineeringBomCommand(req.OrganizationId, req.EnvironmentId, req.BomCode, req.Revision, req.ParentItemCode, req.EffectiveDate, req.Lines, req.IdempotencyKey), ct);
        await Send.OkAsync(new EntityResponse(result.Id).AsResponseData(), ct);
    }
}

public sealed record ReleaseManufacturingBomRequest(
    string OrganizationId,
    string EnvironmentId,
    string? BomCode,
    string Revision,
    string SkuCode,
    string EngineeringBomCode,
    string EngineeringBomRevision,
    DateOnly EffectiveDate,
    IReadOnlyCollection<ManufacturingBomMaterialLineCommand> MaterialLines,
    IReadOnlyCollection<RecipeLineCommand> RecipeLines,
    string? IdempotencyKey = null);

public sealed class ReleaseManufacturingBomEndpoint(ISender sender)
    : ProductEngineeringEndpoint<ReleaseManufacturingBomRequest, ResponseData<EntityResponse>>
{
    public override void Configure()
    {
        ConfigureProductEngineeringContract(ProductEngineeringEndpointContracts.Get<ReleaseManufacturingBomEndpoint>());
    }

    public override async Task HandleAsync(ReleaseManufacturingBomRequest req, CancellationToken ct)
    {
        var result = await sender.Send(new ReleaseManufacturingBomCommand(req.OrganizationId, req.EnvironmentId, req.BomCode, req.Revision, req.SkuCode, req.EngineeringBomCode, req.EngineeringBomRevision, req.EffectiveDate, req.MaterialLines, req.RecipeLines, req.IdempotencyKey), ct);
        await Send.OkAsync(new EntityResponse(result.Id).AsResponseData(), ct);
    }
}

public sealed record ReleaseRoutingRequest(
    string OrganizationId,
    string EnvironmentId,
    string? RoutingCode,
    string Revision,
    string SkuCode,
    DateOnly EffectiveDate,
    IReadOnlyCollection<RoutingOperationCommand> Operations,
    string? IdempotencyKey = null);

public sealed class ReleaseRoutingEndpoint(ISender sender)
    : ProductEngineeringEndpoint<ReleaseRoutingRequest, ResponseData<EntityResponse>>
{
    public override void Configure()
    {
        ConfigureProductEngineeringContract(ProductEngineeringEndpointContracts.Get<ReleaseRoutingEndpoint>());
    }

    public override async Task HandleAsync(ReleaseRoutingRequest req, CancellationToken ct)
    {
        var result = await sender.Send(new ReleaseRoutingCommand(req.OrganizationId, req.EnvironmentId, req.RoutingCode, req.Revision, req.SkuCode, req.EffectiveDate, req.Operations, req.IdempotencyKey), ct);
        await Send.OkAsync(new EntityResponse(result.Id).AsResponseData(), ct);
    }
}

public sealed record ReleaseEngineeringChangeRequest(
    string OrganizationId,
    string EnvironmentId,
    string? ChangeNumber,
    string Reason,
    string ApprovalReferenceId,
    DateOnly EffectiveDate,
    IReadOnlyCollection<AffectedVersionCommand> AffectedVersions,
    string? IdempotencyKey = null);

public sealed class ReleaseEngineeringChangeEndpoint(ISender sender)
    : ProductEngineeringEndpoint<ReleaseEngineeringChangeRequest, ResponseData<EntityResponse>>
{
    public override void Configure()
    {
        ConfigureProductEngineeringContract(ProductEngineeringEndpointContracts.Get<ReleaseEngineeringChangeEndpoint>());
    }

    public override async Task HandleAsync(ReleaseEngineeringChangeRequest req, CancellationToken ct)
    {
        var result = await sender.Send(new ReleaseEngineeringChangeCommand(req.OrganizationId, req.EnvironmentId, req.ChangeNumber, req.Reason, req.ApprovalReferenceId, req.EffectiveDate, req.AffectedVersions, req.IdempotencyKey), ct);
        await Send.OkAsync(new EntityResponse(result.Id).AsResponseData(), ct);
    }
}

public sealed record ListEngineeringBomsRequest(string OrganizationId, string EnvironmentId, string? ParentItemCode, string? Status, int Skip = 0, int Take = 100);

public sealed class ListEngineeringBomsEndpoint(ISender sender)
    : ProductEngineeringEndpoint<ListEngineeringBomsRequest, ResponseData<ListEngineeringBomsResponse>>
{
    public override void Configure()
    {
        ConfigureProductEngineeringContract(ProductEngineeringEndpointContracts.Get<ListEngineeringBomsEndpoint>());
    }

    public override async Task HandleAsync(ListEngineeringBomsRequest req, CancellationToken ct)
    {
        var response = await sender.Send(new ListEngineeringBomsQuery(req.OrganizationId, req.EnvironmentId, req.ParentItemCode, req.Status, req.Skip, req.Take), ct);
        await Send.OkAsync(response.AsResponseData(), ct);
    }
}

public sealed record ListManufacturingBomsRequest(string OrganizationId, string EnvironmentId, string? SkuCode, string? Status, int Skip = 0, int Take = 100);

public sealed class ListManufacturingBomsEndpoint(ISender sender)
    : ProductEngineeringEndpoint<ListManufacturingBomsRequest, ResponseData<ListManufacturingBomsResponse>>
{
    public override void Configure()
    {
        ConfigureProductEngineeringContract(ProductEngineeringEndpointContracts.Get<ListManufacturingBomsEndpoint>());
    }

    public override async Task HandleAsync(ListManufacturingBomsRequest req, CancellationToken ct)
    {
        var response = await sender.Send(new ListManufacturingBomsQuery(req.OrganizationId, req.EnvironmentId, req.SkuCode, req.Status, req.Skip, req.Take), ct);
        await Send.OkAsync(response.AsResponseData(), ct);
    }
}

public sealed record ListRoutingsRequest(string OrganizationId, string EnvironmentId, string? SkuCode, string? Status, int Skip = 0, int Take = 100);

public sealed class ListRoutingsEndpoint(ISender sender)
    : ProductEngineeringEndpoint<ListRoutingsRequest, ResponseData<ListRoutingsResponse>>
{
    public override void Configure()
    {
        ConfigureProductEngineeringContract(ProductEngineeringEndpointContracts.Get<ListRoutingsEndpoint>());
    }

    public override async Task HandleAsync(ListRoutingsRequest req, CancellationToken ct)
    {
        var response = await sender.Send(new ListRoutingsQuery(req.OrganizationId, req.EnvironmentId, req.SkuCode, req.Status, req.Skip, req.Take), ct);
        await Send.OkAsync(response.AsResponseData(), ct);
    }
}

public sealed record ProductEngineeringEndpointContract(
    Type EndpointType,
    string HttpMethod,
    string Route,
    string PermissionCode,
    string OperationId);

public static class ProductEngineeringEndpointContracts
{
    public static readonly IReadOnlyCollection<ProductEngineeringEndpointContract> All =
    [
        new(typeof(RegisterEngineeringDocumentEndpoint), "POST", "/api/business/v1/engineering/documents", EngineeringPermissionCodes.DocumentsManage, "registerBusinessEngineeringDocument"),
        new(typeof(CreateEngineeringItemRevisionEndpoint), "POST", "/api/business/v1/engineering/items", EngineeringPermissionCodes.ItemsManage, "createBusinessEngineeringItemRevision"),
        new(typeof(ReleaseEngineeringBomEndpoint), "POST", "/api/business/v1/engineering/engineering-boms/release", EngineeringPermissionCodes.BomsManage, "releaseBusinessEngineeringBom"),
        new(typeof(ReleaseManufacturingBomEndpoint), "POST", "/api/business/v1/engineering/manufacturing-boms/release", EngineeringPermissionCodes.BomsManage, "releaseBusinessManufacturingBom"),
        new(typeof(ReleaseRoutingEndpoint), "POST", "/api/business/v1/engineering/routings/release", EngineeringPermissionCodes.RoutingsManage, "releaseBusinessRouting"),
        new(typeof(ReleaseEngineeringChangeEndpoint), "POST", "/api/business/v1/engineering/engineering-changes/release", EngineeringPermissionCodes.ChangesManage, "releaseBusinessEngineeringChange"),
        new(typeof(ListEngineeringBomsEndpoint), "GET", "/api/business/v1/engineering/engineering-boms", EngineeringPermissionCodes.BomsRead, "listBusinessEngineeringBoms"),
        new(typeof(ListManufacturingBomsEndpoint), "GET", "/api/business/v1/engineering/manufacturing-boms", EngineeringPermissionCodes.BomsRead, "listBusinessManufacturingBoms"),
        new(typeof(ListRoutingsEndpoint), "GET", "/api/business/v1/engineering/routings", EngineeringPermissionCodes.RoutingsRead, "listBusinessRoutings"),
    ];

    public static ProductEngineeringEndpointContract Get<TEndpoint>()
    {
        return All.Single(x => x.EndpointType == typeof(TEndpoint));
    }

    public static bool TryGet(Type endpointType, [NotNullWhen(true)] out ProductEngineeringEndpointContract? contract)
    {
        contract = All.SingleOrDefault(x => x.EndpointType == endpointType);
        return contract is not null;
    }
}
