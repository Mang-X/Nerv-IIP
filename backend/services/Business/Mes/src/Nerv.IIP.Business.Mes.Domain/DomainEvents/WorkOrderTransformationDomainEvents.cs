using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderTransformationAggregate;

namespace Nerv.IIP.Business.Mes.Domain.DomainEvents;

public sealed record WorkOrderSplitDomainEvent(WorkOrderTransformation Transformation) : IDomainEvent;

public sealed record WorkOrderMergedDomainEvent(WorkOrderTransformation Transformation) : IDomainEvent;
