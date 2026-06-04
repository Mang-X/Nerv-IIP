namespace Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;

public sealed record BusinessConsoleMaintenanceContextRequest(
    string OrganizationId,
    string EnvironmentId);

public sealed record BusinessConsoleMaintenanceSparePartInput(
    string SkuCode,
    decimal Quantity,
    string? UomCode);

public sealed record BusinessConsoleCreateMaintenanceWorkOrderRequest(
    string OrganizationId,
    string EnvironmentId,
    string DeviceAssetId,
    string Priority,
    string? SourceAlarmId,
    string OpenedBy,
    string? AssetUnavailableReason);

public sealed record BusinessConsoleCreateMaintenanceWorkOrderResponse(string WorkOrderId);

public sealed record BusinessConsoleCompleteMaintenanceWorkOrderRequest(
    string OrganizationId,
    string EnvironmentId,
    string Result,
    string DowntimeReasonCode,
    int DowntimeMinutes,
    IReadOnlyCollection<BusinessConsoleMaintenanceSparePartInput> SpareParts);

public sealed record BusinessConsoleCompleteMaintenanceWorkOrderResponse(bool Accepted);

public sealed record BusinessConsoleCreateMaintenancePlanRequest(
    string OrganizationId,
    string EnvironmentId,
    string DeviceAssetId,
    string PlanCode,
    string Interval,
    DateOnly StartsOn,
    string Owner,
    DateTimeOffset? WindowStartUtc,
    DateTimeOffset? WindowEndUtc);

public sealed record BusinessConsoleCreateMaintenancePlanResponse(string PlanId);

public sealed record BusinessConsoleRecordMaintenanceInspectionRequest(
    string OrganizationId,
    string EnvironmentId,
    string? PlanId,
    string? WorkOrderId,
    string Inspector,
    string Result,
    DateTimeOffset InspectedAtUtc);

public sealed record BusinessConsoleRecordMaintenanceInspectionResponse(string InspectionId);

public sealed record BusinessConsoleMaintenanceWorkOrderListResponse(
    IReadOnlyCollection<BusinessConsoleMaintenanceWorkOrderItem> Items);

public sealed record BusinessConsoleMaintenanceWorkOrderItem(
    string WorkOrderId,
    string DeviceAssetId,
    string Priority,
    string Status,
    string? SourceAlarmId,
    string? RelatedAlarmId,
    DateTimeOffset OpenedAtUtc);

public sealed record BusinessConsoleMaintenancePlanListResponse(
    IReadOnlyCollection<BusinessConsoleMaintenancePlanItem> Items);

public sealed record BusinessConsoleMaintenancePlanItem(
    string PlanId,
    string DeviceAssetId,
    string PlanCode,
    string Interval,
    DateOnly StartsOn);
