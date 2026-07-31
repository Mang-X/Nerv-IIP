using System.Text.Json;
using System.Text.Json.Serialization;
using Nerv.IIP.Contracts.IntegrationEvents;

namespace Nerv.IIP.Contracts.Scheduling;

public static class SchedulingWorkbenchLimits
{
    public const int MaxOrderCount = 500;
}

public static class SchedulingJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };
}

public sealed record SchedulingProblemContract(
    int ContractVersion,
    string ProblemId,
    string OrganizationId,
    string EnvironmentId,
    DateTimeOffset HorizonStartUtc,
    DateTimeOffset HorizonEndUtc,
    IReadOnlyCollection<SchedulingOrderContract> Orders,
    IReadOnlyCollection<SchedulingResourceContract> Resources,
    IReadOnlyCollection<SchedulingCalendarContract> Calendars,
    IReadOnlyCollection<SchedulingUnavailabilityWindowContract> UnavailabilityWindows,
    IReadOnlyCollection<SchedulingMaterialReadinessContract> MaterialReadiness,
    IReadOnlyCollection<SchedulingQualityBlockContract> QualityBlocks,
    IReadOnlyCollection<SchedulingLockedAssignmentContract> LockedAssignments,
    // 设备数据风险(软约束):设备没有运行时快照 / 快照已过期 / 采集源不可达。
    // 「不知道」不等于「不可用」——它不进 UnavailabilityWindows(那里只放真实停机与维护),
    // 只作为风险随计划带出,提示排产员这台设备的状态是盲区。
    IReadOnlyCollection<SchedulingEquipmentDataRiskContract>? EquipmentDataRisks = null);

public sealed record SchedulingOrderContract(
    string OrderId,
    string SkuCode,
    decimal Quantity,
    DateTimeOffset DueUtc,
    int Priority,
    bool IsRush,
    IReadOnlyCollection<SchedulingOperationContract> Operations,
    string? BusinessReference = null);

public sealed record SchedulingOperationContract(
    string OperationId,
    int OperationSequence,
    IReadOnlyCollection<string> PredecessorOperationIds,
    int DurationMinutes,
    string RequiredCapabilityCode,
    IReadOnlyCollection<string> EligibleResourceIds,
    string? PrimaryResourceId,
    DateTimeOffset EarliestStartUtc,
    DateTimeOffset DueUtc,
    int Priority,
    bool IsRush,
    ScheduleSplitPolicyContract SplitPolicy,
    DateTimeOffset? MaterialReadyUtc,
    string? QualityBlockReason,
    string? SourceReference,
    int SetupMinutes = 0,
    IReadOnlyCollection<string>? RequiredSkillCodes = null,
    IReadOnlyCollection<string>? RequiredToolingIds = null,
    bool ToolingAvailable = true);

public sealed record SchedulingResourceContract(
    string ResourceId,
    string WorkCenterId,
    IReadOnlyCollection<string> CapabilityCodes,
    int CapacityUnits,
    string CalendarId,
    string SortKey);

public sealed record SchedulingCalendarContract(
    string CalendarId,
    IReadOnlyCollection<SchedulingTimeWindowContract> ShiftWindows);

public sealed record SchedulingTimeWindowContract(
    DateTimeOffset StartUtc,
    DateTimeOffset EndUtc,
    string ReasonCode);

public sealed record SchedulingUnavailabilityWindowContract(
    string? ResourceId,
    string? WorkCenterId,
    DateTimeOffset StartUtc,
    DateTimeOffset EndUtc,
    string ReasonCode);

/// <summary>
/// 设备数据风险(软约束):某台设备在某段窗口内「状态未知」——没有运行时快照、快照已过期,
/// 或采集源当时不可达。这是数据盲区,不是设备真的不能干活,所以它不阻断排程,
/// 只作为风险随计划带出,由排产员决定是否人工确认设备状态。
/// </summary>
public sealed record SchedulingEquipmentDataRiskContract(
    string ResourceId,
    string? WorkCenterId,
    string ReasonCode,
    DateTimeOffset StartUtc,
    DateTimeOffset EndUtc,
    string? SourceReferenceLabel = null);

