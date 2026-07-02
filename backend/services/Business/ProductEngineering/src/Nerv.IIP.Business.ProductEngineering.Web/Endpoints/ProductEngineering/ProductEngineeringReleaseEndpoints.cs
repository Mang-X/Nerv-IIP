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
    string? IdempotencyKey = null,
    string? ItemCode = null);

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
        var result = await sender.Send(new RegisterEngineeringDocumentCommand(req.OrganizationId, req.EnvironmentId, req.DocumentNumber, req.Revision, req.FileId, req.FileName, req.ContentType, req.DocumentType, req.IdempotencyKey, req.ItemCode), ct);
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

public sealed class ReleaseRoutingRequestValidator : Validator<ReleaseRoutingRequest>
{
    public ReleaseRoutingRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.RoutingCode).MaximumLength(100);
        RuleFor(x => x.Revision).NotEmpty().MaximumLength(50);
        RuleFor(x => x.SkuCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Operations).NotEmpty();
        RuleForEach(x => x.Operations).ChildRules(operation =>
        {
            operation.RuleFor(x => x.Sequence).GreaterThan(0);
            operation.RuleFor(x => x.WorkCenterCode).MaximumLength(100);
            operation.RuleFor(x => x.OperationCode).Must(value => !string.IsNullOrWhiteSpace(value)).MaximumLength(100);
            operation.RuleFor(x => x.OperationName).MaximumLength(200);
            operation.RuleFor(x => x.StandardMinutes).GreaterThanOrEqualTo(0);
        });
    }
}

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

public sealed record ListEngineeringDocumentsRequest(string OrganizationId, string EnvironmentId, string? ItemCode, string? DocumentType, int Skip = 0, int Take = 100);

public sealed class ListEngineeringDocumentsEndpoint(ISender sender)
    : ProductEngineeringEndpoint<ListEngineeringDocumentsRequest, ResponseData<ListEngineeringDocumentsResponse>>
{
    public override void Configure()
    {
        ConfigureProductEngineeringContract(ProductEngineeringEndpointContracts.Get<ListEngineeringDocumentsEndpoint>());
    }

    public override async Task HandleAsync(ListEngineeringDocumentsRequest req, CancellationToken ct)
    {
        var response = await sender.Send(new ListEngineeringDocumentsQuery(req.OrganizationId, req.EnvironmentId, req.ItemCode, req.DocumentType, req.Skip, req.Take), ct);
        await Send.OkAsync(response.AsResponseData(), ct);
    }
}

public sealed record GetEngineeringDocumentRequest(string OrganizationId, string EnvironmentId, string DocumentNumber, string Revision);

public sealed class GetEngineeringDocumentEndpoint(ISender sender)
    : ProductEngineeringEndpoint<GetEngineeringDocumentRequest, ResponseData<EngineeringDocumentItem>>
{
    public override void Configure()
    {
        ConfigureProductEngineeringContract(ProductEngineeringEndpointContracts.Get<GetEngineeringDocumentEndpoint>());
    }

    public override async Task HandleAsync(GetEngineeringDocumentRequest req, CancellationToken ct)
    {
        var response = await sender.Send(new GetEngineeringDocumentQuery(req.OrganizationId, req.EnvironmentId, req.DocumentNumber, req.Revision), ct);
        await Send.OkAsync(response.AsResponseData(), ct);
    }
}

public sealed record ListEngineeringItemsRequest(string OrganizationId, string EnvironmentId, string? ItemCode, string? Status, int Skip = 0, int Take = 100);

public sealed class ListEngineeringItemsEndpoint(ISender sender)
    : ProductEngineeringEndpoint<ListEngineeringItemsRequest, ResponseData<ListEngineeringItemsResponse>>
{
    public override void Configure()
    {
        ConfigureProductEngineeringContract(ProductEngineeringEndpointContracts.Get<ListEngineeringItemsEndpoint>());
    }

    public override async Task HandleAsync(ListEngineeringItemsRequest req, CancellationToken ct)
    {
        var response = await sender.Send(new ListEngineeringItemsQuery(req.OrganizationId, req.EnvironmentId, req.ItemCode, req.Status, req.Skip, req.Take), ct);
        await Send.OkAsync(response.AsResponseData(), ct);
    }
}

public sealed record GetEngineeringItemRequest(string OrganizationId, string EnvironmentId, string ItemCode, string Revision);

