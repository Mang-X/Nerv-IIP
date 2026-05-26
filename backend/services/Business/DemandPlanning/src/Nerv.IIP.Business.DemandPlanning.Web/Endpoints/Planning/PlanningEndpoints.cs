using System.Diagnostics.CodeAnalysis;
using FastEndpoints;
using Nerv.IIP.Business.DemandPlanning.Domain.AggregatesModel.MrpRunAggregate;
using Nerv.IIP.Business.DemandPlanning.Domain.AggregatesModel.PlanningSuggestionAggregate;
using Nerv.IIP.Business.DemandPlanning.Web.Application.Auth;
using Nerv.IIP.Business.DemandPlanning.Web.Application.Commands;
using Nerv.IIP.Business.DemandPlanning.Web.Application.Queries;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.Business.DemandPlanning.Web.Endpoints.Planning;

public abstract class DemandPlanningEndpoint<TRequest, TResponse> : Endpoint<TRequest, TResponse>
    where TRequest : notnull
{
    protected void ConfigureDemandPlanningContract(DemandPlanningEndpointContract contract)
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
                throw new NotSupportedException($"HTTP method '{contract.HttpMethod}' is not supported by DemandPlanning endpoints.");
        }

        Tags("Business Demand Planning");
        Policies(contract.AuthorizationPolicy);
    }
}

public sealed record CreateOrUpdateDemandSourceRequest(
    string OrganizationId,
    string EnvironmentId,
    string DemandType,
    string SourceReference,
    string SkuCode,
    string UomCode,
    string SiteCode,
    decimal Quantity,
    DateOnly DueDate);

public sealed record CreateOrUpdateDemandSourceResponse(string DemandSourceId);

public sealed record ListDemandSourcesRequest(string OrganizationId, string EnvironmentId);

public sealed record RunMrpRequest(string OrganizationId, string EnvironmentId, DateOnly HorizonStart, DateOnly HorizonEnd);

public sealed record RunMrpResponse(MrpRunId RunId, int SuggestionCount);

public sealed record ListMrpRunsRequest(string OrganizationId, string EnvironmentId);

public sealed record ListMrpPeggingRequest(MrpRunId RunId);

public sealed record ListPlanningSuggestionsRequest(string OrganizationId, string EnvironmentId, string? Status);

public sealed record AcceptPlanningSuggestionRequest(
    PlanningSuggestionId SuggestionId,
    string DownstreamService,
    string DownstreamDocumentType,
    string DownstreamDocumentId);

public sealed class CreateOrUpdateDemandSourceEndpoint(ISender sender)
    : DemandPlanningEndpoint<CreateOrUpdateDemandSourceRequest, ResponseData<CreateOrUpdateDemandSourceResponse>>
{
    public override void Configure()
    {
        ConfigureDemandPlanningContract(DemandPlanningEndpointContracts.Get<CreateOrUpdateDemandSourceEndpoint>());
    }

    public override async Task HandleAsync(CreateOrUpdateDemandSourceRequest req, CancellationToken ct)
    {
        var id = await sender.Send(new CreateOrUpdateDemandSourceCommand(
            req.OrganizationId,
            req.EnvironmentId,
            req.DemandType,
            req.SourceReference,
            req.SkuCode,
            req.UomCode,
            req.SiteCode,
            req.Quantity,
            req.DueDate), ct);
        await Send.OkAsync(new CreateOrUpdateDemandSourceResponse(id.ToString()).AsResponseData(), cancellation: ct);
    }
}

public sealed class ListDemandSourcesEndpoint(ISender sender)
    : DemandPlanningEndpoint<ListDemandSourcesRequest, ResponseData<IReadOnlyCollection<DemandSourceResponse>>>
{
    public override void Configure()
    {
        ConfigureDemandPlanningContract(DemandPlanningEndpointContracts.Get<ListDemandSourcesEndpoint>());
    }

    public override async Task HandleAsync(ListDemandSourcesRequest req, CancellationToken ct)
    {
        var response = await sender.Send(new ListDemandSourcesQuery(req.OrganizationId, req.EnvironmentId), ct);
        await Send.OkAsync(response.AsResponseData(), cancellation: ct);
    }
}

public sealed class RunMrpEndpoint(ISender sender)
    : DemandPlanningEndpoint<RunMrpRequest, ResponseData<RunMrpResponse>>
{
    public override void Configure()
    {
        ConfigureDemandPlanningContract(DemandPlanningEndpointContracts.Get<RunMrpEndpoint>());
    }

    public override async Task HandleAsync(RunMrpRequest req, CancellationToken ct)
    {
        var result = await sender.Send(new RunMrpCommand(req.OrganizationId, req.EnvironmentId, req.HorizonStart, req.HorizonEnd), ct);
        await Send.OkAsync(new RunMrpResponse(result.RunId, result.SuggestionCount).AsResponseData(), cancellation: ct);
    }
}