/// <summary>
/// 设备「状态未知」的口径:软约束(默认)= 照排 + 带设备数据风险标记;
/// 硬约束 = 沿用旧行为,状态未知即全窗不可用(会把无快照设备整台排除)。
/// 真实的停机/维护窗口(Unavailable)在两种口径下都是硬阻,不受此开关影响。
/// </summary>
public enum SchedulingEquipmentUnknownModeContract
{
    Soft = 0,
    Hard = 1
}

public sealed record SchedulingMaterialReadinessContract(
    string ScopeType,
    string ScopeId,
    DateTimeOffset? MaterialReadyUtc,
    bool IsReady,
    IReadOnlyCollection<string> ReasonCodes,
    IReadOnlyCollection<SchedulingMaterialShortageContract>? Shortages = null);

/// <summary>
/// 单项物料缺口(排程读面用):缺哪个物料、需要多少、可用多少、缺口多少。
/// 排产把物料当软约束,缺口只作为「开工前必须补齐」的风险随计划带出,不再阻断排程。
/// </summary>
public sealed record SchedulingMaterialShortageContract(
    string MaterialId,
    string? MaterialLotId,
    decimal RequiredQuantity,
    decimal AvailableQuantity,
    decimal ShortageQuantity);

/// <summary>
/// 物料约束口径:软约束(默认)= 可排 + 带物料风险标记;硬约束 = 缺料直接不可排。
/// 产品裁决:齐套是开工门槛(MES 侧硬门),不是排产门槛。
/// </summary>
public enum SchedulingMaterialConstraintModeContract
{
    Soft = 0,
    Hard = 1
}

/// <summary>
/// 工艺路线上「该工序需要质检」这一标记的排产口径:软约束(默认)= 照排 + 预警级冲突,
/// 质量放行由 MES/质量侧在开工与流转时把关;硬约束 = 沿用旧行为,带质检标记的工序直接不可排。
/// 产品裁决与物料(#1318)、设备状态未知(#1325)同源:质检要求是开工/放行门槛,不是排产门槛。
/// 注意:真实下达的质量封锁(<see cref="SchedulingQualityBlockContract"/>,针对具体工序或资源)
/// 在两种口径下都是硬阻,不受此开关影响——那是已经发生的封锁,不是路线上的常规检验要求。
/// </summary>
public enum SchedulingQualityConstraintModeContract
{
    Soft = 0,
    Hard = 1
}

public sealed record SchedulingQualityBlockContract(
    string ScopeType,
    string ScopeId,
    string ReasonCode,
    DateTimeOffset? BlockedUntilUtc);

public sealed record SchedulingLockedAssignmentContract(
    string AssignmentId,
    string OrderId,
    string OperationId,
    int OperationSequence,
    string ResourceId,
    string WorkCenterId,
    DateTimeOffset StartUtc,
    DateTimeOffset EndUtc,
    string LockReasonCode);

public sealed record SchedulePlanContract(
    int ContractVersion,
    string PlanId,
    string ProblemId,
    string ProblemFingerprint,
    string AlgorithmVersion,
    SchedulePlanStatusContract Status,
    DateTimeOffset GeneratedAtUtc,
    SchedulePlanMetricsContract Metrics,
    IReadOnlyCollection<ScheduleAssignmentContract> Assignments,
    IReadOnlyCollection<ScheduleResourceLoadContract> ResourceLoads,
    IReadOnlyCollection<ScheduleConflictContract> Conflicts,
    IReadOnlyCollection<UnscheduledOperationContract> UnscheduledOperations,
    IReadOnlyCollection<ScheduleChangeContract> ChangeSummary,
    IReadOnlyCollection<GanttScheduleItemContract> GanttItems,
    IReadOnlyCollection<SchedulePlanCalendarContract>? Calendars = null,
    IReadOnlyCollection<SchedulePlanBlockWindowContract>? BlockWindows = null,
    IReadOnlyCollection<SchedulePlanMaterialRiskContract>? MaterialRisks = null,
    IReadOnlyCollection<SchedulePlanEquipmentRiskContract>? EquipmentRisks = null);

/// <summary>
/// 设备数据风险(软约束):工序已排到这台设备上,但该设备在计划窗口内没有可信的运行时状态
/// (无快照 / 快照过期 / 采集源不可达)。开工前建议人工确认设备可用。
/// </summary>
public sealed record SchedulePlanEquipmentRiskContract(
    string OrderId,
    string OperationId,
    string ResourceId,
    IReadOnlyCollection<string> ReasonCodes,
    string Message);

