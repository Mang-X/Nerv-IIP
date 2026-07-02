using System.Net.Http.Headers;
using System.Net.Http.Json;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.Business.Erp.Web.Application.Approval;

public sealed record PurchaseOrderApprovalRequest(
    string OrganizationId,
    string EnvironmentId,
    string TemplateCode,
    string SourceService,
    string DocumentType,
    string DocumentId,
    string? DocumentLineId,
    string StartedBy,
    string ChainId);

public sealed record PurchaseOrderApprovalResult(string ChainId);

public interface IPurchaseOrderApprovalClient
{
    Task<PurchaseOrderApprovalResult> StartApprovalAsync(PurchaseOrderApprovalRequest request, CancellationToken cancellationToken);
}

public sealed class HttpPurchaseOrderApprovalClient(
    HttpClient httpClient,
    IInternalServiceTokenProvider internalTokenProvider) : IPurchaseOrderApprovalClient
{
    public async Task<PurchaseOrderApprovalResult> StartApprovalAsync(PurchaseOrderApprovalRequest request, CancellationToken cancellationToken)
    {
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/api/business/v1/approvals/chains")
        {
            Content = JsonContent.Create(new StartApprovalChainHttpRequest(
                request.OrganizationId,
                request.EnvironmentId,
                request.TemplateCode,
                request.SourceService,
                request.DocumentType,
                request.DocumentId,
                request.DocumentLineId,
                request.StartedBy)),
        };
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", internalTokenProvider.BearerToken);

        using var response = await httpClient.SendAsync(httpRequest, cancellationToken);
        response.EnsureSuccessStatusCode();
        var envelope = await response.Content.ReadFromJsonAsync<ResponseDataEnvelope<StartApprovalChainHttpResponse>>(cancellationToken);
        if (envelope is null || !envelope.Success || envelope.Data is null)
        {
            throw new KnownException(envelope?.Message ?? "BusinessApproval did not return an approval chain id.");
        }

        return new PurchaseOrderApprovalResult(envelope.Data.ChainId);
    }

    private sealed record StartApprovalChainHttpRequest(
        string OrganizationId,
        string EnvironmentId,
        string TemplateCode,
        string SourceService,
        string DocumentType,
        string DocumentId,
        string? DocumentLineId,
        string StartedBy);

    private sealed record StartApprovalChainHttpResponse(string ChainId);

    private sealed record ResponseDataEnvelope<T>(T? Data, bool Success, string Message, int Code);
}
