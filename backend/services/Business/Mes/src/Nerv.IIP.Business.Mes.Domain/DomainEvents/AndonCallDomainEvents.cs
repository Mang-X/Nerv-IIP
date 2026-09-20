using Nerv.IIP.Business.Mes.Domain.AggregatesModel.AndonCallAggregate;

namespace Nerv.IIP.Business.Mes.Domain.DomainEvents;

public sealed record AndonCallEscalatedDomainEvent(AndonCall Call) : IDomainEvent;
