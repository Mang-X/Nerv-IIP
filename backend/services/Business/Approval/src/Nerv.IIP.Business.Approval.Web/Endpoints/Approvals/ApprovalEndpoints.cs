using System.Diagnostics.CodeAnalysis;
using FastEndpoints;
using Nerv.IIP.Business.Approval.Domain.AggregatesModel.ApprovalChainAggregate;
using Nerv.IIP.Business.Approval.Domain.AggregatesModel.ApprovalDelegationAggregate;
using Nerv.IIP.Business.Approval.Domain.AggregatesModel.ApprovalTemplateAggregate;
using Nerv.IIP.Business.Approval.Web.Application.Auth;
using Nerv.IIP.Business.Approval.Web.Application.Commands.Chains;
using Nerv.IIP.Business.Approval.Web.Application.Commands.Delegations;
using Nerv.IIP.Business.Approval.Web.Application.Commands.Templates;
using Nerv.IIP.Business.Approval.Web.Application.Queries.Chains;
using Nerv.IIP.Business.Approval.Web.Application.Queries.Delegations;
using Nerv.IIP.Business.Approval.Web.Application.Queries.Templates;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.Business.Approval.Web.Endpoints.Approvals;

public abstract class ApprovalEndpoint<TRequest, TResponse> : Endpoint<TRequest, TResponse>
    where TRequest : notnull
{
    protected void ConfigureApprovalContract(ApprovalEndpointContract contract)
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
                throw new NotSupportedException($"HTTP method '{contract.HttpMethod}' is not supported by Approval endpoints.");
        }

        Tags("Business Approval");
        Policies(contract.AuthorizationPolicy);
    }
}

public sealed record CreateOrUpdateApprovalTemplateRequest(
    string OrganizationId,
    string EnvironmentId,
    string TemplateCode,
    string DocumentType,
    int Version,
    bool IsActive,
    IReadOnlyCollection<ApprovalTemplateStepRequest> Steps);

public sealed record ApprovalTemplateStepRequest(
    int StepNo,
    string StepName,
    string? ParallelGroupKey,
    string ApproverType,
    string ApproverRef,
    int? DueInHours,
    string? CompletionPolicy = null,
    string? ConditionExpression = null);

public sealed record CreateOrUpdateApprovalTemplateResponse(ApprovalTemplateId TemplateId);

public sealed record ListApprovalTemplatesRequest(
    string? OrganizationId,
    string? EnvironmentId,
    string? DocumentType,
    bool? IsActive,
    int Skip = 0,
    int Take = 100);

public sealed record ListApprovalChainsRequest(
    string OrganizationId,
    string EnvironmentId,
    string? Status,
    string? StartedBy,
    string? SourceService,
    string? DocumentType,
    string? DocumentId,
    int Skip = 0,
    int Take = 100);

public sealed record StartApprovalChainRequest(
    string OrganizationId,
    string EnvironmentId,
    string TemplateCode,
    string SourceService,
    string DocumentType,
    string DocumentId,
    string? DocumentLineId,
    string StartedBy);

public sealed record StartApprovalChainResponse(ApprovalChainId ChainId);

public sealed record GetApprovalChainRequest(ApprovalChainId ChainId);

public sealed record ListPendingApprovalTasksRequest(
    string OrganizationId,
    string EnvironmentId,
    string ActorType,
    string ActorRef,
    int Skip = 0,
    int Take = 100);

public sealed record CheckOverdueApprovalStepsRequest(
    string OrganizationId,
    string EnvironmentId);

public sealed record CheckOverdueApprovalStepsResponse(int MarkedCount);

public sealed record ListApprovalDecisionsRequest(
    string OrganizationId,
    string EnvironmentId,
    string? ChainId,
    string? ActorType,
    string? ActorRef,
    string? Decision,
    string? DocumentType,
    string? DocumentId,
    int Skip = 0,
    int Take = 100);

public sealed record ResolveApprovalStepRequest(
    ApprovalChainId ChainId,
    int StepNo,
    string ActorType,
    string ActorRef,
    string Decision,
    string? Comment);

public sealed record ResolveApprovalStepResponse(ApprovalDecisionId DecisionId);

public sealed record ListApprovalDelegationsRequest(
    string OrganizationId,
    string EnvironmentId,
    string? Status,
    string? DelegatorActorRef,
    string? DelegateActorRef,
    string? DocumentType,
    int Skip = 0,
    int Take = 100);

public sealed record CreateApprovalDelegationRequest(
    string OrganizationId,
    string EnvironmentId,
    string DelegatorActorType,
    string DelegatorActorRef,
    string DelegateActorType,
    string DelegateActorRef,
    string? DocumentType,
    DateTimeOffset EffectiveFromUtc,
    DateTimeOffset EffectiveToUtc,
    string? Reason,
    string CreatedBy);

