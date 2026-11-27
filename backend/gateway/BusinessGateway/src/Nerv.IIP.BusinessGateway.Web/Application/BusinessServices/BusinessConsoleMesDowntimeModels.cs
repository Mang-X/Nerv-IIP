using FastEndpoints;

namespace Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;

/// <summary>停机事件列表请求：在通用 MES 列表过滤之上增加按停机原因码过滤（#1947）。</summary>
public sealed record BusinessConsoleMesDowntimeEventListRequest(
    string OrganizationId,
    string EnvironmentId,
    string? Status = null,
    string? Keyword = null,
    string? WorkCenterId = null,
    string? ShiftId = null,
    string? DeviceAssetId = null,
    string? WorkOrderId = null,
    string? ReasonCode = null,
    DateTimeOffset? WindowStartUtc = null,
    DateTimeOffset? WindowEndUtc = null,
    int Skip = 0,
    int Take = 100);

public sealed record BusinessConsoleMesDowntimeEventListResponse(
    IReadOnlyCollection<BusinessConsoleMesDowntimeEventRow> Items,
    int Total,
    IReadOnlyCollection<BusinessConsoleMesDowntimeReasonSummaryRow> ReasonSummary);

/// <summary>停机时长按原因分类汇总；不受 reasonCode 过滤影响，供停机读面做原因构成。</summary>
public sealed record BusinessConsoleMesDowntimeReasonSummaryRow(
    string ReasonCode,
    int OpenCount,
    decimal DurationMinutes,
    string? ReasonName = null);

public sealed record BusinessConsoleMesDowntimeEventRow(
    string DowntimeEventId,
    string? WorkOrderId,
    string? OperationTaskId,
    string? DeviceAssetId,
    string Status,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? RecoveredAtUtc,
    string? WorkOrderNo = null,
    string? OperationTaskNo = null,
    string? DeviceAssetCode = null,
    string? DeviceAssetName = null,
    string? WorkCenterId = null,
    string? ReasonCode = null,
    string? ReasonName = null);

public sealed record BusinessConsoleMesRecordDowntimeEventRequest(
    string OrganizationId,
    string EnvironmentId,
    string WorkOrderId,
    string? OperationTaskId,
    string? DeviceAssetId,
    string ReasonCode,
    DateTimeOffset StartedAtUtc,
    string IdempotencyKey);

public sealed record BusinessConsoleMesRecordDowntimeEventV2Request(
    string OrganizationId,
    string EnvironmentId,
    string WorkOrderId,
    string? OperationTaskId,
    string WorkCenterId,
    string? DeviceAssetId,
    string ReasonCode,
    DateTimeOffset StartedAtUtc,
    string IdempotencyKey,
    string ScopeKind,
    string ScopeId,
    DateTimeOffset? ToUtc = null);

public sealed record BusinessMesRecordDowntimeEventRequest(
    string OrganizationId,
    string EnvironmentId,
    string WorkOrderId,
    string? OperationTaskId,
    string WorkCenterId,
    string? DeviceAssetId,
    string ReasonCode,
    DateTimeOffset StartedAtUtc,
    string IdempotencyKey,
    DateTimeOffset? ToUtc = null);

public sealed record BusinessConsoleMesRecoverDowntimeEventRequest(
    [property: RouteParam] string DowntimeEventId,
    [property: QueryParam] string OrganizationId,
    [property: QueryParam] string EnvironmentId,
    DateTimeOffset RecoveredAtUtc);


public sealed record BusinessConsoleMesCapacityImpactListResponse(
    IReadOnlyCollection<BusinessConsoleMesCapacityImpactRow> Items,
    int Total);

public sealed record BusinessConsoleMesCapacityImpactRow(
    string ImpactId,
    string WorkCenterId,
    string? DeviceAssetId,
    string Status,
    DateTimeOffset EffectiveFromUtc,
    DateTimeOffset? EffectiveToUtc,
    string ReasonCode,
    string? WorkCenterCode = null,
    string? WorkCenterName = null,
    string? DeviceAssetCode = null,
    string? DeviceAssetName = null);
