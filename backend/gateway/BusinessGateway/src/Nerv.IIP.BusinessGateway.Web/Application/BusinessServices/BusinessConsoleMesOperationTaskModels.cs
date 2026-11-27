using System.ComponentModel;
using FastEndpoints;

namespace Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;

public sealed record BusinessConsoleMesWipSummaryRequest(
    string OrganizationId,
    string EnvironmentId,
    string? Status = null,
    string? Keyword = null,
    string? WorkCenterId = null,
    string? ShiftId = null,
    string? DeviceAssetId = null,
    string? WorkOrderId = null,
    int Skip = 0,
    int Take = 100,
    string? OperationTaskId = null)
    : BusinessConsoleMesListRequest(OrganizationId, EnvironmentId, Status, Keyword,
        WorkCenterId, ShiftId, DeviceAssetId, WorkOrderId, Skip, Take);

/// <summary>派工任务列表专用请求：比通用 MES 列表多一个按被派工人过滤（PDA「我的任务」）。</summary>
public sealed record BusinessConsoleMesDispatchTaskListRequest(
    string OrganizationId,
    string EnvironmentId,
    string? Status = null,
    string? Keyword = null,
    string? WorkCenterId = null,
    string? ShiftId = null,
    string? DeviceAssetId = null,
    string? AssignedUserId = null,
    int Skip = 0,
    int Take = 100);

public sealed record BusinessConsoleMesOperationTaskListRequest(
    string OrganizationId,
    string EnvironmentId,
    string? Status = null,
    string? Keyword = null,
    string? WorkCenterId = null,
    string? ShiftId = null,
    string? DeviceAssetId = null,
    string? WorkOrderId = null,
    int Skip = 0,
    int Take = 100,
    string? ScopeKind = null,
    string? ScopeId = null,
    string? OperationTaskId = null);

public sealed record BusinessConsoleMesReportableOperationTaskListRequest(
    string OrganizationId,
    string EnvironmentId,
    string? Status = null,
    string? Keyword = null,
    string? WorkCenterId = null,
    string? ShiftId = null,
    string? DeviceAssetId = null,
    string? WorkOrderId = null,
    int Skip = 0,
    int Take = 100,
    string? ScopeKind = null,
    string? ScopeId = null);

public sealed record BusinessMesOperationTaskListRequest(
    string OrganizationId,
    string EnvironmentId,
    string? Status = null,
    string? Keyword = null,
    string? WorkCenterId = null,
    string? ShiftId = null,
    string? DeviceAssetId = null,
    string? WorkOrderId = null,
    int Skip = 0,
    int Take = 100,
    string? AssignedUserIds = null,
    string? TeamIds = null,
    string? WorkCenterIds = null,
    string? OperationTaskId = null);

public sealed record BusinessConsoleMesOperationTaskItem(
    string OperationTaskId,
    string Status,
    int OperationSequence,
    string WorkCenterId,
    IReadOnlyCollection<string> AlternativeWorkCenterIds,
    DateTimeOffset EarliestStartUtc,
    long DurationTicks,
    DateTimeOffset? ExistingStartUtc,
    DateTimeOffset? ExistingEndUtc,
    string? OperationTaskNo = null,
    string? WorkCenterCode = null,
    string? WorkCenterName = null,
    IReadOnlyCollection<string>? AllowedActions = null,
    IReadOnlyCollection<string>? BlockReasons = null,
    DateTimeOffset? EvaluatedAtUtc = null);

public sealed record BusinessConsoleMesDispatchTaskListResponse(
    IReadOnlyCollection<BusinessConsoleMesDispatchTaskRow> Items,
    int Total);

public sealed record BusinessConsoleMesDispatchTaskRow(
    string OperationTaskId,
    string WorkOrderId,
    string Status,
    string WorkCenterId,
    string? DeviceAssetId,
    string? ShiftId,
    string? AssignedUserId,
    string? AssignedUserName,
    DateTimeOffset? PlannedStartUtc,
    IReadOnlyCollection<string> BlockingReasons,
    string? WorkOrderNo = null,
    string? OperationTaskNo = null,
    string? WorkCenterCode = null,
    string? WorkCenterName = null,
    string? DeviceAssetCode = null,
    string? DeviceAssetName = null,
    string? OperationCode = null,
    DateTimeOffset? ScheduledAtUtc = null,
    string? ScheduleInvalidationReasonCode = null,
    string? TeamId = null,
    string? TeamName = null);