public sealed class GetEngineeringItemEndpoint(ISender sender)
    : ProductEngineeringEndpoint<GetEngineeringItemRequest, ResponseData<EngineeringItemRevisionItem>>
{
    public override void Configure()
    {
        ConfigureProductEngineeringContract(ProductEngineeringEndpointContracts.Get<GetEngineeringItemEndpoint>());
    }

    public override async Task HandleAsync(GetEngineeringItemRequest req, CancellationToken ct)
    {
        var response = await sender.Send(new GetEngineeringItemQuery(req.OrganizationId, req.EnvironmentId, req.ItemCode, req.Revision), ct);
        await Send.OkAsync(response.AsResponseData(), ct);
    }
}

public sealed record GetEngineeringBomRequest(string OrganizationId, string EnvironmentId, string BomCode, string Revision);

public sealed class GetEngineeringBomEndpoint(ISender sender)
    : ProductEngineeringEndpoint<GetEngineeringBomRequest, ResponseData<EngineeringBomListItem>>
{
    public override void Configure()
    {
        ConfigureProductEngineeringContract(ProductEngineeringEndpointContracts.Get<GetEngineeringBomEndpoint>());
    }

    public override async Task HandleAsync(GetEngineeringBomRequest req, CancellationToken ct)
    {
        var response = await sender.Send(new GetEngineeringBomQuery(req.OrganizationId, req.EnvironmentId, req.BomCode, req.Revision), ct);
        await Send.OkAsync(response.AsResponseData(), ct);
    }
}

public sealed record GetEngineeringBomExplosionRequest(
    string OrganizationId,
    string EnvironmentId,
    string ItemCode,
    DateOnly EffectiveDate,
    decimal LotSize = 1m,
    string? BomCode = null,
    string? Revision = null);

public sealed class GetEngineeringBomExplosionEndpoint(ISender sender)
    : ProductEngineeringEndpoint<GetEngineeringBomExplosionRequest, ResponseData<BomExplosionResponse>>
{
    public override void Configure()
    {
        ConfigureProductEngineeringContract(ProductEngineeringEndpointContracts.Get<GetEngineeringBomExplosionEndpoint>());
    }

    public override async Task HandleAsync(GetEngineeringBomExplosionRequest req, CancellationToken ct)
    {
        var response = await sender.Send(new GetEngineeringBomExplosionQuery(req.OrganizationId, req.EnvironmentId, req.ItemCode, req.EffectiveDate, req.LotSize, req.BomCode, req.Revision), ct);
        await Send.OkAsync(response.AsResponseData(), ct);
    }
}

public sealed record GetEngineeringBomWhereUsedRequest(
    string OrganizationId,
    string EnvironmentId,
    string ComponentCode,
    DateOnly EffectiveDate);

public sealed class GetEngineeringBomWhereUsedEndpoint(ISender sender)
    : ProductEngineeringEndpoint<GetEngineeringBomWhereUsedRequest, ResponseData<BomWhereUsedResponse>>
{
    public override void Configure()
    {
        ConfigureProductEngineeringContract(ProductEngineeringEndpointContracts.Get<GetEngineeringBomWhereUsedEndpoint>());
    }

    public override async Task HandleAsync(GetEngineeringBomWhereUsedRequest req, CancellationToken ct)
    {
        var response = await sender.Send(new GetEngineeringBomWhereUsedQuery(req.OrganizationId, req.EnvironmentId, req.ComponentCode, req.EffectiveDate), ct);
        await Send.OkAsync(response.AsResponseData(), ct);
    }
}

public sealed record GetManufacturingBomRequest(string OrganizationId, string EnvironmentId, string BomCode, string Revision);

public sealed class GetManufacturingBomEndpoint(ISender sender)
    : ProductEngineeringEndpoint<GetManufacturingBomRequest, ResponseData<ManufacturingBomListItem>>
{
    public override void Configure()
    {
        ConfigureProductEngineeringContract(ProductEngineeringEndpointContracts.Get<GetManufacturingBomEndpoint>());
    }

    public override async Task HandleAsync(GetManufacturingBomRequest req, CancellationToken ct)
    {
        var response = await sender.Send(new GetManufacturingBomQuery(req.OrganizationId, req.EnvironmentId, req.BomCode, req.Revision), ct);
        await Send.OkAsync(response.AsResponseData(), ct);
    }
}

public sealed record GetManufacturingBomExplosionRequest(
    string OrganizationId,
    string EnvironmentId,
    string SkuCode,
    DateOnly EffectiveDate,
    decimal LotSize = 1m,
    string? BomCode = null,
    string? Revision = null);

