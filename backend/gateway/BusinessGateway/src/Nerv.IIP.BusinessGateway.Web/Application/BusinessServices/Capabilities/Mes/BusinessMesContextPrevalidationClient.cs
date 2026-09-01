using System.Net;
using Nerv.IIP.Contracts.Mes;

namespace Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;

public interface IBusinessMesContextPrevalidationClient
{
    Task<MesContextScanPrevalidationResponse> PrevalidateAsync(
        string internalBearerToken,
        string correlationId,
        MesContextScanPrevalidationRequest request,
        CancellationToken cancellationToken);
}

public sealed class HttpBusinessMesContextPrevalidationClient(HttpClient httpClient)
    : BusinessServiceHttpClient(httpClient), IBusinessMesContextPrevalidationClient
{
    public async Task<MesContextScanPrevalidationResponse> PrevalidateAsync(
        string internalBearerToken,
        string correlationId,
        MesContextScanPrevalidationRequest request,
        CancellationToken cancellationToken)
    {
        var result = await SendAsync<MesContextScanPrevalidationResponse>(
            internalBearerToken,
            HttpMethod.Post,
            "/api/business/v1/mes/context-scan-prevalidation",
            request,
            cancellationToken,
            configureRequest: message =>
                message.Headers.TryAddWithoutValidation("X-Correlation-Id", correlationId));
        if (!string.Equals(result.WorkOrderId, request.WorkOrderId, StringComparison.Ordinal) ||
            !string.Equals(result.OperationTaskId, request.OperationTaskId, StringComparison.Ordinal) ||
            result.ObjectType != request.ObjectType ||
            !string.Equals(result.ScannedObjectId, request.ScannedObjectId, StringComparison.Ordinal))
        {
            throw BusinessServiceProxyException.FromSafeDownstreamMessage(
                HttpStatusCode.BadGateway,
                "mes-context-scan-prevalidation-invalid-response");
        }

        return result;
    }
}