/// <summary>
/// Payload forwarded to the MES service. The assignee display name and the team the assignee belongs to
/// are resolved by the gateway from MasterData rather than trusted from the caller, so the dispatch
/// record keeps a verified snapshot.
/// </summary>
public sealed record BusinessConsoleMesAssignDispatchTaskForwardRequest(
    string OrganizationId,
    string EnvironmentId,
    string? AssignedUserId,
    string? AssignedUserName,
    string? DeviceAssetId,
    string? ShiftId,
    string? TeamId = null,
    string? TeamName = null,
    IReadOnlyCollection<BusinessConsoleMesDispatchParticipantForwardInput>? Participants = null);

public sealed record BusinessConsoleMesAssignDispatchTaskRequest(
    [property: RouteParam] string OperationTaskId,
    [property: QueryParam] string OrganizationId,
    [property: QueryParam] string EnvironmentId,
    string? AssignedUserId,
    string? DeviceAssetId,
    string? ShiftId,
    IReadOnlyCollection<BusinessConsoleMesDispatchParticipantRequest>? Participants = null);

public sealed record BusinessConsoleMesClaimOperationTaskRequest(
    [property: RouteParam] string OperationTaskId,
    [property: QueryParam] string OrganizationId,
    [property: QueryParam] string EnvironmentId,
    [property: QueryParam] string ScopeKind,
    [property: QueryParam] string ScopeId,
    string IdempotencyKey);

public sealed record BusinessConsoleMesClaimDispatchTaskForwardRequest(
    string OrganizationId,
    string EnvironmentId,
    string AssignedUserId,
    string AssignedUserName,
    string? DeviceAssetId,
    string? ShiftId,
    string IdempotencyKey,
    string? TeamId = null,
    string? TeamName = null);

public sealed record BusinessConsoleMesOperationTaskListResponse(
    IReadOnlyCollection<BusinessConsoleMesOperationTaskRow> Items,
    int Total);

public sealed record BusinessConsoleMesOperationTaskRow(
    string OperationTaskId,
    string WorkOrderId,
    string Status,
    int OperationSequence,
    string WorkCenterId,
    string? DeviceAssetId,
    string? ShiftId,
    string? AssignedUserId,
    string? AssignedUserName,
    DateTimeOffset? PlannedStartUtc,
    DateTimeOffset? StartedAtUtc,
    string QualityStatus,
    string? WorkOrderNo = null,
    string? OperationTaskNo = null,
    string? WorkCenterCode = null,
    string? WorkCenterName = null,
    string? DeviceAssetCode = null,
    string? DeviceAssetName = null,
    string? OperationCode = null,
    DateTimeOffset? ScheduledAtUtc = null,
    string? ScheduleInvalidationReasonCode = null,
    string? TeamId = null,
    string? TeamName = null,
    IReadOnlyCollection<string>? AllowedActions = null,
    IReadOnlyCollection<string>? BlockReasons = null,
    DateTimeOffset? EvaluatedAtUtc = null,
    [property: Description("工序完成后冻结的累计实际人工工时，单位为小时；工序未完成或冲销后重新打开时为 null。")]
    decimal? ActualLaborHours = null,
    [property: Description("工序完成后冻结的累计实际机器工时，单位为小时；工序未完成或冲销后重新打开时为 null。")]
    decimal? ActualMachineHours = null,
    string WorkOrderType = "standard",
    string? SourceWorkOrderId = null,
    string? SourceNcrId = null,
    string? SourceNcrCode = null,
    [property: Description("任务创建时保存的权威工艺路线号；无权威来源时为 null。")]
    string? RoutingCode = null,
    [property: Description("任务创建时保存的权威工艺路线修订；无权威来源时为 null。")]
    string? RoutingRevision = null);

public sealed record BusinessConsoleMesOperationTaskActionRequest(
    [property: RouteParam] string OperationTaskId,
    [property: QueryParam] string OrganizationId,
    [property: QueryParam] string EnvironmentId,
    string? ReasonCode,
    string IdempotencyKey,
    [property: QueryParam] string ScopeKind,
    [property: QueryParam] string ScopeId);

public sealed record BusinessConsoleMesOperationTaskActionResponse(
    string OperationTaskId,
    string Status,
    DateTimeOffset ChangedAtUtc,
    BusinessConsoleOperationReceipt? OperationReceipt = null);

public sealed record BusinessConsoleMesWipSummaryResponse(
    IReadOnlyCollection<BusinessConsoleMesWipSummaryRow> Items,
    int Total);

public sealed record BusinessConsoleMesWipSummaryRow(
    string WorkOrderId,
    string OperationTaskId,
    string WorkCenterId,
    string Status,
    decimal PlannedQuantity,
    decimal GoodQuantity,
    decimal ScrapQuantity,
    IReadOnlyCollection<string> BlockingReasons,
    string? WorkOrderNo = null,
    string? OperationTaskNo = null,
    string? WorkCenterCode = null,
    string? WorkCenterName = null);
