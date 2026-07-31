using Nerv.IIP.Business.Wms.Domain.AggregatesModel.CountExecutionAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.InboundOrderAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.InventoryMovementRequestAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.OutboundOrderAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.WcsTaskAggregate;

namespace Nerv.IIP.Business.Wms.Domain.DomainEvents;

public sealed record InboundOrderCompletedDomainEvent(InboundOrder InboundOrder) : IDomainEvent;

public sealed record OutboundOrderCompletedDomainEvent(OutboundOrder OutboundOrder) : IDomainEvent;

public sealed record OutboundOrderCancelledDomainEvent(OutboundOrder OutboundOrder) : IDomainEvent;

/// <summary>
/// Raised once the warehouse has an outbound document (and picking task) standing behind a MES
/// material issue request, so MES can show the operator the authoritative 出库单.
/// </summary>
public sealed record MaterialIssueOutboundPreparedDomainEvent(
    OutboundOrder OutboundOrder,
    string MaterialIssueRequestNo,
    string? PickingTaskNo,
    DateTimeOffset PreparedAtUtc) : IDomainEvent;

public sealed record CountExecutionCompletedDomainEvent(CountExecution CountExecution) : IDomainEvent;

public sealed record InventoryMovementRequestCreatedDomainEvent(InventoryMovementRequest InventoryMovementRequest) : IDomainEvent;

public sealed record WcsTaskDispatchedDomainEvent(WcsTask WcsTask) : IDomainEvent;

public sealed record WcsTaskFailedDomainEvent(WcsTask WcsTask) : IDomainEvent;

public sealed record WcsTaskRetryExhaustedDomainEvent(WcsTask WcsTask) : IDomainEvent;

public sealed record WcsTaskCompletedDomainEvent(WcsTask WcsTask) : IDomainEvent;

public sealed record WcsTaskCancelledDomainEvent(WcsTask WcsTask) : IDomainEvent;
