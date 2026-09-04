using Nerv.IIP.Business.Mes.Domain.AggregatesModel.OperationTaskAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.FinishedGoodsReceiptRequestAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.MaterialSupplyAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.ProductionReportAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.QualityAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.EngineeringChangeAggregate;

namespace Nerv.IIP.Business.Mes.Domain.DomainEvents;

public enum OperationTaskManualDispatchClearReason
{
    DeviceCleared,
    OperationCancelled
}

public sealed record WorkOrderCreatedDomainEvent(WorkOrder WorkOrder) : IDomainEvent;

public sealed record ReworkWorkOrderCreatedDomainEvent(
    WorkOrder WorkOrder,
    DateTimeOffset RequestedAtUtc,
    string CorrelationId,
    string CausationId) : IDomainEvent;

/// <param name="ReleasedAt">
/// 发布事实的时刻。由发布动作的调用方给出，不由转换器取 <c>UtcNow</c>。
/// 类型是 <see cref="WorkOrderReleaseFactTime"/> 而不是裸 <c>DateTimeOffset</c>：
/// 「不晚于任何一条既有报工」这条不变量由该类型的构造口径承担，编译器强制每条发布路径交出报工下界，
/// 不靠调用方读注释（#3117）。
/// </param>
public sealed record WorkOrderReleasedDomainEvent(
    WorkOrder WorkOrder,
    IReadOnlyCollection<OperationTask> OperationTasks,
    WorkOrderReleaseFactTime ReleasedAt) : IDomainEvent;

public sealed record WorkOrderCompletedDomainEvent(WorkOrder WorkOrder, DateTimeOffset CompletedAtUtc) : IDomainEvent;

public sealed record WorkOrderClosedDomainEvent(WorkOrder WorkOrder, DateTimeOffset ClosedAtUtc) : IDomainEvent;

public sealed record MesEngineeringChangeWorkOrderImpactDetectedDomainEvent(MesEngineeringChangeWorkOrderImpact Impact) : IDomainEvent;

public sealed record OperationTaskCompletedDomainEvent(OperationTask OperationTask) : IDomainEvent;

public enum MachineTimeFactStatus
{
    Available,
    NotApplicable,
    Unavailable,
}

public static class MachineTimeBasisCodes
{
    public const string SingleDeviceActiveMinusExplicitPauseV1 = "single-device-active-minus-explicit-pause-v1";
}

public sealed record OperationActualTimeSettlementSnapshot(
    string OrganizationId,
    string EnvironmentId,
    string WorkOrderId,
    string OperationTaskId,
    string WorkCenterId,
    long SettlementRevision,
    DateTimeOffset CompletedAtUtc,
    long ActualLaborTicks,
    long ActualMachineTicks,
    IReadOnlyCollection<string> CoveredProductionReportNos,
    string? DeviceAssetId = null,
    MachineTimeFactStatus MachineTimeStatus = MachineTimeFactStatus.Unavailable,
    long? BillableMachineTicks = null,
    string? MachineTimeBasisCode = null);

public sealed record OperationActualTimeSettledDomainEvent(
    OperationActualTimeSettlementSnapshot Settlement) : IDomainEvent;

public sealed record OperationActualTimeSettlementVoidedDomainEvent(
    OperationActualTimeSettlementSnapshot Settlement,
    DateTimeOffset VoidedAtUtc) : IDomainEvent;

public sealed record OperationTaskManualDispatchSnapshot(
    string OrganizationId,
    string EnvironmentId,
    string WorkOrderId,
    string OperationTaskId,
    int OperationSequence,
    string ResourceId,
    string WorkCenterId,
    DateTimeOffset StartUtc,
    DateTimeOffset EndUtc,
    DateTimeOffset OccurredAtUtc,
    long DispatchRevision);

public sealed record OperationTaskManuallyDispatchedDomainEvent(
    OperationTaskManualDispatchSnapshot Dispatch,
    string Actor) : IDomainEvent;

public sealed record OperationTaskManualDispatchClearedDomainEvent(
    OperationTaskManualDispatchSnapshot Dispatch,
    OperationTaskManualDispatchClearReason Reason,
    DateTimeOffset ClearedAtUtc,
    string Actor) : IDomainEvent;

public sealed record WorkOrderCancelledDomainEvent(
    WorkOrder WorkOrder,
    DateTimeOffset CancelledAtUtc,
    string Reason,
    IReadOnlyCollection<string> MaterialIssueRequestNos) : IDomainEvent;

public sealed record ProductionReportOeeProjection(
    string WorkCenterId,
    string? DeviceAssetId,
    string UomCode,
    decimal? TheoreticalRatePerHour);

public sealed record ProductionReportRecordedDomainEvent(
    ProductionReport ProductionReport,
    ProductionReportOeeProjection? OeeProjection = null) : IDomainEvent;

public sealed record ProductionMaterialConsumedDomainEvent(ProductionReportMaterialConsumption MaterialConsumption) : IDomainEvent;

/// <summary>
/// Raised when a material issue request is first created. Drives the warehouse leg of the 领料 chain
/// (WMS outbound order + picking task); the inventory movement legs stay on the receipt/return events.
/// </summary>
public sealed record MaterialIssueRequestCreatedDomainEvent(MaterialIssueRequest MaterialIssueRequest) : IDomainEvent;

public sealed record MaterialIssueRequestedDomainEvent(
    MaterialIssueRequest MaterialIssueRequest,
    decimal IssuedQuantity,
    MaterialTransferAllocation? SourceAllocation = null,
    int AllocationIndex = 0) : IDomainEvent;

public sealed record MaterialLineSideReceiptConfirmedDomainEvent(MaterialIssueRequest MaterialIssueRequest, decimal ReceivedQuantity) : IDomainEvent;

public sealed record MaterialLineSideReturnRequestedDomainEvent(
    MaterialIssueRequest MaterialIssueRequest,
    decimal ReturnedQuantity,
    string MaterialLotId,
    DateTimeOffset ReturnedAtUtc) : IDomainEvent;

public sealed record MaterialReturnedToWarehouseDomainEvent(
    MaterialIssueRequest MaterialIssueRequest,
    decimal ReturnedQuantity,
    string MaterialLotId,
    DateTimeOffset ReturnedAtUtc) : IDomainEvent;

public sealed record FinishedGoodsReceiptRequestedDomainEvent(
    FinishedGoodsReceiptRequest FinishedGoodsReceiptRequest,
    decimal Quantity,
    string IdempotencyKey) : IDomainEvent;

public sealed record DefectRaisedDomainEvent(DefectRecord DefectRecord) : IDomainEvent;
