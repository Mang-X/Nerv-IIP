using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;

namespace Nerv.IIP.BusinessGateway.Web.Application.BusinessServices.Quality;

public interface IBusinessQualityScrapReasonCodeClient
{
    Task<BusinessConsoleQualityReasonListResponse> ListScrapQualityReasonCodesAsync(
        string internalBearerToken,
        BusinessConsoleScrapQualityReasonCodeListRequest request,
        CancellationToken cancellationToken);
}

public sealed class HttpBusinessQualityScrapReasonCodeClient(HttpClient httpClient)
    : BusinessServiceHttpClient(httpClient), IBusinessQualityScrapReasonCodeClient
{
    public Task<BusinessConsoleQualityReasonListResponse> ListScrapQualityReasonCodesAsync(
        string internalBearerToken,
        BusinessConsoleScrapQualityReasonCodeListRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleQualityReasonListResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/quality/scrap-reason-codes?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId),
                ("search", request.Search),
                ("skip", request.Skip),
                ("take", request.Take)),
            null,
            cancellationToken,
            failClosedOnFailureEnvelope: true);
}
