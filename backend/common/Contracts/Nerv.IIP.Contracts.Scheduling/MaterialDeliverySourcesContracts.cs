namespace Nerv.IIP.Contracts.Scheduling;

// DemandPlanning 提供持久化建议/下游身份与完整销售交期来源；Scheduling 不按 SKU 猜关联。
public sealed record MaterialDeliverySourceSelection(
    string SuggestionId,
    string? WorkOrderId,
    IReadOnlyCollection<MaterialDeliveryDueSourceContract> DueSources);

public sealed record MaterialDeliveryDueSourceContract(string SourceReference, DateTimeOffset DueUtc);

public sealed record MaterialDeliverySourcesResponse(
    string PlanId,
    IReadOnlyCollection<MaterialDeliveryOrderSourceContract> Items);

public sealed record MaterialDeliveryOrderSourceContract(
    string SuggestionId,
    string? WorkOrderId,
    string Status,
    DateTimeOffset? ScheduledStartUtc,
    DateTimeOffset? LatestStartUtc,
    string? TightestDueSourceReference,
    IReadOnlyCollection<MaterialDeliveryOperationSourceContract> Operations,
    IReadOnlyCollection<MaterialDeliveryBoundContract> DueBounds);

public sealed record MaterialDeliveryOperationSourceContract(
    string OperationId,
    int OperationSequence,
    string ExecutionStatus,
    decimal NetGoodQuantity,
    decimal RemainingQuantity,
    double RemainingMinutes,
    DateTimeOffset EarliestStartUtc,
    DateTimeOffset? AssignmentStartUtc,
    string AssignmentStatus,
    IReadOnlyCollection<string> PredecessorOperationIds,
    string RoutingVersionId);

public sealed record MaterialDeliveryBoundContract(
    string SourceReference,
    DateTimeOffset DueUtc,
    double RemainingMinutes,
    DateTimeOffset LatestStartUtc,
    IReadOnlyCollection<string> CriticalPathOperationIds);
