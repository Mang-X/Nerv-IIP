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

/// <param name="ReleasedAtUtc">
/// 发布时刻。它是发给 Quality 的发布事实的时刻口径，必须**不晚于** MES 已经掌握的任何一条同工单报工——
/// Quality 的 <c>PeriodicInspectionOperation.ApplyRelease</c> 对「报工早于发布」直接抛出、整封进死信，
/// 而工单在 <c>created</c> 状态就能开工报工（#3113），事后补下达按「现在」记时刻就永远补不进去（#3117）。
/// 由发布动作的调用方给出，不由转换器取 <c>UtcNow</c>。
/// </param>
public sealed record WorkOrderReleasedDomainEvent(
    WorkOrder WorkOrder,
    IReadOnlyCollection<OperationTask> OperationTasks,
    DateTimeOffset ReleasedAtUtc) : IDomainEvent;

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
