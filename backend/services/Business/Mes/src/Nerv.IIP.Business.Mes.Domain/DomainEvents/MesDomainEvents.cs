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

/// <param name="WorkOrder">被发布的工单聚合。</param>
/// <param name="OperationTasks">本次发布携带的工序任务集合。</param>
/// <param name="ReleasedAt">
/// 发布事实的时刻。由发布动作的调用方给出，不由转换器取 <c>UtcNow</c>。
/// 类型是 <see cref="WorkOrderReleaseFactTime"/> 而不是裸 <c>DateTimeOffset</c>：
/// 「不晚于任何一条**既有活动**（报工，或工序完工）」这条不变量由该类型的构造口径承担（#3117）。
///
/// <b>强度按实测写，别读强了</b>：编译器强制的是「**交出一个显式的下界参数**」，
/// **不是**「你确实去查过」——第二参可以传 <c>null</c>，而 <c>null</c>（真的没有既有活动）
/// 与 <c>null</c>（压根没去查）在类型层面不可区分。完整说明见
/// <see cref="WorkOrderReleaseFactTime"/> 的类型注释；两处措辞必须保持一致，
/// 上一轮就是因为只改了其中一处、另一处原样存活而被判阻断。
/// </param>
/// <param name="PreReleaseGoodQuantityByOperationTaskId">
/// 下达动作发生那一刻，每道工序**已经存在**的净良品量（非冲销报工行的 <c>GoodQuantity</c> 之和），
/// 键是 <c>OperationTask.OperationTaskIdValue</c>。工单在 <c>created</c> 状态就能开工报工（#3113），
/// 该事实只有 MES 在下达那一刻掌握，Quality 拿不到（#3129）。
///
/// <b>字典里没有某道工序 = 那道工序一条报工都没有 = 0</b>，不是「没查」。
/// <b>按实际构造点穷举</b>（<c>new WorkOrderReleasedDomainEvent(</c> 在 <c>src/</c> 下恰 3 处，均在
/// <c>WorkOrder.cs</c>）：两处传**空字典**（<c>Release()</c> 与无参 <c>MarkReleased()</c>，
/// 空成立的依据各自写在调用点紧邻注释里）；只有 <c>MarkReleased(tasks, releasedAt, 字典)</c> 收调用方的字典，
/// 而它的生产调用方是 <b>2 个</b>——下达命令 <c>ReleaseWorkOrderCommandHandler</c> 与 #3119 的
/// <c>BackfillCreatedWorkOrderReleaseCommandHandler</c>，两者都用 <c>GroupBy</c>(工序) 从该工单**全部**
/// 报工行构造，空分组天然不出现在结果里。转换器因此按 <c>GetValueOrDefault(id, 0m)</c> 取值。
///
/// <b>强度按实测写，别读强了</b>：编译器强制的是「**交出一个显式的字典**」，
/// **不是**「你确实去查过」——传 <c>[]</c> 与「查完确实全是 0」在类型层面不可区分。
/// 与 <see cref="WorkOrderReleaseFactTime"/> 第二参那条注释同一个形态、同一个限度。
/// </param>
public sealed record WorkOrderReleasedDomainEvent(
    WorkOrder WorkOrder,
    IReadOnlyCollection<OperationTask> OperationTasks,
    WorkOrderReleaseFactTime ReleasedAt,
    IReadOnlyDictionary<string, decimal> PreReleaseGoodQuantityByOperationTaskId) : IDomainEvent;

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

public sealed record MaterialLineSideReceiptConfirmedDomainEvent(MaterialIssueRequest MaterialIssueRequest, decimal ReceivedQuantity, decimal? UnitCost = null) : IDomainEvent;

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
