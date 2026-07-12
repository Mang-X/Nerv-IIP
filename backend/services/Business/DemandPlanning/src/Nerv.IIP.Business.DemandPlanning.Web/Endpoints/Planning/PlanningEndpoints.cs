using System.Diagnostics.CodeAnalysis;
using FastEndpoints;
using Nerv.IIP.Business.DemandPlanning.Domain.AggregatesModel.DemandSourceAggregate;
using Nerv.IIP.Business.DemandPlanning.Domain.AggregatesModel.ForecastInputAggregate;
using Nerv.IIP.Business.DemandPlanning.Domain.AggregatesModel.MasterProductionScheduleAggregate;
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
            case "PUT":
                Put(contract.Route);
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
    string? SourceReference,
    string SkuCode,
    string UomCode,
    string SiteCode,
    decimal Quantity,
    DateOnly DueDate,
    string? IdempotencyKey = null);

public sealed record CreateOrUpdateDemandSourceResponse(string DemandSourceId);

public sealed record ListMasterProductionScheduleBucketsRequest(
    string OrganizationId,
    string EnvironmentId,
    string? SkuCode,
    string? SiteCode,
    DateOnly? FromDate,
    DateOnly? ToDate,
    MasterProductionScheduleStatus? Status);

public sealed record CreateMasterProductionScheduleBucketRequest(
    string OrganizationId,
    string EnvironmentId,
    string SkuCode,
    string UomCode,
    string SiteCode,
    DateOnly BucketDate,
    decimal Quantity);

public sealed record UpdateMasterProductionScheduleBucketRequest(
    [property: RouteParam] MasterProductionScheduleId MpsId,
    string OrganizationId,
    string EnvironmentId,
    string SkuCode,
    string UomCode,
    string SiteCode,
    DateOnly BucketDate,
    decimal Quantity);

public sealed record ReviewMasterProductionScheduleBucketRequest(
    [property: RouteParam] MasterProductionScheduleId MpsId,
    [property: QueryParam] string OrganizationId,
    [property: QueryParam] string EnvironmentId,
    string ReviewedBy);

public sealed record ReleaseMasterProductionScheduleBucketRequest(
    [property: RouteParam] MasterProductionScheduleId MpsId,
    [property: QueryParam] string OrganizationId,
    [property: QueryParam] string EnvironmentId,
    string ReleasedBy);

public sealed record ListDemandSourcesRequest(string OrganizationId, string EnvironmentId);

public sealed record CancelDemandSourceRequest(
    [property: RouteParam] DemandSourceId DemandSourceId,
    [property: QueryParam] string OrganizationId,
    [property: QueryParam] string EnvironmentId);

public sealed record CreateOrUpdateForecastInputRequest(
    string OrganizationId,
    string EnvironmentId,
    string ForecastReference,
    string SkuCode,
    string UomCode,
    string SiteCode,
    DateOnly PeriodStartDate,
    DateOnly PeriodEndDate,
    decimal Quantity,
    int BackwardConsumptionDays = 0,
    int ForwardConsumptionDays = 0);

public sealed record CreateOrUpdateForecastInputResponse(ForecastInputId ForecastInputId);

public sealed record ListForecastInputsRequest(
    string OrganizationId,
    string EnvironmentId,
    string? SkuCode,
    string? SiteCode,
    DateOnly? FromDate,
    DateOnly? ToDate);

public sealed record RunMrpRequest(string OrganizationId, string EnvironmentId, DateOnly HorizonStart, DateOnly HorizonEnd);

public sealed record RunMrpResponse(
    MrpRunId RunId,
    int SuggestionCount,
    bool HasInputDegradation,
    IReadOnlyCollection<string> InputDegradationSources,
    IReadOnlyCollection<string> InputSources,
    DateOnly? InputCoverageStart,
    DateOnly? InputCoverageEnd);

public sealed record ListMrpRunsRequest(string OrganizationId, string EnvironmentId);

public sealed record ListMrpPeggingRequest(MrpRunId RunId);

public sealed record ListPlanningSuggestionsRequest(string OrganizationId, string EnvironmentId, string? Status);

public sealed record AcceptPlanningSuggestionRequest(
    PlanningSuggestionId SuggestionId,
    string DownstreamService,
    string DownstreamDocumentType,
    string? DownstreamDocumentId,
    string? IdempotencyKey = null);

