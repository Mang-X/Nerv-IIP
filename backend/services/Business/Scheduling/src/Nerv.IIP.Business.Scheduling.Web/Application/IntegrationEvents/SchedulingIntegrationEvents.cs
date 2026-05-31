namespace Nerv.IIP.Business.Scheduling.Web.Application.IntegrationEvents;

public static class SchedulingIntegrationEventTypes
{
    public const string SchedulePlanGenerated = "scheduling.SchedulePlanGenerated";
    public const string ScheduleConflictDetected = "scheduling.ScheduleConflictDetected";
    public const string SchedulePlanReleased = "scheduling.SchedulePlanReleased";
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
    TPayload Payload);

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
    string WorkCenterId);