/// <summary>
/// 物料风险(软约束):工序已排入计划,但开工前必须先把这些物料补齐,否则 MES 侧齐套硬门会拦住开工。
/// </summary>
public sealed record SchedulePlanMaterialRiskContract(
    string OrderId,
    string OperationId,
    IReadOnlyCollection<string> ReasonCodes,
    IReadOnlyCollection<SchedulingMaterialShortageContract> Shortages,
    string Message);

/// <summary>
/// 计划所依据的工作日历(投影自排程问题的班次窗口),供读面画工作日/非工作日与班次边界。
/// 一份日历可被多台资源 / 多个工作中心共用,故带出使用它的资源与工作中心清单。
/// </summary>
public sealed record SchedulePlanCalendarContract(
    string CalendarId,
    IReadOnlyCollection<string> ResourceIds,
    IReadOnlyCollection<string> WorkCenterIds,
    IReadOnlyCollection<SchedulePlanShiftWindowContract> ShiftWindows);

/// <summary>单个班次工作窗口。窗口之外即非工作时间。</summary>
public sealed record SchedulePlanShiftWindowContract(
    DateTimeOffset StartUtc,
    DateTimeOffset EndUtc,
    string ShiftCode);

/// <summary>
/// 计划期内资源不可用窗口:设备维护 / 计划停机 / 换线 / 换型。
/// <see cref="ReasonCode"/> 保留上游原始码值,<see cref="Kind"/> 是读面用的归类。
/// </summary>
public sealed record SchedulePlanBlockWindowContract(
    string? ResourceId,
    string? WorkCenterId,
    DateTimeOffset StartUtc,
    DateTimeOffset EndUtc,
    string ReasonCode,
    ScheduleBlockKindContract Kind);

public enum ScheduleBlockKindContract
{
    Maintenance = 0,
    Downtime = 1,
    LineChange = 2,
    Changeover = 3
}

public sealed record SchedulePlanImpactContract(
    bool IsInvalidated,
    string? ReasonCode,
    string? SourceEventType,
    string? SourceEventId,
    DateTimeOffset? OccurredAtUtc,
    IReadOnlyCollection<string> AffectedResourceIds,
    IReadOnlyCollection<string> AffectedWorkOrderIds,
    IReadOnlyCollection<string> AffectedOperationIds);

public sealed record SchedulePlanComparisonContract(
    string BasePlanId,
    string CandidatePlanId,
    SchedulePlanMetricsContract BaseMetrics,
    SchedulePlanMetricsContract CandidateMetrics,
    int MovedOperationCount,
    int LockedOperationCount,
    int UnscheduledOperationCount);

public sealed record SchedulePlanRevisionContract(
    SchedulePlanContract Candidate,
    SchedulePlanImpactContract Impact,
    SchedulePlanComparisonContract Comparison);

public sealed record SchedulePlanMetricsContract(
    int ScheduledOperationCount,
    int UnscheduledOperationCount,
    int AssignedMinutes,
    int MakespanMinutes,
    int TotalTardinessMinutes,
    int LateOperationCount,
    decimal OnTimeRate,
    decimal AverageResourceUtilization,
    int LockedOperationCount = 0,
    int OptimizableOperationCount = 0,
    int MaterialRiskOperationCount = 0,
    int EquipmentRiskOperationCount = 0);

public sealed record ScheduleAssignmentContract(
    string AssignmentId,
    string OrderId,
    string OperationId,
    int OperationSequence,
    string ResourceId,
    string WorkCenterId,
    DateTimeOffset StartUtc,
    DateTimeOffset EndUtc,
    bool IsLocked,
    string ExplanationCode,
    string? StandardOperationCode = null);

public sealed record ScheduleResourceLoadContract(
    string ResourceId,
    DateTimeOffset WindowStartUtc,
    DateTimeOffset WindowEndUtc,
    int AssignedMinutes,
    int AvailableMinutes,
    decimal Utilization);

public sealed record ScheduleConflictContract(
    string ConflictId,
    ScheduleConflictReasonCodeContract ReasonCode,
    ScheduleConflictSeverityContract Severity,
    string? OrderId,
    string? OperationId,
    string? ResourceId,
    string Message);

public sealed record UnscheduledOperationContract(
    string OrderId,
    string OperationId,
    ScheduleConflictReasonCodeContract ReasonCode,
    string Message);

public sealed record ScheduleChangeContract(
    string OrderId,
    string OperationId,
    ScheduleChangeTypeContract ChangeType,
    string Message);

