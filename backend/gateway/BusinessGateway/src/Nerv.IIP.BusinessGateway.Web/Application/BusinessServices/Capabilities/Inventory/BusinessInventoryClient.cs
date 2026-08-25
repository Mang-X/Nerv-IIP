using System.Globalization;
using Microsoft.Extensions.Options;
using Nerv.IIP.Contracts.Inventory;

namespace Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;

public interface IBusinessInventoryClient
{
    Task<LineSideInventoryBalancesResponse> ListLineSideBalancesAsync(
        string internalBearerToken,
        LineSideInventoryBalancesRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleInventoryDirectoryResponse> ListDirectoryAsync(
        string internalBearerToken,
        BusinessConsoleInventoryDirectoryRequest request,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException("Inventory directory client is not configured.");

    Task<BusinessConsoleInventoryStockBySourceResponse> GetStockBySourceAsync(
        string internalBearerToken,
        BusinessConsoleInventoryStockBySourceRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleInventoryAvailabilityResponse> GetAvailabilityAsync(
        string internalBearerToken,
        BusinessConsoleInventoryAvailabilityRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleInventoryExpiryAlertsResponse> ListExpiryAlertsAsync(
        string internalBearerToken,
        BusinessConsoleInventoryExpiryAlertsRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleInventoryMovementListResponse> ListMovementsAsync(
        string internalBearerToken,
        BusinessConsoleInventoryMovementListRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleInventoryCountTaskListResponse> ListCountTasksAsync(
        string internalBearerToken,
        BusinessConsoleInventoryCountTaskListRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleInventoryCountAdjustmentListResponse> ListCountAdjustmentsAsync(
        string internalBearerToken,
        BusinessConsoleInventoryCountAdjustmentListRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsolePostStockMovementResponse> PostMovementAsync(
        string internalBearerToken,
        BusinessConsolePostStockMovementRequest request,
        CancellationToken cancellationToken,
        IReadOnlyCollection<string>? forwardedPermissions = null);

    Task<BusinessConsoleCreateStockCountTaskResponse> CreateCountTaskAsync(
        string internalBearerToken,
        BusinessConsoleCreateStockCountTaskRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleConfirmStockCountAdjustmentResponse> ConfirmCountAdjustmentAsync(
        string internalBearerToken,
        string countTaskId,
        BusinessConsoleConfirmStockCountAdjustmentRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleRestartStockCountTaskResponse> RestartCountTaskAsync(
        string internalBearerToken,
        string countTaskId,
        CancellationToken cancellationToken);

    Task<BusinessConsoleCancelStockCountTaskResponse> CancelCountTaskAsync(
        string internalBearerToken,
        string countTaskId,
        string reason,
        CancellationToken cancellationToken);
}

public sealed class BusinessGatewayInventoryForwardedPermissionOptions
{
    public string Issuer { get; set; } = "business-gateway";

    public string? SigningKey { get; set; }
}

public sealed class HttpBusinessInventoryClient(
    HttpClient httpClient,
    IOptions<BusinessGatewayInventoryForwardedPermissionOptions> forwardedPermissionOptions)
    : BusinessServiceHttpClient(httpClient), IBusinessInventoryClient
{
    public Task<LineSideInventoryBalancesResponse> ListLineSideBalancesAsync(
        string internalBearerToken,
        LineSideInventoryBalancesRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<LineSideInventoryBalancesResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/inventory/v1/line-side-balances?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId),
                ("siteCode", request.SiteCode),
                ("locationCode", request.LocationCode),
                ("skuCode", request.SkuCode),
                ("asOfDate", request.AsOfDate),
                ("page", request.Page),
                ("pageSize", request.PageSize)),
            null,
            cancellationToken);

    public Task<BusinessConsoleInventoryDirectoryResponse> ListDirectoryAsync(
        string internalBearerToken,
        BusinessConsoleInventoryDirectoryRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleInventoryDirectoryResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/inventory/v1/directory?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId),
                ("directoryType", request.DirectoryType),
                ("keyword", request.Keyword),
                ("siteCode", request.SiteCode),
                ("skuCode", request.SkuCode),
                ("skip", request.Skip),
                ("take", request.Take)),
            null,
            cancellationToken,
            failClosedOnFailureEnvelope: true);

    public Task<BusinessConsoleInventoryStockBySourceResponse> GetStockBySourceAsync(
        string internalBearerToken,
        BusinessConsoleInventoryStockBySourceRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleInventoryStockBySourceResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/inventory/v1/movements/by-source?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId),
                ("sourceService", request.SourceService),
                ("sourceDocumentId", request.SourceDocumentId),
                ("sourceDocumentLineId", request.SourceDocumentLineId)),
            null,
            cancellationToken);

    public Task<BusinessConsoleInventoryAvailabilityResponse> GetAvailabilityAsync(
        string internalBearerToken,
        BusinessConsoleInventoryAvailabilityRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleInventoryAvailabilityResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/inventory/v1/availability?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId),
                ("skuCode", request.SkuCode),
                ("uomCode", request.UomCode),
                ("siteCode", request.SiteCode),
                ("locationCode", request.LocationCode),
                ("lotNo", request.LotNo),
                ("serialNo", request.SerialNo),
                ("qualityStatus", request.QualityStatus),
                ("ownerType", request.OwnerType),
                ("ownerId", request.OwnerId),
                ("asOfDate", request.AsOfDate)),
            null,
            cancellationToken);

    public Task<BusinessConsoleInventoryExpiryAlertsResponse> ListExpiryAlertsAsync(
        string internalBearerToken,
        BusinessConsoleInventoryExpiryAlertsRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleInventoryExpiryAlertsResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/inventory/v1/expiry-alerts?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId),
                ("siteCode", request.SiteCode),
                ("skuCode", request.SkuCode),
                ("locationCode", request.LocationCode),
                ("asOfDate", request.AsOfDate),
                ("nearExpiryThresholdDays", request.NearExpiryThresholdDays),
                ("includeZeroAvailable", TrueFlag(request.IncludeZeroAvailable)),
                ("page", request.Page),
                ("pageSize", request.PageSize)),
            null,
            cancellationToken);

