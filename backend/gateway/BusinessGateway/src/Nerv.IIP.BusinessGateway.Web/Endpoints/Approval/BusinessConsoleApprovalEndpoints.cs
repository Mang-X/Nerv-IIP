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

public sealed class BusinessConsoleApprovalTemplateListRequestValidator : Validator<BusinessConsoleApprovalTemplateListRequest>
{
    public BusinessConsoleApprovalTemplateListRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.DocumentType).MaximumLength(100);
    }
}

