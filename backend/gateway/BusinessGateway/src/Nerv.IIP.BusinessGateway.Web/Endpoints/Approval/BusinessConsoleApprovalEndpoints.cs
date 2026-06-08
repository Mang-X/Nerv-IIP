using FastEndpoints;
using FluentValidation;
using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.BusinessGateway.Web.Application.OpenApi;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.BusinessGateway.Web.Endpoints.Approval;

[Tags("Business Console Approval")]
[HttpGet("/api/business-console/v1/approval/templates")]
[BusinessGatewayOperationId("listBusinessConsoleApprovalTemplates")]
public sealed class ListBusinessConsoleApprovalTemplatesEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessApprovalClient approval,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleApprovalTemplateListRequest, BusinessConsoleApprovalTemplateListResponse>(
        auth,
        BusinessGatewayPermissions.ApprovalsRead)
{
    protected override string OrganizationId(BusinessConsoleApprovalTemplateListRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleApprovalTemplateListRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleApprovalTemplateListResponse> ForwardAsync(
        BusinessConsoleApprovalTemplateListRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        approval.ListTemplatesAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console Approval")]
[HttpGet("/api/business-console/v1/approval/chains")]
[BusinessGatewayOperationId("listBusinessConsoleApprovalChains")]
public sealed class ListBusinessConsoleApprovalChainsEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessApprovalClient approval,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleApprovalChainListRequest, BusinessConsoleApprovalChainListResponse>(
        auth,
        BusinessGatewayPermissions.ApprovalsRead)
{
    protected override string OrganizationId(BusinessConsoleApprovalChainListRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleApprovalChainListRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleApprovalChainListResponse> ForwardAsync(
        BusinessConsoleApprovalChainListRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        approval.ListChainsAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console Approval")]
[HttpPost("/api/business-console/v1/approval/templates")]
[BusinessGatewayOperationId("createOrUpdateBusinessConsoleApprovalTemplate")]
public sealed class CreateOrUpdateBusinessConsoleApprovalTemplateEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessApprovalClient approval,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleCreateOrUpdateApprovalTemplateRequest, BusinessConsoleCreateOrUpdateApprovalTemplateResponse>(
        auth,
        BusinessGatewayPermissions.ApprovalsManage)
{
    protected override string OrganizationId(BusinessConsoleCreateOrUpdateApprovalTemplateRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleCreateOrUpdateApprovalTemplateRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleCreateOrUpdateApprovalTemplateResponse> ForwardAsync(
        BusinessConsoleCreateOrUpdateApprovalTemplateRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        approval.CreateOrUpdateTemplateAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console Approval")]
[HttpPost("/api/business-console/v1/approval/chains")]
[BusinessGatewayOperationId("startBusinessConsoleApprovalChain")]
public sealed class StartBusinessConsoleApprovalChainEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessApprovalClient approval,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleStartApprovalChainRequest, BusinessConsoleStartApprovalChainResponse>(
        auth,
        BusinessGatewayPermissions.ApprovalsManage)
{
    protected override string OrganizationId(BusinessConsoleStartApprovalChainRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleStartApprovalChainRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleStartApprovalChainResponse> ForwardAsync(
        BusinessConsoleStartApprovalChainRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        approval.StartChainAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console Approval")]
[HttpGet("/api/business-console/v1/approval/chains/{chainId}")]
[BusinessGatewayOperationId("getBusinessConsoleApprovalChain")]
public sealed class GetBusinessConsoleApprovalChainEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessApprovalClient approval,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleApprovalChainRequest, BusinessConsoleApprovalChainResponse>(
        auth,
        BusinessGatewayPermissions.ApprovalsRead)
{
    protected override string OrganizationId(BusinessConsoleApprovalChainRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleApprovalChainRequest request) => request.EnvironmentId;

    protected override string ResourceType(BusinessConsoleApprovalChainRequest request) => "approval-chain";

    protected override string? ResourceId(BusinessConsoleApprovalChainRequest request) =>
        Route<string>("chainId") ?? request.ChainId;

    protected override Task<BusinessConsoleApprovalChainResponse> ForwardAsync(
        BusinessConsoleApprovalChainRequest request,
        string bearerToken,
        CancellationToken cancellationToken)
    {
        var downstreamRequest = request with { ChainId = Route<string>("chainId") ?? request.ChainId };
        return approval.GetChainAsync(tokenProvider.BearerToken, downstreamRequest, cancellationToken);
    }
}

[Tags("Business Console Approval")]
[HttpGet("/api/business-console/v1/approval/tasks")]
[BusinessGatewayOperationId("listBusinessConsoleApprovalTasks")]
public sealed class ListBusinessConsoleApprovalTasksEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessApprovalClient approval,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleApprovalTaskListRequest, BusinessConsoleApprovalTaskListResponse>(
        auth,
        BusinessGatewayPermissions.ApprovalsRead)
{
    protected override string OrganizationId(BusinessConsoleApprovalTaskListRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleApprovalTaskListRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleApprovalTaskListResponse> ForwardAsync(
        BusinessConsoleApprovalTaskListRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        approval.ListPendingTasksAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console Approval")]
[HttpGet("/api/business-console/v1/approval/decisions")]
[BusinessGatewayOperationId("listBusinessConsoleApprovalDecisions")]
public sealed class ListBusinessConsoleApprovalDecisionsEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessApprovalClient approval,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleApprovalDecisionListRequest, BusinessConsoleApprovalDecisionListResponse>(
        auth,
        BusinessGatewayPermissions.ApprovalsRead)
{
    protected override string OrganizationId(BusinessConsoleApprovalDecisionListRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleApprovalDecisionListRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleApprovalDecisionListResponse> ForwardAsync(
        BusinessConsoleApprovalDecisionListRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        approval.ListDecisionsAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console Approval")]
[HttpPost("/api/business-console/v1/approval/chains/{chainId}/steps/{stepNo}/resolve")]
[BusinessGatewayOperationId("resolveBusinessConsoleApprovalStep")]
public sealed class ResolveBusinessConsoleApprovalStepEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessApprovalClient approval,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleResolveApprovalStepRequest, BusinessConsoleResolveApprovalStepResponse>(
        auth,
        BusinessGatewayPermissions.ApprovalsManage)
{
    protected override string OrganizationId(BusinessConsoleResolveApprovalStepRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleResolveApprovalStepRequest request) => request.EnvironmentId;

    protected override string ResourceType(BusinessConsoleResolveApprovalStepRequest request) => "approval-chain";

    protected override string? ResourceId(BusinessConsoleResolveApprovalStepRequest request) =>
        Route<string>("chainId") ?? request.ChainId;

    protected override Task<BusinessConsoleResolveApprovalStepResponse> ForwardAsync(
        BusinessConsoleResolveApprovalStepRequest request,
        string bearerToken,
        CancellationToken cancellationToken)
    {
        var downstreamRequest = request with
        {
            ChainId = Route<string>("chainId") ?? request.ChainId,
            StepNo = Route<int>("stepNo"),
        };
        return approval.ResolveStepAsync(tokenProvider.BearerToken, downstreamRequest, cancellationToken);
    }
}

[Tags("Business Console Approval")]
[HttpGet("/api/business-console/v1/approval/delegations")]
[BusinessGatewayOperationId("listBusinessConsoleApprovalDelegations")]
public sealed class ListBusinessConsoleApprovalDelegationsEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessApprovalClient approval,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleApprovalDelegationListRequest, BusinessConsoleApprovalDelegationListResponse>(
        auth,
        BusinessGatewayPermissions.ApprovalsRead)
{
    protected override string OrganizationId(BusinessConsoleApprovalDelegationListRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleApprovalDelegationListRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleApprovalDelegationListResponse> ForwardAsync(
        BusinessConsoleApprovalDelegationListRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        approval.ListDelegationsAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console Approval")]
[HttpPost("/api/business-console/v1/approval/delegations")]
[BusinessGatewayOperationId("createBusinessConsoleApprovalDelegation")]
public sealed class CreateBusinessConsoleApprovalDelegationEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessApprovalClient approval,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleCreateApprovalDelegationRequest, BusinessConsoleCreateApprovalDelegationResponse>(
        auth,
        BusinessGatewayPermissions.ApprovalsManage)
{
    protected override string OrganizationId(BusinessConsoleCreateApprovalDelegationRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleCreateApprovalDelegationRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleCreateApprovalDelegationResponse> ForwardAsync(
        BusinessConsoleCreateApprovalDelegationRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        approval.CreateDelegationAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console Approval")]
[HttpPost("/api/business-console/v1/approval/delegations/{delegationId}/revoke")]
[BusinessGatewayOperationId("revokeBusinessConsoleApprovalDelegation")]
public sealed class RevokeBusinessConsoleApprovalDelegationEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessApprovalClient approval,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleRevokeApprovalDelegationRequest, BusinessConsoleAcceptedResponse>(
        auth,
        BusinessGatewayPermissions.ApprovalsManage)
{
    protected override string OrganizationId(BusinessConsoleRevokeApprovalDelegationRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleRevokeApprovalDelegationRequest request) => request.EnvironmentId;

    protected override string ResourceType(BusinessConsoleRevokeApprovalDelegationRequest request) => "approval-delegation";

    protected override string? ResourceId(BusinessConsoleRevokeApprovalDelegationRequest request) =>
        Route<string>("delegationId") ?? request.DelegationId;

    protected override Task<BusinessConsoleAcceptedResponse> ForwardAsync(
        BusinessConsoleRevokeApprovalDelegationRequest request,
        string bearerToken,
        CancellationToken cancellationToken)
    {
        var delegationId = Route<string>("delegationId") ?? request.DelegationId;
        return approval.RevokeDelegationAsync(
            tokenProvider.BearerToken,
            delegationId,
            request with { DelegationId = delegationId },
            cancellationToken);
    }
}

public sealed class BusinessConsoleApprovalTemplateListRequestValidator : Validator<BusinessConsoleApprovalTemplateListRequest>
{
    public BusinessConsoleApprovalTemplateListRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.DocumentType).MaximumLength(100);
        RuleFor(x => x.Skip).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Take).InclusiveBetween(1, 500);
    }
}

public sealed class BusinessConsoleApprovalChainListRequestValidator : Validator<BusinessConsoleApprovalChainListRequest>
{
    public BusinessConsoleApprovalChainListRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Status).MaximumLength(50);
        RuleFor(x => x.StartedBy).MaximumLength(150);
        RuleFor(x => x.SourceService).MaximumLength(100);
        RuleFor(x => x.DocumentType).MaximumLength(100);
        RuleFor(x => x.DocumentId).MaximumLength(150);
        RuleFor(x => x.Skip).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Take).InclusiveBetween(1, 500);
    }
}

public sealed class BusinessConsoleApprovalTaskListRequestValidator : Validator<BusinessConsoleApprovalTaskListRequest>
{
    public BusinessConsoleApprovalTaskListRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.ActorType).NotEmpty().MaximumLength(50);
        RuleFor(x => x.ActorRef).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Skip).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Take).InclusiveBetween(1, 500);
    }
}

public sealed class BusinessConsoleApprovalDecisionListRequestValidator : Validator<BusinessConsoleApprovalDecisionListRequest>
{
    public BusinessConsoleApprovalDecisionListRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.ChainId).MaximumLength(150);
        RuleFor(x => x.ActorType).MaximumLength(50);
        RuleFor(x => x.ActorRef).MaximumLength(150);
        RuleFor(x => x.Decision).MaximumLength(50);
        RuleFor(x => x.DocumentType).MaximumLength(100);
        RuleFor(x => x.DocumentId).MaximumLength(150);
        RuleFor(x => x.Skip).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Take).InclusiveBetween(1, 500);
    }
}

public sealed class BusinessConsoleApprovalDelegationListRequestValidator : Validator<BusinessConsoleApprovalDelegationListRequest>
{
    public BusinessConsoleApprovalDelegationListRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Status).MaximumLength(50);
        RuleFor(x => x.DelegatorActorRef).MaximumLength(150);
        RuleFor(x => x.DelegateActorRef).MaximumLength(150);
        RuleFor(x => x.DocumentType).MaximumLength(100);
        RuleFor(x => x.Skip).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Take).InclusiveBetween(1, 500);
    }
}

public sealed class BusinessConsoleCreateApprovalDelegationRequestValidator : Validator<BusinessConsoleCreateApprovalDelegationRequest>
{
    public BusinessConsoleCreateApprovalDelegationRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.DelegatorActorType).NotEmpty().MaximumLength(50);
        RuleFor(x => x.DelegatorActorRef).NotEmpty().MaximumLength(150);
        RuleFor(x => x.DelegateActorType).NotEmpty().MaximumLength(50);
        RuleFor(x => x.DelegateActorRef).NotEmpty().MaximumLength(150);
        RuleFor(x => x.DocumentType).MaximumLength(100);
        RuleFor(x => x.EffectiveToUtc).GreaterThan(x => x.EffectiveFromUtc);
        RuleFor(x => x.Reason).MaximumLength(500);
        RuleFor(x => x.CreatedBy).NotEmpty().MaximumLength(150);
    }
}

public sealed class BusinessConsoleRevokeApprovalDelegationRequestValidator : Validator<BusinessConsoleRevokeApprovalDelegationRequest>
{
    public BusinessConsoleRevokeApprovalDelegationRequestValidator()
    {
        RuleFor(x => x.DelegationId).NotEmpty().MaximumLength(150);
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.RevokedBy).NotEmpty().MaximumLength(150);
    }
}
