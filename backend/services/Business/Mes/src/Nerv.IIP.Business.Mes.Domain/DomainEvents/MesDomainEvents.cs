using Nerv.IIP.Business.Mes.Domain.AggregatesModel.OperationTaskAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.FinishedGoodsReceiptRequestAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.MaterialSupplyAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.ProductionReportAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.QualityAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.EngineeringChangeAggregate;

namespace Nerv.IIP.Business.Mes.Domain.DomainEvents;

public sealed record WorkOrderCreatedDomainEvent(WorkOrder WorkOrder) : IDomainEvent;

public sealed record WorkOrderReleasedDomainEvent(WorkOrder WorkOrder, IReadOnlyCollection<OperationTask> OperationTasks) : IDomainEvent;

public sealed record WorkOrderCompletedDomainEvent(WorkOrder WorkOrder, DateTimeOffset CompletedAtUtc) : IDomainEvent;

public sealed record WorkOrderClosedDomainEvent(WorkOrder WorkOrder, DateTimeOffset ClosedAtUtc) : IDomainEvent;

public sealed record MesEngineeringChangeWorkOrderImpactDetectedDomainEvent(MesEngineeringChangeWorkOrderImpact Impact) : IDomainEvent;

public sealed record OperationTaskCompletedDomainEvent(OperationTask OperationTask) : IDomainEvent;
public sealed record OperationTaskManuallyDispatchedDomainEvent(OperationTask OperationTask, string Actor) : IDomainEvent;

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

public sealed record MaterialIssueRequestedDomainEvent(MaterialIssueRequest MaterialIssueRequest, decimal IssuedQuantity) : IDomainEvent;

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
