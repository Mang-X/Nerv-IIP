using System.Diagnostics.CodeAnalysis;
using FastEndpoints;
using Nerv.IIP.Business.ProductEngineering.Web.Application.Auth;
using Nerv.IIP.Business.ProductEngineering.Web.Application.Commands.StandardOperations;
using Nerv.IIP.Business.ProductEngineering.Web.Application.Queries.StandardOperations;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.Business.ProductEngineering.Web.Endpoints.StandardOperations;

public abstract class StandardOperationEndpoint<TRequest, TResponse> : Endpoint<TRequest, TResponse>
    where TRequest : notnull
{
    protected void ConfigureStandardOperationContract(StandardOperationEndpointContract contract)
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
                throw new NotSupportedException($"HTTP method '{contract.HttpMethod}' is not supported by StandardOperation endpoints.");
        }

        Tags("Business ProductEngineering");
        Policies(InternalServiceAuthorizationPolicy.Name);
    }
}

public sealed record ListStandardOperationsRequest(
    string OrganizationId,
    string EnvironmentId,
    bool? Enabled,
    string? Search,
    int Skip = 0,
    int Take = 100);

public sealed class ListStandardOperationsEndpoint(ISender sender)
    : StandardOperationEndpoint<ListStandardOperationsRequest, ResponseData<ListStandardOperationsResponse>>
{
    public override void Configure()
    {
        ConfigureStandardOperationContract(StandardOperationEndpointContracts.Get<ListStandardOperationsEndpoint>());
    }

    public override async Task HandleAsync(ListStandardOperationsRequest req, CancellationToken ct)
    {
        var response = await sender.Send(new ListStandardOperationsQuery(req.OrganizationId, req.EnvironmentId, req.Enabled, req.Search, req.Skip, req.Take), ct);
        await Send.OkAsync(response.AsResponseData(), ct);
    }
}

public sealed record GetStandardOperationRequest(string OrganizationId, string EnvironmentId, string OperationCode);

public sealed class GetStandardOperationEndpoint(ISender sender)
    : StandardOperationEndpoint<GetStandardOperationRequest, ResponseData<StandardOperationItem>>
{
    public override void Configure()
    {
        ConfigureStandardOperationContract(StandardOperationEndpointContracts.Get<GetStandardOperationEndpoint>());
    }

    public override async Task HandleAsync(GetStandardOperationRequest req, CancellationToken ct)
    {
        var response = await sender.Send(new GetStandardOperationQuery(req.OrganizationId, req.EnvironmentId, req.OperationCode), ct);
        await Send.OkAsync(response.AsResponseData(), ct);
    }
}

public sealed record StandardOperationResponse(string OperationCode);

public sealed record CreateStandardOperationRequest(
    string OrganizationId,
    string EnvironmentId,
    string OperationCode,
    string OperationName,
    string DefaultWorkCenterCode,
    int StandardSetupMinutes,
    int StandardRunMinutes,
    string ControlKey,
    bool RequiresReporting,
    bool RequiresQualityInspection,
    bool IsOutsourced,
    string? Description);

public sealed class CreateStandardOperationEndpoint(ISender sender)
    : StandardOperationEndpoint<CreateStandardOperationRequest, ResponseData<StandardOperationResponse>>
{
    public override void Configure()
    {
        ConfigureStandardOperationContract(StandardOperationEndpointContracts.Get<CreateStandardOperationEndpoint>());
    }

    public override async Task HandleAsync(CreateStandardOperationRequest req, CancellationToken ct)
    {
        var result = await sender.Send(new CreateStandardOperationCommand(
            req.OrganizationId,
            req.EnvironmentId,
            req.OperationCode,
            req.OperationName,
            req.DefaultWorkCenterCode,
            req.StandardSetupMinutes,
            req.StandardRunMinutes,
            req.ControlKey,
            req.RequiresReporting,
            req.RequiresQualityInspection,
            req.IsOutsourced,
            req.Description), ct);
        await Send.OkAsync(new StandardOperationResponse(result.OperationCode).AsResponseData(), ct);
    }
}