public sealed record CreateApprovalDelegationResponse(ApprovalDelegationId DelegationId);

public sealed record RevokeApprovalDelegationRequest(
    ApprovalDelegationId DelegationId,
    string OrganizationId,
    string EnvironmentId,
    string RevokedBy);

public sealed record RevokeApprovalDelegationResponse(bool Accepted);

public sealed class CreateOrUpdateApprovalTemplateEndpoint(ISender sender)
    : ApprovalEndpoint<CreateOrUpdateApprovalTemplateRequest, ResponseData<CreateOrUpdateApprovalTemplateResponse>>
{
    public override void Configure()
    {
        ConfigureApprovalContract(ApprovalEndpointContracts.Get<CreateOrUpdateApprovalTemplateEndpoint>());
    }

    public override async Task HandleAsync(CreateOrUpdateApprovalTemplateRequest req, CancellationToken ct)
    {
        var templateId = await sender.Send(new CreateOrUpdateApprovalTemplateCommand(
            req.OrganizationId,
            req.EnvironmentId,
            req.TemplateCode,
            req.DocumentType,
            req.Version,
            req.IsActive,
            req.Steps.Select(x => new ApprovalTemplateStepInput(
                x.StepNo,
                x.StepName,
                x.ParallelGroupKey,
                x.ApproverType,
                x.ApproverRef,
                x.DueInHours,
                x.CompletionPolicy,
                x.ConditionExpression)).ToArray()), ct);
        await Send.OkAsync(new CreateOrUpdateApprovalTemplateResponse(templateId).AsResponseData(), cancellation: ct);
    }
}

public sealed class ListApprovalTemplatesEndpoint(ISender sender)
    : ApprovalEndpoint<ListApprovalTemplatesRequest, ResponseData<ApprovalTemplateListResponse>>
{
    public override void Configure()
    {
        ConfigureApprovalContract(ApprovalEndpointContracts.Get<ListApprovalTemplatesEndpoint>());
    }

    public override async Task HandleAsync(ListApprovalTemplatesRequest req, CancellationToken ct)
    {
        var response = await sender.Send(new ListApprovalTemplatesQuery(req.OrganizationId, req.EnvironmentId, req.DocumentType, req.IsActive, req.Skip, req.Take), ct);
        await Send.OkAsync(response.AsResponseData(), cancellation: ct);
    }
}

public sealed class ListApprovalChainsEndpoint(ISender sender)
    : ApprovalEndpoint<ListApprovalChainsRequest, ResponseData<ApprovalChainListResponse>>
{
    public override void Configure()
    {
        ConfigureApprovalContract(ApprovalEndpointContracts.Get<ListApprovalChainsEndpoint>());
    }

    public override async Task HandleAsync(ListApprovalChainsRequest req, CancellationToken ct)
    {
        var response = await sender.Send(new ListApprovalChainsQuery(
            req.OrganizationId,
            req.EnvironmentId,
            req.Status,
            req.StartedBy,
            req.SourceService,
            req.DocumentType,
            req.DocumentId,
            req.Skip,
            req.Take), ct);
        await Send.OkAsync(response.AsResponseData(), cancellation: ct);
    }
}

public sealed class StartApprovalChainEndpoint(ISender sender)
    : ApprovalEndpoint<StartApprovalChainRequest, ResponseData<StartApprovalChainResponse>>
{
    public override void Configure()
    {
        ConfigureApprovalContract(ApprovalEndpointContracts.Get<StartApprovalChainEndpoint>());
    }

    public override async Task HandleAsync(StartApprovalChainRequest req, CancellationToken ct)
    {
        var chainId = await sender.Send(new StartApprovalChainCommand(
            req.OrganizationId,
            req.EnvironmentId,
            req.TemplateCode,
            req.SourceService,
            req.DocumentType,
            req.DocumentId,
            req.DocumentLineId,
            req.StartedBy), ct);
        await Send.OkAsync(new StartApprovalChainResponse(chainId).AsResponseData(), cancellation: ct);
    }
}

public sealed class GetApprovalChainEndpoint(ISender sender)
    : ApprovalEndpoint<GetApprovalChainRequest, ResponseData<ApprovalChainResponse>>
{
    public override void Configure()
    {
        ConfigureApprovalContract(ApprovalEndpointContracts.Get<GetApprovalChainEndpoint>());
    }

    public override async Task HandleAsync(GetApprovalChainRequest req, CancellationToken ct)
    {
        var response = await sender.Send(new GetApprovalChainQuery(req.ChainId), ct);
        await Send.OkAsync(response.AsResponseData(), cancellation: ct);
    }
}

