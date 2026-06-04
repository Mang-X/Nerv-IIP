namespace Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;

public sealed record BusinessConsoleWmsListRequest(
    string OrganizationId,
    string EnvironmentId);

public sealed record BusinessConsoleWmsInboundOrderListRequest(
    string OrganizationId,
    string EnvironmentId,
    string? SkuCode,
    string? UomCode,
    string? SiteCode,
    string? LocationCode,
    string? LotNo,
    string? SerialNo,
    string? QualityStatus,
    string? OwnerType,
    string? OwnerId);

public sealed record BusinessConsoleWmsInboundOrderListResponse(
    IReadOnlyCollection<BusinessConsoleWmsInboundOrderItem> Items,
    BusinessConsoleWmsInventoryContext? InventoryContext,
    string SourceStatus);

public sealed record BusinessConsoleWmsInboundOrderItem(
    string InboundOrderId,
    string InboundOrderNo,
    string Status,
    DateTime CreatedAtUtc);

public sealed record BusinessConsoleWmsOutboundOrderListResponse(
    IReadOnlyCollection<BusinessConsoleWmsOutboundOrderItem> Items);

public sealed record BusinessConsoleWmsOutboundOrderItem(
    string OutboundOrderId,
    string OutboundOrderNo,
    string Status,
    DateTime CreatedAtUtc);

public sealed record BusinessConsoleWmsWcsTaskListRequest(
    string OrganizationId,
    string EnvironmentId,
    string? ExternalTaskId,
    string? WarehouseTaskId);

public sealed record BusinessConsoleWmsWcsTaskListResponse(
    IReadOnlyCollection<BusinessConsoleWmsWcsTaskItem> Items);

public sealed record BusinessConsoleWmsWcsTaskItem(
    string WcsTaskId,
    string OrganizationId,
    string EnvironmentId,
    string WarehouseTaskId,
    string AdapterType,
    string ExternalTaskId,
    string Status,
    int AttemptCount,
    string? FailureCode,
    string? FailureMessage,
    DateTime DispatchedAtUtc,
    DateTime? FailedAtUtc,
    DateTime? CompletedAtUtc);

public sealed record BusinessConsoleWmsInventoryContext(
    string Source,
    string Status,
    string? PermissionCode,
    string? Reason,
    string? SkuCode,
    string? UomCode,
    string? SiteCode,
    string? LocationCode,
    string? LotNo,
    string? SerialNo,
    string? QualityStatus,
    string? OwnerType,
    string? OwnerId,
    decimal? OnHandQuantity,
    decimal? ReservedQuantity,
    decimal? AvailableQuantity,
    IReadOnlyCollection<BusinessConsoleInventoryAvailabilityLineResponse> Items);
