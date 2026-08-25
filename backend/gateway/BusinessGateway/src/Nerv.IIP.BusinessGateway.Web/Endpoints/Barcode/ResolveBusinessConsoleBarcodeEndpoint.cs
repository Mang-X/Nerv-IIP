using FastEndpoints;
using FluentValidation;
using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.BusinessGateway.Web.Application.OpenApi;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.BusinessGateway.Web.Endpoints.Barcode;

public sealed record BusinessConsoleBarcodeResolveRequest(
    string OrganizationId,
    string EnvironmentId,
    string ScannedValue,
    int PageIndex = 1,
    int PageSize = 20);

public sealed record BusinessConsoleBarcodeResolveResponse(
    string Status,
    string? ReasonCode,
    IReadOnlyCollection<BusinessConsoleBarcodeResolveCandidate> Candidates,
    int Total);

public sealed record BusinessConsoleBarcodeResolveCandidate(
    string ObjectType,
    IReadOnlyDictionary<string, string> StrongIds,
    string Authority,
    string Source,
    DateTimeOffset ObservedAtUtc);

[Tags("Business Console Barcode")]
[HttpPost("/api/business-console/v1/barcode/resolve")]
[BusinessGatewayOperationId("resolveBusinessConsoleBarcode")]
public sealed class ResolveBusinessConsoleBarcodeEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessBarcodeResolverClient barcode,
    IBusinessMesClient mes,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleBarcodeResolveRequest, BusinessConsoleBarcodeResolveResponse>(
        auth,
        BusinessGatewayPermissions.BarcodeScansWrite)
{
    protected override string OrganizationId(BusinessConsoleBarcodeResolveRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleBarcodeResolveRequest request) => request.EnvironmentId;

    protected override async Task<BusinessConsoleBarcodeResolveResponse> ForwardAsync(
        BusinessConsoleBarcodeResolveRequest request,
        string bearerToken,
        CancellationToken cancellationToken)
    {
        var downstream = await barcode.ResolveAsync(
            tokenProvider.BearerToken,
            new BusinessBarcodeResolveRequest(
                request.OrganizationId,
                request.EnvironmentId,
                request.ScannedValue,
                checked((request.PageIndex - 1) * request.PageSize),
                request.PageSize),
            cancellationToken);
        if (downstream.Status is not ("resolved" or "ambiguous"))
        {
            return new BusinessConsoleBarcodeResolveResponse(
                downstream.Status,
                downstream.ReasonCode,
                [],
                downstream.Total);
        }

        var mapped = new List<BusinessConsoleBarcodeResolveCandidate>(downstream.Candidates.Count);
        foreach (var candidate in downstream.Candidates)
        {
            var resolved = await MapCandidateAsync(request, candidate, cancellationToken);
            if (resolved is null)
            {
                return new BusinessConsoleBarcodeResolveResponse(
                    "unsupported",
                    "resolved-object-type-unsupported",
                    [],
                    0);
            }

            mapped.Add(resolved);
        }

        return new BusinessConsoleBarcodeResolveResponse(
            downstream.Status,
            downstream.ReasonCode,
            mapped,
            downstream.Total);
    }

    private async Task<BusinessConsoleBarcodeResolveCandidate?> MapCandidateAsync(
        BusinessConsoleBarcodeResolveRequest request,
        BusinessBarcodeResolveCandidate candidate,
        CancellationToken cancellationToken)
    {
        var sourceType = candidate.SourceDocumentType.Trim().ToLowerInvariant();
        IReadOnlyDictionary<string, string>? strongIds = sourceType switch
        {
            "work-order" => StrongId("workOrderId", candidate.SourceDocumentId),
            "material-issue" => StrongId("materialIssueRequestId", candidate.SourceDocumentId),
            "finished-goods-receipt" => StrongId("finishedGoodsReceiptRequestId", candidate.SourceDocumentId),
            "work-center" => StrongId("workCenterId", candidate.SourceDocumentId),
            "device-asset" => StrongId("deviceAssetId", candidate.SourceDocumentId),
            "personnel" => StrongId("userId", candidate.SourceDocumentId),
            "inventory-batch" => StrongId("inventoryBatchId", candidate.SourceDocumentId),
            "inventory-location" => StrongId("inventoryLocationId", candidate.SourceDocumentId),
            "purchase-receipt" => StrongId("purchaseReceiptId", candidate.SourceDocumentId),
            "delivery-order" => StrongId("deliveryOrderId", candidate.SourceDocumentId),
            "operation-task" => await ResolveOperationIdsAsync(request, candidate.SourceDocumentId, cancellationToken),
            _ => null,
        };
        if (strongIds is null)
        {
            return null;
        }

        return new BusinessConsoleBarcodeResolveCandidate(
            ObjectType(sourceType),
            strongIds,
            candidate.Authority,
            "barcode-label-print-item",
            candidate.ObservedAtUtc);
    }

    private async Task<IReadOnlyDictionary<string, string>?> ResolveOperationIdsAsync(
        BusinessConsoleBarcodeResolveRequest request,
        string operationTaskId,
        CancellationToken cancellationToken)
    {
        var result = await mes.ListOperationTasksAsync(
            tokenProvider.BearerToken,
            new BusinessMesOperationTaskListRequest(
                request.OrganizationId,
                request.EnvironmentId,
                OperationTaskId: operationTaskId,
                Take: 2),
            cancellationToken);
        var exact = result.Items
            .Where(item => string.Equals(item.OperationTaskId, operationTaskId, StringComparison.Ordinal))
            .Take(2)
            .ToArray();
        return exact.Length == 1
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["workOrderId"] = exact[0].WorkOrderId,
                ["operationTaskId"] = exact[0].OperationTaskId,
            }
            : null;
    }

    private static IReadOnlyDictionary<string, string> StrongId(string name, string value) =>
        new Dictionary<string, string>(StringComparer.Ordinal) { [name] = value };

    private static string ObjectType(string sourceType) => sourceType switch
    {
        "work-order" => "mes-work-order",
        "operation-task" => "mes-operation",
        "material-issue" => "mes-material-issue-request",
        "finished-goods-receipt" => "mes-finished-goods-receipt-request",
        "work-center" => "work-center",
        "device-asset" => "equipment-device",
        "personnel" => "personnel",
        "inventory-batch" => "inventory-batch",
        "inventory-location" => "inventory-location",
        "purchase-receipt" => "erp-purchase-receipt",
        "delivery-order" => "erp-delivery-order",
        _ => throw new InvalidOperationException("Unsupported barcode source type."),
    };
}

public sealed class BusinessConsoleBarcodeResolveRequestValidator : Validator<BusinessConsoleBarcodeResolveRequest>
{
    public BusinessConsoleBarcodeResolveRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.ScannedValue).NotEmpty().MaximumLength(200);
        RuleFor(x => x.PageIndex).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
    }
}