public sealed record AcceptPlanningSuggestionResponse(
    bool Accepted,
    string DownstreamService,
    string DownstreamDocumentType,
    string? DownstreamDocumentId);

public sealed class ListMasterProductionScheduleBucketsEndpoint(ISender sender)
    : DemandPlanningEndpoint<ListMasterProductionScheduleBucketsRequest, ResponseData<IReadOnlyCollection<MasterProductionScheduleBucketResponse>>>
{
    public override void Configure()
    {
        ConfigureDemandPlanningContract(DemandPlanningEndpointContracts.Get<ListMasterProductionScheduleBucketsEndpoint>());
    }

    public override async Task HandleAsync(ListMasterProductionScheduleBucketsRequest req, CancellationToken ct)
    {
        var response = await sender.Send(new ListMasterProductionScheduleBucketsQuery(
            req.OrganizationId,
            req.EnvironmentId,
            req.SkuCode,
            req.SiteCode,
            req.FromDate,
            req.ToDate,
            req.Status), ct);
        await Send.OkAsync(response.AsResponseData(), cancellation: ct);
    }
}

public sealed class CreateMasterProductionScheduleBucketEndpoint(ISender sender)
    : DemandPlanningEndpoint<CreateMasterProductionScheduleBucketRequest, ResponseData<MasterProductionScheduleBucketResponse>>
{
    public override void Configure()
    {
        ConfigureDemandPlanningContract(DemandPlanningEndpointContracts.Get<CreateMasterProductionScheduleBucketEndpoint>());
    }

    public override async Task HandleAsync(CreateMasterProductionScheduleBucketRequest req, CancellationToken ct)
    {
        var id = await sender.Send(new CreateMasterProductionScheduleBucketCommand(
            req.OrganizationId,
            req.EnvironmentId,
            req.SkuCode,
            req.UomCode,
            req.SiteCode,
            req.BucketDate,
            req.Quantity), ct);
        var response = await sender.Send(new ListMasterProductionScheduleBucketsQuery(
            req.OrganizationId,
            req.EnvironmentId,
            req.SkuCode,
            req.SiteCode,
            req.BucketDate,
            req.BucketDate), ct);
        await Send.OkAsync(response.Single(x => x.MpsId == id).AsResponseData(), cancellation: ct);
    }
}

public sealed class UpdateMasterProductionScheduleBucketEndpoint(ISender sender)
    : DemandPlanningEndpoint<UpdateMasterProductionScheduleBucketRequest, ResponseData<MasterProductionScheduleBucketResponse>>
{
    public override void Configure()
    {
        ConfigureDemandPlanningContract(DemandPlanningEndpointContracts.Get<UpdateMasterProductionScheduleBucketEndpoint>());
    }

    public override async Task HandleAsync(UpdateMasterProductionScheduleBucketRequest req, CancellationToken ct)
    {
        var mpsId = Route<MasterProductionScheduleId>("mpsId") ?? req.MpsId;
        await sender.Send(new UpdateMasterProductionScheduleBucketCommand(
            req.OrganizationId,
            req.EnvironmentId,
            mpsId,
            req.SkuCode,
            req.UomCode,
            req.SiteCode,
            req.BucketDate,
            req.Quantity), ct);
        await Send.OkAsync((await MasterProductionScheduleEndpointLoader.LoadBucketAsync(sender, req.OrganizationId, req.EnvironmentId, mpsId, ct)).AsResponseData(), cancellation: ct);
    }
}

public sealed class ReviewMasterProductionScheduleBucketEndpoint(ISender sender)
    : DemandPlanningEndpoint<ReviewMasterProductionScheduleBucketRequest, ResponseData<MasterProductionScheduleBucketResponse>>
{
    public override void Configure()
    {
        ConfigureDemandPlanningContract(DemandPlanningEndpointContracts.Get<ReviewMasterProductionScheduleBucketEndpoint>());
    }

    public override async Task HandleAsync(ReviewMasterProductionScheduleBucketRequest req, CancellationToken ct)
    {
        var mpsId = Route<MasterProductionScheduleId>("mpsId") ?? req.MpsId;
        await sender.Send(new ReviewMasterProductionScheduleBucketCommand(req.OrganizationId, req.EnvironmentId, mpsId, req.ReviewedBy), ct);
        await Send.OkAsync((await MasterProductionScheduleEndpointLoader.LoadBucketAsync(sender, req.OrganizationId, req.EnvironmentId, mpsId, ct)).AsResponseData(), cancellation: ct);
    }
}

