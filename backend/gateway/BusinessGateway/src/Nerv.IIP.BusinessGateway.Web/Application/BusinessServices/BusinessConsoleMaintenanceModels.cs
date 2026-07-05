namespace Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;

public sealed record BusinessConsoleMaintenanceContextRequest(
    string OrganizationId,
    string EnvironmentId);

public sealed record BusinessConsoleMaintenanceListRequest(
    string OrganizationId,
    string EnvironmentId,
    int Skip = 0,
    int Take = 100);

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
    string? AssetUnavailableReason,
    string? AssignedTechnicianUserId = null,
    int? EstimatedLaborMinutes = null);

public sealed record BusinessConsoleCreateMaintenanceWorkOrderResponse(string WorkOrderId);

public sealed record BusinessConsoleCompleteMaintenanceWorkOrderRequest(
    string OrganizationId,
    string EnvironmentId,
    string Result,
    string DowntimeReasonCode,
    int DowntimeMinutes,
    IReadOnlyCollection<BusinessConsoleMaintenanceSparePartInput> SpareParts,
    int? ActualLaborMinutes = null,
    decimal? SparePartCostAmount = null,
    decimal? ExternalServiceCostAmount = null,
    string? CostCurrencyCode = null);

public sealed record BusinessConsoleCompleteMaintenanceWorkOrderResponse(bool Accepted);

public sealed record BusinessConsoleCreateMaintenancePlanRequest(
    string OrganizationId,
    string EnvironmentId,
    string DeviceAssetId,
    string? PlanCode,
    string Interval,
    DateOnly StartsOn,
    string Owner,
    DateTimeOffset? WindowStartUtc,
    DateTimeOffset? WindowEndUtc,
    string? IdempotencyKey = null);

public sealed record BusinessConsoleCreateMaintenancePlanResponse(string PlanId);

public sealed record BusinessConsoleGenerateDueMaintenanceWorkOrdersRequest(
    string OrganizationId,
    string EnvironmentId,
    DateOnly BusinessDate,
    string RequestedBy);

public sealed record BusinessConsoleGenerateDueMaintenanceWorkOrdersResponse(
    int GeneratedCount,
    IReadOnlyCollection<string> WorkOrderIds);

public sealed record BusinessConsoleMaintenanceInspectionMeasurementInput(
    string CharacteristicCode,
    decimal MeasuredValue,
    string UomCode,
    decimal? LowerSpecLimit = null,
    decimal? UpperSpecLimit = null);

public sealed record BusinessConsoleRecordMaintenanceInspectionRequest(
    string OrganizationId,
    string EnvironmentId,
    string? PlanId,
    string? WorkOrderId,
    string Inspector,
    string Result,
    DateTimeOffset InspectedAtUtc,
    IReadOnlyCollection<BusinessConsoleMaintenanceInspectionMeasurementInput>? Measurements = null);

public sealed record BusinessConsoleRecordMaintenanceInspectionResponse(string InspectionId);

public sealed record BusinessConsoleMaintenanceWorkOrderListResponse(
    IReadOnlyCollection<BusinessConsoleMaintenanceWorkOrderItem> Items,
    int Skip,
    int Take,
    int Total);

public sealed record BusinessConsoleMaintenanceWorkOrderItem(
    string WorkOrderId,
    string DeviceAssetId,
    string Priority,
    string Status,
    string? SourceAlarmId,
    string? RelatedAlarmId,
    DateTimeOffset OpenedAtUtc,
    string? AssignedTechnicianUserId = null,
    int? EstimatedLaborMinutes = null,
    int? ActualLaborMinutes = null,
    decimal? SparePartCostAmount = null,
    decimal? ExternalServiceCostAmount = null,
    string? CostCurrencyCode = null);

public sealed record BusinessConsoleMaintenancePlanListResponse(
    IReadOnlyCollection<BusinessConsoleMaintenancePlanItem> Items,
    int Skip,
    int Take,
    int Total);

public sealed record BusinessConsoleMaintenancePlanItem(
    string PlanId,
    string DeviceAssetId,
    string PlanCode,
    string Interval,
    DateOnly StartsOn);

public sealed record BusinessConsoleMaintenanceInspectionListResponse(
    IReadOnlyCollection<BusinessConsoleMaintenanceInspectionItem> Items,
    int Skip,
    int Take,
    int Total);

