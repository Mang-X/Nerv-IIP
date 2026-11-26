using System.ComponentModel;
using FastEndpoints;

namespace Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;

public sealed record BusinessConsoleMesWorkOrderQualityHoldSummary(
    string SourceService,
    string SourceDocumentId,
    string Scope,
    bool IsActive,
    string? OperationTaskId,
    string? HoldReason,
    DateTimeOffset? HeldAtUtc,
    string? HeldBy,
    string? HeldInspectionRecordId,
    string? HeldInspectionDocumentId,
    string InspectionRecordId,
    DateTimeOffset? ReleasedAtUtc = null,
    string? ReleasedBy = null,
    string? ReleaseReason = null,
    string? ReleaseSource = null);

public sealed record BusinessConsoleMesForceReleaseQualityHoldRequest(
    [property: RouteParam] string SourceDocumentId,
    [property: QueryParam] string OrganizationId,
    [property: QueryParam] string EnvironmentId,
    string Reason,
    string? SourceService,
    DateTimeOffset? ReleasedAtUtc,
    string IdempotencyKey);

public sealed record BusinessConsoleMesQualityHoldTimelineRequest(
    [property: RouteParam] string SourceDocumentId,
    [property: QueryParam] string OrganizationId,
    [property: QueryParam] string EnvironmentId,
    [property: QueryParam] string SourceService);

public sealed record BusinessConsoleMesQualityHoldTimelineItem(
    // 时间线转移 id 以字符串透传:MES 端投影为 x.Id.ToString()(与本网关所有其他强类型 id 端点一致)。
    // 此前 MES 直投强类型 QualityHoldTransitionId、网关按 Guid 反序列化,真实响应无法解析导致 502。
    string TransitionId,
    string SourceService,
    string SourceDocumentId,
    string HoldCycleId,
    string CorrelationId,
    string EventKind,
    string Actor,
    DateTimeOffset OccurredAtUtc,
    string? Reason,
    string? SourceInspectionRecordId,
    string? SourceInspectionDocumentId,
    string Origin,
    string? IdempotencyKey);

public sealed record BusinessConsoleMesQualityHoldTimelineResponse(
    IReadOnlyCollection<BusinessConsoleMesQualityHoldTimelineItem> Items);

// Actor is intentionally omitted: the gateway binds the downstream reversal actor to the
// authenticated principal and ignores caller-supplied actor-like JSON properties.
public sealed record BusinessConsoleMesRecordDefectRequest(
    string OrganizationId,
    string EnvironmentId,
    string WorkOrderId,
    string OperationTaskId,
    string DefectCode,
    decimal DefectQuantity,
    string? MaterialLotId,
    string? BatchOrSerial,
    string IdempotencyKey);

public sealed record BusinessConsoleMesRecordDefectV2Request(
    string OrganizationId,
    string EnvironmentId,
    string WorkOrderId,
    string? OperationTaskId,
    string DefectCode,
    decimal Quantity,
    DateTimeOffset RecordedAtUtc,
    string IdempotencyKey,
    string ScopeKind,
    string ScopeId);

public sealed record BusinessMesRecordDefectRequest(
    string OrganizationId,
    string EnvironmentId,
    string WorkOrderId,
    string? OperationTaskId,
    string DefectCode,
    decimal Quantity,
    DateTimeOffset RecordedAtUtc,
    string IdempotencyKey);

public sealed record BusinessConsoleMesRelatedQualityItemListResponse(
    IReadOnlyCollection<BusinessConsoleMesRelatedQualityItemRow> Items,
    int Total);

public sealed record BusinessConsoleMesRelatedQualityItemRow(
    string QualityItemId,
    string SourceType,
    string SourceDocumentId,
    string Status,
    string? DefectCode,
    string? NcrId,
    string? WorkOrderId = null,
    string? OperationTaskId = null);

public sealed record BusinessConsoleMesTraceabilityByWorkOrderRequest(
    [property: RouteParam] string WorkOrderId,
    [property: QueryParam] string OrganizationId,
    [property: QueryParam] string EnvironmentId);

public sealed record BusinessConsoleMesTraceabilityByBatchRequest(
    [property: RouteParam] string BatchOrSerial,
    [property: QueryParam] string OrganizationId,
    [property: QueryParam] string EnvironmentId);

public sealed record BusinessConsoleMesTraceabilityByMaterialLotRequest(
    [property: RouteParam] string MaterialLotId,
    [property: QueryParam] string OrganizationId,
    [property: QueryParam] string EnvironmentId);

public sealed record BusinessConsoleMesTraceabilityResponse(
    IReadOnlyCollection<BusinessConsoleMesTraceabilityNode> Nodes,
    IReadOnlyCollection<BusinessConsoleMesTraceabilityEdge> Edges);

public sealed record BusinessConsoleMesTraceabilityNode(
    string NodeId,
    string NodeType,
    string DisplayName,
    string Status,
    [property: Description("该节点对应事实的发生时刻；主数据类节点没有单一发生时刻时为 null。")]
    DateTimeOffset? OccurredAtUtc = null);

public sealed record BusinessConsoleMesTraceabilityEdge(string FromNodeId, string ToNodeId, string RelationType);
