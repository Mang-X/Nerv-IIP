using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Nerv.IIP.Business.Quality.Domain;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.Business.Quality.Web.Application.Approvals;

public interface IApprovalChainStatusClient
{
    Task<bool> IsApprovedForNcrDispositionAsync(
        string chainId,
        string organizationId,
        string environmentId,
        string ncrCode,
        CancellationToken cancellationToken);
}

public sealed class HttpApprovalChainStatusClient(
    HttpClient httpClient,
    IInternalServiceTokenProvider tokenProvider) : IApprovalChainStatusClient
{
    private static readonly string[] QualitySourceServices = [QualityFacts.ServiceName, "business-quality", "quality"];
    private static readonly string[] NcrDispositionDocumentTypes = ["nonconformance-report-disposition", "nonconformance-report", "quality-ncr"];

    public async Task<bool> IsApprovedForNcrDispositionAsync(
        string chainId,
        string organizationId,
        string environmentId,
        string ncrCode,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/business/v1/approvals/chains/{Uri.EscapeDataString(chainId)}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenProvider.BearerToken);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden)
        {
            return false;
        }

        response.EnsureSuccessStatusCode();
        var envelope = await response.Content.ReadFromJsonAsync<ResponseDataEnvelope<ApprovalChainStatusResponse>>(
            cancellationToken);
        return envelope?.Data is { } chain
            && string.Equals(chain.Status, "approved", StringComparison.OrdinalIgnoreCase)
            && string.Equals(chain.OrganizationId, organizationId, StringComparison.Ordinal)
            && string.Equals(chain.EnvironmentId, environmentId, StringComparison.Ordinal)
            && QualitySourceServices.Contains(chain.SourceService, StringComparer.OrdinalIgnoreCase)
            && NcrDispositionDocumentTypes.Contains(chain.DocumentType, StringComparer.OrdinalIgnoreCase)
            && string.Equals(chain.DocumentId, ncrCode, StringComparison.Ordinal);
    }

    private sealed record ApprovalChainStatusResponse(
        string ChainId,
        string OrganizationId,
        string EnvironmentId,
        string Status,
        string SourceService,
        string DocumentType,
        string DocumentId);

    private sealed record ResponseDataEnvelope<T>(T? Data, bool Success, string Message, int Code);
}