public sealed class GetManufacturingBomExplosionEndpoint(ISender sender)
    : ProductEngineeringEndpoint<GetManufacturingBomExplosionRequest, ResponseData<BomExplosionResponse>>
{
    public override void Configure()
    {
        ConfigureProductEngineeringContract(ProductEngineeringEndpointContracts.Get<GetManufacturingBomExplosionEndpoint>());
    }

    public override async Task HandleAsync(GetManufacturingBomExplosionRequest req, CancellationToken ct)
    {
        var response = await sender.Send(new GetManufacturingBomExplosionQuery(req.OrganizationId, req.EnvironmentId, req.SkuCode, req.EffectiveDate, req.LotSize, req.BomCode, req.Revision), ct);
        await Send.OkAsync(response.AsResponseData(), ct);
    }
}

public sealed record GetManufacturingBomWhereUsedRequest(
    string OrganizationId,
    string EnvironmentId,
    string ComponentCode,
    DateOnly EffectiveDate);

public sealed class GetManufacturingBomWhereUsedEndpoint(ISender sender)
    : ProductEngineeringEndpoint<GetManufacturingBomWhereUsedRequest, ResponseData<BomWhereUsedResponse>>
{
    public override void Configure()
    {
        ConfigureProductEngineeringContract(ProductEngineeringEndpointContracts.Get<GetManufacturingBomWhereUsedEndpoint>());
    }

    public override async Task HandleAsync(GetManufacturingBomWhereUsedRequest req, CancellationToken ct)
    {
        var response = await sender.Send(new GetManufacturingBomWhereUsedQuery(req.OrganizationId, req.EnvironmentId, req.ComponentCode, req.EffectiveDate), ct);
        await Send.OkAsync(response.AsResponseData(), ct);
    }
}

public sealed record GetRoutingRequest(string OrganizationId, string EnvironmentId, string RoutingCode, string Revision);

public sealed class GetRoutingEndpoint(ISender sender)
    : ProductEngineeringEndpoint<GetRoutingRequest, ResponseData<RoutingListItem>>
{
    public override void Configure()
    {
        ConfigureProductEngineeringContract(ProductEngineeringEndpointContracts.Get<GetRoutingEndpoint>());
    }

    public override async Task HandleAsync(GetRoutingRequest req, CancellationToken ct)
    {
        var response = await sender.Send(new GetRoutingQuery(req.OrganizationId, req.EnvironmentId, req.RoutingCode, req.Revision), ct);
        await Send.OkAsync(response.AsResponseData(), ct);
    }
}

public sealed record GetMasterDataWorkCenterUsageRequest(string OrganizationId, string EnvironmentId, string WorkCenterCode);

public sealed class GetMasterDataWorkCenterUsageEndpoint(ISender sender)
    : Endpoint<GetMasterDataWorkCenterUsageRequest, ResponseData<MasterDataWorkCenterUsageResponse>>
{
    public override void Configure()
    {
        Get("/api/business/v1/engineering/internal/master-data/work-centers/{workCenterCode}/usage");
        Tags("Business ProductEngineering");
        Policies(InternalServiceAuthorizationPolicy.Name);
    }

    public override async Task HandleAsync(GetMasterDataWorkCenterUsageRequest req, CancellationToken ct)
    {
        var response = await sender.Send(
            new GetMasterDataWorkCenterUsageQuery(req.OrganizationId, req.EnvironmentId, req.WorkCenterCode),
            ct);
        await Send.OkAsync(response.AsResponseData(), ct);
    }
}

public sealed record ListEngineeringChangesRequest(string OrganizationId, string EnvironmentId, string? Status, int Skip = 0, int Take = 100);

public sealed class ListEngineeringChangesEndpoint(ISender sender)
    : ProductEngineeringEndpoint<ListEngineeringChangesRequest, ResponseData<ListEngineeringChangesResponse>>
{
    public override void Configure()
    {
        ConfigureProductEngineeringContract(ProductEngineeringEndpointContracts.Get<ListEngineeringChangesEndpoint>());
    }

    public override async Task HandleAsync(ListEngineeringChangesRequest req, CancellationToken ct)
    {
        var response = await sender.Send(new ListEngineeringChangesQuery(req.OrganizationId, req.EnvironmentId, req.Status, req.Skip, req.Take), ct);
        await Send.OkAsync(response.AsResponseData(), ct);
    }
}

public sealed record GetEngineeringChangeRequest(string OrganizationId, string EnvironmentId, string ChangeNumber);