    public Task<BusinessConsoleInventoryMovementListResponse> ListMovementsAsync(
        string internalBearerToken,
        BusinessConsoleInventoryMovementListRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleInventoryMovementListResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/inventory/v1/movements?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId),
                ("skuCode", request.SkuCode),
                ("siteCode", request.SiteCode),
                ("locationCode", request.LocationCode),
                ("lotNo", request.LotNo),
                ("movementType", request.MovementType),
                ("sourceService", request.SourceService),
                ("sourceDocumentId", request.SourceDocumentId),
                ("fromDate", request.FromDate),
                ("toDate", request.ToDate),
                ("page", request.Page),
                ("pageSize", request.PageSize)),
            null,
            cancellationToken);

    public Task<BusinessConsoleInventoryCountTaskListResponse> ListCountTasksAsync(
        string internalBearerToken,
        BusinessConsoleInventoryCountTaskListRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleInventoryCountTaskListResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/inventory/v1/count-tasks?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId),
                ("status", request.Status),
                ("skuCode", request.SkuCode),
                ("siteCode", request.SiteCode),
                ("locationCode", request.LocationCode),
                ("countTaskCode", request.CountTaskCode),
                ("page", request.Page),
                ("pageSize", request.PageSize)),
            null,
            cancellationToken);

    public Task<BusinessConsoleInventoryCountAdjustmentListResponse> ListCountAdjustmentsAsync(
        string internalBearerToken,
        BusinessConsoleInventoryCountAdjustmentListRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleInventoryCountAdjustmentListResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/inventory/v1/count-adjustments?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId),
                ("status", request.Status),
                ("countTaskCode", request.CountTaskCode),
                ("skuCode", request.SkuCode),
                ("page", request.Page),
                ("pageSize", request.PageSize)),
            null,
            cancellationToken);

    public Task<BusinessConsolePostStockMovementResponse> PostMovementAsync(
        string internalBearerToken,
        BusinessConsolePostStockMovementRequest request,
        CancellationToken cancellationToken,
        IReadOnlyCollection<string>? forwardedPermissions = null) =>
        SendAsync<BusinessConsolePostStockMovementResponse>(
            internalBearerToken,
            HttpMethod.Post,
            "/api/inventory/v1/movements",
            request,
            cancellationToken,
            configureRequest: httpRequest => AddForwardedPermissions(
                httpRequest,
                forwardedPermissions,
                request.OrganizationId,
                request.EnvironmentId,
                request.IdempotencyKey));

    public Task<BusinessConsoleCreateStockCountTaskResponse> CreateCountTaskAsync(
        string internalBearerToken,
        BusinessConsoleCreateStockCountTaskRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleCreateStockCountTaskResponse>(
            internalBearerToken,
            HttpMethod.Post,
            "/api/inventory/v1/count-tasks",
            request,
            cancellationToken);

    public Task<BusinessConsoleConfirmStockCountAdjustmentResponse> ConfirmCountAdjustmentAsync(
        string internalBearerToken,
        string countTaskId,
        BusinessConsoleConfirmStockCountAdjustmentRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleConfirmStockCountAdjustmentResponse>(
            internalBearerToken,
            HttpMethod.Post,
            $"/api/inventory/v1/count-tasks/{Uri.EscapeDataString(countTaskId)}/adjustments",
            new DownstreamConfirmStockCountAdjustmentRequest(
                countTaskId,
                request.CountedQuantity,
                request.IdempotencyKey),
            cancellationToken);

    public Task<BusinessConsoleRestartStockCountTaskResponse> RestartCountTaskAsync(
        string internalBearerToken,
        string countTaskId,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleRestartStockCountTaskResponse>(
            internalBearerToken,
            HttpMethod.Post,
            $"/api/inventory/v1/count-tasks/{Uri.EscapeDataString(countTaskId)}/recount",
            new DownstreamRestartStockCountTaskRequest(countTaskId),
            cancellationToken);

    public Task<BusinessConsoleCancelStockCountTaskResponse> CancelCountTaskAsync(
        string internalBearerToken,
        string countTaskId,
        string reason,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleCancelStockCountTaskResponse>(
            internalBearerToken,
            HttpMethod.Post,
            $"/api/inventory/v1/count-tasks/{Uri.EscapeDataString(countTaskId)}/cancel",
            new DownstreamCancelStockCountTaskRequest(countTaskId, reason),
            cancellationToken);

    private sealed record DownstreamConfirmStockCountAdjustmentRequest(
        string CountTaskId,
        decimal CountedQuantity,
        string IdempotencyKey);

    private sealed record DownstreamRestartStockCountTaskRequest(string CountTaskId);

    private sealed record DownstreamCancelStockCountTaskRequest(string CountTaskId, string Reason);

    private void AddForwardedPermissions(
        HttpRequestMessage request,
        IReadOnlyCollection<string>? forwardedPermissions,
        string organizationId,
        string environmentId,
        string requestKey)
    {
        if (forwardedPermissions is null || forwardedPermissions.Count == 0)
        {
            return;
        }

        var options = forwardedPermissionOptions.Value;
        if (string.IsNullOrWhiteSpace(options.SigningKey))
        {
            return;
        }

        var permissions = string.Join(' ', forwardedPermissions.Order(StringComparer.Ordinal));
        var issuedAtUnixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var signature = InventoryForwardedPermissionHeaders.CreateSignature(
            options.SigningKey,
            options.Issuer,
            permissions,
            organizationId,
            environmentId,
            requestKey,
            issuedAtUnixSeconds);
        request.Headers.TryAddWithoutValidation(InventoryForwardedPermissionHeaders.PermissionsHeaderName, permissions);
        request.Headers.TryAddWithoutValidation(InventoryForwardedPermissionHeaders.IssuerHeaderName, options.Issuer);
        request.Headers.TryAddWithoutValidation(InventoryForwardedPermissionHeaders.OrganizationHeaderName, organizationId);
        request.Headers.TryAddWithoutValidation(InventoryForwardedPermissionHeaders.EnvironmentHeaderName, environmentId);
        request.Headers.TryAddWithoutValidation(InventoryForwardedPermissionHeaders.RequestKeyHeaderName, requestKey);
        request.Headers.TryAddWithoutValidation(InventoryForwardedPermissionHeaders.IssuedAtHeaderName, issuedAtUnixSeconds.ToString(CultureInfo.InvariantCulture));
        request.Headers.TryAddWithoutValidation(InventoryForwardedPermissionHeaders.SignatureHeaderName, signature);
    }
}