public sealed class ReleaseMasterProductionScheduleBucketEndpoint(ISender sender)
    : DemandPlanningEndpoint<ReleaseMasterProductionScheduleBucketRequest, ResponseData<MasterProductionScheduleBucketResponse>>
{
    public override void Configure()
    {
        ConfigureDemandPlanningContract(DemandPlanningEndpointContracts.Get<ReleaseMasterProductionScheduleBucketEndpoint>());
    }

    public override async Task HandleAsync(ReleaseMasterProductionScheduleBucketRequest req, CancellationToken ct)
    {
        var mpsId = Route<MasterProductionScheduleId>("mpsId") ?? req.MpsId;
        await sender.Send(new ReleaseMasterProductionScheduleBucketCommand(req.OrganizationId, req.EnvironmentId, mpsId, req.ReleasedBy), ct);
        await Send.OkAsync((await MasterProductionScheduleEndpointLoader.LoadBucketAsync(sender, req.OrganizationId, req.EnvironmentId, mpsId, ct)).AsResponseData(), cancellation: ct);
    }
}

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
            req.DueDate,
            req.IdempotencyKey), ct);
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

public sealed class CancelDemandSourceEndpoint(ISender sender)
    : DemandPlanningEndpoint<CancelDemandSourceRequest, ResponseData<string>>
{
    public override void Configure()
    {
        ConfigureDemandPlanningContract(DemandPlanningEndpointContracts.Get<CancelDemandSourceEndpoint>());
    }

    public override async Task HandleAsync(CancelDemandSourceRequest req, CancellationToken ct)
    {
        var demandSourceId = Route<DemandSourceId>("demandSourceId") ?? req.DemandSourceId;
        await sender.Send(new CancelDemandSourceCommand(req.OrganizationId, req.EnvironmentId, demandSourceId), ct);
        await Send.OkAsync("cancelled".AsResponseData(), cancellation: ct);
    }
}

public sealed class CreateOrUpdateForecastInputEndpoint(ISender sender)
    : DemandPlanningEndpoint<CreateOrUpdateForecastInputRequest, ResponseData<CreateOrUpdateForecastInputResponse>>
{
    public override void Configure()
    {
        ConfigureDemandPlanningContract(DemandPlanningEndpointContracts.Get<CreateOrUpdateForecastInputEndpoint>());
    }

    public override async Task HandleAsync(CreateOrUpdateForecastInputRequest req, CancellationToken ct)
    {
        var id = await sender.Send(new CreateOrUpdateForecastInputCommand(
            req.OrganizationId,
            req.EnvironmentId,
            req.ForecastReference,
            req.SkuCode,
            req.UomCode,
            req.SiteCode,
            req.PeriodStartDate,
            req.PeriodEndDate,
            req.Quantity,
            req.BackwardConsumptionDays,
            req.ForwardConsumptionDays), ct);
        await Send.OkAsync(new CreateOrUpdateForecastInputResponse(id).AsResponseData(), cancellation: ct);
    }
}