public sealed record GanttScheduleItemContract(
    string ItemId,
    string OrderId,
    string OperationId,
    int OperationSequence,
    string ResourceId,
    string WorkCenterId,
    DateTimeOffset StartUtc,
    DateTimeOffset EndUtc,
    SchedulePlanStatusContract Status,
    bool HasConflict,
    ScheduleConflictReasonCodeContract? ConflictReasonCode,
    bool HasMaterialRisk = false,
    bool HasEquipmentRisk = false);

public static class SchedulingIntegrationEventTypes
{
    public const string SchedulePlanGenerated = "scheduling.SchedulePlanGenerated";
    public const string ScheduleConflictDetected = "scheduling.ScheduleConflictDetected";
    public const string SchedulePlanReleased = "scheduling.SchedulePlanReleased";
    public const string SchedulePlanRevoked = "scheduling.SchedulePlanRevoked";
    public const string SchedulePlanInvalidated = "scheduling.SchedulePlanInvalidated";
}

public static class SchedulingIntegrationEventVersions
{
    public const int V1 = 1;
}

public static class SchedulingIntegrationEventSources
{
    public const string BusinessScheduling = "business-scheduling";
}

public sealed record SchedulingIntegrationEvent<TPayload>(
    string EventId,
    string EventType,
    int EventVersion,
    DateTimeOffset OccurredAtUtc,
    string SourceService,
    string CorrelationId,
    string CausationId,
    string OrganizationId,
    string EnvironmentId,
    string Actor,
    string IdempotencyKey,
    TPayload Payload) : IIntegrationEventEnvelope
{
    object? IIntegrationEventEnvelope.PayloadObject => Payload;
}

public sealed record SchedulePlanReleasedIntegrationEvent(
    string EventId,
    string EventType,
    int EventVersion,
    DateTimeOffset OccurredAtUtc,
    string SourceService,
    string CorrelationId,
    string CausationId,
    string OrganizationId,
    string EnvironmentId,
    string Actor,
    string IdempotencyKey,
    SchedulePlanLifecyclePayload Payload) : IIntegrationEventEnvelope
{
    object? IIntegrationEventEnvelope.PayloadObject => Payload;
}

public sealed record SchedulePlanInvalidatedIntegrationEvent(
    string EventId,
    string EventType,
    int EventVersion,
    DateTimeOffset OccurredAtUtc,
    string SourceService,
    string CorrelationId,
    string CausationId,
    string OrganizationId,
    string EnvironmentId,
    string Actor,
    string IdempotencyKey,
    SchedulePlanInvalidatedPayload Payload) : IIntegrationEventEnvelope
{
    object? IIntegrationEventEnvelope.PayloadObject => Payload;
}

public sealed record SchedulePlanRevokedIntegrationEvent(
    string EventId,
    string EventType,
    int EventVersion,
    DateTimeOffset OccurredAtUtc,
    string SourceService,
    string CorrelationId,
    string CausationId,
    string OrganizationId,
    string EnvironmentId,
    string Actor,
    string IdempotencyKey,
    SchedulePlanRevokedPayload Payload) : IIntegrationEventEnvelope
{
    object? IIntegrationEventEnvelope.PayloadObject => Payload;
}

public sealed record ScheduleConflictDetectedIntegrationEvent(
    string EventId,
    string EventType,
    int EventVersion,
    DateTimeOffset OccurredAtUtc,
    string SourceService,
    string CorrelationId,
    string CausationId,
    string OrganizationId,
    string EnvironmentId,
    string Actor,
    string IdempotencyKey,
    ScheduleConflictDetectedPayload Payload) : IIntegrationEventEnvelope
{
    object? IIntegrationEventEnvelope.PayloadObject => Payload;
}

public sealed record SchedulePlanLifecyclePayload(
    string PlanId,
    string ProblemId,
    int ContractVersion,
    string AlgorithmVersion,
    string ProblemFingerprint,
    string PlanStatus,
    IReadOnlyCollection<SchedulePlanAffectedOperationPayload> AffectedOperations,
    long? ReleaseRevision = null);

public sealed record SchedulePlanRevokedPayload(
    string PlanId,
    string ProblemId,
    int ContractVersion,
    string AlgorithmVersion,
    string ProblemFingerprint,
    long ReleaseRevision,
    string Reason,
    string? SupersededByPlanId,
    IReadOnlyCollection<SchedulePlanAffectedOperationPayload> AffectedOperations);