public sealed class ListMrpRunsEndpoint(ISender sender)
    : DemandPlanningEndpoint<ListMrpRunsRequest, ResponseData<IReadOnlyCollection<MrpRunResponse>>>
{
    public override void Configure()
    {
        ConfigureDemandPlanningContract(DemandPlanningEndpointContracts.Get<ListMrpRunsEndpoint>());
    }

    public override async Task HandleAsync(ListMrpRunsRequest req, CancellationToken ct)
    {
        var response = await sender.Send(new ListMrpRunsQuery(req.OrganizationId, req.EnvironmentId), ct);
        await Send.OkAsync(response.AsResponseData(), cancellation: ct);
    }
}

public sealed class ListMrpPeggingEndpoint(ISender sender)
    : DemandPlanningEndpoint<ListMrpPeggingRequest, ResponseData<IReadOnlyCollection<PeggingLinkResponse>>>
{
    public override void Configure()
    {
        ConfigureDemandPlanningContract(DemandPlanningEndpointContracts.Get<ListMrpPeggingEndpoint>());
    }

    public override async Task HandleAsync(ListMrpPeggingRequest req, CancellationToken ct)
    {
        var response = await sender.Send(new ListMrpPeggingQuery(req.RunId), ct);
        await Send.OkAsync(response.AsResponseData(), cancellation: ct);
    }
}

public sealed class ListPlanningSuggestionsEndpoint(ISender sender)
    : DemandPlanningEndpoint<ListPlanningSuggestionsRequest, ResponseData<IReadOnlyCollection<PlanningSuggestionResponse>>>
{
    public override void Configure()
    {
        ConfigureDemandPlanningContract(DemandPlanningEndpointContracts.Get<ListPlanningSuggestionsEndpoint>());
    }

    public override async Task HandleAsync(ListPlanningSuggestionsRequest req, CancellationToken ct)
    {
        var response = await sender.Send(new ListPlanningSuggestionsQuery(req.OrganizationId, req.EnvironmentId, req.Status), ct);
        await Send.OkAsync(response.AsResponseData(), cancellation: ct);
    }
}

public sealed class AcceptPlanningSuggestionEndpoint(ISender sender)
    : DemandPlanningEndpoint<AcceptPlanningSuggestionRequest, ResponseData<string>>
{
    public override void Configure()
    {
        ConfigureDemandPlanningContract(DemandPlanningEndpointContracts.Get<AcceptPlanningSuggestionEndpoint>());
    }

    public override async Task HandleAsync(AcceptPlanningSuggestionRequest req, CancellationToken ct)
    {
        await sender.Send(new AcceptPlanningSuggestionCommand(
            req.SuggestionId,
            req.DownstreamService,
            req.DownstreamDocumentType,
            req.DownstreamDocumentId), ct);
        await Send.OkAsync("accepted".AsResponseData(), cancellation: ct);
    }
}

public sealed record DemandPlanningEndpointContract(
    Type EndpointType,
    string HttpMethod,
    string Route,
    string PermissionCode,
    string AuthorizationPolicy,
    string OperationId);

public static class DemandPlanningEndpointContracts
{
    public static readonly IReadOnlyCollection<DemandPlanningEndpointContract> All =
    [
        new(typeof(CreateOrUpdateDemandSourceEndpoint), "POST", "/api/business/v1/planning/demands", DemandPlanningPermissionCodes.DemandsManage, InternalServiceAuthorizationPolicy.Name, "createOrUpdatePlanningDemand"),
        new(typeof(ListDemandSourcesEndpoint), "GET", "/api/business/v1/planning/demands", DemandPlanningPermissionCodes.DemandsRead, InternalServiceAuthorizationPolicy.Name, "listPlanningDemands"),
        new(typeof(RunMrpEndpoint), "POST", "/api/business/v1/planning/mrp-runs", DemandPlanningPermissionCodes.MrpRun, InternalServiceAuthorizationPolicy.Name, "runPlanningMrp"),
        new(typeof(ListMrpRunsEndpoint), "GET", "/api/business/v1/planning/mrp-runs", DemandPlanningPermissionCodes.MrpRead, InternalServiceAuthorizationPolicy.Name, "listPlanningMrpRuns"),
        new(typeof(ListMrpPeggingEndpoint), "GET", "/api/business/v1/planning/mrp-runs/{runId}/pegging", DemandPlanningPermissionCodes.MrpRead, InternalServiceAuthorizationPolicy.Name, "getPlanningMrpPegging"),
        new(typeof(ListPlanningSuggestionsEndpoint), "GET", "/api/business/v1/planning/suggestions", DemandPlanningPermissionCodes.MrpRead, InternalServiceAuthorizationPolicy.Name, "listPlanningSuggestions"),
        new(typeof(AcceptPlanningSuggestionEndpoint), "POST", "/api/business/v1/planning/suggestions/{suggestionId}/accept", DemandPlanningPermissionCodes.SuggestionsManage, InternalServiceAuthorizationPolicy.Name, "acceptPlanningSuggestion"),
    ];

    public static DemandPlanningEndpointContract Get<TEndpoint>()
    {
        return All.Single(x => x.EndpointType == typeof(TEndpoint));
    }

    public static bool TryGet(Type endpointType, [NotNullWhen(true)] out DemandPlanningEndpointContract? contract)
    {
        contract = All.SingleOrDefault(x => x.EndpointType == endpointType);
        return contract is not null;
    }
}
