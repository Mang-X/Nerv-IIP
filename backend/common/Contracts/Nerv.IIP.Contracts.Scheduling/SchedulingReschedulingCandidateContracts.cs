namespace Nerv.IIP.Contracts.Scheduling;

public enum SchedulingReschedulingStrategyContract { RightShift = 0, ResourceTransfer = 1 }
public enum SchedulingCandidateReasonCodeContract { OperationDeviation, NewOperation, ResourceUnavailable, PredecessorDependency, ResourceCapacity }
public enum SchedulingCandidateDeviationKindContract { ResourceUnavailable, OperationDeviation, NewOperation }

public sealed record SchedulingCandidatePreviewRequestContract(string OrganizationId, string EnvironmentId, string BaselinePlanId);
public sealed record SchedulingCandidateSelectRequestContract(string OrganizationId, string EnvironmentId, string BaselinePlanId,
    DateTimeOffset AsOfUtc, string InputFingerprint, SchedulingReschedulingStrategyContract Strategy);

/// <summary>同一明确时点和输入的 Preview 集合；选定仍须核实当前事实，且不自动发布。</summary>
public sealed record SchedulingCandidateSetContract(int ContractVersion, string BaselinePlanId, DateTimeOffset AsOfUtc,
    string InputFingerprint, IReadOnlyList<SchedulingCandidateContract> Candidates);
public sealed record SchedulingCandidateContract(SchedulingReschedulingStrategyContract Strategy, string InputFingerprint,
    SchedulePlanContract Plan, SchedulingCandidateKpisContract Kpis, IReadOnlyList<SchedulingCandidateMovementContract> Movements,
    IReadOnlyList<SchedulingCandidateExplanationContract> Explanations, IReadOnlyList<SchedulingCandidateTransferContract> Transfers);
public sealed record SchedulingCandidateKpisContract(decimal BaselineOnTimeRate, decimal CandidateOnTimeRate,
    decimal OnTimeRateChange, int BaselineOnTimeDenominator, int CandidateOnTimeDenominator,
    int BaselineLateOrderCount, int CandidateLateOrderCount, int LateOrderCountChange, int MovedOperationCount,
    decimal BaselineResourceUtilization, decimal CandidateResourceUtilization, decimal ResourceUtilizationChange,
    int BaselineUnscheduledCount, int CandidateUnscheduledCount, int UnscheduledCountChange,
    int PreservedLockedCount, int TotalLockedCount, IReadOnlyList<SchedulingCandidateLockPreservationContract> LockedAssignments);
public sealed record SchedulingCandidateLockPreservationContract(ScheduleAssignmentContract Original,
    ScheduleAssignmentContract? Candidate, bool Preserved);
public sealed record SchedulingCandidateMovementContract(ScheduleAssignmentContract Original, ScheduleAssignmentContract Candidate,
    IReadOnlyList<SchedulingCandidateReasonContract> Reasons, IReadOnlyList<SchedulingCandidatePathContract> Paths);
public sealed record SchedulingCandidateExplanationContract(string OrderId, string OperationId, string Code,
    IReadOnlyList<SchedulingCandidateReasonContract> Reasons, IReadOnlyList<SchedulingCandidatePathContract> Paths);
public sealed record SchedulingCandidateReasonContract(SchedulingCandidateReasonCodeContract Code, SchedulingCandidateDeviationContract Source);
public sealed record SchedulingCandidateDeviationContract(SchedulingCandidateDeviationKindContract Kind, string SourceReference,
    string SourceVersion, DateTimeOffset OccurredAtUtc, string ReasonCode, string? ResourceId = null,
    DateTimeOffset? StartUtc = null, DateTimeOffset? EndUtc = null, string? OrderId = null, string? OperationId = null);
public sealed record SchedulingCandidateOperationContract(string OrderId, string OperationId);
public sealed record SchedulingCandidateStepContract(SchedulingCandidateOperationContract From, SchedulingCandidateOperationContract To,
    SchedulingCandidateReasonCodeContract Code, ScheduleAssignmentSegmentContract? CompetitionWindow = null, int? CapacityUnits = null);
public sealed record SchedulingCandidatePathContract(SchedulingCandidateDeviationContract Source, SchedulingCandidateOperationContract Root,
    IReadOnlyList<SchedulingCandidateStepContract> Steps);
public sealed record SchedulingCandidateTransferContract(string OrderId, string OperationId, string OriginalResourceId,
    string ResourceId, int SetupMinutes, IReadOnlyList<SchedulingCandidateDeviceSourceContract> DeviceSources);
public sealed record SchedulingCandidateDeviceSourceContract(string ResourceId, string SubstituteResourceId,
    string SourceReference, string ReasonCode);
public sealed record SchedulingCandidateSelectionContract(SchedulePlanContract Plan, SchedulingWorkingDraftContract WorkingDraft,
    SchedulePlanComparisonContract Comparison);
