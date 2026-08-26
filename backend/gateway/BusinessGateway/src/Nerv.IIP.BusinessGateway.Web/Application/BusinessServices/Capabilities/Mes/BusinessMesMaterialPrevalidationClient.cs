using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Nerv.IIP.Contracts.Mes;

namespace Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;

public interface IBusinessMesMaterialPrevalidationClient
{
    Task<BusinessConsoleMesMaterialScanPrevalidationResponse> PrevalidateAsync(
        string internalBearerToken,
        string correlationId,
        BusinessConsoleMesMaterialScanPrevalidationRequest request,
        CancellationToken cancellationToken);
}

public sealed class HttpBusinessMesMaterialPrevalidationClient(HttpClient httpClient)
    : IBusinessMesMaterialPrevalidationClient
{
    public async Task<BusinessConsoleMesMaterialScanPrevalidationResponse> PrevalidateAsync(
        string internalBearerToken,
        string correlationId,
        BusinessConsoleMesMaterialScanPrevalidationRequest request,
        CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, "/api/business/v1/mes/material-scan-prevalidation");
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", internalBearerToken);
        message.Headers.TryAddWithoutValidation("X-Correlation-Id", correlationId);
        message.Content = JsonContent.Create(request);
        using var response = await httpClient.SendAsync(message, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw BusinessServiceProxyException.FromSafeDownstreamMessage(
                response.StatusCode,
                "mes-material-scan-prevalidation-failed");
        }

        try
        {
            var result = await response.Content.ReadFromJsonAsync<BusinessConsoleMesMaterialScanPrevalidationResponse>(
                cancellationToken: cancellationToken)
                ?? throw BusinessServiceProxyException.FromSafeDownstreamMessage(
                    HttpStatusCode.BadGateway,
                    "mes-material-scan-prevalidation-empty-response");
            var accepted = result.Decision == MesMaterialScanDecision.Accepted;
            var acceptedFactsAreInvalid = accepted &&
                (!string.Equals(result.ReasonCode, "material-scan-accepted", StringComparison.Ordinal) ||
                 string.IsNullOrWhiteSpace(result.MaterialId) ||
                 string.IsNullOrWhiteSpace(result.MaterialLotId) ||
                 (result.MaterialQualification is not "primary" and not "substitute"));
            var rejectedFactsAreInvalid = !accepted &&
                (string.Equals(result.ReasonCode, "material-scan-accepted", StringComparison.Ordinal) ||
                 result.MaterialQualification is not null);
            if (string.IsNullOrWhiteSpace(result.ReasonCode) ||
                string.IsNullOrWhiteSpace(result.MaterialIssueRequestId) ||
                string.IsNullOrWhiteSpace(result.WorkOrderId) ||
                string.IsNullOrWhiteSpace(result.OperationTaskId) ||
                !string.Equals(result.MaterialIssueRequestId, request.MaterialIssueRequestId, StringComparison.Ordinal) ||
                !string.Equals(result.WorkOrderId, request.WorkOrderId, StringComparison.Ordinal) ||
                !string.Equals(result.OperationTaskId, request.OperationTaskId, StringComparison.Ordinal) ||
                acceptedFactsAreInvalid ||
                rejectedFactsAreInvalid ||
                result.EvaluatedAtUtc == default)
            {
                throw BusinessServiceProxyException.FromSafeDownstreamMessage(
                    HttpStatusCode.BadGateway,
                    "mes-material-scan-prevalidation-invalid-response");
            }

            return result;
        }
        catch (JsonException)
        {
            throw BusinessServiceProxyException.FromSafeDownstreamMessage(
                HttpStatusCode.BadGateway,
                "mes-material-scan-prevalidation-invalid-response");
        }
        catch (NotSupportedException)
        {
            throw BusinessServiceProxyException.FromSafeDownstreamMessage(
                HttpStatusCode.BadGateway,
                "mes-material-scan-prevalidation-invalid-response");
        }
    }
}
