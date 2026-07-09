namespace Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;

public sealed record BusinessConsoleTelemetryTagListRequest(
    string OrganizationId,
    string EnvironmentId,
    string? DeviceAssetId,
    int Skip = 0,
    int Take = 100);

public sealed record BusinessConsoleTelemetryTagListResponse(
    IReadOnlyCollection<BusinessConsoleTelemetryTagItem> Items,
    int Total = 0);

public sealed record BusinessConsoleTelemetryTagItem(
    string TelemetryTagId,
    string OrganizationId,
    string EnvironmentId,
    string DeviceAssetId,
    string TagKey,
    string ValueType,
    string UnitCode,
    string SamplingPolicy);

public sealed record BusinessConsoleTelemetryAlarmRuleListRequest(
    string OrganizationId,
    string EnvironmentId,
    string? DeviceAssetId,
    bool? IsEnabled,
    int Skip = 0,
    int Take = 100);

public sealed record BusinessConsoleTelemetryAlarmRuleListResponse(
    IReadOnlyCollection<BusinessConsoleTelemetryAlarmRuleItem> Items,
    int Total = 0);

public sealed record BusinessConsoleTelemetryAlarmRuleItem(
    string AlarmRuleId,
    string OrganizationId,
    string EnvironmentId,
    string DeviceAssetId,
    string RuleCode,
    string AlarmCode,
    string Severity,
    string TagKey,
    string ComparisonOperator,
    decimal ThresholdValue,
    string UnitCode,
    bool IsEnabled,
    DateTimeOffset UpdatedAtUtc);

public sealed record BusinessConsoleCreateOrUpdateTelemetryAlarmRuleRequest(
    string OrganizationId,
    string EnvironmentId,
    string DeviceAssetId,
    string RuleCode,
    string AlarmCode,
    string Severity,
    string TagKey,
    string ComparisonOperator,
    decimal ThresholdValue,
    string UnitCode,
    bool IsEnabled);

public sealed record BusinessConsoleCreateOrUpdateTelemetryAlarmRuleResponse(
    string AlarmRuleId);

// RequestedBy is intentionally omitted: the gateway injects the authenticated principal
// as the command requester so callers cannot forge the device-control audit actor.
public sealed record BusinessConsoleTelemetryDeviceControlCommandRequest(
    string OrganizationId,
    string EnvironmentId,
    string ConnectorHostId,
    string InstanceKey,
    string DeviceAssetId,
    string CommandType,
    string? TagKey,
    string? Value,
    IReadOnlyDictionary<string, string>? Parameters,
    string Reason,
    string IdempotencyKey,
    string CorrelationId);

public sealed record BusinessConsoleTelemetryDeviceControlCommandResponse(
    string OperationTaskId,
    string Status,
    BusinessConsoleTelemetryOperationApprovalSummary? Approval);

// Mirrors Nerv.IIP.Contracts.Ops.OperationApprovalSummary; the gateway does not
// reference Contracts.Ops, so the shape is duplicated here as a passthrough DTO.
public sealed record BusinessConsoleTelemetryOperationApprovalSummary(
    string Status,
    string RequestedBy,
    DateTimeOffset RequestedAtUtc,
    string? DecidedBy,
    DateTimeOffset? DecidedAtUtc,
    string? DecisionReason);

// DeviceAssetId scopes the single-command read to a device resource: device control audit is a
// device-resource surface, so the gateway authorizes on device-asset and the service verifies the
// command belongs to that device rather than letting any org/env reader resolve it by command id.
public sealed record BusinessConsoleTelemetryDeviceControlCommandContextRequest(
    string OrganizationId,
    string EnvironmentId,
    string DeviceAssetId);

public sealed record BusinessConsoleTelemetryDeviceControlCommandListRequest(
    string OrganizationId,
    string EnvironmentId,
    string? DeviceAssetId,
    string? Status,
    DateTimeOffset? FromUtc,
    DateTimeOffset? ToUtc,
    int Skip = 0,
    int Take = 100);

public sealed record BusinessConsoleTelemetryDeviceControlCommandListResponse(
    IReadOnlyCollection<BusinessConsoleTelemetryDeviceControlCommandListItem> Items,
    int Total = 0);

public sealed record BusinessConsoleTelemetryDeviceControlCommandListItem(
    string CommandId,
    string OperationTaskId,
    string OrganizationId,
    string EnvironmentId,
    string ConnectorHostId,
    string DeviceAssetId,
    string CommandType,
    string? TagKey,
    string? Value,
    string RequestedBy,
    string Reason,
    string Status,
    string? ApprovalStatus,
    string CorrelationId,
    DateTimeOffset RequestedAtUtc);

