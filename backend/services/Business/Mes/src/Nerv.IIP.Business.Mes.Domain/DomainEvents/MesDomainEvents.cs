using Nerv.IIP.Business.Mes.Domain.AggregatesModel.OperationTaskAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;

namespace Nerv.IIP.Business.Mes.Domain.DomainEvents;

public sealed record WorkOrderCreatedDomainEvent(WorkOrder WorkOrder) : IDomainEvent;

public sealed record WorkOrderReleasedDomainEvent(WorkOrder WorkOrder, IReadOnlyCollection<OperationTask> OperationTasks) : IDomainEvent;
