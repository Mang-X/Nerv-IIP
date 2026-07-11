using Nerv.IIP.Business.Erp.Domain.AggregatesModel.WorkOrderCostAggregate;

namespace Nerv.IIP.Business.Erp.Domain.DomainEvents;

public sealed record WorkOrderCostCompletedDomainEvent(WorkOrderCost WorkOrderCost) : IDomainEvent;
