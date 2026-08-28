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
        var (objectType, scannedObjectId) = request switch
        {
            { ScannedOperationTaskId: { } id } => (MesContextScanObjectType.OperationTask, id),
            { DeviceAssetId: { } id } => (MesContextScanObjectType.DeviceAsset, id),
            _ => (MesContextScanObjectType.Personnel, request.UserId!),
        };
        var objectName = objectType switch
        {
            MesContextScanObjectType.OperationTask => "operation-task",
            MesContextScanObjectType.DeviceAsset => "device-asset",
            MesContextScanObjectType.Personnel => "personnel",
            _ => throw new ArgumentOutOfRangeException(nameof(objectType), objectType, null),
        };
        var acceptedReason = $"{objectName}-scan-accepted";
        var invalidDecision = result.Decision == MesContextScanDecision.Accepted
            ? !string.Equals(result.ReasonCode, acceptedReason, StringComparison.Ordinal)
            : string.Equals(result.ReasonCode, acceptedReason, StringComparison.Ordinal);
        if (!string.Equals(result.WorkOrderId, request.WorkOrderId, StringComparison.Ordinal) ||
            !string.Equals(result.OperationTaskId, request.OperationTaskId, StringComparison.Ordinal) ||
            result.ObjectType != objectType ||
            !string.Equals(result.ScannedObjectId, scannedObjectId, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(result.ReasonCode) ||
            result.EvaluatedAtUtc == default ||
            invalidDecision)
        {
            throw BusinessServiceProxyException.FromSafeDownstreamMessage(
                HttpStatusCode.BadGateway,
                "mes-context-scan-prevalidation-invalid-response");
        }

        return result;
    }
}
