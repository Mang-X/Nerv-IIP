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
        if (contract.AuthorizationPolicy is { } policy)
        {
            Policies(policy);
        }
    }

    protected Task WriteApprovalErrorAsync(int status, string message, CancellationToken ct)
    {
        HttpContext.Response.StatusCode = status;
        return HttpContext.Response.WriteAsJsonAsync(new ResponseData(false, message, status, []), ct);
    }
}

public sealed record CreateOrUpdateApprovalTemplateRequest(
    string OrganizationId,
    string EnvironmentId,
    string TemplateCode,
    string DocumentType,
    int Version,
    bool IsActive,
    IReadOnlyCollection<ApprovalTemplateStepRequest> Steps,
    bool CreateOnly = false);

public sealed record ApprovalTemplateStepRequest(
    int StepNo,
    string StepName,
    string? ParallelGroupKey,
    string ApproverType,
    string ApproverRef,
    int? DueInHours,
    string? CompletionPolicy = null,
    string? ConditionExpression = null,
    ApprovalRoutingConditionInput? Condition = null);

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
    string StartedBy,
    decimal? Amount = null,
    string? RoutingOrganizationId = null,
    string? DepartmentId = null);

public sealed record StartApprovalChainResponse(ApprovalChainId ChainId, string Status);

public sealed record GetApprovalChainRequest(ApprovalChainId ChainId);

public sealed record ListPendingApprovalTasksRequest(
    string OrganizationId,
    string EnvironmentId,
    string ActorType,
    string ActorRef,
    int Skip = 0,
    int Take = 100,
    string? RoleRefs = null,
    string? DepartmentRef = null);

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
    string OrganizationId,
    string EnvironmentId,
    [property: RouteParam] string ChainId,
    [property: RouteParam] int StepNo,
    string Decision,
    string? Comment,
    string? TargetStepId = null);

public sealed record ResolveApprovalStepResponse(ApprovalDecisionId DecisionId);

public sealed record WithdrawApprovalChainRequest(
    string OrganizationId,
    string EnvironmentId,
    [property: RouteParam] string ChainId,
    string? Reason);

public sealed record ApprovalActionAcceptedResponse(bool Accepted);

public sealed record ResubmitApprovalChainRequest(
    ApprovalChainId? ChainId,
    string? ActorType,
    string? ActorRef,
    string? Reason,
    string? OrganizationId = null,
    string? EnvironmentId = null);

public sealed record AddApprovalStepSignerRequest(
    ApprovalChainId ChainId,
    int StepNo,
    string ApproverType,
    string ApproverRef,
    string RequestedByActorType,
    string RequestedByActorRef,
    string? Reason,
    string? OrganizationId = null,
    string? EnvironmentId = null);

public sealed record AddApprovalStepSignerResponse(ApprovalStepId StepId);

