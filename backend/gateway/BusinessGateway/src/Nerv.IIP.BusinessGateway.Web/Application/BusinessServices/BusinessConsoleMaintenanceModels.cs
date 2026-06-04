namespace Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;

public sealed record BusinessConsoleMaintenanceContextRequest(
    string OrganizationId,
    string EnvironmentId);

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
