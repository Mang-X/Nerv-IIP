using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.EngineeringBomAggregate;
using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.EngineeringChangeAggregate;
using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.EngineeringDocumentAggregate;
using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.EngineeringItemAggregate;
using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.ManufacturingBomAggregate;
using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.RoutingAggregate;

namespace Nerv.IIP.Business.ProductEngineering.Domain.DomainEvents;

public sealed record EngineeringDocumentRegisteredDomainEvent(EngineeringDocument Document) : IDomainEvent;

// Domain-only in the MVP; add an integration converter when external consumers need item revision notifications.
public sealed record EngineeringItemRevisionCreatedDomainEvent(EngineeringItem Item) : IDomainEvent;

public sealed record EngineeringBomReleasedDomainEvent(EngineeringBom Bom) : IDomainEvent;

public sealed record ManufacturingBomReleasedDomainEvent(ManufacturingBom Bom) : IDomainEvent;

public sealed record RoutingReleasedDomainEvent(Routing Routing) : IDomainEvent;

public sealed record EngineeringChangeReleasedDomainEvent(EngineeringChange Change) : IDomainEvent;