public sealed class ListPendingApprovalTasksEndpoint(ISender sender)
    : ApprovalEndpoint<ListPendingApprovalTasksRequest, ResponseData<PendingApprovalTaskListResponse>>
{
    public override void Configure()
    {
        ConfigureApprovalContract(ApprovalEndpointContracts.Get<ListPendingApprovalTasksEndpoint>());
    }

    public override async Task HandleAsync(ListPendingApprovalTasksRequest req, CancellationToken ct)
    {
        var response = await sender.Send(new ListPendingApprovalTasksQuery(req.OrganizationId, req.EnvironmentId, req.ActorType, req.ActorRef, req.Skip, req.Take), ct);
        await Send.OkAsync(response.AsResponseData(), cancellation: ct);
    }
}

public sealed class CheckOverdueApprovalStepsEndpoint(ISender sender)
    : ApprovalEndpoint<CheckOverdueApprovalStepsRequest, ResponseData<CheckOverdueApprovalStepsResponse>>
{
    public override void Configure()
    {
        ConfigureApprovalContract(ApprovalEndpointContracts.Get<CheckOverdueApprovalStepsEndpoint>());
    }

    public override async Task HandleAsync(CheckOverdueApprovalStepsRequest req, CancellationToken ct)
    {
        var marked = await sender.Send(new CheckOverdueApprovalStepsCommand(req.OrganizationId, req.EnvironmentId), ct);
        await Send.OkAsync(new CheckOverdueApprovalStepsResponse(marked).AsResponseData(), cancellation: ct);
    }
}

public sealed class ListApprovalDecisionsEndpoint(ISender sender)
    : ApprovalEndpoint<ListApprovalDecisionsRequest, ResponseData<ApprovalDecisionListResponse>>
{
    public override void Configure()
    {
        ConfigureApprovalContract(ApprovalEndpointContracts.Get<ListApprovalDecisionsEndpoint>());
    }

    public override async Task HandleAsync(ListApprovalDecisionsRequest req, CancellationToken ct)
    {
        var response = await sender.Send(new ListApprovalDecisionsQuery(
            req.OrganizationId,
            req.EnvironmentId,
            req.ChainId,
            req.ActorType,
            req.ActorRef,
            req.Decision,
            req.DocumentType,
            req.DocumentId,
            req.Skip,
            req.Take), ct);
        await Send.OkAsync(response.AsResponseData(), cancellation: ct);
    }
}

public sealed class ResolveApprovalStepEndpoint(ISender sender)
    : ApprovalEndpoint<ResolveApprovalStepRequest, ResponseData<ResolveApprovalStepResponse>>
{
    public override void Configure()
    {
        ConfigureApprovalContract(ApprovalEndpointContracts.Get<ResolveApprovalStepEndpoint>());
    }

    public override async Task HandleAsync(ResolveApprovalStepRequest req, CancellationToken ct)
    {
        var decisionId = await sender.Send(new ResolveApprovalStepCommand(
            req.ChainId,
            req.StepNo,
            req.ActorType,
            req.ActorRef,
            req.Decision,
            req.Comment), ct);
        await Send.OkAsync(new ResolveApprovalStepResponse(decisionId).AsResponseData(), cancellation: ct);
    }
}

public sealed class ListApprovalDelegationsEndpoint(ISender sender)
    : ApprovalEndpoint<ListApprovalDelegationsRequest, ResponseData<ApprovalDelegationListResponse>>
{
    public override void Configure()
    {
        ConfigureApprovalContract(ApprovalEndpointContracts.Get<ListApprovalDelegationsEndpoint>());
    }

    public override async Task HandleAsync(ListApprovalDelegationsRequest req, CancellationToken ct)
    {
        var response = await sender.Send(new ListApprovalDelegationsQuery(
            req.OrganizationId,
            req.EnvironmentId,
            req.Status,
            req.DelegatorActorRef,
            req.DelegateActorRef,
            req.DocumentType,
            req.Skip,
            req.Take), ct);
        await Send.OkAsync(response.AsResponseData(), cancellation: ct);
    }
}

public sealed class CreateApprovalDelegationEndpoint(ISender sender)
    : ApprovalEndpoint<CreateApprovalDelegationRequest, ResponseData<CreateApprovalDelegationResponse>>
{
    public override void Configure()
    {
        ConfigureApprovalContract(ApprovalEndpointContracts.Get<CreateApprovalDelegationEndpoint>());
    }

    public override async Task HandleAsync(CreateApprovalDelegationRequest req, CancellationToken ct)
    {
        var delegationId = await sender.Send(new CreateApprovalDelegationCommand(
            req.OrganizationId,
            req.EnvironmentId,
            req.DelegatorActorType,
            req.DelegatorActorRef,
            req.DelegateActorType,
            req.DelegateActorRef,
            req.DocumentType,
            req.EffectiveFromUtc,
            req.EffectiveToUtc,
            req.Reason,
            req.CreatedBy), ct);
        await Send.OkAsync(new CreateApprovalDelegationResponse(delegationId).AsResponseData(), cancellation: ct);
    }
}

