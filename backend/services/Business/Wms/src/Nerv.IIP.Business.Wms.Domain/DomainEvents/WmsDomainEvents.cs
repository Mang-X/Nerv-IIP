using Nerv.IIP.Business.Wms.Domain.AggregatesModel.CountExecutionAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.InboundOrderAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.OutboundOrderAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.WcsTaskAggregate;

namespace Nerv.IIP.Business.Wms.Domain.DomainEvents;

public sealed record InboundOrderCompletedDomainEvent(InboundOrder InboundOrder) : IDomainEvent;

public sealed record OutboundOrderCompletedDomainEvent(OutboundOrder OutboundOrder) : IDomainEvent;

public sealed record CountExecutionCompletedDomainEvent(CountExecution CountExecution) : IDomainEvent;

public sealed record WcsTaskDispatchedDomainEvent(WcsTask WcsTask) : IDomainEvent;

public sealed record WcsTaskFailedDomainEvent(WcsTask WcsTask) : IDomainEvent;

public sealed record WcsTaskCompletedDomainEvent(WcsTask WcsTask) : IDomainEvent;
