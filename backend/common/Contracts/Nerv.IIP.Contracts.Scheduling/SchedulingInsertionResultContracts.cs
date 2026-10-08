namespace Nerv.IIP.Contracts.Scheduling;

public enum SchedulingInsertionFailureContract { UnknownMaterialEta, IncompleteChain, BlockingConflict }
public enum SchedulingInsertionOrderStatusContract { Unchanged, Delayed, New, Unscheduled }
public sealed record SchedulingInsertionExecutionFactContract(string OrderId, string OperationId,
    DateTimeOffset? ActualStartedAtUtc, DateTimeOffset? ActualCompletedAtUtc);
public sealed record SchedulingInsertionCalculationSnapshotContract(
    SchedulingProblemContract Problem, SchedulePlanContract Baseline,
    SchedulePlanContract CalculationBaseline, SchedulePlanFreezeContextContract Freeze,
    IReadOnlyCollection<SchedulingInsertionExecutionFactContract> Execution,
    IReadOnlyCollection<SchedulePlanFixedReservationContract> FixedReservations,
    SchedulingMaterialConstraintModeContract MaterialMode, SchedulingQualityConstraintModeContract QualityMode,
    SchedulingEquipmentUnknownModeContract EquipmentUnknownMode);
public sealed record SchedulingInsertionOrderImpactContract(string OrderId, bool IsNew,
    SchedulingInsertionOrderStatusContract Status, DateTimeOffset? BaselineCompletionUtc,
    DateTimeOffset? CandidateCompletionUtc, decimal? DelayDays,
    bool BaselineLate, bool CandidateLate, bool NewlyLate);
public sealed record SchedulingInsertionPropagationStepContract(string FromOrderId, string FromOperationId,
    string ToOrderId, string ToOperationId, string ReasonCode,
    ScheduleAssignmentSegmentContract? CompetitionWindow, int? CapacityUnits);
public sealed record SchedulingInsertionOperationImpactContract(string OrderId, string OperationId, DateTimeOffset DueUtc,
    ScheduleAssignmentContract? Baseline, ScheduleAssignmentContract? Candidate,
    IReadOnlyCollection<string> ReasonCodes, string SourceReference,
    IReadOnlyCollection<IReadOnlyCollection<SchedulingInsertionPropagationStepContract>> Paths);
public sealed record SchedulingInsertionRateComparisonContract(decimal Baseline, decimal Candidate, decimal Delta,
    int BaselineDenominator, int CandidateDenominator);
public sealed record SchedulingInsertionCountComparisonContract(int Baseline, int Candidate, int Delta);
public sealed record SchedulingInsertionUtilizationComparisonContract(decimal Baseline, decimal Candidate, decimal Delta);
public sealed record SchedulingInsertionOperationKeyContract(string OrderId, string OperationId);
public sealed record SchedulingInsertionLockRetentionContract(int Preserved, int Total,
    IReadOnlyCollection<SchedulingInsertionOperationKeyContract> NotPreserved);
/// <summary>ADR 0032 §5：六项 KPI 同一输入/资源窗口；准交率是可优化已排工序率。</summary>
public sealed record SchedulingInsertionKpisContract(SchedulingInsertionRateComparisonContract OnTimeRate,
    SchedulingInsertionCountComparisonContract LateOrderCount, int MovedOperationCount,
    SchedulingInsertionUtilizationComparisonContract ResourceUtilization,
    SchedulingInsertionCountComparisonContract UnscheduledOperationCount,
    SchedulingInsertionLockRetentionContract LockRetention);
public sealed record SchedulingInsertionPreviewResultContract(int ContractVersion, string BaselinePlanId,
    string CandidatePlanId, string InputFingerprint, SchedulePlanContract Candidate, DateTimeOffset? PromiseUtc,
    IReadOnlyCollection<SchedulingInsertionFailureContract> Failures,
    SchedulingInsertionCalculationSnapshotContract Snapshot, SchedulingInsertionKpisContract Kpis,
    IReadOnlyCollection<SchedulingInsertionOrderImpactContract> Orders,
    IReadOnlyCollection<SchedulingInsertionOperationImpactContract> Operations);

/// <summary>Scheduling producer 的详细任务读面；现有 Gateway facade 合同由后续消费方迁移。</summary>
public sealed record SchedulingInsertionAcceptedBaselineContract(SchedulePlanContract Baseline, SchedulingProblemContract Problem);
public sealed record SchedulingInsertionPreviewJobDetailContract(
    Guid JobId, SchedulingInsertionPreviewJobStatusContract Status, SchedulingInsertionPreviewInputContract Input,
    DateTimeOffset CreatedAtUtc, DateTimeOffset? StartedAtUtc, DateTimeOffset? FinishedAtUtc,
    SchedulePlanContract? Preview, string? FailureReason, SchedulingInsertionPreviewResultContract? Result = null,
    SchedulingInsertionAcceptedBaselineContract? AcceptedBaseline = null);