public sealed record BusinessConsoleMaintenanceInspectionItem(
    string InspectionId,
    string? PlanId,
    string? WorkOrderId,
    string Inspector,
    string Result,
    DateTimeOffset InspectedAtUtc,
    IReadOnlyCollection<BusinessConsoleMaintenanceInspectionMeasurementItem>? Measurements = null);

public sealed record BusinessConsoleMaintenanceInspectionMeasurementItem(
    string CharacteristicCode,
    decimal MeasuredValue,
    string UomCode,
    decimal? LowerSpecLimit,
    decimal? UpperSpecLimit,
    bool IsWithinSpec);

public sealed record BusinessConsoleMaintenanceSparePartListResponse(
    IReadOnlyCollection<BusinessConsoleMaintenanceSparePartItem> Items,
    int Skip,
    int Take,
    int Total);

public sealed record BusinessConsoleMaintenanceSparePartItem(
    string SparePartLineId,
    string WorkOrderId,
    string DeviceAssetId,
    string SkuCode,
    decimal Quantity,
    string? UomCode);

public sealed record BusinessConsoleCreateMaintenanceSparePartRequest(
    string OrganizationId,
    string EnvironmentId,
    string WorkOrderId,
    string SkuCode,
    decimal Quantity,
    string? UomCode);

public sealed record BusinessConsoleCreateMaintenanceSparePartResponse(string SparePartLineId);

public sealed record BusinessConsoleQueryMaintenanceAssetReliabilityRequest(
    string OrganizationId,
    string EnvironmentId,
    DateTimeOffset WindowStartUtc,
    DateTimeOffset WindowEndUtc);

public sealed record BusinessConsoleQueryMaintenanceReliabilitySummaryRequest(
    string OrganizationId,
    string EnvironmentId,
    DateTimeOffset WindowStartUtc,
    DateTimeOffset WindowEndUtc,
    string? DeviceAssetId = null,
    string? TechnicianUserId = null);

public sealed record BusinessConsoleMaintenanceReliabilitySummaryResponse(
    string OrganizationId,
    string EnvironmentId,
    DateTimeOffset WindowStartUtc,
    DateTimeOffset WindowEndUtc,
    IReadOnlyCollection<BusinessConsoleMaintenanceReliabilitySummaryItem> Items);

public sealed record BusinessConsoleMaintenanceReliabilitySummaryItem(
    string DeviceAssetId,
    string? AssignedTechnicianUserId,
    string? CostCurrencyCode,
    int WorkOrderCount,
    int CompletedWorkOrderCount,
    int EstimatedLaborMinutes,
    int ActualLaborMinutes,
    decimal SparePartCostAmount,
    decimal ExternalServiceCostAmount,
    decimal TotalCostAmount);

public sealed record BusinessConsoleQueryMaintenanceInspectionMeasurementTrendRequest(
    string OrganizationId,
    string EnvironmentId,
    string DeviceAssetId,
    string CharacteristicCode,
    DateTimeOffset WindowStartUtc,
    DateTimeOffset WindowEndUtc);

public sealed record BusinessConsoleMaintenanceInspectionMeasurementTrendResponse(
    string OrganizationId,
    string EnvironmentId,
    string DeviceAssetId,
    string CharacteristicCode,
    DateTimeOffset WindowStartUtc,
    DateTimeOffset WindowEndUtc,
    IReadOnlyCollection<BusinessConsoleMaintenanceInspectionMeasurementTrendItem> Items);

public sealed record BusinessConsoleMaintenanceInspectionMeasurementTrendItem(
    string InspectionId,
    string? PlanId,
    string? WorkOrderId,
    DateTimeOffset InspectedAtUtc,
    decimal MeasuredValue,
    string UomCode,
    decimal? LowerSpecLimit,
    decimal? UpperSpecLimit,
    bool IsWithinSpec);

public sealed record BusinessConsoleAssetReliabilityResponse(
    string OrganizationId,
    string EnvironmentId,
    string DeviceAssetId,
    DateTimeOffset WindowStartUtc,
    DateTimeOffset WindowEndUtc,
    int FailureCount,
    int RepairCount,
    decimal? MtbfHours,
    decimal? MttrMinutes,
    string MtbfRuntimeSource,
    bool MtbfRuntimeHasSamples);
