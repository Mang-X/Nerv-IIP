using System.Net;

namespace Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;

public sealed record BusinessBarcodeResolveRequest(
    string OrganizationId,
    string EnvironmentId,
    string ScannedValue,
    int Skip,
    int Take);

public sealed record BusinessBarcodeResolveResponse(
    string Status,
    string? ReasonCode,
    IReadOnlyCollection<BusinessBarcodeResolveCandidate> Candidates,
    int Total);

public sealed record BusinessBarcodeResolveCandidate(
    string SourceDocumentType,
    string SourceDocumentId,
    string Authority,
    DateTimeOffset ObservedAtUtc);

public interface IBusinessBarcodeResolverClient
{
    Task<BusinessBarcodeResolveResponse> ResolveAsync(
        string internalBearerToken,
        BusinessBarcodeResolveRequest request,
        CancellationToken cancellationToken);
}

public sealed class HttpBusinessBarcodeResolverClient(HttpClient httpClient)
    : BusinessServiceHttpClient(httpClient), IBusinessBarcodeResolverClient
{
    public async Task<BusinessBarcodeResolveResponse> ResolveAsync(
        string internalBearerToken,
        BusinessBarcodeResolveRequest request,
        CancellationToken cancellationToken)
    {
        var response = await SendAsync<BusinessBarcodeResolveResponse>(
            internalBearerToken,
            HttpMethod.Post,
            "/api/business/v1/barcodes/resolve",
            request,
            cancellationToken,
            failClosedOnFailureEnvelope: true);
        if (!IsValid(response))
        {
            throw BusinessServiceProxyException.FromSafeDownstreamMessage(
                HttpStatusCode.BadGateway,
                "downstream-invalid-response");
        }

        return response;
    }

    private static bool IsValid(BusinessBarcodeResolveResponse response)
    {
        if (response.Candidates is null || response.Total < 0 || response.Candidates.Count > response.Total)
        {
            return false;
        }

        var validStatus = response.Status switch
        {
            "resolved" => response.Total == 1 && response.Candidates.Count == 1,
            "ambiguous" => response.Total > 1,
            "unknown" or "unsupported" or "forbidden" => response.Total == 0 && response.Candidates.Count == 0,
            _ => false,
        };
        return validStatus && response.Candidates.All(candidate =>
            !string.IsNullOrWhiteSpace(candidate.SourceDocumentType) &&
            !string.IsNullOrWhiteSpace(candidate.SourceDocumentId) &&
            !string.IsNullOrWhiteSpace(candidate.Authority) &&
            candidate.ObservedAtUtc != default);
    }
}