public sealed record TransferApprovalStepRequest(
    ApprovalChainId? ChainId,
    int StepNo,
    string FromActorType,
    string FromActorRef,
    string ToActorType,
    string ToActorRef,
    string? RequestedByActorType,
    string? RequestedByActorRef,
    string? Reason,
    string? OrganizationId = null,
    string? EnvironmentId = null);

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
        if (!ApprovalGatewayOperationAuthorization.WriteTemplates.Allows(User, req.OrganizationId, req.EnvironmentId))
        {
            await Send.ForbiddenAsync(ct);
            return;
        }

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
                x.ConditionExpression,
                x.Condition)).ToArray(), req.CreateOnly), ct);
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
        if (!ApprovalGatewayOperationAuthorization.ReadTemplates.Allows(User, req.OrganizationId, req.EnvironmentId))
        {
            await Send.ForbiddenAsync(ct);
            return;
        }

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
        if (!ApprovalGatewayOperationAuthorization.ReadCollections.Allows(User, req.OrganizationId, req.EnvironmentId))
        {
            await Send.ForbiddenAsync(ct);
            return;
        }

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
        if (!CreateApprovalChainAuthorization.Allows(User, req))
        {
            await Send.ForbiddenAsync(ct);
            return;
        }

        var chain = await sender.Send(new StartApprovalChainCommand(
            req.OrganizationId,
            req.EnvironmentId,
            req.TemplateCode,
            req.SourceService,
            req.DocumentType,
            req.DocumentId,
            req.DocumentLineId,
            req.StartedBy,
            req.Amount,
            req.RoutingOrganizationId,
            req.DepartmentId), ct);
        await Send.OkAsync(new StartApprovalChainResponse(chain.ChainId, chain.Status).AsResponseData(), cancellation: ct);
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
        if (!GetApprovalChainAuthorization.Allows(User, response))
        {
            await Send.ForbiddenAsync(ct);
            return;
        }

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
        if (!ApprovalGatewayOperationAuthorization.ReadCollections.Allows(User, req.OrganizationId, req.EnvironmentId))
        {
            await Send.ForbiddenAsync(ct);
            return;
        }

        var response = await sender.Send(new ListPendingApprovalTasksQuery(req.OrganizationId, req.EnvironmentId, req.ActorType, req.ActorRef, req.Skip, req.Take, req.RoleRefs, req.DepartmentRef), ct);
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
        if (!ApprovalGatewayOperationAuthorization.ReadCollections.Allows(User, req.OrganizationId, req.EnvironmentId))
        {
            await Send.ForbiddenAsync(ct);
            return;
        }

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