public sealed record UpdateStandardOperationRequest(
    string OrganizationId,
    string EnvironmentId,
    string OperationCode,
    string OperationName,
    string DefaultWorkCenterCode,
    int StandardSetupMinutes,
    int StandardRunMinutes,
    string ControlKey,
    bool RequiresReporting,
    bool RequiresQualityInspection,
    bool IsOutsourced,
    string? Description);

public sealed class UpdateStandardOperationEndpoint(ISender sender)
    : StandardOperationEndpoint<UpdateStandardOperationRequest, ResponseData<StandardOperationResponse>>
{
    public override void Configure()
    {
        ConfigureStandardOperationContract(StandardOperationEndpointContracts.Get<UpdateStandardOperationEndpoint>());
    }

    public override async Task HandleAsync(UpdateStandardOperationRequest req, CancellationToken ct)
    {
        var result = await sender.Send(new UpdateStandardOperationCommand(
            req.OrganizationId,
            req.EnvironmentId,
            req.OperationCode,
            req.OperationName,
            req.DefaultWorkCenterCode,
            req.StandardSetupMinutes,
            req.StandardRunMinutes,
            req.ControlKey,
            req.RequiresReporting,
            req.RequiresQualityInspection,
            req.IsOutsourced,
            req.Description), ct);
        await Send.OkAsync(new StandardOperationResponse(result.OperationCode).AsResponseData(), ct);
    }
}

public sealed record ArchiveStandardOperationRequest(string OrganizationId, string EnvironmentId, string OperationCode, string Reason);

public sealed class ArchiveStandardOperationEndpoint(ISender sender)
    : StandardOperationEndpoint<ArchiveStandardOperationRequest, ResponseData<object>>
{
    public override void Configure()
    {
        ConfigureStandardOperationContract(StandardOperationEndpointContracts.Get<ArchiveStandardOperationEndpoint>());
    }

    public override async Task HandleAsync(ArchiveStandardOperationRequest req, CancellationToken ct)
    {
        await sender.Send(new ArchiveStandardOperationCommand(req.OrganizationId, req.EnvironmentId, req.OperationCode, req.Reason), ct);
        await Send.OkAsync(new object().AsResponseData(), ct);
    }
}

public sealed record StandardOperationEndpointContract(
    Type EndpointType,
    string HttpMethod,
    string Route,
    string PermissionCode,
    string OperationId);

public static class StandardOperationEndpointContracts
{
    public static readonly IReadOnlyCollection<StandardOperationEndpointContract> All =
    [
        new(typeof(ListStandardOperationsEndpoint), "GET", "/api/business/v1/engineering/standard-operations", EngineeringPermissionCodes.StandardOperationsRead, "listBusinessStandardOperations"),
        new(typeof(GetStandardOperationEndpoint), "GET", "/api/business/v1/engineering/standard-operations/{operationCode}", EngineeringPermissionCodes.StandardOperationsRead, "getBusinessStandardOperation"),
        new(typeof(CreateStandardOperationEndpoint), "POST", "/api/business/v1/engineering/standard-operations", EngineeringPermissionCodes.StandardOperationsManage, "createBusinessStandardOperation"),
        new(typeof(UpdateStandardOperationEndpoint), "PUT", "/api/business/v1/engineering/standard-operations/{operationCode}", EngineeringPermissionCodes.StandardOperationsManage, "updateBusinessStandardOperation"),
        new(typeof(ArchiveStandardOperationEndpoint), "POST", "/api/business/v1/engineering/standard-operations/{operationCode}/archive", EngineeringPermissionCodes.StandardOperationsManage, "archiveBusinessStandardOperation"),
    ];

    public static StandardOperationEndpointContract Get<TEndpoint>()
    {
        return All.Single(x => x.EndpointType == typeof(TEndpoint));
    }

    public static bool TryGet(Type endpointType, [NotNullWhen(true)] out StandardOperationEndpointContract? contract)
    {
        contract = All.SingleOrDefault(x => x.EndpointType == endpointType);
        return contract is not null;
    }
}
