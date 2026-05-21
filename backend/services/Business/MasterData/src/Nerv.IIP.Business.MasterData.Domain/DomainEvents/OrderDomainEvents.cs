using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.OrderAggregate;

namespace Nerv.IIP.Business.MasterData.Domain.DomainEvents;

public record OrderCreatedDomainEvent(Order Order) : IDomainEvent;

public record OrderPaidDomainEvent(Order Order) : IDomainEvent;