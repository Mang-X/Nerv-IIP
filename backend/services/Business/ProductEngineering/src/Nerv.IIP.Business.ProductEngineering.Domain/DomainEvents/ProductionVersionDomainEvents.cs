using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.ProductionVersionAggregate;

namespace Nerv.IIP.Business.ProductEngineering.Domain.DomainEvents;

public sealed record ProductionVersionCreatedDomainEvent(ProductionVersion ProductionVersion) : IDomainEvent;

public sealed record ProductionVersionUpdatedDomainEvent(ProductionVersion ProductionVersion) : IDomainEvent;

public sealed record ProductionVersionArchivedDomainEvent(ProductionVersion ProductionVersion) : IDomainEvent;