public sealed record BusinessConsoleTelemetryDeviceControlCommandDetail(
    string CommandId,
    string OperationTaskId,
    string OrganizationId,
    string EnvironmentId,
    string ConnectorHostId,
    string InstanceKey,
    string DeviceAssetId,
    string CommandType,
    string? TagKey,
    string? Value,
    IReadOnlyDictionary<string, string>? Parameters,
    string RequestedBy,
    string Reason,
    string CorrelationId,
    string IdempotencyKey,
    DateTimeOffset RequestedAtUtc,
    string Status,
    bool StatusFromLiveOps,
    BusinessConsoleTelemetryOperationApprovalSummary? Approval,
    string? CurrentAttemptId,
    IReadOnlyList<BusinessConsoleTelemetryDeviceControlCommandAttempt> Attempts);

public sealed record BusinessConsoleTelemetryDeviceControlCommandAttempt(
    string AttemptId,
    string Status,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? FinishedAtUtc,
    string? FailureCode,
    IReadOnlyDictionary<string, string>? Output);

public sealed record BusinessConsoleTelemetryAlarmListRequest(
    string OrganizationId,
    string EnvironmentId,
    string? DeviceAssetId,
    string? Status,
    int Skip = 0,
    int Take = 100,
    string? DeviceAssetIds = null);

public sealed record BusinessConsoleTelemetryAlarmEventListResponse(
    IReadOnlyCollection<BusinessConsoleTelemetryAlarmEventItem> Items,
    int Total = 0);

public sealed record BusinessConsoleTelemetryAlarmEventItem(
    string AlarmEventId,
    string OrganizationId,
    string EnvironmentId,
    string DeviceAssetId,
    string AlarmCode,
    string Severity,
    string Status,
    DateTimeOffset RaisedAtUtc,
    DateTimeOffset? ClearedAtUtc,
    string ExternalAlarmId,
    DateTimeOffset? AcknowledgedAtUtc = null,
    string? AcknowledgedBy = null,
    DateTimeOffset? ShelvedAtUtc = null,
    DateTimeOffset? ShelvedUntilUtc = null,
    string? ShelvedBy = null,
    string? ShelveReason = null,
    DateTimeOffset? EscalatedAtUtc = null,
    string? EscalationReason = null,
    IReadOnlyCollection<string>? EscalationRecipientRefs = null);

public sealed record BusinessConsoleEquipmentAlarmListRequest(
    string OrganizationId,
    string EnvironmentId,
    string? DeviceAssetId,
    string? Status,
    int Skip = 0,
    int Take = 100,
    string? DeviceAssetIds = null);

public sealed record BusinessConsoleEquipmentAlarmListPageResponse(
    IReadOnlyCollection<BusinessConsoleTelemetryAlarmEventItem> Items,
    int Total = 0);

public sealed record BusinessConsoleAcknowledgeAlarmRequest(
    string OrganizationId,
    string EnvironmentId,
    DateTimeOffset AcknowledgedAtUtc,
    string AcknowledgedBy);

public sealed record BusinessConsoleShelveAlarmRequest(
    string OrganizationId,
    string EnvironmentId,
    DateTimeOffset ShelvedAtUtc,
    int DurationMinutes,
    string ShelvedBy,
    string? Reason);

public sealed record BusinessConsoleUnshelveAlarmRequest(
    string OrganizationId,
    string EnvironmentId,
    DateTimeOffset? UnshelvedAtUtc);

public sealed record BusinessConsoleAlarmLifecycleResponse(string AlarmEventId);

public sealed record BusinessConsoleTelemetryHistoryRequest(
    string OrganizationId,
    string EnvironmentId,
    DateTimeOffset? FromUtc,
    DateTimeOffset? ToUtc);

public sealed record BusinessConsoleTelemetryHistoryResponse(
    IReadOnlyCollection<BusinessConsoleTelemetryHistoryItem> Items);

public sealed record BusinessConsoleTelemetryHistoryItem(
    string ItemType,
    string DeviceAssetId,
    string? TagKey,
    string Value,
    DateTimeOffset OccurredAtUtc);

public sealed record BusinessConsoleTelemetryOeeRequest(
    string OrganizationId,
    string EnvironmentId,
    string DeviceAssetId,
    DateTimeOffset WindowStartUtc,
    DateTimeOffset WindowEndUtc);

public sealed record BusinessConsoleTelemetryOeeResponse(
    string OrganizationId,
    string EnvironmentId,
    string DeviceAssetId,
    DateTimeOffset WindowStartUtc,
    DateTimeOffset WindowEndUtc,
    int StateSampleCount,
    decimal AvailabilityRate,
    decimal LoadingRate,
    decimal PerformanceRate,
    decimal QualityRate,
    decimal OeeRate,
    bool PerformanceRateEstimated,
    bool QualityRateEstimated);
