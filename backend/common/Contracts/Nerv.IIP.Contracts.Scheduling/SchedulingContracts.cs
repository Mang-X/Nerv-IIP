using System.Text.Json;
using System.Text.Json.Serialization;
using Nerv.IIP.Contracts.IntegrationEvents;

namespace Nerv.IIP.Contracts.Scheduling;

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
    IReadOnlyCollection<SchedulingLockedAssignmentContract> LockedAssignments);

public sealed record SchedulingOrderContract(
    string OrderId,
    string SkuCode,
    decimal Quantity,
    DateTimeOffset DueUtc,
    int Priority,
    bool IsRush,
    IReadOnlyCollection<SchedulingOperationContract> Operations);

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
    IReadOnlyCollection<string>? RequiredToolingIds = null);

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

public sealed record SchedulingMaterialReadinessContract(
    string ScopeType,
    string ScopeId,
    DateTimeOffset? MaterialReadyUtc,
    bool IsReady,
    IReadOnlyCollection<string> ReasonCodes);

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
    IReadOnlyCollection<ScheduleAssignmentContract> Assignments,
    IReadOnlyCollection<ScheduleResourceLoadContract> ResourceLoads,
    IReadOnlyCollection<ScheduleConflictContract> Conflicts,
    IReadOnlyCollection<UnscheduledOperationContract> UnscheduledOperations,
    IReadOnlyCollection<ScheduleChangeContract> ChangeSummary,
    IReadOnlyCollection<GanttScheduleItemContract> GanttItems);

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
    string ExplanationCode);

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
    ScheduleConflictReasonCodeContract? ConflictReasonCode);

public static class SchedulingIntegrationEventTypes
{
    public const string SchedulePlanGenerated = "scheduling.SchedulePlanGenerated";
    public const string ScheduleConflictDetected = "scheduling.ScheduleConflictDetected";
    public const string SchedulePlanReleased = "scheduling.SchedulePlanReleased";
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

public sealed record SchedulePlanLifecyclePayload(
    string PlanId,
    string ProblemId,
    int ContractVersion,
    string AlgorithmVersion,
    string ProblemFingerprint,
    string PlanStatus,
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
    DateTimeOffset EndUtc);

public enum ScheduleSplitPolicyContract
{
    NonSplittable = 0
}

public enum SchedulePlanStatusContract
{
    Preview = 0,
    Generated = 1,
    Released = 2
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
    PredecessorUnscheduled = 9
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
