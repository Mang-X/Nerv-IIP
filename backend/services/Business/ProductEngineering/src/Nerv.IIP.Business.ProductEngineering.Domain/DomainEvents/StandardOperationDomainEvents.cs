using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.StandardOperationAggregate;

namespace Nerv.IIP.Business.ProductEngineering.Domain.DomainEvents;

public sealed record StandardOperationCreatedDomainEvent(StandardOperation StandardOperation) : IDomainEvent;

public sealed record StandardOperationUpdatedDomainEvent(StandardOperation StandardOperation) : IDomainEvent;

public sealed record StandardOperationArchivedDomainEvent(StandardOperation StandardOperation) : IDomainEvent;
