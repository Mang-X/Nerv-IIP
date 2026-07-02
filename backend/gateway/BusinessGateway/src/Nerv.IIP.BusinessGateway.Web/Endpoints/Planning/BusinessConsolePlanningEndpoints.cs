using FastEndpoints;
using FluentValidation;
using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.BusinessGateway.Web.Application.OpenApi;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.BusinessGateway.Web.Endpoints.Planning;

[Tags("Business Console Planning")]
[HttpGet("/api/business-console/v1/planning/demands")]
[BusinessGatewayOperationId("listBusinessConsolePlanningDemands")]
public sealed class ListBusinessConsolePlanningDemandsEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessPlanningClient planning,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsolePlanningContextRequest, BusinessConsoleDemandSourceListResponse>(
        auth,
        BusinessGatewayPermissions.PlanningDemandsRead)
{
    protected override string OrganizationId(BusinessConsolePlanningContextRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsolePlanningContextRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleDemandSourceListResponse> ForwardAsync(
        BusinessConsolePlanningContextRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        planning.ListDemandSourcesAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console Planning")]
[HttpPost("/api/business-console/v1/planning/demands")]
[BusinessGatewayOperationId("createOrUpdateBusinessConsolePlanningDemand")]
public sealed class CreateOrUpdateBusinessConsolePlanningDemandEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessPlanningClient planning,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleCreateOrUpdateDemandSourceRequest, BusinessConsoleDemandSourceResponse>(
        auth,
        BusinessGatewayPermissions.PlanningDemandsManage)
{
    protected override string OrganizationId(BusinessConsoleCreateOrUpdateDemandSourceRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleCreateOrUpdateDemandSourceRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleDemandSourceResponse> ForwardAsync(
        BusinessConsoleCreateOrUpdateDemandSourceRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        planning.CreateOrUpdateDemandSourceAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console Planning")]
[HttpPost("/api/business-console/v1/planning/demands/{demandSourceId}/cancel")]
[BusinessGatewayOperationId("cancelBusinessConsolePlanningDemand")]
public sealed class CancelBusinessConsolePlanningDemandEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessPlanningClient planning,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsolePlanningDemandCancelRequest, BusinessConsoleAcceptedResponse>(
        auth,
        BusinessGatewayPermissions.PlanningDemandsManage)
{
    protected override string OrganizationId(BusinessConsolePlanningDemandCancelRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsolePlanningDemandCancelRequest request) => request.EnvironmentId;

    protected override string ResourceType(BusinessConsolePlanningDemandCancelRequest request) => "planning-demand";

    protected override string? ResourceId(BusinessConsolePlanningDemandCancelRequest request) =>
        Route<string>("demandSourceId") ?? request.DemandSourceId;

    protected override Task<BusinessConsoleAcceptedResponse> ForwardAsync(
        BusinessConsolePlanningDemandCancelRequest request,
        string bearerToken,
        CancellationToken cancellationToken)
    {
        var demandSourceId = Route<string>("demandSourceId") ?? request.DemandSourceId;
        return planning.CancelDemandSourceAsync(
            tokenProvider.BearerToken,
            demandSourceId,
            request with { DemandSourceId = demandSourceId },
            cancellationToken);
    }
}

[Tags("Business Console Planning")]
[HttpPost("/api/business-console/v1/planning/mrp-runs")]
[BusinessGatewayOperationId("runBusinessConsolePlanningMrp")]
public sealed class RunBusinessConsolePlanningMrpEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessPlanningClient planning,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleRunMrpRequest, BusinessConsoleRunMrpResponse>(
        auth,
        BusinessGatewayPermissions.PlanningMrpRun)
{
    protected override string OrganizationId(BusinessConsoleRunMrpRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleRunMrpRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleRunMrpResponse> ForwardAsync(
        BusinessConsoleRunMrpRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        planning.RunMrpAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console Planning")]
[HttpGet("/api/business-console/v1/planning/mrp-runs")]
[BusinessGatewayOperationId("listBusinessConsolePlanningMrpRuns")]
public sealed class ListBusinessConsolePlanningMrpRunsEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessPlanningClient planning,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsolePlanningContextRequest, BusinessConsoleMrpRunListResponse>(
        auth,
        BusinessGatewayPermissions.PlanningMrpRead)
{
    protected override string OrganizationId(BusinessConsolePlanningContextRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsolePlanningContextRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleMrpRunListResponse> ForwardAsync(
        BusinessConsolePlanningContextRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        planning.ListMrpRunsAsync(tokenProvider.BearerToken, request, cancellationToken);
}

public sealed record BusinessConsoleMrpPeggingRequest(
    [property: RouteParam] string RunId,
    [property: QueryParam] string OrganizationId,
    [property: QueryParam] string EnvironmentId);

[Tags("Business Console Planning")]
[HttpGet("/api/business-console/v1/planning/mrp-runs/{runId}/pegging")]
[BusinessGatewayOperationId("getBusinessConsolePlanningMrpPegging")]
public sealed class GetBusinessConsolePlanningMrpPeggingEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessPlanningClient planning,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleMrpPeggingRequest, BusinessConsoleMrpPeggingListResponse>(
        auth,
        BusinessGatewayPermissions.PlanningMrpRead)
{
    protected override string OrganizationId(BusinessConsoleMrpPeggingRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleMrpPeggingRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleMrpPeggingListResponse> ForwardAsync(
        BusinessConsoleMrpPeggingRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        planning.ListMrpPeggingAsync(tokenProvider.BearerToken, Route<string>("runId") ?? request.RunId, cancellationToken);
}

[Tags("Business Console Planning")]
[HttpGet("/api/business-console/v1/planning/suggestions")]
[BusinessGatewayOperationId("listBusinessConsolePlanningSuggestions")]
public sealed class ListBusinessConsolePlanningSuggestionsEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessPlanningClient planning,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsolePlanningSuggestionListRequest, BusinessConsolePlanningSuggestionListResponse>(
        auth,
        // Suggestions are the public read surface of an MRP run, so they intentionally share the same read permission.
        BusinessGatewayPermissions.PlanningMrpRead)
{
    protected override string OrganizationId(BusinessConsolePlanningSuggestionListRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsolePlanningSuggestionListRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsolePlanningSuggestionListResponse> ForwardAsync(
        BusinessConsolePlanningSuggestionListRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        planning.ListSuggestionsAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console Planning")]
[HttpPost("/api/business-console/v1/planning/suggestions/{suggestionId}/accept")]
[BusinessGatewayOperationId("acceptBusinessConsolePlanningSuggestion")]
public sealed class AcceptBusinessConsolePlanningSuggestionEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessPlanningClient planning,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleAcceptPlanningSuggestionRequest, BusinessConsoleAcceptedResponse>(
        auth,
        BusinessGatewayPermissions.PlanningSuggestionsManage)
{
    protected override string OrganizationId(BusinessConsoleAcceptPlanningSuggestionRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleAcceptPlanningSuggestionRequest request) => request.EnvironmentId;

    protected override string ResourceType(BusinessConsoleAcceptPlanningSuggestionRequest request) => "planning-suggestion";

    protected override string? ResourceId(BusinessConsoleAcceptPlanningSuggestionRequest request) => Route<string>("suggestionId") ?? request.SuggestionId;

    protected override Task<BusinessConsoleAcceptedResponse> ForwardAsync(
        BusinessConsoleAcceptPlanningSuggestionRequest request,
        string bearerToken,
        CancellationToken cancellationToken)
    {
        var suggestionId = Route<string>("suggestionId") ?? request.SuggestionId;
        return planning.AcceptSuggestionAsync(tokenProvider.BearerToken, suggestionId, request with { SuggestionId = suggestionId }, cancellationToken);
    }
}

public sealed class BusinessConsoleCreateOrUpdateDemandSourceRequestValidator
    : Validator<BusinessConsoleCreateOrUpdateDemandSourceRequest>
{
    public BusinessConsoleCreateOrUpdateDemandSourceRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.DemandType).NotEmpty().MaximumLength(50);
        RuleFor(x => x.SourceReference).MaximumLength(150);
        RuleFor(x => x.SkuCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.UomCode).NotEmpty().MaximumLength(50);
        RuleFor(x => x.SiteCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Quantity).GreaterThan(0);
        RuleFor(x => x.IdempotencyKey).MaximumLength(150);
    }
}

public sealed class BusinessConsoleRunMrpRequestValidator : Validator<BusinessConsoleRunMrpRequest>
{
    public BusinessConsoleRunMrpRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.HorizonEnd).GreaterThanOrEqualTo(x => x.HorizonStart);
    }
}

public sealed class BusinessConsolePlanningDemandCancelRequestValidator
    : Validator<BusinessConsolePlanningDemandCancelRequest>
{
    public BusinessConsolePlanningDemandCancelRequestValidator()
    {
        RuleFor(x => x.DemandSourceId).NotEmpty().MaximumLength(150);
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
    }
}

public sealed class BusinessConsoleAcceptPlanningSuggestionRequestValidator
    : Validator<BusinessConsoleAcceptPlanningSuggestionRequest>
{
    public BusinessConsoleAcceptPlanningSuggestionRequestValidator()
    {
        RuleFor(x => x.SuggestionId).NotEmpty().MaximumLength(150);
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.DownstreamService).NotEmpty().MaximumLength(100);
        RuleFor(x => x.DownstreamDocumentType).NotEmpty().MaximumLength(100);
        RuleFor(x => x.DownstreamDocumentId).MaximumLength(150);
        RuleFor(x => x.IdempotencyKey).MaximumLength(150);
    }
}
