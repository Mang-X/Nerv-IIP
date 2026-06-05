namespace Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;

public sealed record BusinessConsoleTelemetryTagListRequest(
    string OrganizationId,
    string EnvironmentId,
    string? DeviceAssetId);

public sealed record BusinessConsoleTelemetryTagListResponse(
    IReadOnlyCollection<BusinessConsoleTelemetryTagItem> Items);

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
    bool? IsEnabled);

public sealed record BusinessConsoleTelemetryAlarmRuleListResponse(
    IReadOnlyCollection<BusinessConsoleTelemetryAlarmRuleItem> Items);

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
    string? Status);

public sealed record BusinessConsoleTelemetryAlarmEventListResponse(
    IReadOnlyCollection<BusinessConsoleTelemetryAlarmEventItem> Items);

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
    string ExternalAlarmId);

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
    decimal PerformanceRate,
    decimal QualityRate,
    decimal OeeRate,
    bool PerformanceRateEstimated,
    bool QualityRateEstimated);
