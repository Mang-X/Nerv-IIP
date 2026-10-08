namespace Nerv.IIP.Contracts.Scheduling;

/// <summary>Original source timestamps. A prediction never replaces actual recovery.</summary>
public sealed record SchedulingDowntimeFactContract(string Source, string SourceReferenceId, string? DeviceAssetId,
    string? WorkCenterId, DateTimeOffset StartedAtUtc, DateTimeOffset? RecoveredAtUtc, DateTimeOffset? ExpectedRestoreAtUtc,
    string? OriginSource = null, string? OriginSourceType = null, string? OriginSourceReferenceId = null,
    DateTimeOffset? PredictedRestoreAtUtc = null, string? RestorePredictionSource = null, string? RestorePredictionSourceVersion = null);

public sealed record SchedulingDowntimeAffectedOperationContract(string WorkOrderId, string OperationId,
    IReadOnlyList<string> AvailableAlternativeResourceIds);

/// <summary>Alternative count counts operations, not devices or feasible transfer candidates.</summary>
public sealed record SchedulingDowntimeImpactContract(SchedulingDowntimeFactContract Fact,
    IReadOnlyList<SchedulingDowntimeAffectedOperationContract> AffectedOperations, int OperationsWithAlternativesCount);

public sealed record SchedulingDowntimeImpactResponse(string BaselinePlanId, string ProblemId,
    DateTimeOffset ObservedAtUtc, IReadOnlyList<SchedulingDowntimeImpactContract> Items);
