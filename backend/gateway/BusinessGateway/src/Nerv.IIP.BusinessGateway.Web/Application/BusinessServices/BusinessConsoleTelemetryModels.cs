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

public sealed record BusinessConsoleTelemetryAlarmListRequest(
    string OrganizationId,
    string EnvironmentId,
    string? DeviceAssetId,
    string? Status,
    int Skip = 0,
    int Take = 100);

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
    int Take = 100);

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