public sealed class ResolveApprovalStepEndpoint(ISender sender, ResolveApprovalStepAuthority authority)
    : ApprovalEndpoint<ResolveApprovalStepRequest, ResponseData<ResolveApprovalStepResponse>>
{
    public override void Configure()
    {
        ConfigureApprovalContract(ApprovalEndpointContracts.Get<ResolveApprovalStepEndpoint>());
        // IAM verifies the end-principal bearer before dispatching the command.
        AllowAnonymous();
    }

    public override async Task HandleAsync(ResolveApprovalStepRequest req, CancellationToken ct)
    {
        var stepNo = Route<int>("stepNo");
        var targetId = Guid.Empty;
        if (!Guid.TryParse(Route<string>("chainId"), out var chainGuid) || chainGuid == Guid.Empty || stepNo <= 0
            || (req.TargetStepId is not null && (!Guid.TryParse(req.TargetStepId, out targetId) || targetId == Guid.Empty))
            || string.IsNullOrWhiteSpace(req.OrganizationId) || string.IsNullOrWhiteSpace(req.EnvironmentId))
        {
            await WriteErrorAsync(StatusCodes.Status400BadRequest, "审批目标或组织环境无效。", ct);
            return;
        }

        var chainId = new ApprovalChainId(chainGuid);
        var (status, actor) = await authority.ResolveChainActionAsync(
            HttpContext.Request.Headers.Authorization.ToString(), req.OrganizationId, req.EnvironmentId, chainId, ct,
            loadApproverContext: true);
        if (actor is null)
        {
            await WriteErrorAsync(status, "无法授权当前审批裁决。", ct);
            return;
        }

        var decisionId = await sender.Send(new ResolveApprovalStepCommand(
            actor.OrganizationId,
            actor.EnvironmentId,
            chainId,
            stepNo,
            actor.Type,
            actor.Id,
            req.Decision,
            req.Comment,
            actor.RoleRefs,
            actor.DepartmentRef,
            targetId == Guid.Empty ? null : new ApprovalStepId(targetId)), ct);
        if (decisionId is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        await Send.OkAsync(new ResolveApprovalStepResponse(decisionId).AsResponseData(), cancellation: ct);
    }

    private Task WriteErrorAsync(int status, string message, CancellationToken ct)
    {
        HttpContext.Response.StatusCode = status;
        return HttpContext.Response.WriteAsJsonAsync(new ResponseData(false, message, status, []), ct);
    }
}

public sealed class WithdrawApprovalChainEndpoint(ISender sender, ResolveApprovalStepAuthority authority)
    : ApprovalEndpoint<WithdrawApprovalChainRequest, ResponseData<ApprovalActionAcceptedResponse>>
{
    public override void Configure()
    {
        ConfigureApprovalContract(ApprovalEndpointContracts.Get<WithdrawApprovalChainEndpoint>());
        // IAM verifies the end-principal bearer before dispatching the command.
        AllowAnonymous();
    }

    public override async Task HandleAsync(WithdrawApprovalChainRequest req, CancellationToken ct)
    {
        if (!Guid.TryParse(Route<string>("chainId"), out var chainGuid) || chainGuid == Guid.Empty
            || string.IsNullOrWhiteSpace(req.OrganizationId) || string.IsNullOrWhiteSpace(req.EnvironmentId))
        {
            await WriteErrorAsync(StatusCodes.Status400BadRequest, "审批目标或组织环境无效。", ct);
            return;
        }

        var chainId = new ApprovalChainId(chainGuid);
        var (status, actor) = await authority.ResolveChainActionAsync(
            HttpContext.Request.Headers.Authorization.ToString(), req.OrganizationId, req.EnvironmentId, chainId, ct,
            loadApproverContext: false);
        if (actor is null)
        {
            await WriteErrorAsync(status, "无法授权当前审批撤回。", ct);
            return;
        }

        await sender.Send(new WithdrawApprovalChainCommand(
            actor.OrganizationId, actor.EnvironmentId, chainId, actor.Type, actor.Id, req.Reason), ct);
        await Send.OkAsync(new ApprovalActionAcceptedResponse(true).AsResponseData(), cancellation: ct);
    }

    private Task WriteErrorAsync(int status, string message, CancellationToken ct)
    {
        HttpContext.Response.StatusCode = status;
        return HttpContext.Response.WriteAsJsonAsync(new ResponseData(false, message, status, []), ct);
    }
}

public sealed class ResubmitApprovalChainEndpoint(ISender sender, ResolveApprovalStepAuthority authority)
    : ApprovalEndpoint<ResubmitApprovalChainRequest, ResponseData<ApprovalActionAcceptedResponse>>
{
    public override void Configure()
    {
        ConfigureApprovalContract(ApprovalEndpointContracts.Get<ResubmitApprovalChainEndpoint>());
        // IAM verifies the end-principal bearer before dispatching the command.
        AllowAnonymous();
    }

    public override async Task HandleAsync(ResubmitApprovalChainRequest req, CancellationToken ct)
    {
        if (!Guid.TryParse(Route<string>("chainId"), out var chainGuid) || chainGuid == Guid.Empty)
        {
            await WriteErrorAsync(StatusCodes.Status400BadRequest, "审批目标或组织环境无效。", ct);
            return;
        }

        var chainId = new ApprovalChainId(chainGuid);
        var (status, actor) = await authority.ResolveChainActionAsync(
            HttpContext.Request.Headers.Authorization.ToString(), req.OrganizationId, req.EnvironmentId, chainId, ct);
        if (actor is null)
        {
            await WriteErrorAsync(status, "无法授权当前审批重新提交。", ct);
            return;
        }

        await sender.Send(new ResubmitApprovalChainCommand(
            actor.OrganizationId, actor.EnvironmentId, chainId, actor.Type, actor.Id, req.Reason), ct);
        await Send.OkAsync(new ApprovalActionAcceptedResponse(true).AsResponseData(), cancellation: ct);
    }

    private Task WriteErrorAsync(int status, string message, CancellationToken ct)
    {
        HttpContext.Response.StatusCode = status;
        return HttpContext.Response.WriteAsJsonAsync(new ResponseData(false, message, status, []), ct);
    }
}

public sealed class AddApprovalStepSignerEndpoint(ISender sender, ResolveApprovalStepAuthority authority)
    : ApprovalEndpoint<AddApprovalStepSignerRequest, ResponseData<AddApprovalStepSignerResponse>>
{
    public override void Configure()
    {
        ConfigureApprovalContract(ApprovalEndpointContracts.Get<AddApprovalStepSignerEndpoint>());
        // IAM verifies the end-principal bearer before dispatching the command.
        AllowAnonymous();
    }

    public override async Task HandleAsync(AddApprovalStepSignerRequest req, CancellationToken ct)
    {
        var stepNo = Route<int>("stepNo");
        if (!Guid.TryParse(Route<string>("chainId"), out var chainGuid) || chainGuid == Guid.Empty || stepNo <= 0)
        {
            await WriteErrorAsync(StatusCodes.Status400BadRequest, "审批目标或组织环境无效。", ct);
            return;
        }

        var chainId = new ApprovalChainId(chainGuid);
        var (status, actor) = await authority.ResolveChainActionAsync(
            HttpContext.Request.Headers.Authorization.ToString(), req.OrganizationId, req.EnvironmentId, chainId, ct);
        if (actor is null)
        {
            await WriteErrorAsync(status, "无法授权当前审批加签。", ct);
            return;
        }

        var stepId = await sender.Send(new AddApprovalStepSignerCommand(
            actor.OrganizationId,
            actor.EnvironmentId,
            chainId,
            stepNo,
            req.ApproverType,
            req.ApproverRef,
            actor.Type,
            actor.Id,
            req.Reason), ct);
        await Send.OkAsync(new AddApprovalStepSignerResponse(stepId).AsResponseData(), cancellation: ct);
    }

    private Task WriteErrorAsync(int status, string message, CancellationToken ct)
    {
        HttpContext.Response.StatusCode = status;
        return HttpContext.Response.WriteAsJsonAsync(new ResponseData(false, message, status, []), ct);
    }
}

public sealed class TransferApprovalStepEndpoint(ISender sender, ResolveApprovalStepAuthority authority)
    : ApprovalEndpoint<TransferApprovalStepRequest, ResponseData<ApprovalActionAcceptedResponse>>
{
    public override void Configure()
    {
        ConfigureApprovalContract(ApprovalEndpointContracts.Get<TransferApprovalStepEndpoint>());
        // IAM verifies the end-principal bearer before dispatching the command.
        AllowAnonymous();
    }

    public override async Task HandleAsync(TransferApprovalStepRequest req, CancellationToken ct)
    {
        var stepNo = Route<int>("stepNo");
        if (!Guid.TryParse(Route<string>("chainId"), out var chainGuid) || chainGuid == Guid.Empty || stepNo <= 0)
        {
            await WriteErrorAsync(StatusCodes.Status400BadRequest, "审批目标或组织环境无效。", ct);
            return;
        }

        var chainId = new ApprovalChainId(chainGuid);
        var (status, actor) = await authority.ResolveChainActionAsync(
            HttpContext.Request.Headers.Authorization.ToString(), req.OrganizationId, req.EnvironmentId, chainId, ct);
        if (actor is null)
        {
            await WriteErrorAsync(status, "无法授权当前审批转交。", ct);
            return;
        }

        await sender.Send(new TransferApprovalStepCommand(
            actor.OrganizationId,
            actor.EnvironmentId,
            chainId,
            stepNo,
            req.FromActorType,
            req.FromActorRef,
            req.ToActorType,
            req.ToActorRef,
            actor.Type,
            actor.Id,
            req.Reason), ct);
        await Send.OkAsync(new ApprovalActionAcceptedResponse(true).AsResponseData(), cancellation: ct);
    }

    private Task WriteErrorAsync(int status, string message, CancellationToken ct)
    {
        HttpContext.Response.StatusCode = status;
        return HttpContext.Response.WriteAsJsonAsync(new ResponseData(false, message, status, []), ct);
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

public sealed class CreateApprovalDelegationEndpoint(ISender sender, ResolveApprovalStepAuthority authority)
    : ApprovalEndpoint<CreateApprovalDelegationRequest, ResponseData<CreateApprovalDelegationResponse>>
{
    public override void Configure()
    {
        ConfigureApprovalContract(ApprovalEndpointContracts.Get<CreateApprovalDelegationEndpoint>());
        // IAM verifies the end-principal bearer before dispatching the command.
        AllowAnonymous();
    }

    public override async Task HandleAsync(CreateApprovalDelegationRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.OrganizationId) || string.IsNullOrWhiteSpace(req.EnvironmentId))
        {
            await WriteApprovalErrorAsync(StatusCodes.Status400BadRequest, "组织环境无效。", ct);
            return;
        }

        var (status, actor) = await authority.ResolveDelegationAsync(
            HttpContext.Request.Headers.Authorization.ToString(), req.OrganizationId, req.EnvironmentId, null, ct);
        if (actor is null)
        {
            await WriteApprovalErrorAsync(status, "无法授权当前审批委托创建。", ct);
            return;
        }

        var delegationId = await sender.Send(new CreateApprovalDelegationCommand(
            actor.OrganizationId,
            actor.EnvironmentId,
            actor.Type,
            actor.Id,
            req.DelegateActorType,
            req.DelegateActorRef,
            req.DocumentType,
            req.EffectiveFromUtc,
            req.EffectiveToUtc,
            req.Reason,
            actor.Id), ct);
        await Send.OkAsync(new CreateApprovalDelegationResponse(delegationId).AsResponseData(), cancellation: ct);
    }
}

public sealed class RevokeApprovalDelegationEndpoint(ISender sender, ResolveApprovalStepAuthority authority)
    : ApprovalEndpoint<RevokeApprovalDelegationRequest, ResponseData<RevokeApprovalDelegationResponse>>
{
    public override void Configure()
    {
        ConfigureApprovalContract(ApprovalEndpointContracts.Get<RevokeApprovalDelegationEndpoint>());
        // IAM verifies the end-principal bearer before dispatching the command.
        AllowAnonymous();
    }

    public override async Task HandleAsync(RevokeApprovalDelegationRequest req, CancellationToken ct)
    {
        if (!Guid.TryParse(Route<string>("delegationId"), out var delegationGuid) || delegationGuid == Guid.Empty
            || string.IsNullOrWhiteSpace(req.OrganizationId) || string.IsNullOrWhiteSpace(req.EnvironmentId))
        {
            await WriteApprovalErrorAsync(StatusCodes.Status400BadRequest, "委托目标或组织环境无效。", ct);
            return;
        }

        var delegationId = new ApprovalDelegationId(delegationGuid);
        var (status, actor) = await authority.ResolveDelegationAsync(
            HttpContext.Request.Headers.Authorization.ToString(), req.OrganizationId, req.EnvironmentId, delegationId, ct);
        if (actor is null)
        {
            await WriteApprovalErrorAsync(status, "无法授权当前审批委托撤销。", ct);
            return;
        }

        await sender.Send(new RevokeApprovalDelegationCommand(
            delegationId,
            actor.OrganizationId,
            actor.EnvironmentId,
            actor.Id), ct);
        await Send.OkAsync(new RevokeApprovalDelegationResponse(true).AsResponseData(), cancellation: ct);
    }
}

public sealed record ApprovalEndpointContract(
    Type EndpointType,
    string HttpMethod,
    string Route,
    string PermissionCode,
    string? AuthorizationPolicy,
    string OperationId);

public static class ApprovalEndpointContracts
{
    public static readonly IReadOnlyCollection<ApprovalEndpointContract> All =
    [
        new(typeof(CreateOrUpdateApprovalTemplateEndpoint), "POST", "/api/business/v1/approvals/templates", ApprovalPermissionCodes.Manage, ApprovalGatewayOperationAuthorization.WriteTemplates.PolicyName, "createOrUpdateApprovalTemplate"),
        new(typeof(ListApprovalTemplatesEndpoint), "GET", "/api/business/v1/approvals/templates", ApprovalPermissionCodes.Read, ApprovalGatewayOperationAuthorization.ReadTemplates.PolicyName, "listApprovalTemplates"),
        new(typeof(ListApprovalChainsEndpoint), "GET", "/api/business/v1/approvals/chains", ApprovalPermissionCodes.Read, ApprovalGatewayOperationAuthorization.ReadCollections.PolicyName, "listApprovalChains"),
        new(typeof(StartApprovalChainEndpoint), "POST", "/api/business/v1/approvals/chains", ApprovalPermissionCodes.Manage, CreateApprovalChainAuthorization.PolicyName, "startApprovalChain"),
        new(typeof(GetApprovalChainEndpoint), "GET", "/api/business/v1/approvals/chains/{chainId}", ApprovalPermissionCodes.Read, GetApprovalChainAuthorization.PolicyName, "getApprovalChain"),
        new(typeof(ListPendingApprovalTasksEndpoint), "GET", "/api/business/v1/approvals/tasks", ApprovalPermissionCodes.Read, ApprovalGatewayOperationAuthorization.ReadCollections.PolicyName, "listPendingApprovalTasks"),
        new(typeof(CheckOverdueApprovalStepsEndpoint), "POST", "/api/business/v1/approvals/tasks/overdue/check", ApprovalPermissionCodes.Manage, InternalServiceAuthorizationPolicy.Name, "checkOverdueApprovalSteps"),
        new(typeof(ListApprovalDecisionsEndpoint), "GET", "/api/business/v1/approvals/decisions", ApprovalPermissionCodes.Read, ApprovalGatewayOperationAuthorization.ReadCollections.PolicyName, "listApprovalDecisions"),
        new(typeof(ResolveApprovalStepEndpoint), "POST", "/api/business/v1/approvals/chains/{chainId}/steps/{stepNo}/resolve", ApprovalPermissionCodes.Manage, null, "resolveApprovalStep"),
        new(typeof(WithdrawApprovalChainEndpoint), "POST", "/api/business/v1/approvals/chains/{chainId}/withdraw", ApprovalPermissionCodes.Manage, null, "withdrawApprovalChain"),
        new(typeof(ResubmitApprovalChainEndpoint), "POST", "/api/business/v1/approvals/chains/{chainId}/resubmit", ApprovalPermissionCodes.Manage, null, "resubmitApprovalChain"),
        new(typeof(AddApprovalStepSignerEndpoint), "POST", "/api/business/v1/approvals/chains/{chainId}/steps/{stepNo}/add-signer", ApprovalPermissionCodes.Manage, null, "addApprovalStepSigner"),
        new(typeof(TransferApprovalStepEndpoint), "POST", "/api/business/v1/approvals/chains/{chainId}/steps/{stepNo}/transfer", ApprovalPermissionCodes.Manage, null, "transferApprovalStep"),
        new(typeof(ListApprovalDelegationsEndpoint), "GET", "/api/business/v1/approvals/delegations", ApprovalPermissionCodes.Read, InternalServiceAuthorizationPolicy.Name, "listApprovalDelegations"),
        new(typeof(CreateApprovalDelegationEndpoint), "POST", "/api/business/v1/approvals/delegations", ApprovalPermissionCodes.Manage, null, "createApprovalDelegation"),
        new(typeof(RevokeApprovalDelegationEndpoint), "POST", "/api/business/v1/approvals/delegations/{delegationId}/revoke", ApprovalPermissionCodes.Manage, null, "revokeApprovalDelegation"),
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
