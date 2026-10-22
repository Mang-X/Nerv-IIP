using System.Net;
using Nerv.IIP.Contracts.Mes;

namespace Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;

public interface IBusinessMesMaterialPrevalidationClient
{
    Task<MesMaterialScanPrevalidationResponse> PrevalidateAsync(
        string internalBearerToken,
        string correlationId,
        MesMaterialScanPrevalidationRequest request,
        CancellationToken cancellationToken);
}

public sealed class HttpBusinessMesMaterialPrevalidationClient(HttpClient httpClient)
    : BusinessServiceHttpClient(httpClient), IBusinessMesMaterialPrevalidationClient
{
    public async Task<MesMaterialScanPrevalidationResponse> PrevalidateAsync(
        string internalBearerToken,
        string correlationId,
        MesMaterialScanPrevalidationRequest request,
        CancellationToken cancellationToken)
    {
        var result = await SendAsync<MesMaterialScanPrevalidationResponse>(
            internalBearerToken,
            HttpMethod.Post,
            "/api/business/v1/mes/material-scan-prevalidation",
            request,
            cancellationToken,
            configureRequest: message =>
                message.Headers.TryAddWithoutValidation("X-Correlation-Id", correlationId));
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
}
