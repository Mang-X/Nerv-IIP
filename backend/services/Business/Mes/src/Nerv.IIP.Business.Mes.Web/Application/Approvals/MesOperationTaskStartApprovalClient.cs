using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Nerv.IIP.Contracts.Approval;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.Business.Mes.Web.Application.Approvals;

public sealed record MesOperationTaskStartApproval(
    string ApprovalChainId,
    string AuthorizedBy);

public interface IMesOperationTaskStartApprovalClient
{
    Task<MesOperationTaskStartApproval?> GetApprovedAsync(
        string approvalChainId,
        string organizationId,
        string environmentId,
        string operationTaskId,
        string workOrderId,
        CancellationToken cancellationToken);
}

public sealed class HttpMesOperationTaskStartApprovalClient(
    HttpClient httpClient,
    IInternalServiceTokenProvider tokenProvider) : IMesOperationTaskStartApprovalClient
{
    public async Task<MesOperationTaskStartApproval?> GetApprovedAsync(
        string approvalChainId,
        string organizationId,
        string environmentId,
        string operationTaskId,
        string workOrderId,
        CancellationToken cancellationToken)
    {
        var canonicalApprovalChainId = approvalChainId.Trim();
        if (string.IsNullOrWhiteSpace(canonicalApprovalChainId))
        {
            return null;
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/business/v1/approvals/chains/{Uri.EscapeDataString(canonicalApprovalChainId)}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenProvider.BearerToken);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        var envelope = await response.Content.ReadFromJsonAsync<ResponseDataEnvelope<ApprovalChainResponse>>(
            cancellationToken);
        var chain = envelope?.Data;
        if (chain is null ||
            string.IsNullOrWhiteSpace(chain.ChainId) ||
            !string.Equals(chain.ChainId.Trim(), canonicalApprovalChainId, StringComparison.Ordinal) ||
            !string.Equals(chain.Status, ApprovalChainStatuses.Approved, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(chain.OrganizationId, organizationId, StringComparison.Ordinal) ||
            !string.Equals(chain.EnvironmentId, environmentId, StringComparison.Ordinal) ||
            !string.Equals(chain.SourceService, ApprovalSourceServices.BusinessMes, StringComparison.Ordinal) ||
            !string.Equals(chain.DocumentType, ApprovalDocumentTypes.MesOperationTaskStartAuthorization, StringComparison.Ordinal) ||
            !string.Equals(chain.DocumentId, operationTaskId, StringComparison.Ordinal) ||
            !string.Equals(chain.DocumentLineId, workOrderId, StringComparison.Ordinal))
        {
            return null;
        }

        var decision = chain.Decisions
            .Where(x => string.Equals(x.Decision, ApprovalDecisions.Approve, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x.DecidedAtUtc)
            .FirstOrDefault();
        return decision is null || string.IsNullOrWhiteSpace(decision.ActorType) || string.IsNullOrWhiteSpace(decision.ActorRef)
            ? null
            : new MesOperationTaskStartApproval(canonicalApprovalChainId, $"{decision.ActorType}:{decision.ActorRef}");
    }

    private sealed record ApprovalChainResponse(
        string? ChainId,
        string OrganizationId,
        string EnvironmentId,
        string Status,
        string? SourceService,
        string? DocumentType,
        string DocumentId,
        string? DocumentLineId,
        IReadOnlyCollection<ApprovalDecisionResponse> Decisions);

    private sealed record ApprovalDecisionResponse(
        string DecisionId,
        int StepNo,
        string ActorType,
        string ActorRef,
        string Decision,
        DateTimeOffset DecidedAtUtc);

    private sealed record ResponseDataEnvelope<T>(T? Data, bool Success, string Message, int Code);
}