public sealed record SchedulePlanInvalidatedPayload(
    string PlanId,
    string ProblemId,
    int ContractVersion,
    string AlgorithmVersion,
    string ProblemFingerprint,
    string PlanStatus,
    string ReasonCode,
    string SourceEventType,
    string SourceEventId,
    IReadOnlyCollection<string> AffectedResourceIds,
    IReadOnlyCollection<SchedulePlanAffectedOperationPayload> AffectedOperations);

public sealed record ScheduleConflictDetectedPayload(
    string PlanId,
    string ProblemId,
    int ContractVersion,
    string AlgorithmVersion,
    string ProblemFingerprint,
    string PlanStatus,
    string ConflictId,
    string ConflictReasonCode,
    string ConflictSeverity,
    string WorkOrderId,
    string OperationId,
    string ResourceId);

public sealed record SchedulePlanAffectedOperationPayload(
    string WorkOrderId,
    string OperationId,
    int OperationSequence,
    string ResourceId,
    string WorkCenterId,
    DateTimeOffset StartUtc,
    DateTimeOffset EndUtc,
    string? StandardOperationCode = null);

public enum ScheduleSplitPolicyContract
{
    NonSplittable = 0
}

public enum SchedulePlanStatusContract
{
    Preview = 0,
    Generated = 1,
    Released = 2,
    Superseded = 3,
    Revoked = 4
}

public enum ScheduleConflictReasonCodeContract
{
    DueDate = 0,
    Capacity = 1,
    Calendar = 2,
    Material = 3,
    Quality = 4,
    Equipment = 5,
    NoEligibleResource = 6,
    OutsideHorizon = 7,
    InvalidLockedAssignment = 8,
    PredecessorUnscheduled = 9,
    Tooling = 10
}

public enum ScheduleConflictSeverityContract
{
    Info = 0,
    Warning = 1,
    Error = 2
}

public enum ScheduleChangeTypeContract
{
    Added = 0,
    Moved = 1,
    Delayed = 2,
    Preserved = 3,
    Blocked = 4
}

public sealed record OrderUrgencyBusinessPriorityContract(
    string Level,
    string Source,
    string Reason,
    DateTimeOffset SetAtUtc,
    DateTimeOffset? ExpiresAtUtc,
    long Revision,
    IReadOnlyCollection<string> ReasonCodes);

public sealed record OrderUrgencyTimeCriticalityContract(
    string Level,
    decimal? CriticalRatio,
    decimal? SlackHours,
    decimal ExpectedDelayHours,
    DateTimeOffset? DueUtc,
    DateTimeOffset EstimatedCompletionUtc,
    decimal RemainingCycleHours,
    IReadOnlyCollection<string> ReasonCodes);

public sealed record OrderUrgencyExecutionRiskFactContract(
    string ReasonCode,
    string Category,
    bool IsBlocking,
    string SourceReference,
    DateTimeOffset ObservedAtUtc);

public sealed record OrderUrgencyExecutionRiskContract(
    string Level,
    bool IsSourceMissing,
    bool IsSourceStale,
    DateTimeOffset? FactsObservedAtUtc,
    IReadOnlyCollection<string> ReasonCodes,
    IReadOnlyCollection<OrderUrgencyExecutionRiskFactContract> Facts);

public sealed record OrderUrgencyContract(
    string OrderId,
    string BusinessReference,
    string Level,
    OrderUrgencyBusinessPriorityContract BusinessPriority,
    OrderUrgencyTimeCriticalityContract TimeCriticality,
    OrderUrgencyExecutionRiskContract ExecutionRisk,
    DateTimeOffset CalculatedAtUtc,
    string ModelVersion,
    string InputFingerprint);

public sealed record OrderUrgencyBusinessPriorityChangeContract(
    long Revision,
    string? PreviousLevel,
    string NewLevel,
    string ChangedBy,
    string Reason,
    DateTimeOffset ChangedAtUtc,
    DateTimeOffset? ExpiresAtUtc);

public sealed record OrderUrgencyDetailContract(
    OrderUrgencyContract Current,
    IReadOnlyCollection<OrderUrgencyContract> History,
    IReadOnlyCollection<OrderUrgencyBusinessPriorityChangeContract> BusinessPriorityChanges);
