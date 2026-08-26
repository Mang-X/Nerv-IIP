using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;

public interface IBusinessMesMaterialPrevalidationClient
{
    Task<BusinessConsoleMesMaterialScanPrevalidationResponse> PrevalidateAsync(
        string internalBearerToken,
        BusinessConsoleMesMaterialScanPrevalidationRequest request,
        CancellationToken cancellationToken);
}

public sealed class HttpBusinessMesMaterialPrevalidationClient(HttpClient httpClient)
    : IBusinessMesMaterialPrevalidationClient
{
    public async Task<BusinessConsoleMesMaterialScanPrevalidationResponse> PrevalidateAsync(
        string internalBearerToken,
        BusinessConsoleMesMaterialScanPrevalidationRequest request,
        CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, "/api/business/v1/mes/material-scan-prevalidation");
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", internalBearerToken);
        message.Content = JsonContent.Create(request);
        using var response = await httpClient.SendAsync(message, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw BusinessServiceProxyException.FromSafeDownstreamMessage(
                response.StatusCode,
                "mes-material-scan-prevalidation-failed");
        }

        return await response.Content.ReadFromJsonAsync<BusinessConsoleMesMaterialScanPrevalidationResponse>(
            cancellationToken: cancellationToken)
            ?? throw BusinessServiceProxyException.FromSafeDownstreamMessage(
                HttpStatusCode.BadGateway,
                "mes-material-scan-prevalidation-empty-response");
    }
}

public sealed record BusinessConsoleMesMaterialScanPrevalidationRequest(
    string OrganizationId,
    string EnvironmentId,
    string MaterialIssueRequestId,
    string WorkOrderId,
    string OperationTaskId);

public sealed record BusinessConsoleMesMaterialScanPrevalidationResponse(
    string Decision,
    string ReasonCode,
    string MaterialIssueRequestId,
    string WorkOrderId,
    string OperationTaskId,
    string? MaterialId,
    string? MaterialLotId,
    string? MaterialQualification,
    DateTimeOffset EvaluatedAtUtc);
