using System.Text.Json;
using System.Text.Json.Serialization;
using FastEndpoints;

namespace Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;

public sealed record BusinessConsoleMesWorkOrderListRequest(
    string OrganizationId,
    string EnvironmentId,
    string? Status = null,
    string? Keyword = null,
    string? WorkCenterId = null,
    string? ShiftId = null,
    string? DeviceAssetId = null,
    int Skip = 0,
    int Take = 100,
    string? WorkCenterIds = null,
    string? DeviceAssetIds = null,
    string? Statuses = null,
    string? ScopeKind = null,
    string? ScopeId = null);

public sealed record BusinessMesWorkOrderListRequest(
    string OrganizationId,
    string EnvironmentId,
    string? Status = null,
    string? Keyword = null,
    string? WorkCenterId = null,
    string? ShiftId = null,
    string? DeviceAssetId = null,
    int Skip = 0,
    int Take = 100,
    string? AssignedUserIds = null,
    string? TeamIds = null,
    string? WorkCenterIds = null,
    string? DeviceAssetIds = null,
    string? Statuses = null,
    string? WorkOrderId = null,
    string? AuthorizedAssignedUserIds = null,
    string? AuthorizedTeamIds = null,
    string? AuthorizedWorkCenterIds = null);

public sealed record BusinessConsoleMesProductionPlanListRequest(
    string OrganizationId,
    string EnvironmentId,
    string? Status = null,
    string? Keyword = null,
    string? WorkCenterId = null,
    string? ShiftId = null,
    string? DeviceAssetId = null,
    string? Source = null,
    string? ReadinessStatus = null,
    int Skip = 0,
    int Take = 100);

public sealed record BusinessConsoleMesWorkOrderListResponse(
    IReadOnlyCollection<BusinessConsoleMesWorkOrderItem> Items,
    int Total);

public sealed record BusinessConsoleMesWorkOrderItem(
    string WorkOrderId,
    string SkuId,
    string? ProductionVersionId,
    decimal Quantity,
    int Priority,
    DateTimeOffset DueUtc,
    string Status,
    IReadOnlyCollection<BusinessConsoleMesOperationTaskItem> OperationTasks,
    string? WorkOrderNo = null,
    string? SkuCode = null,
    bool HasActiveQualityHold = false,
    string WorkOrderType = "standard",
    string? SourceWorkOrderId = null,
    string? SourceNcrId = null,
    string? SourceNcrCode = null,
    bool HasChangedDemand = false,
    bool HasCancelledDemand = false,
    bool IsRush = false,
    decimal CompletedQuantity = 0,
    BusinessConsoleMesSourcePlanReference? SourcePlanReference = null,
    BusinessConsoleMesCommercialSourceFacts? CommercialSourceFacts = null);

public sealed record BusinessConsoleCreateRushWorkOrderRequest(
    string OrganizationId,
    string EnvironmentId,
    string? WorkOrderId,
    string SkuId,
    string? ProductionVersionId,
    decimal Quantity,
    DateTimeOffset DueUtc,
    string WorkCenterId,
    string? OperationTaskId,
    int? OperationSequence,
    int DurationMinutes,
    string? IdempotencyKey = null);

public sealed record BusinessConsoleCreateRushWorkOrderResponse(string WorkOrderId);

public sealed record BusinessConsoleMesProductionPlanListResponse(
    IReadOnlyCollection<BusinessConsoleMesProductionPlanRow> Items,
    int Total);

public sealed record BusinessConsoleMesProductionPlanRow(
    string ProductionPlanId,
    string SourceSystem,
    string SourceDocumentType,
    string SourceDocumentId,
    string? SourceDemandReference,
    string SkuId,
    decimal PlannedQuantity,
    string UomCode,
    string Status,
    string ReadinessStatus,
    IReadOnlyCollection<string> BlockingReasons,
    DateTimeOffset? PlannedStartUtc,
    DateTimeOffset? PlannedEndUtc);

public sealed record BusinessConsoleMesProductionPlanReadinessRequest(
    [property: RouteParam] string ProductionPlanId,
    [property: QueryParam] string OrganizationId,
    [property: QueryParam] string EnvironmentId);

public sealed record BusinessConsoleMesConvertPlanToWorkOrderRequest(
    [property: RouteParam] string ProductionPlanId,
    [property: QueryParam] string OrganizationId,
    [property: QueryParam] string EnvironmentId,
    string? WorkOrderId,
    string SkuId,
    string? ProductionVersionId,
    decimal PlannedQuantity,
    string UomCode,
    string? WorkCenterId,
    DateTimeOffset? DueUtc,
    string? SourceSystem = null,
    string? SourceDocumentType = null,
    string? SourceDocumentId = null,
    string? SourceDemandReference = null,
    string? IdempotencyKey = null);

