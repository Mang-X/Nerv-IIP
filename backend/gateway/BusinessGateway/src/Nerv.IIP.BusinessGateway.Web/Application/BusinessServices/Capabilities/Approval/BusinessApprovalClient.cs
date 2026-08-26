using System.Globalization;

namespace Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;

public interface IBusinessApprovalClient
{
    Task<BusinessConsoleApprovalTemplateListResponse> ListTemplatesAsync(
        string internalBearerToken,
        BusinessConsoleApprovalTemplateListRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleCreateOrUpdateApprovalTemplateResponse> CreateOrUpdateTemplateAsync(
        string internalBearerToken,
        BusinessConsoleCreateOrUpdateApprovalTemplateRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleStartApprovalChainResponse> StartChainAsync(
        string internalBearerToken,
        BusinessConsoleStartApprovalChainRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleApprovalChainListResponse> ListChainsAsync(
        string internalBearerToken,
        BusinessConsoleApprovalChainListRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleApprovalChainResponse> GetChainAsync(
        string internalBearerToken,
        BusinessConsoleApprovalChainRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleApprovalTaskListResponse> ListPendingTasksAsync(
        string internalBearerToken,
        BusinessConsoleApprovalTaskListRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleApprovalDecisionListResponse> ListDecisionsAsync(
        string internalBearerToken,
        BusinessConsoleApprovalDecisionListRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleResolveApprovalStepResponse> ResolveStepAsync(
        string internalBearerToken,
        BusinessConsoleResolveApprovalStepRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleApprovalDelegationListResponse> ListDelegationsAsync(
        string internalBearerToken,
        BusinessConsoleApprovalDelegationListRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleCreateApprovalDelegationResponse> CreateDelegationAsync(
        string internalBearerToken,
        BusinessConsoleCreateApprovalDelegationRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleAcceptedResponse> RevokeDelegationAsync(
        string internalBearerToken,
        string delegationId,
        BusinessConsoleRevokeApprovalDelegationRequest request,
        CancellationToken cancellationToken);
}

public sealed class HttpBusinessApprovalClient(HttpClient httpClient) : BusinessServiceHttpClient(httpClient), IBusinessApprovalClient
{
    public Task<BusinessConsoleApprovalTemplateListResponse> ListTemplatesAsync(
        string internalBearerToken,
        BusinessConsoleApprovalTemplateListRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleApprovalTemplateListResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/approvals/templates?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId),
                ("documentType", request.DocumentType),
                ("isActive", request.IsActive),
                ("skip", request.Skip),
                ("take", request.Take)),
            null,
            cancellationToken);

    public Task<BusinessConsoleCreateOrUpdateApprovalTemplateResponse> CreateOrUpdateTemplateAsync(
        string internalBearerToken,
        BusinessConsoleCreateOrUpdateApprovalTemplateRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleCreateOrUpdateApprovalTemplateResponse>(
            internalBearerToken,
            HttpMethod.Post,
            "/api/business/v1/approvals/templates",
            request,
            cancellationToken);

    public Task<BusinessConsoleStartApprovalChainResponse> StartChainAsync(
        string internalBearerToken,
        BusinessConsoleStartApprovalChainRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleStartApprovalChainResponse>(
            internalBearerToken,
            HttpMethod.Post,
            "/api/business/v1/approvals/chains",
            request,
            cancellationToken);

    public Task<BusinessConsoleApprovalChainListResponse> ListChainsAsync(
        string internalBearerToken,
        BusinessConsoleApprovalChainListRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleApprovalChainListResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/approvals/chains?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId),
                ("status", request.Status),
                ("startedBy", request.StartedBy),
                ("sourceService", request.SourceService),
                ("documentType", request.DocumentType),
                ("documentId", request.DocumentId),
                ("skip", request.Skip),
                ("take", request.Take)),
            null,
            cancellationToken);

    public Task<BusinessConsoleApprovalChainResponse> GetChainAsync(
        string internalBearerToken,
        BusinessConsoleApprovalChainRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleApprovalChainResponse>(
            internalBearerToken,
            HttpMethod.Get,
            $"/api/business/v1/approvals/chains/{Uri.EscapeDataString(request.ChainId)}",
            null,
            cancellationToken);

    public Task<BusinessConsoleApprovalTaskListResponse> ListPendingTasksAsync(
        string internalBearerToken,
        BusinessConsoleApprovalTaskListRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleApprovalTaskListResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/approvals/tasks?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId),
                ("actorType", request.ActorType),
                ("actorRef", request.ActorRef),
                ("skip", request.Skip),
                ("take", request.Take)),
            null,
            cancellationToken);

    public Task<BusinessConsoleApprovalDecisionListResponse> ListDecisionsAsync(
        string internalBearerToken,
        BusinessConsoleApprovalDecisionListRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleApprovalDecisionListResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/approvals/decisions?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId),
                ("chainId", request.ChainId),
                ("actorType", request.ActorType),
                ("actorRef", request.ActorRef),
                ("decision", request.Decision),
                ("documentType", request.DocumentType),
                ("documentId", request.DocumentId),
                ("skip", request.Skip),
                ("take", request.Take)),
            null,
            cancellationToken);

    public Task<BusinessConsoleResolveApprovalStepResponse> ResolveStepAsync(
        string internalBearerToken,
        BusinessConsoleResolveApprovalStepRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleResolveApprovalStepResponse>(
            internalBearerToken,
            HttpMethod.Post,
            $"/api/business/v1/approvals/chains/{Uri.EscapeDataString(request.ChainId)}/steps/{request.StepNo.ToString(CultureInfo.InvariantCulture)}/resolve",
            request,
            cancellationToken);

    public Task<BusinessConsoleApprovalDelegationListResponse> ListDelegationsAsync(
        string internalBearerToken,
        BusinessConsoleApprovalDelegationListRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleApprovalDelegationListResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/approvals/delegations?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId),
                ("status", request.Status),
                ("delegatorActorRef", request.DelegatorActorRef),
                ("delegateActorRef", request.DelegateActorRef),
                ("documentType", request.DocumentType),
                ("skip", request.Skip),
                ("take", request.Take)),
            null,
            cancellationToken);

    public Task<BusinessConsoleCreateApprovalDelegationResponse> CreateDelegationAsync(
        string internalBearerToken,
        BusinessConsoleCreateApprovalDelegationRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleCreateApprovalDelegationResponse>(
            internalBearerToken,
            HttpMethod.Post,
            "/api/business/v1/approvals/delegations",
            request,
            cancellationToken);

    public Task<BusinessConsoleAcceptedResponse> RevokeDelegationAsync(
        string internalBearerToken,
        string delegationId,
        BusinessConsoleRevokeApprovalDelegationRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleAcceptedResponse>(
            internalBearerToken,
            HttpMethod.Post,
            $"/api/business/v1/approvals/delegations/{Uri.EscapeDataString(delegationId)}/revoke?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId)),
            request,
            cancellationToken);
}