public sealed class RevokeApprovalDelegationEndpoint(ISender sender)
    : ApprovalEndpoint<RevokeApprovalDelegationRequest, ResponseData<RevokeApprovalDelegationResponse>>
{
    public override void Configure()
    {
        ConfigureApprovalContract(ApprovalEndpointContracts.Get<RevokeApprovalDelegationEndpoint>());
    }

    public override async Task HandleAsync(RevokeApprovalDelegationRequest req, CancellationToken ct)
    {
        var delegationId = Route<ApprovalDelegationId>("delegationId") ?? req.DelegationId;
        await sender.Send(new RevokeApprovalDelegationCommand(
            delegationId,
            req.OrganizationId,
            req.EnvironmentId,
            req.RevokedBy), ct);
        await Send.OkAsync(new RevokeApprovalDelegationResponse(true).AsResponseData(), cancellation: ct);
    }
}

public sealed record ApprovalEndpointContract(
    Type EndpointType,
    string HttpMethod,
    string Route,
    string PermissionCode,
    string AuthorizationPolicy,
    string OperationId);

public static class ApprovalEndpointContracts
{
    public static readonly IReadOnlyCollection<ApprovalEndpointContract> All =
    [
        new(typeof(CreateOrUpdateApprovalTemplateEndpoint), "POST", "/api/business/v1/approvals/templates", ApprovalPermissionCodes.Manage, InternalServiceAuthorizationPolicy.Name, "createOrUpdateApprovalTemplate"),
        new(typeof(ListApprovalTemplatesEndpoint), "GET", "/api/business/v1/approvals/templates", ApprovalPermissionCodes.Read, InternalServiceAuthorizationPolicy.Name, "listApprovalTemplates"),
        new(typeof(ListApprovalChainsEndpoint), "GET", "/api/business/v1/approvals/chains", ApprovalPermissionCodes.Read, InternalServiceAuthorizationPolicy.Name, "listApprovalChains"),
        new(typeof(StartApprovalChainEndpoint), "POST", "/api/business/v1/approvals/chains", ApprovalPermissionCodes.Manage, InternalServiceAuthorizationPolicy.Name, "startApprovalChain"),
        new(typeof(GetApprovalChainEndpoint), "GET", "/api/business/v1/approvals/chains/{chainId}", ApprovalPermissionCodes.Read, InternalServiceAuthorizationPolicy.Name, "getApprovalChain"),
        new(typeof(ListPendingApprovalTasksEndpoint), "GET", "/api/business/v1/approvals/tasks", ApprovalPermissionCodes.Read, InternalServiceAuthorizationPolicy.Name, "listPendingApprovalTasks"),
        new(typeof(CheckOverdueApprovalStepsEndpoint), "POST", "/api/business/v1/approvals/tasks/overdue/check", ApprovalPermissionCodes.Manage, InternalServiceAuthorizationPolicy.Name, "checkOverdueApprovalSteps"),
        new(typeof(ListApprovalDecisionsEndpoint), "GET", "/api/business/v1/approvals/decisions", ApprovalPermissionCodes.Read, InternalServiceAuthorizationPolicy.Name, "listApprovalDecisions"),
        new(typeof(ResolveApprovalStepEndpoint), "POST", "/api/business/v1/approvals/chains/{chainId}/steps/{stepNo}/resolve", ApprovalPermissionCodes.Manage, InternalServiceAuthorizationPolicy.Name, "resolveApprovalStep"),
        new(typeof(ListApprovalDelegationsEndpoint), "GET", "/api/business/v1/approvals/delegations", ApprovalPermissionCodes.Read, InternalServiceAuthorizationPolicy.Name, "listApprovalDelegations"),
        new(typeof(CreateApprovalDelegationEndpoint), "POST", "/api/business/v1/approvals/delegations", ApprovalPermissionCodes.Manage, InternalServiceAuthorizationPolicy.Name, "createApprovalDelegation"),
        new(typeof(RevokeApprovalDelegationEndpoint), "POST", "/api/business/v1/approvals/delegations/{delegationId}/revoke", ApprovalPermissionCodes.Manage, InternalServiceAuthorizationPolicy.Name, "revokeApprovalDelegation"),
    ];

    public static ApprovalEndpointContract Get<TEndpoint>()
    {
        return All.Single(x => x.EndpointType == typeof(TEndpoint));
    }

    public static bool TryGet(Type endpointType, [NotNullWhen(true)] out ApprovalEndpointContract? contract)
    {
        contract = All.SingleOrDefault(x => x.EndpointType == endpointType);
        return contract is not null;
    }
}