public sealed record BusinessConsoleMesWorkOrderDetailRequest(
    [property: RouteParam] string WorkOrderId,
    [property: QueryParam] string OrganizationId,
    [property: QueryParam] string EnvironmentId,
    [property: QueryParam] string? ScopeKind = null,
    [property: QueryParam] string? ScopeId = null);

public sealed record BusinessConsoleMesWorkOrderDetailResponse(
    string WorkOrderId,
    string SkuId,
    string? ProductionVersionId,
    decimal Quantity,
    string Status,
    string ReadinessStatus,
    IReadOnlyCollection<string> BlockingReasons,
    IReadOnlyCollection<BusinessConsoleMesOperationTaskRow> OperationTasks,
    BusinessConsoleMesSourcePlanReference? SourcePlanReference = null,
    IReadOnlyCollection<BusinessConsoleMesWorkOrderQualityHoldSummary>? QualityHolds = null,
    string WorkOrderType = "standard",
    string? SourceWorkOrderId = null,
    string? SourceNcrId = null,
    string? SourceNcrCode = null,
    bool IsRush = false,
    int Priority = 0,
    decimal CompletedQuantity = 0,
    BusinessConsoleMesCommercialSourceFacts? CommercialSourceFacts = null);

public sealed record BusinessConsoleMesSourcePlanReference(
    string SourceSystem,
    string SourceDocumentType,
    string SourceDocumentId,
    string? SourceDemandReference,
    IReadOnlyCollection<string>? SourceDemandReferences = null);

public sealed record BusinessConsoleMesAdjustWorkOrderPriorityRequest(
    [property: RouteParam] string WorkOrderId,
    [property: QueryParam] string OrganizationId,
    [property: QueryParam] string EnvironmentId,
    bool IsRush,
    int Priority,
    DateTimeOffset? ChangedAtUtc,
    [property: QueryParam] string? ScopeKind = null,
    [property: QueryParam] string? ScopeId = null);

public sealed record BusinessConsoleMesReleaseWorkOrderRequest(
    [property: RouteParam] string WorkOrderId,
    [property: QueryParam] string OrganizationId,
    [property: QueryParam] string EnvironmentId,
    bool ConfirmWarnings,
    [property: QueryParam] string? ScopeKind = null,
    [property: QueryParam] string? ScopeId = null);

public sealed record BusinessConsoleMesWorkOrderReasonRequest(
    [property: RouteParam] string WorkOrderId,
    [property: QueryParam] string OrganizationId,
    [property: QueryParam] string EnvironmentId,
    string Reason,
    DateTimeOffset? ChangedAtUtc,
    [property: QueryParam] string? ScopeKind = null,
    [property: QueryParam] string? ScopeId = null);

public sealed record BusinessConsoleMesCloseWorkOrderRequest(
    [property: RouteParam] string WorkOrderId,
    [property: QueryParam] string OrganizationId,
    [property: QueryParam] string EnvironmentId,
    DateTimeOffset? ClosedAtUtc,
    [property: QueryParam] string? ScopeKind = null,
    [property: QueryParam] string? ScopeId = null);

public sealed record BusinessConsoleMesEngineeringChangeDecisionRequest(
    [property: RouteParam] string WorkOrderId,
    [property: QueryParam] string OrganizationId,
    [property: QueryParam] string EnvironmentId,
    string ChangeNumber,
    string Decision,
    string Reason,
    [property: QueryParam] string? ScopeKind = null,
    [property: QueryParam] string? ScopeId = null);

// Actor is intentionally omitted: the gateway injects the authenticated principal as the
// force-release audit actor so a caller cannot forge the releaser identity via the request body.
[JsonConverter(typeof(BusinessConsoleMesCommercialSourceStatusJsonConverter))]
public enum BusinessConsoleMesCommercialSourceStatus
{
    Available,
    Forbidden,
}

public sealed class BusinessConsoleMesCommercialSourceStatusJsonConverter()
    : JsonStringEnumConverter<BusinessConsoleMesCommercialSourceStatus>(JsonNamingPolicy.CamelCase, allowIntegerValues: false);

public sealed record BusinessConsoleMesCommercialSourceFacts(
    BusinessConsoleMesCommercialSourceStatus Status,
    IReadOnlyCollection<BusinessConsoleMesSalesOrderLink> SalesOrders);

public sealed record BusinessConsoleMesSalesOrderLink(
    string SalesOrderNo,
    string SourceLineReference,
    string? CustomerCode);