public sealed class GetEngineeringChangeEndpoint(ISender sender)
    : ProductEngineeringEndpoint<GetEngineeringChangeRequest, ResponseData<EngineeringChangeItem>>
{
    public override void Configure()
    {
        ConfigureProductEngineeringContract(ProductEngineeringEndpointContracts.Get<GetEngineeringChangeEndpoint>());
    }

    public override async Task HandleAsync(GetEngineeringChangeRequest req, CancellationToken ct)
    {
        var response = await sender.Send(new GetEngineeringChangeQuery(req.OrganizationId, req.EnvironmentId, req.ChangeNumber), ct);
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
        new(typeof(ListEngineeringDocumentsEndpoint), "GET", "/api/business/v1/engineering/documents", EngineeringPermissionCodes.DocumentsRead, "listBusinessEngineeringDocuments"),
        new(typeof(GetEngineeringDocumentEndpoint), "GET", "/api/business/v1/engineering/documents/{documentNumber}/{revision}", EngineeringPermissionCodes.DocumentsRead, "getBusinessEngineeringDocument"),
        new(typeof(CreateEngineeringItemRevisionEndpoint), "POST", "/api/business/v1/engineering/items", EngineeringPermissionCodes.ItemsManage, "createBusinessEngineeringItemRevision"),
        new(typeof(ListEngineeringItemsEndpoint), "GET", "/api/business/v1/engineering/items", EngineeringPermissionCodes.ItemsRead, "listBusinessEngineeringItems"),
        new(typeof(GetEngineeringItemEndpoint), "GET", "/api/business/v1/engineering/items/{itemCode}/{revision}", EngineeringPermissionCodes.ItemsRead, "getBusinessEngineeringItem"),
        new(typeof(ReleaseEngineeringBomEndpoint), "POST", "/api/business/v1/engineering/engineering-boms/release", EngineeringPermissionCodes.BomsManage, "releaseBusinessEngineeringBom"),
        new(typeof(GetEngineeringBomEndpoint), "GET", "/api/business/v1/engineering/engineering-boms/{bomCode}/{revision}", EngineeringPermissionCodes.BomsRead, "getBusinessEngineeringBom"),
        new(typeof(GetEngineeringBomExplosionEndpoint), "GET", "/api/business/v1/engineering/engineering-boms/explosion", EngineeringPermissionCodes.BomsRead, "getBusinessEngineeringBomExplosion"),
        new(typeof(GetEngineeringBomWhereUsedEndpoint), "GET", "/api/business/v1/engineering/engineering-boms/where-used", EngineeringPermissionCodes.BomsRead, "getBusinessEngineeringBomWhereUsed"),
        new(typeof(ReleaseManufacturingBomEndpoint), "POST", "/api/business/v1/engineering/manufacturing-boms/release", EngineeringPermissionCodes.BomsManage, "releaseBusinessManufacturingBom"),
        new(typeof(GetManufacturingBomEndpoint), "GET", "/api/business/v1/engineering/manufacturing-boms/{bomCode}/{revision}", EngineeringPermissionCodes.BomsRead, "getBusinessManufacturingBom"),
        new(typeof(GetManufacturingBomExplosionEndpoint), "GET", "/api/business/v1/engineering/manufacturing-boms/explosion", EngineeringPermissionCodes.BomsRead, "getBusinessManufacturingBomExplosion"),
        new(typeof(GetManufacturingBomWhereUsedEndpoint), "GET", "/api/business/v1/engineering/manufacturing-boms/where-used", EngineeringPermissionCodes.BomsRead, "getBusinessManufacturingBomWhereUsed"),
        new(typeof(ReleaseRoutingEndpoint), "POST", "/api/business/v1/engineering/routings/release", EngineeringPermissionCodes.RoutingsManage, "releaseBusinessRouting"),
        new(typeof(GetRoutingEndpoint), "GET", "/api/business/v1/engineering/routings/{routingCode}/{revision}", EngineeringPermissionCodes.RoutingsRead, "getBusinessRouting"),
        new(typeof(ReleaseEngineeringChangeEndpoint), "POST", "/api/business/v1/engineering/engineering-changes/release", EngineeringPermissionCodes.ChangesManage, "releaseBusinessEngineeringChange"),
        new(typeof(ListEngineeringChangesEndpoint), "GET", "/api/business/v1/engineering/engineering-changes", EngineeringPermissionCodes.ChangesRead, "listBusinessEngineeringChanges"),
        new(typeof(GetEngineeringChangeEndpoint), "GET", "/api/business/v1/engineering/engineering-changes/{changeNumber}", EngineeringPermissionCodes.ChangesRead, "getBusinessEngineeringChange"),
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