public sealed class ListForecastInputsEndpoint(ISender sender)
    : DemandPlanningEndpoint<ListForecastInputsRequest, ResponseData<IReadOnlyCollection<ForecastInputResponse>>>
{
    public override void Configure()
    {
        ConfigureDemandPlanningContract(DemandPlanningEndpointContracts.Get<ListForecastInputsEndpoint>());
    }

    public override async Task HandleAsync(ListForecastInputsRequest req, CancellationToken ct)
    {
        var response = await sender.Send(new ListForecastInputsQuery(
            req.OrganizationId,
            req.EnvironmentId,
            req.SkuCode,
            req.SiteCode,
            req.FromDate,
            req.ToDate), ct);
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
        await Send.OkAsync(new RunMrpResponse(
            result.RunId,
            result.SuggestionCount,
            result.HasInputDegradation,
            result.InputDegradationSources,
            result.InputSources,
            result.InputCoverageStart,
            result.InputCoverageEnd).AsResponseData(), cancellation: ct);
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
    : DemandPlanningEndpoint<AcceptPlanningSuggestionRequest, ResponseData<AcceptPlanningSuggestionResponse>>
{
    public override void Configure()
    {
        ConfigureDemandPlanningContract(DemandPlanningEndpointContracts.Get<AcceptPlanningSuggestionEndpoint>());
    }

    public override async Task HandleAsync(AcceptPlanningSuggestionRequest req, CancellationToken ct)
    {
        var result = await sender.Send(new AcceptPlanningSuggestionCommand(
            req.SuggestionId,
            req.DownstreamService,
            req.DownstreamDocumentType,
            req.DownstreamDocumentId,
            req.IdempotencyKey), ct);
        await Send.OkAsync(new AcceptPlanningSuggestionResponse(
            true,
            result.DownstreamService,
            result.DownstreamDocumentType,
            result.DownstreamDocumentId).AsResponseData(), cancellation: ct);
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
        new(typeof(ListMasterProductionScheduleBucketsEndpoint), "GET", "/api/business/v1/planning/mps", DemandPlanningPermissionCodes.MpsRead, InternalServiceAuthorizationPolicy.Name, "listPlanningMpsBuckets"),
        new(typeof(CreateMasterProductionScheduleBucketEndpoint), "POST", "/api/business/v1/planning/mps", DemandPlanningPermissionCodes.MpsManage, InternalServiceAuthorizationPolicy.Name, "createPlanningMpsBucket"),
        new(typeof(UpdateMasterProductionScheduleBucketEndpoint), "PUT", "/api/business/v1/planning/mps/{mpsId}", DemandPlanningPermissionCodes.MpsManage, InternalServiceAuthorizationPolicy.Name, "updatePlanningMpsBucket"),
        new(typeof(ReviewMasterProductionScheduleBucketEndpoint), "POST", "/api/business/v1/planning/mps/{mpsId}/review", DemandPlanningPermissionCodes.MpsManage, InternalServiceAuthorizationPolicy.Name, "reviewPlanningMpsBucket"),
        new(typeof(ReleaseMasterProductionScheduleBucketEndpoint), "POST", "/api/business/v1/planning/mps/{mpsId}/release", DemandPlanningPermissionCodes.MpsRelease, InternalServiceAuthorizationPolicy.Name, "releasePlanningMpsBucket"),
        new(typeof(CreateOrUpdateDemandSourceEndpoint), "POST", "/api/business/v1/planning/demands", DemandPlanningPermissionCodes.DemandsManage, InternalServiceAuthorizationPolicy.Name, "createOrUpdatePlanningDemand"),
        new(typeof(ListDemandSourcesEndpoint), "GET", "/api/business/v1/planning/demands", DemandPlanningPermissionCodes.DemandsRead, InternalServiceAuthorizationPolicy.Name, "listPlanningDemands"),
        new(typeof(CancelDemandSourceEndpoint), "POST", "/api/business/v1/planning/demands/{demandSourceId}/cancel", DemandPlanningPermissionCodes.DemandsManage, InternalServiceAuthorizationPolicy.Name, "cancelPlanningDemand"),
        new(typeof(CreateOrUpdateForecastInputEndpoint), "POST", "/api/business/v1/planning/forecasts", DemandPlanningPermissionCodes.DemandsManage, InternalServiceAuthorizationPolicy.Name, "createOrUpdatePlanningForecast"),
        new(typeof(ListForecastInputsEndpoint), "GET", "/api/business/v1/planning/forecasts", DemandPlanningPermissionCodes.DemandsRead, InternalServiceAuthorizationPolicy.Name, "listPlanningForecasts"),
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

file static class MasterProductionScheduleEndpointLoader
{
    public static async Task<MasterProductionScheduleBucketResponse> LoadBucketAsync(
        ISender sender,
        string organizationId,
        string environmentId,
        MasterProductionScheduleId mpsId,
        CancellationToken cancellationToken)
    {
        var buckets = await sender.Send(new ListMasterProductionScheduleBucketsQuery(
            organizationId,
            environmentId,
            null,
            null,
            null,
            null), cancellationToken);
        return buckets.Single(x => x.MpsId == mpsId);
    }
}
